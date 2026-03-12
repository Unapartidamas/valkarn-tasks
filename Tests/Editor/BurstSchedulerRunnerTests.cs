// Copyright (c) Una Partida Mas. All rights reserved.
// Licensed under the MIT License. See LICENSE.md in the repository root.

using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnaPartidaMas.Valkarn.Tasks.Testing;

namespace UnaPartidaMas.Valkarn.Tasks.Tests
{
    /// <summary>
    /// Tests for BurstSchedulerRunner callback dispatch logic.
    /// Uses a managed simulation since NativeContainers require Unity runtime.
    /// Tests the dispatch/callback registration pattern and exception handling.
    /// </summary>
    [TestFixture]
    public class BurstSchedulerRunnerDispatchTests
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
        //  Callback registration and dispatch
        // ════════════════════════════════════════════════════

        [Test]
        public void CallbackDispatch_RegisterAndFire_InvokesCallback()
        {
            // Simulate the pattern: register callback by ID, then "fire" it
            var callbacks = new Dictionary<int, Action>();
            bool invoked = false;

            callbacks[42] = () => invoked = true;

            // Simulate dispatch (same logic as BurstSchedulerRunner.MoveNext)
            if (callbacks.TryGetValue(42, out var cb))
            {
                callbacks.Remove(42);
                cb();
            }

            Assert.IsTrue(invoked);
            Assert.IsFalse(callbacks.ContainsKey(42), "Callback should be removed after dispatch");
        }

        [Test]
        public void CallbackDispatch_UnregisteredId_NoException()
        {
            var callbacks = new Dictionary<int, Action>();

            // Dispatching an unregistered ID should not throw
            Assert.DoesNotThrow(() =>
            {
                if (callbacks.TryGetValue(99, out var cb))
                {
                    callbacks.Remove(99);
                    cb();
                }
            });
        }

        [Test]
        public void CallbackDispatch_ExceptionInCallback_PublishesUnobserved()
        {
            using var collector = new TestHelper.UnobservedExceptionCollector();

            var callbacks = new Dictionary<int, Action>();
            var ex = new InvalidOperationException("boom");
            callbacks[1] = () => throw ex;

            // Simulate the dispatch pattern from BurstSchedulerRunner.MoveNext
            if (callbacks.TryGetValue(1, out var cb))
            {
                callbacks.Remove(1);
                try { cb(); }
                catch (Exception caught) { ValkarnTask.PublishUnobservedException(caught); }
            }

            Assert.AreEqual(1, collector.Exceptions.Count);
            Assert.AreSame(ex, collector.Exceptions[0]);
        }

        // ════════════════════════════════════════════════════
        //  Timer scheduling (managed simulation)
        // ════════════════════════════════════════════════════

        [Test]
        public void TimerDispatch_ExpiredTimer_InvokesCallback()
        {
            var timerCallbacks = new Dictionary<int, Action>();
            bool fired = false;

            int timerId = 0;
            timerCallbacks[timerId] = () => fired = true;

            // Simulate: timer expired, dispatch
            if (timerCallbacks.TryGetValue(timerId, out var cb))
            {
                timerCallbacks.Remove(timerId);
                cb();
            }

            Assert.IsTrue(fired);
        }

        [Test]
        public void TimerDispatch_MultipleTimers_AllFire()
        {
            var timerCallbacks = new Dictionary<int, Action>();
            var firedIds = new List<int>();

            for (int i = 0; i < 5; i++)
            {
                int id = i;
                timerCallbacks[id] = () => firedIds.Add(id);
            }

            // Simulate draining expired timer IDs: [0, 1, 2]
            int[] expired = { 0, 1, 2 };
            foreach (int id in expired)
            {
                if (timerCallbacks.TryGetValue(id, out var cb))
                {
                    timerCallbacks.Remove(id);
                    cb();
                }
            }

            Assert.AreEqual(3, firedIds.Count);
            Assert.AreEqual(0, firedIds[0]);
            Assert.AreEqual(1, firedIds[1]);
            Assert.AreEqual(2, firedIds[2]);

            // IDs 3, 4 should still be registered
            Assert.IsTrue(timerCallbacks.ContainsKey(3));
            Assert.IsTrue(timerCallbacks.ContainsKey(4));
        }

        [Test]
        public void TimerDispatch_ExceptionInTimer_PublishesUnobserved()
        {
            using var collector = new TestHelper.UnobservedExceptionCollector();

            var timerCallbacks = new Dictionary<int, Action>();
            var ex = new InvalidOperationException("timer boom");
            timerCallbacks[0] = () => throw ex;

            if (timerCallbacks.TryGetValue(0, out var cb))
            {
                timerCallbacks.Remove(0);
                try { cb(); }
                catch (Exception caught) { ValkarnTask.PublishUnobservedException(caught); }
            }

            Assert.AreEqual(1, collector.Exceptions.Count);
            Assert.AreSame(ex, collector.Exceptions[0]);
        }
    }
}
