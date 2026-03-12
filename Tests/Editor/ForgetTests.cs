// Copyright (c) Una Partida Mas. All rights reserved.
// Licensed under the MIT License. See LICENSE.md in the repository root.

using System;
using NUnit.Framework;

namespace UnaPartidaMas.Valkarn.Tasks.Tests
{
    [TestFixture]
    public class ForgetTests
    {
        [SetUp]
        public void SetUp()
        {
            TestHelper.EnsureInitialized();
        }

        // ── Non-generic ──

        [Test]
        public void Forget_CompletedTask_NoOp()
        {
            // Should not throw
            Assert.DoesNotThrow(() => ValkarnTask.CompletedTask.Forget());
        }

        [Test]
        public void Forget_FaultedTask_PublishesUnobservedException()
        {
            using var collector = new TestHelper.UnobservedExceptionCollector();

            var ex = new InvalidOperationException("discard error");
            var task = ValkarnTask.FromException(ex);
            task.Forget();

            Assert.AreEqual(1, collector.Exceptions.Count);
            Assert.AreSame(ex, collector.Exceptions[0]);
        }

        [Test]
        public void Forget_CanceledTask_DoesNotPublishException()
        {
            using var collector = new TestHelper.UnobservedExceptionCollector();

            var task = ValkarnTask.FromCanceled();
            task.Forget();

            Assert.AreEqual(0, collector.Exceptions.Count);
        }

        [Test]
        public void Forget_PendingTask_ObservesOnCompletion()
        {
            using var collector = new TestHelper.UnobservedExceptionCollector();

            var promise = new ValkarnTask.Promise();
            var task = promise.Task;
            task.Forget();

            Assert.AreEqual(0, collector.Exceptions.Count, "No exception yet — task still pending");

            // Complete with success — should not raise
            promise.TrySetResult();
            // Note: Discard registered on the ISource; for success, no exception expected.
            Assert.AreEqual(0, collector.Exceptions.Count);
        }

        // ── Generic ──

        [Test]
        public void Forget_Generic_CompletedTask_NoOp()
        {
            Assert.DoesNotThrow(() => ValkarnTask.FromResult(42).Forget());
        }

        [Test]
        public void Forget_Generic_FaultedTask_PublishesUnobservedException()
        {
            using var collector = new TestHelper.UnobservedExceptionCollector();

            var ex = new Exception("generic discard");
            var task = ValkarnTask.FromException<int>(ex);
            task.Forget();

            Assert.AreEqual(1, collector.Exceptions.Count);
            Assert.AreSame(ex, collector.Exceptions[0]);
        }

        [Test]
        public void Forget_Generic_CanceledTask_DoesNotPublishException()
        {
            using var collector = new TestHelper.UnobservedExceptionCollector();

            var task = ValkarnTask.FromCanceled<int>();
            task.Forget();

            Assert.AreEqual(0, collector.Exceptions.Count);
        }
    }
}
