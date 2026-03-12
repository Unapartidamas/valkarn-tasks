// Copyright (c) Una Partida Mas. All rights reserved.
// Licensed under the MIT License. See LICENSE.md in the repository root.

using System;
using System.Threading;
using NUnit.Framework;

namespace UnaPartidaMas.Valkarn.Tasks.Tests
{
    [TestFixture]
    public class ValkarnCompletionCoreTests
    {
        ValkarnCompletionCore<int> core;
        uint token;

        [SetUp]
        public void SetUp()
        {
            TestHelper.EnsureInitialized();
            core = default;
            token = core.Token;
        }

        // ── Status ──

        [Test]
        public void InitialStatus_IsPending()
        {
            Assert.AreEqual(ValkarnTask.Status.Pending, core.GetStatus(token));
            Assert.AreEqual(ValkarnTask.Status.Pending, core.UnsafeGetStatus());
        }

        [Test]
        public void TrySetResult_ChangesStatusToSucceeded()
        {
            Assert.IsTrue(core.TrySetResult(42));
            Assert.AreEqual(ValkarnTask.Status.Succeeded, core.GetStatus(token));
        }

        [Test]
        public void TrySetException_ChangesStatusToFaulted()
        {
            Assert.IsTrue(core.TrySetException(new Exception("test")));
            Assert.AreEqual(ValkarnTask.Status.Faulted, core.GetStatus(token));
        }

        [Test]
        public void TrySetCanceled_ChangesStatusToCanceled()
        {
            Assert.IsTrue(core.TrySetCanceled());
            Assert.AreEqual(ValkarnTask.Status.Canceled, core.GetStatus(token));
        }

        // ── Double completion ──

        [Test]
        public void TrySetResult_SecondCall_ReturnsFalse()
        {
            Assert.IsTrue(core.TrySetResult(1));
            Assert.IsFalse(core.TrySetResult(2));
            Assert.AreEqual(1, core.GetResult(token));
        }

        [Test]
        public void TrySetException_AfterResult_ReturnsFalse()
        {
            core.TrySetResult(1);
            Assert.IsFalse(core.TrySetException(new Exception()));
            Assert.AreEqual(ValkarnTask.Status.Succeeded, core.GetStatus(token));
        }

        [Test]
        public void TrySetCanceled_AfterResult_ReturnsFalse()
        {
            core.TrySetResult(1);
            Assert.IsFalse(core.TrySetCanceled());
            Assert.AreEqual(ValkarnTask.Status.Succeeded, core.GetStatus(token));
        }

        // ── GetResult ──

        [Test]
        public void GetResult_Success_ReturnsValue()
        {
            core.TrySetResult(42);
            Assert.AreEqual(42, core.GetResult(token));
        }

        [Test]
        public void GetResult_Faulted_ThrowsSameException()
        {
            var ex = new InvalidOperationException("test error");
            core.TrySetException(ex);

            var thrown = Assert.Throws<InvalidOperationException>(() => core.GetResult(token));
            Assert.AreSame(ex, thrown);
        }

        [Test]
        public void GetResult_Canceled_ThrowsOperationCanceled()
        {
            using var cts = new CancellationTokenSource();
            cts.Cancel();
            core.TrySetCanceled(cts.Token);

            var thrown = Assert.Throws<OperationCanceledException>(() => core.GetResult(token));
            Assert.AreEqual(cts.Token, thrown.CancellationToken);
        }

        [Test]
        public void GetResult_Pending_Throws()
        {
            Assert.Throws<InvalidOperationException>(() => core.GetResult(token));
        }

        // ── Token validation ──

        [Test]
        public void GetStatus_InvalidToken_Throws()
        {
            uint badToken = 999;
            Assert.Throws<InvalidOperationException>(() => core.GetStatus(badToken));
        }

        [Test]
        public void GetResult_StaleToken_Throws()
        {
            core.TrySetResult(1);
            core.GetResult(token); // consume
            core.Reset();
            // After reset, generation has incremented — old token is stale
            Assert.Throws<InvalidOperationException>(() => core.GetResult(token));
        }

        // ── OnCompleted ──

        [Test]
        public void OnCompleted_BeforeCompletion_ContinuationInvokedOnSetResult()
        {
            bool invoked = false;
            object receivedState = null;

            core.OnCompleted(
                (state) => { invoked = true; receivedState = state; },
                "my-state",
                token);

            Assert.IsFalse(invoked);

            core.TrySetResult(42);

            Assert.IsTrue(invoked);
            Assert.AreEqual("my-state", receivedState);
        }

        [Test]
        public void OnCompleted_AfterCompletion_ContinuationInvokedImmediately()
        {
            core.TrySetResult(42);

            bool invoked = false;
            core.OnCompleted(
                (state) => { invoked = true; },
                null,
                token);

            Assert.IsTrue(invoked, "Continuation should be invoked inline when task is already complete.");
        }

        [Test]
        public void OnCompleted_InvalidToken_Throws()
        {
            uint badToken = 999;
            Assert.Throws<InvalidOperationException>(() =>
                core.OnCompleted((_) => { }, null, badToken));
        }

        // ── Reset ──

        [Test]
        public void Reset_IncreasesGeneration()
        {
            var gen0 = core.Token;
            core.TrySetResult(1);
            core.GetResult(token);
            core.Reset();
            var gen1 = core.Token;

            Assert.AreEqual(gen0 + 1, gen1);
        }

        [Test]
        public void Reset_ResetsStatus()
        {
            core.TrySetResult(1);
            core.GetResult(token);
            core.Reset();

            var newToken = core.Token;
            Assert.AreEqual(ValkarnTask.Status.Pending, core.GetStatus(newToken));
        }

        [Test]
        public void Reset_AllowsReuse()
        {
            core.TrySetResult(1);
            core.GetResult(token);
            core.Reset();

            var newToken = core.Token;
            Assert.IsTrue(core.TrySetResult(99));
            Assert.AreEqual(99, core.GetResult(newToken));
        }

        [Test]
        public void Reset_PublishesUnobservedException_WhenErrorNotConsumed()
        {
            using var collector = new TestHelper.UnobservedExceptionCollector();

            core.TrySetException(new InvalidOperationException("unobserved"));
            // Don't call GetResult — error remains unhandled
            core.Reset();

            Assert.AreEqual(1, collector.Exceptions.Count);
            Assert.IsInstanceOf<InvalidOperationException>(collector.Exceptions[0]);
        }

        [Test]
        public void Reset_DoesNotPublishException_WhenErrorConsumed()
        {
            using var collector = new TestHelper.UnobservedExceptionCollector();

            core.TrySetException(new InvalidOperationException("observed"));
            try { core.GetResult(token); } catch { /* consumed */ }
            core.Reset();

            Assert.AreEqual(0, collector.Exceptions.Count);
        }

        // ── Sentinel race ──

        [Test]
        public void Sentinel_CompletedSentinel_IsNotNull()
        {
            Assert.IsNotNull(ContinuationSentinel.CompletedSentinel);
        }

    }
}
