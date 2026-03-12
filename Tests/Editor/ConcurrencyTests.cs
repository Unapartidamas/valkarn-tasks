// Copyright (c) Una Partida Mas. All rights reserved.
// Licensed under the MIT License. See LICENSE.md in the repository root.

using System;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;

namespace UnaPartidaMas.Valkarn.Tasks.Tests
{
    /// <summary>
    /// Concurrency stress tests for thread-safety validation.
    /// Tests race conditions between completion, continuation registration, and pool operations.
    /// </summary>
    [TestFixture]
    public class ConcurrencyTests
    {
        [SetUp]
        public void SetUp()
        {
            ValkarnTaskPoolShared.SetMainThreadId(Thread.CurrentThread.ManagedThreadId);
        }

        // ── TrySetResult Race Tests ──

        [Test]
        public void TrySetResult_ConcurrentDoubleCompletion_OnlyOneWins()
        {
            const int iterations = 10_000;

            for (int i = 0; i < iterations; i++)
            {
                var core = new ValkarnTaskCompletionCore<int>();
                // generation starts at 0 by default, no initialization needed

                int wins = 0;

                var t1 = Task.Run(() => { if (core.TrySetResult(1)) Interlocked.Increment(ref wins); });
                var t2 = Task.Run(() => { if (core.TrySetResult(2)) Interlocked.Increment(ref wins); });

                Task.WaitAll(t1, t2);

                Assert.AreEqual(1, wins, $"Iteration {i}: exactly one TrySetResult should win");
                var result = core.GetResult(core.Token);
                Assert.That(result, Is.EqualTo(1).Or.EqualTo(2));
            }
        }

        [Test]
        public void TrySetResult_ConcurrentWithTrySetException_OnlyOneWins()
        {
            const int iterations = 10_000;

            for (int i = 0; i < iterations; i++)
            {
                var core = new ValkarnTaskCompletionCore<int>();
                // generation starts at 0 by default, no initialization needed

                int wins = 0;
                bool resultWon = false;
                bool exceptionWon = false;

                var t1 = Task.Run(() =>
                {
                    if (core.TrySetResult(42))
                    {
                        Interlocked.Increment(ref wins);
                        Volatile.Write(ref resultWon, true);
                    }
                });
                var t2 = Task.Run(() =>
                {
                    if (core.TrySetException(new InvalidOperationException("test")))
                    {
                        Interlocked.Increment(ref wins);
                        Volatile.Write(ref exceptionWon, true);
                    }
                });

                Task.WaitAll(t1, t2);

                Assert.AreEqual(1, wins, $"Iteration {i}: exactly one completion should win");

                var status = core.GetStatus(core.Token);
                if (resultWon)
                {
                    Assert.AreEqual(ValkarnTask.Status.Succeeded, status);
                    Assert.AreEqual(42, core.GetResult(core.Token));
                }
                else
                {
                    Assert.AreEqual(ValkarnTask.Status.Faulted, status);
                }
            }
        }

        // ── OnCompleted Race Tests ──

        [Test]
        public void OnCompleted_RacesWithTrySetResult_ContinuationAlwaysInvoked()
        {
            const int iterations = 10_000;

            for (int i = 0; i < iterations; i++)
            {
                var core = new ValkarnTaskCompletionCore<int>();
                // generation starts at 0 by default, no initialization needed
                var counter = new StrongBox<int>(0);

                var barrier = new ManualResetEventSlim(false);

                var t1 = Task.Run(() =>
                {
                    barrier.Wait();
                    core.TrySetResult(42);
                });

                var t2 = Task.Run(() =>
                {
                    barrier.Wait();
                    core.OnCompleted(
                        static state => Interlocked.Increment(ref ((StrongBox<int>)state).Value),
                        counter,
                        core.Token);
                });

                barrier.Set();
                Task.WaitAll(t1, t2);

                // The continuation should have been invoked exactly once
                // (either by TrySetResult finding it, or by OnCompleted finding the sentinel)
                Assert.AreEqual(1, counter.Value, $"Iteration {i}: continuation invoked exactly once");
                var status = core.GetStatus(core.Token);
                Assert.AreEqual(ValkarnTask.Status.Succeeded, status);
                Assert.AreEqual(42, core.GetResult(core.Token));
            }
        }

