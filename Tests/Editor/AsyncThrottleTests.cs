// Copyright (c) Una Partida Mas. All rights reserved.
// Licensed under the MIT License. See LICENSE.md in the repository root.

using System;
using System.Threading;
using NUnit.Framework;

namespace UnaPartidaMas.Valkarn.Tasks.Tests
{
    [TestFixture]
    public class AsyncThrottleTests
    {
        [SetUp]
        public void SetUp()
        {
            TestHelper.EnsureInitialized();
        }

        // ── Constructor Validation ──

        [Test]
        public void Constructor_Zero_Throws()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new AsyncThrottle(0));
        }

        [Test]
        public void Constructor_Negative_Throws()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new AsyncThrottle(-1));
        }

        [Test]
        public void Constructor_ValidMax_SetsMaxConcurrency()
        {
            var throttle = new AsyncThrottle(4);
            Assert.AreEqual(4, throttle.MaxConcurrency);
            Assert.AreEqual(0, throttle.InFlight);
        }

        // ── TryAcquire / Release ──

        [Test]
        public void TryAcquire_SucceedsUpToMax()
        {
            var throttle = new AsyncThrottle(3);

            Assert.IsTrue(throttle.TryAcquire());
            Assert.IsTrue(throttle.TryAcquire());
            Assert.IsTrue(throttle.TryAcquire());
            Assert.AreEqual(3, throttle.InFlight);
        }

        [Test]
        public void TryAcquire_FailsAtMax()
        {
            var throttle = new AsyncThrottle(2);

            Assert.IsTrue(throttle.TryAcquire());
            Assert.IsTrue(throttle.TryAcquire());
            Assert.IsFalse(throttle.TryAcquire());
            Assert.AreEqual(2, throttle.InFlight);
        }

        [Test]
        public void Release_AllowsNewAcquire()
        {
            var throttle = new AsyncThrottle(1);

            Assert.IsTrue(throttle.TryAcquire());
            Assert.IsFalse(throttle.TryAcquire());

            throttle.Release();
            Assert.AreEqual(0, throttle.InFlight);

            Assert.IsTrue(throttle.TryAcquire());
            Assert.AreEqual(1, throttle.InFlight);
        }

        // ── InFlight Tracking ──

        [Test]
        public void InFlight_TracksCorrectly()
        {
            var throttle = new AsyncThrottle(5);

            Assert.AreEqual(0, throttle.InFlight);

            throttle.TryAcquire();
            Assert.AreEqual(1, throttle.InFlight);

            throttle.TryAcquire();
            throttle.TryAcquire();
            Assert.AreEqual(3, throttle.InFlight);

            throttle.Release();
            Assert.AreEqual(2, throttle.InFlight);

            throttle.Release();
            throttle.Release();
            Assert.AreEqual(0, throttle.InFlight);
        }

        // ── ThrottledForget ──

        [Test]
        public void ThrottledForget_SyncCompleted_ReleasesImmediately()
        {
            var throttle = new AsyncThrottle(2);
            throttle.TryAcquire();
            Assert.AreEqual(1, throttle.InFlight);

            ValkarnTask.CompletedTask.ThrottledForget(throttle);
            Assert.AreEqual(0, throttle.InFlight);
        }

        [Test]
        public void ThrottledForget_FaultedTask_ReleasesAndPublishesException()
        {
            using var collector = new TestHelper.UnobservedExceptionCollector();

            var throttle = new AsyncThrottle(2);
            throttle.TryAcquire();

            var ex = new InvalidOperationException("throttle fault test");
            var task = ValkarnTask.FromException(ex);
            task.ThrottledForget(throttle);

            Assert.AreEqual(0, throttle.InFlight);
            Assert.AreEqual(1, collector.Exceptions.Count);
            Assert.AreSame(ex, collector.Exceptions[0]);
        }

        [Test]
        public void ThrottledForget_CanceledTask_ReleasesWithoutException()
        {
            using var collector = new TestHelper.UnobservedExceptionCollector();

            var throttle = new AsyncThrottle(2);
            throttle.TryAcquire();

            var task = ValkarnTask.FromCanceled();
            task.ThrottledForget(throttle);

            Assert.AreEqual(0, throttle.InFlight);
            Assert.AreEqual(0, collector.Exceptions.Count);
        }

        [Test]
        public void ThrottledForget_PendingTask_ReleasesOnCompletion()
        {
            var throttle = new AsyncThrottle(2);
            throttle.TryAcquire();

            var promise = new ValkarnTask.Promise();
            var task = promise.Task;
            task.ThrottledForget(throttle);

            // Still pending — slot still held.
            Assert.AreEqual(1, throttle.InFlight);

            // Complete the task.
            promise.TrySetResult();

            // Continuation should have released the slot.
            Assert.AreEqual(0, throttle.InFlight);
        }

        [Test]
        public void ThrottledForget_PendingFault_ReleasesAndPublishesException()
        {
            using var collector = new TestHelper.UnobservedExceptionCollector();

            var throttle = new AsyncThrottle(2);
            throttle.TryAcquire();

            var promise = new ValkarnTask.Promise();
            var task = promise.Task;
            task.ThrottledForget(throttle);

            Assert.AreEqual(1, throttle.InFlight);

            var ex = new InvalidOperationException("deferred fault");
            promise.TrySetException(ex);

            Assert.AreEqual(0, throttle.InFlight);
            Assert.AreEqual(1, collector.Exceptions.Count);
            Assert.AreSame(ex, collector.Exceptions[0]);
        }

        // ── Underflow Guard ──

        [Test]
        public void Release_WithoutAcquire_DoesNotUnderflow()
        {
            var throttle = new AsyncThrottle(2);
            Assert.AreEqual(0, throttle.InFlight);

            // Release without matching Acquire — should not go negative
            throttle.Release();
            Assert.AreEqual(0, throttle.InFlight);

            // TryAcquire should still respect the original max
            Assert.IsTrue(throttle.TryAcquire());
            Assert.IsTrue(throttle.TryAcquire());
            Assert.IsFalse(throttle.TryAcquire());
            Assert.AreEqual(2, throttle.InFlight);
        }

        // ── Thread-Safety ──

        [Test]
        public void TryAcquire_ConcurrentAccess_NeverExceedsMax()
        {
            const int max = 4;
            const int threadCount = 8;
            const int attemptsPerThread = 10_000;

            var throttle = new AsyncThrottle(max);
            int peakInFlight = 0;
            var barrier = new Barrier(threadCount);

            var threads = new Thread[threadCount];
            for (int t = 0; t < threadCount; t++)
            {
                threads[t] = new Thread(() =>
                {
                    barrier.SignalAndWait();
                    for (int i = 0; i < attemptsPerThread; i++)
                    {
                        if (throttle.TryAcquire())
                        {
                            // Record peak (best-effort, not perfectly accurate but validates no overflow).
                            int current = throttle.InFlight;
                            int observed;
                            do
                            {
                                observed = Volatile.Read(ref peakInFlight);
                                if (current <= observed) break;
                            } while (Interlocked.CompareExchange(ref peakInFlight, current, observed) != observed);

                            Thread.SpinWait(10);
                            throttle.Release();
                        }
                    }
                });
                threads[t].IsBackground = true;
                threads[t].Start();
            }

            foreach (var thread in threads) thread.Join();

            Assert.LessOrEqual(peakInFlight, max, "In-flight count should never exceed max concurrency");
            Assert.AreEqual(0, throttle.InFlight, "All slots should be released after test");
        }
    }
}
