// Copyright (c) Una Partida Mas. All rights reserved.
// Licensed under the MIT License. See LICENSE.md in the repository root.

using System;
using System.Linq;
using NUnit.Framework;

namespace UnaPartidaMas.Valkarn.Tasks.Tests
{
    [TestFixture]
    public class WhenAnyTypedTests
    {
        [SetUp]
        public void SetUp()
        {
            TestHelper.EnsureInitialized();
        }

        [Test]
        public void WhenAny_2Tuple_BothCompleted_FirstWins()
        {
            // Both are sync-completed; the first one encountered wins
            var t1 = ValkarnTask.FromResult(10);
            var t2 = ValkarnTask.FromResult(20);

            var resultTask = ValkarnTask.WhenAny(t1, t2);
            var (winnerIndex, result) = TestHelper.RunSync(resultTask);

            Assert.AreEqual(0, winnerIndex);
            Assert.AreEqual(10, result);
        }

        [Test]
        public void WhenAny_2Tuple_SecondCompleted_FirstPending()
        {
            // First is pending (Never-like), second is completed
            // Note: We can't easily make a typed Never, so we use a Promise
            var promise = new ValkarnTask.Promise<int>();
            var t1 = promise.Task;
            var t2 = ValkarnTask.FromResult(42);

            // WhenAny uses async void Add — with sync-completed t2, it should resolve
            var resultTask = ValkarnTask.WhenAny(t1, t2);

            // The result task should complete because t2 completed
            Assert.IsTrue(resultTask.IsCompleted);
            var (idx, val) = resultTask.GetAwaiter().GetResult();
            Assert.AreEqual(1, idx);
            Assert.AreEqual(42, val);
        }

        [Test]
        public void WhenAny_Enumerable_AllCompleted_FirstWins()
        {
            var tasks = new[]
            {
                ValkarnTask.FromResult(1),
                ValkarnTask.FromResult(2),
                ValkarnTask.FromResult(3)
            };

            var resultTask = ValkarnTask.WhenAny<int>(tasks);
            var (idx, val) = TestHelper.RunSync(resultTask);

            Assert.AreEqual(0, idx);
            Assert.AreEqual(1, val);
        }

        [Test]
        public void WhenAny_Faulted_ReportsException()
        {
            var ex = new InvalidOperationException("err");
            var t1 = ValkarnTask.FromException<int>(ex);
            var t2 = ValkarnTask.FromResult(42);

            var resultTask = ValkarnTask.WhenAny(t1, t2);

            // First task faults — WhenAny should report the exception
            Assert.IsTrue(resultTask.IsCompleted);
            Assert.Throws<InvalidOperationException>(() => resultTask.GetAwaiter().GetResult());
        }

        [Test]
        public void WhenAny_Canceled_ReportsCancellation()
        {
            var t1 = ValkarnTask.FromCanceled<int>();
            var t2 = ValkarnTask.FromResult(42);

            var resultTask = ValkarnTask.WhenAny(t1, t2);

            Assert.IsTrue(resultTask.IsCompleted);
            Assert.Throws<OperationCanceledException>(() => resultTask.GetAwaiter().GetResult());
        }
    }

    [TestFixture]
    public class WhenAnyNonGenericTests
    {
        [SetUp]
        public void SetUp()
        {
            TestHelper.EnsureInitialized();
        }

        [Test]
        public void WhenAny_2Tasks_BothCompleted_FirstWins()
        {
            var resultTask = ValkarnTask.WhenAny(ValkarnTask.CompletedTask, ValkarnTask.CompletedTask);

            Assert.IsTrue(resultTask.IsCompleted);
            var idx = resultTask.GetAwaiter().GetResult();
            Assert.AreEqual(0, idx);
        }

        [Test]
        public void WhenAny_2Tasks_SecondCompleted_FirstPending()
        {
            var promise = new ValkarnTask.Promise();
            var t1 = promise.Task;
            var t2 = ValkarnTask.CompletedTask;

            var resultTask = ValkarnTask.WhenAny(t1, t2);

            Assert.IsTrue(resultTask.IsCompleted);
            Assert.AreEqual(1, resultTask.GetAwaiter().GetResult());
        }

