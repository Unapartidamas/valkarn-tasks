// Copyright (c) Una Partida Mas. All rights reserved.
// Licensed under the MIT License. See LICENSE.md in the repository root.

using System;
using System.Threading;
using NUnit.Framework;

namespace UnaPartidaMas.Valkarn.Tasks.Tests
{
    /// <summary>
    /// End-to-end tests for the async ValkarnTask pipeline.
    /// These verify that the full chain works: method builder → runner → completion core → awaiter.
    /// </summary>
    [TestFixture]
    public class AsyncPipelineTests
    {
        [SetUp]
        public void SetUp()
        {
            TestHelper.EnsureInitialized();
        }

        // ── Sync fast path (no suspension) ──

        [Test]
        public void AsyncMethod_NoAwait_CompletesSync()
        {
            var task = ReturnValueNoAwait();
            Assert.IsTrue(task.IsCompleted);
            Assert.AreEqual(42, task.GetAwaiter().GetResult());
        }

        static async ValkarnTask<int> ReturnValueNoAwait()
        {
            return 42;
        }

        [Test]
        public void AsyncVoidMethod_NoAwait_CompletesSync()
        {
            var task = DoNothingAsync();
            Assert.IsTrue(task.IsCompleted);
            Assert.DoesNotThrow(() => task.GetAwaiter().GetResult());
        }

        static async ValkarnTask DoNothingAsync()
        {
            // No await — completes synchronously
        }

        // ── Sync fast path with completed await ──

        [Test]
        public void AsyncMethod_AwaitCompletedTask_CompletesSync()
        {
            var task = AwaitCompletedTask();
            Assert.IsTrue(task.IsCompleted);
            task.GetAwaiter().GetResult();
        }

        static async ValkarnTask AwaitCompletedTask()
        {
            await ValkarnTask.CompletedTask;
        }

        [Test]
        public void AsyncMethod_AwaitFromResult_CompletesSync()
        {
            var task = AwaitFromResult();
            Assert.IsTrue(task.IsCompleted);
            Assert.AreEqual(99, task.GetAwaiter().GetResult());
        }

        static async ValkarnTask<int> AwaitFromResult()
        {
            var x = await ValkarnTask.FromResult(99);
            return x;
        }

        // ── Chained awaits ──

        [Test]
        public void AsyncMethod_ChainedAwaits_CompletesSync()
        {
            var task = ChainedAwaits();
            Assert.IsTrue(task.IsCompleted);
            Assert.AreEqual(30, task.GetAwaiter().GetResult());
        }

        static async ValkarnTask<int> ChainedAwaits()
        {
            var a = await ValkarnTask.FromResult(10);
            var b = await ValkarnTask.FromResult(20);
            return a + b;
        }

        // ── Exception propagation ──

        [Test]
        public void AsyncMethod_ThrowsException_PropagatesOnGetResult()
        {
            var task = ThrowingAsync();
            Assert.IsTrue(task.IsCompleted);
            Assert.AreEqual(ValkarnTask.Status.Faulted, task.GetStatus());

            var thrown = Assert.Throws<InvalidOperationException>(() => task.GetAwaiter().GetResult());
            Assert.AreEqual("test error", thrown.Message);
        }

        static async ValkarnTask ThrowingAsync()
        {
            await ValkarnTask.CompletedTask;
            throw new InvalidOperationException("test error");
        }

        [Test]
        public void AsyncMethod_ThrowsException_Generic_PropagatesOnGetResult()
        {
            var task = ThrowingAsyncGeneric();
            Assert.IsTrue(task.IsCompleted);
            Assert.AreEqual(ValkarnTask.Status.Faulted, task.GetStatus());

            var thrown = Assert.Throws<InvalidOperationException>(() => task.GetAwaiter().GetResult());
            Assert.AreEqual("typed error", thrown.Message);
        }

        static async ValkarnTask<int> ThrowingAsyncGeneric()
        {
            await ValkarnTask.CompletedTask;
            throw new InvalidOperationException("typed error");
        }

        // ── Cancellation propagation ──

        [Test]
        public void AsyncMethod_AwaitCanceled_PropagatesCancellation()
        {
            var task = AwaitCanceled();
            Assert.IsTrue(task.IsCompleted);
            // OperationCanceledException is detected by ValkarnTaskCompletionCore → status is Canceled
            Assert.AreEqual(ValkarnTask.Status.Canceled, task.GetStatus());
            Assert.Throws<OperationCanceledException>(() => task.GetAwaiter().GetResult());
        }