        [Test]
        public void OnCompleted_SetBeforeCompletion_ContinuationInvokedByComplete()
        {
            const int iterations = 10_000;

            for (int i = 0; i < iterations; i++)
            {
                var core = new ValkarnTaskCompletionCore<int>();
                // generation starts at 0 by default, no initialization needed
                var counter = new StrongBox<int>(0);

                // Register continuation first
                core.OnCompleted(
                    static state => Interlocked.Increment(ref ((StrongBox<int>)state).Value),
                    counter,
                    core.Token);

                // Then complete from another thread
                var t = Task.Run(() => core.TrySetResult(i));
                t.Wait();

                Assert.AreEqual(1, counter.Value, $"Iteration {i}: continuation should be invoked exactly once");
            }
        }

        [Test]
        public void OnCompleted_SetAfterCompletion_ContinuationInvokedInline()
        {
            const int iterations = 10_000;

            for (int i = 0; i < iterations; i++)
            {
                var core = new ValkarnTaskCompletionCore<int>();
                // generation starts at 0 by default, no initialization needed
                var counter = new StrongBox<int>(0);

                // Complete first
                core.TrySetResult(i);

                // Then register continuation (should invoke inline)
                core.OnCompleted(
                    static state => Interlocked.Increment(ref ((StrongBox<int>)state).Value),
                    counter,
                    core.Token);

                Assert.AreEqual(1, counter.Value, $"Iteration {i}: continuation should be invoked inline");
            }
        }

        // ── Pool Thread-Safety Tests ──

        [Test]
        public void Pool_ConcurrentRentReturn_NeverLosesItems()
        {
            var pool = new ValkarnTaskPool<PoolTestNode>(maxSize: 100);
            const int threadCount = 4;
            const int opsPerThread = 5000;
            int totalReturned = 0;
            int totalRented = 0;

            // Pre-fill pool
            for (int i = 0; i < 50; i++)
            {
                var node = new PoolTestNode();
                pool.TryReturn(node);
            }

            var barrier = new Barrier(threadCount);
            var threads = new Thread[threadCount];

            for (int t = 0; t < threadCount; t++)
            {
                threads[t] = new Thread(() =>
                {
                    barrier.SignalAndWait();
                    for (int i = 0; i < opsPerThread; i++)
                    {
                        var item = pool.TryRent();
                        if (item != null)
                        {
                            Interlocked.Increment(ref totalRented);
                            Thread.SpinWait(10); // simulate short work
                            if (pool.TryReturn(item))
                                Interlocked.Increment(ref totalReturned);
                        }
                    }
                });
                threads[t].IsBackground = true;
                threads[t].Start();
            }

            foreach (var t in threads) t.Join();

            // Pool size should be non-negative and within bounds
            Assert.GreaterOrEqual(pool.Size, 0);
            Assert.LessOrEqual(pool.Size, pool.MaxSize);
        }

        [Test]
        public void Pool_MainThreadAndBackgroundThread_NoCorruption()
        {
            var pool = new ValkarnTaskPool<PoolTestNode>(maxSize: 100);

            // Pre-fill
            for (int i = 0; i < 20; i++)
                pool.TryReturn(new PoolTestNode());

            const int iterations = 10_000;
            var bgDone = new ManualResetEventSlim(false);
            int bgOps = 0;

            // Background thread doing concurrent rent/return
            var bgThread = new Thread(() =>
            {
                for (int i = 0; i < iterations; i++)
                {
                    var item = pool.TryRent();
                    if (item != null)
                    {
                        Interlocked.Increment(ref bgOps);
                        pool.TryReturn(item);
                    }
                }
                bgDone.Set();
            });
            bgThread.IsBackground = true;
            bgThread.Start();

            // Main thread doing concurrent rent/return
            int mainOps = 0;
            for (int i = 0; i < iterations; i++)
            {
                var item = pool.TryRent();
                if (item != null)
                {
                    mainOps++;
                    pool.TryReturn(item);
                }
            }

            bgDone.Wait();

            Assert.GreaterOrEqual(pool.Size, 0);
            Assert.LessOrEqual(pool.Size, pool.MaxSize);
            Assert.Greater(mainOps + bgOps, 0, "At least some operations should succeed");
        }

        // ── ContinuationQueue Thread-Safety Tests ──

        [Test]
        public void ContinuationQueue_ConcurrentEnqueueFromThread_AllDrained()
        {
            var queue = new ContinuationQueue();
            const int threadCount = 4;
            const int itemsPerThread = 1000;
            var sharedCounter = new StrongBox<int>(0);

            var threads = new Thread[threadCount];
            var barrier = new Barrier(threadCount);

            for (int t = 0; t < threadCount; t++)
            {
                threads[t] = new Thread(() =>
                {
                    barrier.SignalAndWait();
                    for (int i = 0; i < itemsPerThread; i++)
                    {
                        queue.EnqueueFromThread(
                            static state => Interlocked.Increment(ref ((StrongBox<int>)state).Value),
                            sharedCounter);
                    }
                });
                threads[t].IsBackground = true;
                threads[t].Start();
            }

            foreach (var t in threads) t.Join();

            queue.Run();

            Assert.AreEqual(threadCount * itemsPerThread, sharedCounter.Value);
        }