        [Test]
        public void WhenAny_Enumerable_AllCompleted()
        {
            var tasks = new[] { ValkarnTask.CompletedTask, ValkarnTask.CompletedTask, ValkarnTask.CompletedTask };
            var resultTask = ValkarnTask.WhenAny((System.Collections.Generic.IEnumerable<ValkarnTask>)tasks);

            Assert.IsTrue(resultTask.IsCompleted);
            Assert.AreEqual(0, resultTask.GetAwaiter().GetResult());
        }

        [Test]
        public void WhenAny_Faulted_PropagatesException()
        {
            var ex = new Exception("boom");
            var resultTask = ValkarnTask.WhenAny(ValkarnTask.FromException(ex), ValkarnTask.CompletedTask);

            Assert.IsTrue(resultTask.IsCompleted);
            Assert.Throws<Exception>(() => resultTask.GetAwaiter().GetResult());
        }

        [Test]
        public void WhenAny_Canceled_PropagatesCancellation()
        {
            var resultTask = ValkarnTask.WhenAny(ValkarnTask.FromCanceled(), ValkarnTask.CompletedTask);

            Assert.IsTrue(resultTask.IsCompleted);
            Assert.Throws<OperationCanceledException>(() => resultTask.GetAwaiter().GetResult());
        }
    }

    // ════════════════════════════════════════════════════════════════
    //  WhenAny — Pooling, returnGuard, and loser error handling tests
    // ════════════════════════════════════════════════════════════════

    [TestFixture]
    public class WhenAnyPoolingTests
    {
        [SetUp]
        public void SetUp()
        {
            TestHelper.EnsureInitialized();
        }

        [Test]
        public void WhenAny_Typed_2Arity_PoolReuse()
        {
            // Create and consume multiple WhenAny tasks. After the first one is fully
            // consumed (GetResult + all callbacks done), the promise should return to pool
            // and be reused by subsequent calls.
            var promise1 = new ValkarnTask.Promise<int>();
            var t1 = promise1.Task;
            var t2 = ValkarnTask.FromResult(99);

            var resultTask = ValkarnTask.WhenAny(t1, t2);
            Assert.IsTrue(resultTask.IsCompleted);
            var (idx, val) = resultTask.GetAwaiter().GetResult();
            Assert.AreEqual(1, idx);
            Assert.AreEqual(99, val);

            // Complete the loser so returnGuard reaches 0 and promise returns to pool
            promise1.TrySetResult(0);

            // Check pool has an entry for WhenAnyPromise2
            var poolInfo = ValkarnTask.GetPoolInfo().ToList();
            var whenAnyPool = poolInfo.Find(p => p.type == typeof(WhenAnyPromise2<int>));
            Assert.IsTrue(whenAnyPool.size >= 1, "Pool should contain the returned promise");

            // Create another WhenAny — should reuse the pooled promise
            var sizeBefore = whenAnyPool.size;
            var promise2 = new ValkarnTask.Promise<int>();
            var resultTask2 = ValkarnTask.WhenAny(promise2.Task, ValkarnTask.FromResult(77));
            resultTask2.GetAwaiter().GetResult();
            promise2.TrySetResult(0);

            // Pool size should be the same (rented, then returned)
            var afterInfo = ValkarnTask.GetPoolInfo().ToList();
            var afterPool = afterInfo.Find(p => p.type == typeof(WhenAnyPromise2<int>));
            Assert.AreEqual(sizeBefore, afterPool.size);
        }

        [Test]
        public void WhenAny_Void_2Arity_PoolReuse()
        {
            var promise1 = new ValkarnTask.Promise();
            var resultTask = ValkarnTask.WhenAny(promise1.Task, ValkarnTask.CompletedTask);
            Assert.IsTrue(resultTask.IsCompleted);
            resultTask.GetAwaiter().GetResult();

            // Complete loser → returnGuard reaches 0 → pool return
            promise1.TrySetResult();

            var poolInfo = ValkarnTask.GetPoolInfo().ToList();
            var whenAnyPool = poolInfo.Find(p => p.type == typeof(WhenAnyVoidPromise2));
            Assert.IsTrue(whenAnyPool.size >= 1, "Pool should contain the returned promise");
        }

