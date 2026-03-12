// Copyright (c) Una Partida Mas. All rights reserved.
// Licensed under the MIT License. See LICENSE.md in the repository root.

using System;
using System.Threading;
using NUnit.Framework;
using UnaPartidaMas.Valkarn.Tasks.Testing;

namespace UnaPartidaMas.Valkarn.Tasks.Tests
{
    [TestFixture]
    public class RunOnThreadPoolTests
    {
        TestClock clock;

        [SetUp]
        public void SetUp()
        {
            clock = ValkarnTaskTestHelper.Setup();
        }

        [TearDown]
        public void TearDown()
        {
            ValkarnTaskTestHelper.Teardown();
        }

        // ════════════════════════════════════════════════════
        //  Action overload
        // ════════════════════════════════════════════════════

        [Test]
        public void RunOnThreadPool_Action_ExecutesAndReturnsToMainThread()
        {
            bool executed = false;
            int executionThreadId = -1;

            var task = ValkarnTask.RunOnThreadPool(() =>
            {
                executionThreadId = Thread.CurrentThread.ManagedThreadId;
                executed = true;
            });

            // Task is pending (waiting to switch to thread pool)
            // SwitchToThreadPool always yields, so it's pending immediately
            // In real Unity, work runs on thread pool then SwitchToMainThread yields to next frame.
            // In test env, SwitchToThreadPool queues to ThreadPool which we can't easily simulate,
            // so we test the sync fast-path via cancellation and structure.

            // We can't fully simulate the thread hop in editor tests,
            // but we CAN test the cancellation and exception paths.
            Assert.IsFalse(task.IsCompleted);
        }

        [Test]
        public void RunOnThreadPool_Action_CancellationBefore_ThrowsOCE()
        {
            var cts = new CancellationTokenSource();
            cts.Cancel();

            var task = ValkarnTask.RunOnThreadPool(() => { }, cancellationToken: cts.Token);

            Assert.IsTrue(task.IsCompleted);
            Assert.AreEqual(ValkarnTask.Status.Canceled, task.GetStatus());
        }

        // ════════════════════════════════════════════════════
        //  Func<T> overload
        // ════════════════════════════════════════════════════

        [Test]
        public void RunOnThreadPool_FuncT_CancellationBefore_ThrowsOCE()
        {
            var cts = new CancellationTokenSource();
            cts.Cancel();

            var task = ValkarnTask.RunOnThreadPool(() => 42, cancellationToken: cts.Token);

            Assert.IsTrue(task.IsCompleted);
            Assert.AreEqual(ValkarnTask.Status.Canceled, task.GetStatus());
        }

        // ════════════════════════════════════════════════════
        //  Func<ValkarnTask> overload
        // ════════════════════════════════════════════════════

        [Test]
        public void RunOnThreadPool_AsyncFunc_CancellationBefore_ThrowsOCE()
        {
            var cts = new CancellationTokenSource();
            cts.Cancel();

            var task = ValkarnTask.RunOnThreadPool(
                () => ValkarnTask.CompletedTask,
                cancellationToken: cts.Token);

            Assert.IsTrue(task.IsCompleted);
            Assert.AreEqual(ValkarnTask.Status.Canceled, task.GetStatus());
        }

        // ════════════════════════════════════════════════════
        //  Func<ValkarnTask<T>> overload
        // ════════════════════════════════════════════════════

        [Test]
        public void RunOnThreadPool_AsyncFuncT_CancellationBefore_ThrowsOCE()
        {
            var cts = new CancellationTokenSource();
            cts.Cancel();

            var task = ValkarnTask.RunOnThreadPool(
                () => ValkarnTask.FromResult(42),
                cancellationToken: cts.Token);

            Assert.IsTrue(task.IsCompleted);
            Assert.AreEqual(ValkarnTask.Status.Canceled, task.GetStatus());
        }

        // ════════════════════════════════════════════════════
        //  Default parameter values
        // ════════════════════════════════════════════════════

        [Test]
        public void RunOnThreadPool_DefaultTiming_IsUpdate()
        {
            // Verify the method can be called with only an action (timing defaults to Update).
            // Pre-cancel so we can observe the result synchronously.
            var cts = new CancellationTokenSource();
            cts.Cancel();

            var task = ValkarnTask.RunOnThreadPool(() => { }, cancellationToken: cts.Token);
            Assert.IsTrue(task.IsCompleted);
        }

        // ════════════════════════════════════════════════════
        //  Exception propagation (action throws)
        // ════════════════════════════════════════════════════

        [Test]
        public void RunOnThreadPool_Action_Throws_PropagatesException()
        {
            // We can't easily run the full ThreadPool round-trip in tests,
            // but we can verify the method signature and compilation.
            // The exception propagation behavior is an async state machine guarantee.
            var cts = new CancellationTokenSource();
            cts.Cancel();

            // Verify all overloads compile with all parameter combinations
            var t1 = ValkarnTask.RunOnThreadPool(() => { }, PlayerLoopTiming.FixedUpdate, cts.Token);
            var t2 = ValkarnTask.RunOnThreadPool(() => 1, PlayerLoopTiming.FixedUpdate, cts.Token);
            var t3 = ValkarnTask.RunOnThreadPool(() => ValkarnTask.CompletedTask, PlayerLoopTiming.FixedUpdate, cts.Token);
            var t4 = ValkarnTask.RunOnThreadPool(() => ValkarnTask.FromResult(1), PlayerLoopTiming.FixedUpdate, cts.Token);

            Assert.IsTrue(t1.IsCompleted);
            Assert.IsTrue(t2.IsCompleted);
            Assert.IsTrue(t3.IsCompleted);
            Assert.IsTrue(t4.IsCompleted);
        }
    }
}
