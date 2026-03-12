// Copyright (c) Una Partida Mas. All rights reserved.
// Licensed under the MIT License. See LICENSE.md in the repository root.

using System;
using System.Threading;
using NUnit.Framework;
using Unity.Collections;
using Unity.Jobs;
using UnaPartidaMas.Valkarn.Tasks.Bridge;
using UnaPartidaMas.Valkarn.Tasks.Testing;

namespace UnaPartidaMas.Valkarn.Tasks.Tests.Bridge
{
    /// <summary>
    /// Integration tests for <see cref="JobHandleExtensions.WhenAll"/>
    /// exercising real Unity <see cref="JobHandle"/> arrays.
    /// </summary>
    [TestFixture]
    public class JobHandleWhenAllIntegrationTests
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

        struct NoOpJob : IJob
        {
            public void Execute() { }
        }

        struct WriteJob : IJob
        {
            public NativeArray<int> Output;
            public int Value;
            public void Execute() => Output[0] = Value;
        }

        // ════════════════════════════════════════════════════
        //  Empty array → CompletedTask
        // ════════════════════════════════════════════════════

        [Test]
        public void EmptyArray_ReturnsCompletedTask()
        {
            var task = JobHandleExtensions.WhenAll(new JobHandle[0]);

            Assert.IsTrue(task.IsCompleted);
            Assert.AreEqual(ValkarnTask.Status.Succeeded, task.GetStatus());
        }

        // ════════════════════════════════════════════════════
        //  Null array → ArgumentNullException
        // ════════════════════════════════════════════════════

        [Test]
        public void NullArray_ThrowsArgumentNullException()
        {
            Assert.Throws<ArgumentNullException>(() =>
                JobHandleExtensions.WhenAll(null));
        }

        // ════════════════════════════════════════════════════
        //  All handles already completed → fast path
        // ════════════════════════════════════════════════════

        [Test]
        public void AllAlreadyCompleted_FastPath_ReturnsCompletedTask()
        {
            var h1 = new NoOpJob().Schedule();
            var h2 = new NoOpJob().Schedule();
            h1.Complete();
            h2.Complete();

            var task = JobHandleExtensions.WhenAll(h1, h2);

            Assert.IsTrue(task.IsCompleted);
            Assert.AreEqual(ValkarnTask.Status.Succeeded, task.GetStatus());
        }

        // ════════════════════════════════════════════════════
        //  Multiple pending jobs → advance frames → completes
        // ════════════════════════════════════════════════════

        [Test]
        public void MultiplePendingJobs_AdvanceFrames_TaskCompletes()
        {
            var o1 = new NativeArray<int>(1, Allocator.TempJob);
            var o2 = new NativeArray<int>(1, Allocator.TempJob);
            var h1 = new WriteJob { Output = o1, Value = 10 }.Schedule();
            var h2 = new WriteJob { Output = o2, Value = 20 }.Schedule();
            JobHandle.ScheduleBatchedJobs();
            try
            {
                var task = JobHandleExtensions.WhenAll(h1, h2);

                for (int i = 0; i < 10 && !task.IsCompleted; i++)
                    clock.AdvanceFrame();

                Assert.IsTrue(task.IsCompleted);
                Assert.AreEqual(ValkarnTask.Status.Succeeded, task.GetStatus());
                Assert.AreEqual(10, o1[0]);
                Assert.AreEqual(20, o2[0]);
            }
            finally
            {
                h1.Complete();
                h2.Complete();
                o1.Dispose();
                o2.Dispose();
            }
        }

        // ════════════════════════════════════════════════════
        //  Cancellation → all remaining handles completed
        // ════════════════════════════════════════════════════

        [Test]
        public void Cancellation_CompletesAllRemainingHandles()
        {
            var o1 = new NativeArray<int>(1, Allocator.TempJob);
            var cts = new CancellationTokenSource();
            cts.Cancel();
            var h1 = new WriteJob { Output = o1, Value = 99 }.Schedule();
            JobHandle.ScheduleBatchedJobs();
            try
            {
                var task = JobHandleExtensions.WhenAll(
                    new[] { h1 }, PlayerLoopTiming.Update, cts.Token);

                for (int i = 0; i < 10 && !task.IsCompleted; i++)
                    clock.AdvanceFrame();

                Assert.IsTrue(task.IsCompleted);
                // Verify the job actually ran (handle was completed, no job leak).
                // The write proves Complete() was called on the handle.
                Assert.AreEqual(99, o1[0]);
            }
            finally
            {
                h1.Complete();
                o1.Dispose();
            }
        }

        // ════════════════════════════════════════════════════
        //  ToValkarnTask(JobHandle[]) delegates to WhenAll
        // ════════════════════════════════════════════════════

        [Test]
        public void ToValkarnTask_ArrayExtension_DelegatesToWhenAll()
        {
            var handles = new[]
            {
                new NoOpJob().Schedule(),
                new NoOpJob().Schedule()
            };
            handles[0].Complete();
            handles[1].Complete();

            var task = handles.ToValkarnTask();

            Assert.IsTrue(task.IsCompleted);
            Assert.AreEqual(ValkarnTask.Status.Succeeded, task.GetStatus());
        }

        // ════════════════════════════════════════════════════
        //  Single job → completes
        // ════════════════════════════════════════════════════

        [Test]
        public void SingleJob_Completes()
        {
            var handle = new NoOpJob().Schedule();
            JobHandle.ScheduleBatchedJobs();

            var task = JobHandleExtensions.WhenAll(handle);

            for (int i = 0; i < 10 && !task.IsCompleted; i++)
                clock.AdvanceFrame();

            Assert.IsTrue(task.IsCompleted);
            Assert.AreEqual(ValkarnTask.Status.Succeeded, task.GetStatus());
        }
    }
}
