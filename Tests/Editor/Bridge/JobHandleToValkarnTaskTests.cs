// Copyright (c) Una Partida Mas. All rights reserved.
// Licensed under the MIT License. See LICENSE.md in the repository root.

using System.Threading;
using NUnit.Framework;
using Unity.Collections;
using Unity.Jobs;
using UnaPartidaMas.Valkarn.Tasks.Bridge;
using UnaPartidaMas.Valkarn.Tasks.Testing;

namespace UnaPartidaMas.Valkarn.Tasks.Tests.Bridge
{
    /// <summary>
    /// Integration tests for <see cref="JobHandleExtensions.ToValkarnTask"/>
    /// exercising real Unity <see cref="JobHandle"/> and <see cref="NativeArray{T}"/>.
    /// </summary>
    [TestFixture]
    public class JobHandleToValkarnTaskTests
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
            public void Execute() => Output[0] = 42;
        }

        // ════════════════════════════════════════════════════
        //  Fast path: already completed
        // ════════════════════════════════════════════════════

        [Test]
        public void AlreadyCompletedHandle_ReturnsCompletedTask()
        {
            var handle = new NoOpJob().Schedule();
            handle.Complete();

            var task = handle.ToValkarnTask();

            Assert.IsTrue(task.IsCompleted);
            Assert.AreEqual(ValkarnTask.Status.Succeeded, task.GetStatus());
        }

        // ════════════════════════════════════════════════════
        //  Slow path: pending job polled via PlayerLoop
        // ════════════════════════════════════════════════════

        [Test]
        public void PendingJob_AdvanceFrames_TaskCompletes()
        {
            var output = new NativeArray<int>(1, Allocator.TempJob);
            var handle = new WriteJob { Output = output }.Schedule();
            JobHandle.ScheduleBatchedJobs();
            try
            {
                var task = handle.ToValkarnTask();

                for (int i = 0; i < 10 && !task.IsCompleted; i++)
                    clock.AdvanceFrame();

                Assert.IsTrue(task.IsCompleted);
                Assert.AreEqual(ValkarnTask.Status.Succeeded, task.GetStatus());
                Assert.AreEqual(42, output[0]);
            }
            finally
            {
                handle.Complete();
                output.Dispose();
            }
        }

        // ════════════════════════════════════════════════════
        //  Cancellation: pre-canceled token
        // ════════════════════════════════════════════════════

        [Test]
        public void PreCanceledToken_CompletesHandleAndCancels()
        {
            var output = new NativeArray<int>(1, Allocator.TempJob);
            var cts = new CancellationTokenSource();
            cts.Cancel();
            var handle = new WriteJob { Output = output }.Schedule();
            JobHandle.ScheduleBatchedJobs();
            try
            {
                var task = handle.ToValkarnTask(cancellationToken: cts.Token);

                for (int i = 0; i < 10 && !task.IsCompleted; i++)
                    clock.AdvanceFrame();

                Assert.IsTrue(task.IsCompleted);
                // Fast-path race: if handle was already completed before ToValkarnTask,
                // we get Succeeded; otherwise MoveNext detects cancellation first.
                var status = task.GetStatus();
                Assert.IsTrue(
                    status == ValkarnTask.Status.Canceled || status == ValkarnTask.Status.Succeeded,
                    "Expected Canceled or Succeeded, got " + status);
            }
            finally
            {
                handle.Complete();
                output.Dispose();
            }
        }

        // ════════════════════════════════════════════════════
        //  Pool reuse across sequential completions
        // ════════════════════════════════════════════════════

        [Test]
        public void PoolReuse_SequentialCompletions_NoCorruption()
        {
            for (int i = 0; i < 5; i++)
            {
                var handle = new NoOpJob().Schedule();
                handle.Complete();
                var task = handle.ToValkarnTask();

                Assert.IsTrue(task.IsCompleted);
                task.GetAwaiter().GetResult();
            }
        }

        // ════════════════════════════════════════════════════
        //  Default overload (no explicit timing or token)
        // ════════════════════════════════════════════════════

        [Test]
        public void DefaultOverload_NoExplicitTiming_Completes()
        {
            // Verifies the parameterless overload compiles and completes correctly
            var output = new NativeArray<int>(1, Allocator.TempJob);
            var handle = new WriteJob { Output = output }.Schedule();
            JobHandle.ScheduleBatchedJobs();
            try
            {
                var task = handle.ToValkarnTask();

                for (int i = 0; i < 10 && !task.IsCompleted; i++)
                    clock.AdvanceFrame();

                Assert.IsTrue(task.IsCompleted);
                Assert.AreEqual(ValkarnTask.Status.Succeeded, task.GetStatus());
                Assert.AreEqual(42, output[0]);
            }
            finally
            {
                handle.Complete();
                output.Dispose();
            }
        }

        // ════════════════════════════════════════════════════
        //  GetResult after completion does not throw
        // ════════════════════════════════════════════════════

        [Test]
        public void CompletedTask_GetResult_DoesNotThrow()
        {
            var handle = new NoOpJob().Schedule();
            JobHandle.ScheduleBatchedJobs();
            var task = handle.ToValkarnTask();

            for (int i = 0; i < 10 && !task.IsCompleted; i++)
                clock.AdvanceFrame();

            Assert.DoesNotThrow(() => task.GetAwaiter().GetResult());
        }
    }
}
