// Copyright (c) Una Partida Mas. All rights reserved.
// Licensed under the MIT License. See LICENSE.md in the repository root.

using System;
using System.Threading;
using NUnit.Framework;

namespace UnaPartidaMas.Valkarn.Tasks.Tests
{
    [TestFixture]
    public class PooledPromiseTests
    {
        [SetUp]
        public void SetUp()
        {
            TestHelper.EnsureInitialized();
        }

        [Test]
        public void Create_ReturnsPendingSource()
        {
            var source = ValkarnTask.PooledPromise<int>.Create(out var token);
            Assert.AreEqual(ValkarnTask.Status.Pending, source.GetStatus(token));
        }

        [Test]
        public void TrySetResult_CompletesSource()
        {
            var source = ValkarnTask.PooledPromise<int>.Create(out var token);
            Assert.IsTrue(source.TrySetResult(42));
            Assert.AreEqual(ValkarnTask.Status.Succeeded, source.GetStatus(token));
        }

        [Test]
        public void GetResult_ReturnsValue_AndReturnsToPool()
        {
            var source = ValkarnTask.PooledPromise<int>.Create(out var token);
            source.TrySetResult(42);

            // GetResult via ISource<T> should return value and auto-return to pool
            var result = ((ValkarnTask.ISource<int>)source).GetResult(token);
            Assert.AreEqual(42, result);

            // After GetResult, the source is returned to pool and reset.
            // Using the old token should now fail.
            Assert.Throws<InvalidOperationException>(() => source.GetStatus(token));
        }

        [Test]
        public void CreateCompleted_ReturnsCompletedSource()
        {
            var source = ValkarnTask.PooledPromise<string>.CreateCompleted("hello", out var token);
            Assert.AreEqual(ValkarnTask.Status.Succeeded, source.GetStatus(token));
        }

        [Test]
        public void TrySetException_FaultsSource()
        {
            var source = ValkarnTask.PooledPromise<int>.Create(out var token);
            var ex = new Exception("test");
            Assert.IsTrue(source.TrySetException(ex));
            Assert.AreEqual(ValkarnTask.Status.Faulted, source.GetStatus(token));
        }

        [Test]
        public void TrySetCanceled_CancelsSource()
        {
            var source = ValkarnTask.PooledPromise<int>.Create(out var token);
            Assert.IsTrue(source.TrySetCanceled());
            Assert.AreEqual(ValkarnTask.Status.Canceled, source.GetStatus(token));
        }

        [Test]
        public void DoubleCompletion_ReturnsFalse()
        {
            var source = ValkarnTask.PooledPromise<int>.Create(out var token);
            Assert.IsTrue(source.TrySetResult(1));
            Assert.IsFalse(source.TrySetResult(2));
        }

        [Test]
        public void Pooling_ReusesInstances()
        {
            // Create and consume two pooled promises — the second Create should reuse the first
            var s1 = ValkarnTask.PooledPromise<int>.Create(out var t1);
            s1.TrySetResult(1);
            ((ValkarnTask.ISource<int>)s1).GetResult(t1); // returns to pool

            var s2 = ValkarnTask.PooledPromise<int>.Create(out var t2);
            // s2 might be the same instance as s1 (reused from pool)
            // The token should be different (generation incremented)
            if (ReferenceEquals(s1, s2))
            {
                Assert.AreNotEqual(t1, t2, "Reused instance should have a different token (new generation)");
            }

            // Either way, it should work correctly
            s2.TrySetResult(99);
            Assert.AreEqual(99, ((ValkarnTask.ISource<int>)s2).GetResult(t2));
        }

        [Test]
        public void OnCompleted_InvokedOnCompletion()
        {
            var source = ValkarnTask.PooledPromise<int>.Create(out var token);
            bool invoked = false;

            source.OnCompleted(
                (state) => { invoked = true; },
                null,
                token);

            Assert.IsFalse(invoked);
            source.TrySetResult(42);
            Assert.IsTrue(invoked);
        }
    }
}
