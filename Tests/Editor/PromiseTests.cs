// Copyright (c) Una Partida Mas. All rights reserved.
// Licensed under the MIT License. See LICENSE.md in the repository root.

using System;
using System.Threading;
using NUnit.Framework;

namespace UnaPartidaMas.Valkarn.Tasks.Tests
{
    [TestFixture]
    public class PromiseNonGenericTests
    {
        [SetUp]
        public void SetUp()
        {
            TestHelper.EnsureInitialized();
        }

        [Test]
        public void Task_InitiallyPending()
        {
            var promise = new ValkarnTask.Promise();
            var task = promise.Task;
            Assert.IsFalse(task.IsCompleted);
            Assert.AreEqual(ValkarnTask.Status.Pending, task.GetStatus());
        }

        [Test]
        public void TrySetResult_CompletesTask()
        {
            var promise = new ValkarnTask.Promise();
            var task = promise.Task;

            Assert.IsTrue(promise.TrySetResult());
            Assert.IsTrue(task.IsCompleted);
            Assert.AreEqual(ValkarnTask.Status.Succeeded, task.GetStatus());
        }

        [Test]
        public void TrySetResult_DoesNotThrow_OnGetResult()
        {
            var promise = new ValkarnTask.Promise();
            var task = promise.Task;
            promise.TrySetResult();

            Assert.DoesNotThrow(() => task.GetAwaiter().GetResult());
        }

        [Test]
        public void TrySetException_FaultsTask()
        {
            var promise = new ValkarnTask.Promise();
            var task = promise.Task;
            var ex = new InvalidOperationException("fail");

            Assert.IsTrue(promise.TrySetException(ex));
            Assert.IsTrue(task.IsCompleted);
            Assert.AreEqual(ValkarnTask.Status.Faulted, task.GetStatus());

            var thrown = Assert.Throws<InvalidOperationException>(() => task.GetAwaiter().GetResult());
            Assert.AreSame(ex, thrown);
        }

        [Test]
        public void TrySetCanceled_CancelsTask()
        {
            var promise = new ValkarnTask.Promise();
            var task = promise.Task;
            using var cts = new CancellationTokenSource();
            cts.Cancel();

            Assert.IsTrue(promise.TrySetCanceled(cts.Token));
            Assert.IsTrue(task.IsCompleted);
            Assert.AreEqual(ValkarnTask.Status.Canceled, task.GetStatus());
            Assert.Throws<OperationCanceledException>(() => task.GetAwaiter().GetResult());
        }

        [Test]
        public void DoubleCompletion_ReturnsFalse()
        {
            var promise = new ValkarnTask.Promise();
            Assert.IsTrue(promise.TrySetResult());
            Assert.IsFalse(promise.TrySetResult());
            Assert.IsFalse(promise.TrySetException(new Exception()));
            Assert.IsFalse(promise.TrySetCanceled());
        }

        [Test]
        public void OnCompleted_InvokedOnCompletion()
        {
            var promise = new ValkarnTask.Promise();
            var task = promise.Task;
            bool invoked = false;

            task.GetAwaiter().UnsafeOnCompleted(() => invoked = true);
            Assert.IsFalse(invoked);

            promise.TrySetResult();
            Assert.IsTrue(invoked);
        }
    }

    [TestFixture]
    public class PromiseGenericTests
    {
        [SetUp]
        public void SetUp()
        {
            TestHelper.EnsureInitialized();
        }

        [Test]
        public void Task_InitiallyPending()
        {
            var promise = new ValkarnTask.Promise<int>();
            var task = promise.Task;
            Assert.IsFalse(task.IsCompleted);
        }

        [Test]
        public void TrySetResult_CompletesWithValue()
        {
            var promise = new ValkarnTask.Promise<int>();
            var task = promise.Task;

            Assert.IsTrue(promise.TrySetResult(42));
            Assert.IsTrue(task.IsCompleted);
            Assert.AreEqual(42, task.GetAwaiter().GetResult());
        }

        [Test]
        public void TrySetResult_StringValue()
        {
            var promise = new ValkarnTask.Promise<string>();
            var task = promise.Task;

            promise.TrySetResult("hello");
            Assert.AreEqual("hello", task.GetAwaiter().GetResult());
        }

        [Test]
        public void TrySetResult_NullValue()
        {
            var promise = new ValkarnTask.Promise<string>();
            var task = promise.Task;

            promise.TrySetResult(null);
            Assert.IsNull(task.GetAwaiter().GetResult());
        }

        [Test]
        public void TrySetException_FaultsTask()
        {
            var promise = new ValkarnTask.Promise<int>();
            var task = promise.Task;
            var ex = new Exception("test");

            promise.TrySetException(ex);
            Assert.AreEqual(ValkarnTask.Status.Faulted, task.GetStatus());

            var thrown = Assert.Throws<Exception>(() => task.GetAwaiter().GetResult());
            Assert.AreSame(ex, thrown);
        }

        [Test]
        public void TrySetCanceled_CancelsTask()
        {
            var promise = new ValkarnTask.Promise<int>();
            var task = promise.Task;
            using var cts = new CancellationTokenSource();
            cts.Cancel();

            promise.TrySetCanceled(cts.Token);
            Assert.AreEqual(ValkarnTask.Status.Canceled, task.GetStatus());
            Assert.Throws<OperationCanceledException>(() => task.GetAwaiter().GetResult());
        }

        [Test]
        public void DoubleCompletion_ReturnsFalse()
        {
            var promise = new ValkarnTask.Promise<int>();
            Assert.IsTrue(promise.TrySetResult(1));
            Assert.IsFalse(promise.TrySetResult(2));
        }

        [Test]
        public void OnCompleted_InvokedOnCompletion()
        {
            var promise = new ValkarnTask.Promise<int>();
            var task = promise.Task;
            bool invoked = false;
            int receivedValue = 0;

            // Register continuation before completion
            var awaiter = task.GetAwaiter();
            awaiter.UnsafeOnCompleted(() =>
            {
                invoked = true;
                receivedValue = awaiter.GetResult();
            });

            Assert.IsFalse(invoked);
            promise.TrySetResult(99);

            Assert.IsTrue(invoked);
            Assert.AreEqual(99, receivedValue);
        }

        [Test]
        public void OnCompleted_AlreadyCompleted_InvokesImmediately()
        {
            var promise = new ValkarnTask.Promise<int>();
            promise.TrySetResult(42);
            var task = promise.Task;

            bool invoked = false;
            task.GetAwaiter().UnsafeOnCompleted(() => invoked = true);
            Assert.IsTrue(invoked);
        }
    }
}