        [Test]
        public void WhenAny_Typed_ReturnGuard_DelaysPoolReturn()
        {
            // With both tasks pending, returnGuard = 3 (2 callbacks + 1 GetResult).
            // After winner completes + GetResult, loser callback hasn't fired yet,
            // so returnGuard > 0 and promise should NOT be in pool yet.
            var promise1 = new ValkarnTask.Promise<int>();
            var promise2 = new ValkarnTask.Promise<int>();

            var resultTask = ValkarnTask.WhenAny(promise1.Task, promise2.Task);
            Assert.IsFalse(resultTask.IsCompleted);

            // Complete task1 (winner)
            promise1.TrySetResult(42);
            Assert.IsTrue(resultTask.IsCompleted);
            var (idx, val) = resultTask.GetAwaiter().GetResult();
            Assert.AreEqual(0, idx);
            Assert.AreEqual(42, val);

            // At this point, returnGuard should be 1 (task2's callback hasn't fired).
            // The promise should NOT be in the pool yet.
            // We can verify indirectly: complete the loser and then check pool.
            var poolBefore = ValkarnTask.GetPoolInfo().ToList()
                .Find(p => p.type == typeof(WhenAnyPromise2<int>));
            int sizeBefore = poolBefore.size;

            // Complete the loser → its callback fires → returnGuard reaches 0 → pool return
            promise2.TrySetResult(99);

            var poolAfter = ValkarnTask.GetPoolInfo().ToList()
                .Find(p => p.type == typeof(WhenAnyPromise2<int>));
            Assert.AreEqual(sizeBefore + 1, poolAfter.size,
                "Promise should return to pool only after loser callback fires");
        }

        [Test]
        public void WhenAny_Typed_LoserFault_PublishesUnobservedException()
        {
            using var collector = new TestHelper.UnobservedExceptionCollector();

            var promise1 = new ValkarnTask.Promise<int>();
            var promise2 = new ValkarnTask.Promise<int>();

            var resultTask = ValkarnTask.WhenAny(promise1.Task, promise2.Task);

            // Task1 wins
            promise1.TrySetResult(10);
            resultTask.GetAwaiter().GetResult();

            // Task2 (loser) faults → exception should go to UnobservedException
            var loserEx = new InvalidOperationException("loser fault");
            promise2.TrySetException(loserEx);

            Assert.AreEqual(1, collector.Exceptions.Count);
            Assert.AreSame(loserEx, collector.Exceptions[0]);
        }

        [Test]
        public void WhenAny_Void_LoserFault_PublishesUnobservedException()
        {
            using var collector = new TestHelper.UnobservedExceptionCollector();

            var promise1 = new ValkarnTask.Promise();
            var promise2 = new ValkarnTask.Promise();

            var resultTask = ValkarnTask.WhenAny(promise1.Task, promise2.Task);

            // Task1 wins
            promise1.TrySetResult();
            resultTask.GetAwaiter().GetResult();

            // Task2 (loser) faults
            var loserEx = new InvalidOperationException("loser fault");
            promise2.TrySetException(loserEx);

            Assert.AreEqual(1, collector.Exceptions.Count);
            Assert.AreSame(loserEx, collector.Exceptions[0]);
        }

        [Test]
        public void WhenAny_LoserCanceled_DoesNotPublishUnobservedException()
        {
            using var collector = new TestHelper.UnobservedExceptionCollector();

            var promise1 = new ValkarnTask.Promise<int>();
            var promise2 = new ValkarnTask.Promise<int>();

            var resultTask = ValkarnTask.WhenAny(promise1.Task, promise2.Task);

            // Task1 wins
            promise1.TrySetResult(10);
            resultTask.GetAwaiter().GetResult();

            // Task2 (loser) cancels — should NOT go to UnobservedException
            promise2.TrySetCanceled();

            Assert.AreEqual(0, collector.Exceptions.Count,
                "Loser cancellation should not be published as unobserved exception");
        }
    }
}