        static async ValkarnTask AwaitCanceled()
        {
            await ValkarnTask.FromCanceled();
        }

        // ── Await ValkarnTask<T> inside async ValkarnTask ──

        [Test]
        public void AsyncMethod_AwaitGenericInsideVoid_Works()
        {
            int captured = 0;
            var task = CaptureGenericResult(v => captured = v);
            Assert.IsTrue(task.IsCompleted);
            Assert.AreEqual(42, captured);
        }

        static async ValkarnTask CaptureGenericResult(Action<int> capture)
        {
            var value = await ValkarnTask.FromResult(42);
            capture(value);
        }

        // ── Multiple sequential awaits ──

        [Test]
        public void AsyncMethod_MultipleAwaits_AllSync()
        {
            var task = MultipleAwaits();
            Assert.IsTrue(task.IsCompleted);
            Assert.AreEqual("abc", task.GetAwaiter().GetResult());
        }

        static async ValkarnTask<string> MultipleAwaits()
        {
            var a = await ValkarnTask.FromResult("a");
            var b = await ValkarnTask.FromResult("b");
            var c = await ValkarnTask.FromResult("c");
            return a + b + c;
        }

        // ── Promise-driven async (actual suspension) ──

        [Test]
        public void AsyncMethod_WithPromise_SuspendsAndResumes()
        {
            var promise = new ValkarnTask.Promise<int>();
            var task = AwaitPromise(promise);

            // Task should be pending because the promise is not yet completed
            Assert.IsFalse(task.IsCompleted);
            Assert.AreEqual(ValkarnTask.Status.Pending, task.GetStatus());

            // Complete the promise — this should resume the async method
            promise.TrySetResult(42);

            // Now the task should be completed
            Assert.IsTrue(task.IsCompleted);
            Assert.AreEqual(42, task.GetAwaiter().GetResult());
        }

        static async ValkarnTask<int> AwaitPromise(ValkarnTask.Promise<int> promise)
        {
            return await promise.Task;
        }

        [Test]
        public void AsyncMethod_WithPromise_ExceptionResume()
        {
            var promise = new ValkarnTask.Promise<int>();
            var task = AwaitPromise(promise);

            Assert.IsFalse(task.IsCompleted);

            promise.TrySetException(new InvalidOperationException("async error"));

            Assert.IsTrue(task.IsCompleted);
            Assert.AreEqual(ValkarnTask.Status.Faulted, task.GetStatus());
            Assert.Throws<InvalidOperationException>(() => task.GetAwaiter().GetResult());
        }

        [Test]
        public void AsyncMethod_WithPromise_CancellationResume()
        {
            var promise = new ValkarnTask.Promise<int>();
            var task = AwaitPromise(promise);

            Assert.IsFalse(task.IsCompleted);

            promise.TrySetCanceled();

            Assert.IsTrue(task.IsCompleted);
            Assert.Throws<OperationCanceledException>(() => task.GetAwaiter().GetResult());
        }

        // ── Nested async calls ──

        [Test]
        public void AsyncMethod_NestedCalls_Work()
        {
            var task = OuterAsync();
            Assert.IsTrue(task.IsCompleted);
            Assert.AreEqual(100, task.GetAwaiter().GetResult());
        }

        static async ValkarnTask<int> OuterAsync()
        {
            var inner = await InnerAsync(50);
            return inner * 2;
        }

        static async ValkarnTask<int> InnerAsync(int x)
        {
            return await ValkarnTask.FromResult(x);
        }

        // ── AsResult inside async method ──

        [Test]
        public void AsyncMethod_AsResult_CatchesException()
        {
            var task = AsResultCatchAsync();
            Assert.IsTrue(task.IsCompleted);
            var result = task.GetAwaiter().GetResult();
            Assert.IsTrue(result.IsFaulted);
        }

        static async ValkarnTask<Result<int>> AsResultCatchAsync()
        {
            var faulted = ValkarnTask.FromException<int>(new Exception("fail"));
            return await faulted.AsResult();
        }

        // ── WhenAll inside async method ──

        [Test]
        public void AsyncMethod_WhenAll_Works()
        {
            var task = WhenAllAsync();
            Assert.IsTrue(task.IsCompleted);
            var (r1, r2) = task.GetAwaiter().GetResult();
            Assert.AreEqual(10, r1);
            Assert.AreEqual(20, r2);
        }

        static async ValkarnTask<(int, int)> WhenAllAsync()
        {
            return await ValkarnTask.WhenAll(
                ValkarnTask.FromResult(10),
                ValkarnTask.FromResult(20));
        }
    }
}
