// Copyright (c) Una Partida Mas. All rights reserved.
// Licensed under the MIT License. See LICENSE.md in the repository root.

using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnaPartidaMas.Valkarn.Tasks.Burst;
using UnaPartidaMas.Valkarn.Tasks.Testing;

namespace UnaPartidaMas.Valkarn.Tasks.Tests.Burst
{
    /// <summary>
    /// Integration tests for <see cref="BurstSchedulerRunner"/>
    /// exercising real native containers and PlayerLoop dispatch.
    /// </summary>
    [TestFixture]
    public class BurstSchedulerRunnerIntegrationTests
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

        /// <summary>
        /// Collects exceptions published via <see cref="ValkarnTask.UnobservedException"/>.
        /// </summary>
        sealed class UnobservedExceptionCollector : IDisposable
        {
            readonly Action<Exception> handler;
            public List<Exception> Exceptions { get; } = new();

            public UnobservedExceptionCollector()
            {
                handler = ex => Exceptions.Add(ex);
                ValkarnTask.UnobservedException += handler;
            }

            public void Dispose()
            {
                ValkarnTask.UnobservedException -= handler;
            }
        }

        // ════════════════════════════════════════════════════
        //  Create + Dispose → no native leaks
        // ════════════════════════════════════════════════════

        [Test]
        public void CreateAndDispose_NoNativeLeaks()
        {
            var runner = BurstSchedulerRunner.Create();
            Assert.DoesNotThrow(() => runner.Dispose());
        }

        // ════════════════════════════════════════════════════
        //  RegisterCallback + Enqueue + MoveNext → fires
        // ════════════════════════════════════════════════════

        [Test]
        public void RegisterCallback_Enqueue_MoveNext_CallbackFires()
        {
            var runner = BurstSchedulerRunner.Create();
            try
            {
                bool fired = false;
                runner.RegisterCallback(42, () => fired = true);
                runner.Scheduler.Enqueue(new ScheduledWork { Id = 42, Type = WorkType.Custom });

                clock.AdvanceFrame();

                Assert.IsTrue(fired);
            }
            finally
            {
                runner.Dispose();
            }
        }

        // ════════════════════════════════════════════════════
        //  Unregistered work ID → no exception
        // ════════════════════════════════════════════════════

        [Test]
        public void UnregisteredWorkId_NoExceptionOnDispatch()
        {
            var runner = BurstSchedulerRunner.Create();
            try
            {
                runner.Scheduler.Enqueue(new ScheduledWork { Id = 999, Type = WorkType.Custom });

                Assert.DoesNotThrow(() => clock.AdvanceFrame());
            }
            finally
            {
                runner.Dispose();
            }
        }

        // ════════════════════════════════════════════════════
        //  Exception in callback → PublishUnobservedException
        // ════════════════════════════════════════════════════

        [Test]
        public void ExceptionInCallback_PublishesUnobservedException()
        {
            using var collector = new UnobservedExceptionCollector();
            var runner = BurstSchedulerRunner.Create();
            try
            {
                var ex = new InvalidOperationException("test boom");
                runner.RegisterCallback(1, () => throw ex);
                runner.Scheduler.Enqueue(new ScheduledWork { Id = 1, Type = WorkType.Custom });

                clock.AdvanceFrame();

                Assert.AreEqual(1, collector.Exceptions.Count);
                Assert.AreSame(ex, collector.Exceptions[0]);
            }
            finally
            {
                runner.Dispose();
            }
        }

        // ════════════════════════════════════════════════════
        //  Callback fires exactly once
        // ════════════════════════════════════════════════════

        [Test]
        public void CallbackFiresExactlyOnce_NotOnSecondMoveNext()
        {
            var runner = BurstSchedulerRunner.Create();
            try
            {
                int callCount = 0;
                runner.RegisterCallback(7, () => callCount++);
                runner.Scheduler.Enqueue(new ScheduledWork { Id = 7, Type = WorkType.Custom });

                clock.AdvanceFrame();
                Assert.AreEqual(1, callCount);

                // Second frame should not fire again
                clock.AdvanceFrame();
                Assert.AreEqual(1, callCount);
            }
            finally
            {
                runner.Dispose();
            }
        }

        // ════════════════════════════════════════════════════
        //  Dispose → MoveNext returns false
        // ════════════════════════════════════════════════════

        [Test]
        public void Dispose_MoveNext_ReturnsFalse()
        {
            var runner = BurstSchedulerRunner.Create();
            runner.Dispose();

            Assert.IsFalse(runner.MoveNext());
        }

        // ════════════════════════════════════════════════════
        //  ScheduleTimer after Dispose → ObjectDisposedException
        // ════════════════════════════════════════════════════

        [Test]
        public void ScheduleTimer_AfterDispose_ThrowsObjectDisposedException()
        {
            var runner = BurstSchedulerRunner.Create();
            runner.Dispose();

            Assert.Throws<ObjectDisposedException>(() =>
                runner.ScheduleTimer(TimeSpan.FromSeconds(1), () => { }));
        }

        // ════════════════════════════════════════════════════
        //  RegisterCallback after Dispose → ObjectDisposedException
        // ════════════════════════════════════════════════════

        [Test]
        public void RegisterCallback_AfterDispose_ThrowsObjectDisposedException()
        {
            var runner = BurstSchedulerRunner.Create();
            runner.Dispose();

            Assert.Throws<ObjectDisposedException>(() =>
                runner.RegisterCallback(1, () => { }));
        }
    }
}
