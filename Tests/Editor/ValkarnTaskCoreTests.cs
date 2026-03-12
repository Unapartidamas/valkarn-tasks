// Copyright (c) Una Partida Mas. All rights reserved.
// Licensed under the MIT License. See LICENSE.md in the repository root.

using System;
using NUnit.Framework;

namespace UnaPartidaMas.Valkarn.Tasks.Tests
{
    [TestFixture]
    public class ValkarnTaskCoreTests
    {
        [SetUp]
        public void SetUp()
        {
            TestHelper.EnsureInitialized();
        }

        // ── CompletedTask ──

        [Test]
        public void CompletedTask_IsCompleted()
        {
            Assert.IsTrue(ValkarnTask.CompletedTask.IsCompleted);
        }

        [Test]
        public void CompletedTask_StatusIsSucceeded()
        {
            Assert.AreEqual(ValkarnTask.Status.Succeeded, ValkarnTask.CompletedTask.GetStatus());
        }

        [Test]
        public void CompletedTask_SourceIsNull()
        {
            Assert.IsNull(ValkarnTask.CompletedTask.source);
        }

        [Test]
        public void CompletedTask_GetResult_DoesNotThrow()
        {
            Assert.DoesNotThrow(() => ValkarnTask.CompletedTask.GetAwaiter().GetResult());
        }

        [Test]
        public void CompletedTask_Awaiter_IsCompleted()
        {
            Assert.IsTrue(ValkarnTask.CompletedTask.GetAwaiter().IsCompleted);
        }

        // ── FromResult<T> ──

        [Test]
        public void FromResult_Int_IsCompleted()
        {
            var task = ValkarnTask.FromResult(42);
            Assert.IsTrue(task.IsCompleted);
        }

        [Test]
        public void FromResult_Int_ReturnsValue()
        {
            var task = ValkarnTask.FromResult(42);
            Assert.AreEqual(42, task.GetAwaiter().GetResult());
        }

        [Test]
        public void FromResult_SourceIsNull()
        {
            var task = ValkarnTask.FromResult("hello");
            Assert.IsNull(task.source);
        }

        [Test]
        public void FromResult_Null_IsValid()
        {
            var task = ValkarnTask.FromResult<string>(null);
            Assert.IsTrue(task.IsCompleted);
            Assert.IsNull(task.GetAwaiter().GetResult());
        }

        [Test]
        public void FromResult_Status_IsSucceeded()
        {
            var task = ValkarnTask.FromResult(1.5f);
            Assert.AreEqual(ValkarnTask.Status.Succeeded, task.GetStatus());
        }

        // ── Default ValkarnTask{T} ──

        [Test]
        public void Default_ValkarnTaskT_IsCompleted_WithDefaultValue()
        {
            ValkarnTask<int> task = default;
            Assert.IsTrue(task.IsCompleted);
            Assert.AreEqual(0, task.GetAwaiter().GetResult());
        }

        // ── AsNonGeneric ──

        [Test]
        public void AsNonGeneric_PreservesCompletionStatus()
        {
            var typed = ValkarnTask.FromResult(42);
            var untyped = typed.AsNonGeneric();

            Assert.IsTrue(untyped.IsCompleted);
            Assert.AreEqual(ValkarnTask.Status.Succeeded, untyped.GetStatus());
        }

        // ── Status enum extension ──

        [Test]
        public void StatusIsCompleted_Pending_ReturnsFalse()
        {
            Assert.IsFalse(ValkarnTask.Status.Pending.IsCompleted());
        }

        [Test]
        public void StatusIsCompleted_Succeeded_ReturnsTrue()
        {
            Assert.IsTrue(ValkarnTask.Status.Succeeded.IsCompleted());
        }

        [Test]
        public void StatusIsCompleted_Faulted_ReturnsTrue()
        {
            Assert.IsTrue(ValkarnTask.Status.Faulted.IsCompleted());
        }

        [Test]
        public void StatusIsCompleted_Canceled_ReturnsTrue()
        {
            Assert.IsTrue(ValkarnTask.Status.Canceled.IsCompleted());
        }

        // ── Pool config statics ──

        [Test]
        public void DefaultMaxPoolSize_Default_Is256()
        {
            Assert.AreEqual(256, ValkarnTask.DefaultMaxPoolSize);
        }

        [Test]
        public void TrimCheckInterval_Default_Is300()
        {
            Assert.AreEqual(300, ValkarnTask.TrimCheckInterval);
        }

        [Test]
        public void MinPoolSize_Default_Is8()
        {
            Assert.AreEqual(8, ValkarnTask.MinPoolSize);
        }

        [Test]
        public void DefaultMaxPoolSize_ClampedToMinimum1()
        {
            int old = ValkarnTask.DefaultMaxPoolSize;
            try
            {
                ValkarnTask.DefaultMaxPoolSize = 0;
                Assert.AreEqual(1, ValkarnTask.DefaultMaxPoolSize);

                ValkarnTask.DefaultMaxPoolSize = -10;
                Assert.AreEqual(1, ValkarnTask.DefaultMaxPoolSize);
            }
            finally
            {
                ValkarnTask.DefaultMaxPoolSize = old;
            }
        }

        // ── UnobservedException event ──

        [Test]
        public void PublishUnobservedException_InvokesHandler()
        {
            using var collector = new TestHelper.UnobservedExceptionCollector();

            var ex = new Exception("test");
            ValkarnTask.PublishUnobservedException(ex);

            Assert.AreEqual(1, collector.Exceptions.Count);
            Assert.AreSame(ex, collector.Exceptions[0]);
        }

        [Test]
        public void PublishUnobservedException_NoHandler_DoesNotThrow()
        {
            // No handlers registered at this point (collector not created)
            Assert.DoesNotThrow(() => ValkarnTask.PublishUnobservedException(new Exception()));
        }

        // ── Awaiter OnCompleted for sync tasks ──

        [Test]
        public void Awaiter_OnCompleted_SyncCompleted_InvokesImmediately()
        {
            bool invoked = false;
            ValkarnTask.CompletedTask.GetAwaiter().OnCompleted(() => invoked = true);
            Assert.IsTrue(invoked);
        }

        [Test]
        public void Awaiter_UnsafeOnCompleted_SyncCompleted_InvokesImmediately()
        {
            bool invoked = false;
            ValkarnTask.CompletedTask.GetAwaiter().UnsafeOnCompleted(() => invoked = true);
            Assert.IsTrue(invoked);
        }

        [Test]
        public void TypedAwaiter_OnCompleted_SyncCompleted_InvokesImmediately()
        {
            bool invoked = false;
            ValkarnTask.FromResult(42).GetAwaiter().OnCompleted(() => invoked = true);
            Assert.IsTrue(invoked);
        }
    }
}