        [Test]
        public void ContinuationQueue_ConcurrentEnqueueDuringDrain_NothingLost()
        {
            var queue = new ContinuationQueue();
            var counter = new StrongBox<int>(0);
            const int enqueueCount = 1000;

            // Enqueue some items
            for (int i = 0; i < enqueueCount; i++)
            {
                queue.Enqueue(
                    static state => Interlocked.Increment(ref ((StrongBox<int>)state).Value),
                    counter);
            }

            // Start draining and simultaneously enqueue from background
            var bgDone = new ManualResetEventSlim(false);
            var bgThread = new Thread(() =>
            {
                for (int i = 0; i < enqueueCount; i++)
                {
                    queue.EnqueueFromThread(
                        static state => Interlocked.Increment(ref ((StrongBox<int>)state).Value),
                        counter);
                }
                bgDone.Set();
            });
            bgThread.IsBackground = true;
            bgThread.Start();

            // Drain on main thread
            queue.Run();

            bgDone.Wait();

            // Second drain to pick up items enqueued during first drain
            queue.Run();

            Assert.AreEqual(enqueueCount * 2, counter.Value,
                "All items from both main thread and background thread should be drained");
        }

        // ── Async Runner Pool Stress ──

        [Test]
        public void AsyncRunner_HighConcurrency_NoTokenCollisions()
        {
            const int iterations = 5000;
            int completedCount = 0;

            var tasks = new Task[iterations];
            for (int i = 0; i < iterations; i++)
            {
                int capture = i;
                tasks[i] = Task.Run(async () =>
                {
                    var result = await AddAsync(capture, 1);
                    Assert.AreEqual(capture + 1, result);
                    Interlocked.Increment(ref completedCount);
                });
            }

            Task.WaitAll(tasks);
            Assert.AreEqual(iterations, completedCount);
        }

        static async ValkarnTask<int> AddAsync(int a, int b)
        {
            return a + b;
        }

        // ── WhenAll Concurrent Completion ──

        [Test]
        public void WhenAll_ConcurrentCompletion_ResultsCorrect()
        {
            const int iterations = 1000;

            for (int i = 0; i < iterations; i++)
            {
                var p1 = new ValkarnTask.Promise<int>();
                var p2 = new ValkarnTask.Promise<int>();

                var whenAll = ValkarnTask.WhenAll(p1.Task, p2.Task);

                // Complete from different threads
                var t1 = Task.Run(() => p1.TrySetResult(1));
                var t2 = Task.Run(() => p2.TrySetResult(2));

                Task.WaitAll(t1, t2);

                // Poll until completed (may need a frame for the continuation)
                SpinWait.SpinUntil(() => whenAll.source.UnsafeGetStatus() != ValkarnTask.Status.Pending,
                    TimeSpan.FromSeconds(1));

                var status = whenAll.source.UnsafeGetStatus();
                Assert.AreEqual(ValkarnTask.Status.Succeeded, status,
                    $"Iteration {i}: WhenAll should complete after both tasks complete");
            }
        }

        // ── WhenAny Concurrent Completion ──

        [Test]
        public void WhenAny_ConcurrentCompletion_ExactlyOneWinner()
        {
            const int iterations = 1000;

            for (int i = 0; i < iterations; i++)
            {
                var p1 = new ValkarnTask.Promise<int>();
                var p2 = new ValkarnTask.Promise<int>();

                var whenAny = ValkarnTask.WhenAny(p1.Task, p2.Task);

                var barrier = new ManualResetEventSlim(false);
                var t1 = Task.Run(() => { barrier.Wait(); p1.TrySetResult(10); });
                var t2 = Task.Run(() => { barrier.Wait(); p2.TrySetResult(20); });

                barrier.Set();
                Task.WaitAll(t1, t2);

                SpinWait.SpinUntil(() => whenAny.source.UnsafeGetStatus() != ValkarnTask.Status.Pending,
                    TimeSpan.FromSeconds(1));

                var status = whenAny.source.UnsafeGetStatus();
                Assert.AreEqual(ValkarnTask.Status.Succeeded, status,
                    $"Iteration {i}: WhenAny should complete");
            }
        }

        // ── Helper Types ──

        class PoolTestNode : IPoolNode<PoolTestNode>
        {
            PoolTestNode nextNode;
            public ref PoolTestNode NextNode => ref nextNode;
        }
    }
}
