// Copyright (c) Una Partida Mas. All rights reserved.
// Licensed under the MIT License. See LICENSE.md in the repository root.

using NUnit.Framework;
using Unity.Collections;
using Unity.Jobs;
using UnaPartidaMas.Valkarn.Tasks.Burst;

namespace UnaPartidaMas.Valkarn.Tasks.Tests.Burst
{
    /// <summary>
    /// Integration tests for <see cref="NativeScheduler"/>
    /// exercising real <see cref="NativeQueue{T}"/> and Burst-compatible enqueueing.
    /// </summary>
    [TestFixture]
    public class NativeSchedulerIntegrationTests
    {
        // ════════════════════════════════════════════════════
        //  Create → IsCreated true
        // ════════════════════════════════════════════════════

        [Test]
        public void Create_IsCreatedTrue()
        {
            var scheduler = new NativeScheduler(8, Allocator.Persistent);
            try
            {
                Assert.IsTrue(scheduler.IsCreated);
            }
            finally
            {
                scheduler.Dispose();
            }
        }

        // ════════════════════════════════════════════════════
        //  Enqueue one → Drain returns it
        // ════════════════════════════════════════════════════

        [Test]
        public void EnqueueOne_DrainReturnsIt()
        {
            var scheduler = new NativeScheduler(8, Allocator.Persistent);
            var results = new NativeList<ScheduledWork>(8, Allocator.Persistent);
            try
            {
                var work = new ScheduledWork { Id = 1, Type = WorkType.Custom, Payload = 100 };
                scheduler.Enqueue(work);

                int count = scheduler.Drain(results);

                Assert.AreEqual(1, count);
                Assert.AreEqual(1, results.Length);
                Assert.AreEqual(1, results[0].Id);
                Assert.AreEqual(WorkType.Custom, results[0].Type);
                Assert.AreEqual(100, results[0].Payload);
            }
            finally
            {
                results.Dispose();
                scheduler.Dispose();
            }
        }

        // ════════════════════════════════════════════════════
        //  Enqueue N → Drain returns all
        // ════════════════════════════════════════════════════

        [Test]
        public void EnqueueN_DrainReturnsAll()
        {
            var scheduler = new NativeScheduler(8, Allocator.Persistent);
            var results = new NativeList<ScheduledWork>(8, Allocator.Persistent);
            try
            {
                for (int i = 0; i < 5; i++)
                    scheduler.Enqueue(new ScheduledWork { Id = i, Type = WorkType.JobCompleted });

                int count = scheduler.Drain(results);

                Assert.AreEqual(5, count);
                Assert.AreEqual(5, results.Length);
            }
            finally
            {
                results.Dispose();
                scheduler.Dispose();
            }
        }

        // ════════════════════════════════════════════════════
        //  Empty drain → returns 0
        // ════════════════════════════════════════════════════

        [Test]
        public void EmptyDrain_ReturnsZero()
        {
            var scheduler = new NativeScheduler(8, Allocator.Persistent);
            var results = new NativeList<ScheduledWork>(8, Allocator.Persistent);
            try
            {
                int count = scheduler.Drain(results);

                Assert.AreEqual(0, count);
                Assert.AreEqual(0, results.Length);
            }
            finally
            {
                results.Dispose();
                scheduler.Dispose();
            }
        }

        // ════════════════════════════════════════════════════
        //  Dispose → IsCreated false
        // ════════════════════════════════════════════════════

        [Test]
        public void Dispose_IsCreatedFalse()
        {
            var scheduler = new NativeScheduler(8, Allocator.Persistent);
            scheduler.Dispose();

            Assert.IsFalse(scheduler.IsCreated);
        }

        // ════════════════════════════════════════════════════
        //  Double dispose → no crash
        // ════════════════════════════════════════════════════

        [Test]
        public void DoubleDispose_DoesNotCrash()
        {
            var scheduler = new NativeScheduler(8, Allocator.Persistent);
            scheduler.Dispose();

            Assert.DoesNotThrow(() => scheduler.Dispose());
        }

        // ════════════════════════════════════════════════════
        //  ParallelWriter from IJob → items appear in Drain
        // ════════════════════════════════════════════════════

        struct EnqueueJob : IJob
        {
            public NativeQueue<ScheduledWork>.ParallelWriter Writer;
            public ScheduledWork Work;

            public void Execute()
            {
                Writer.Enqueue(Work);
            }
        }

        [Test]
        public void ParallelWriter_FromJob_ItemsAppearInDrain()
        {
            var scheduler = new NativeScheduler(8, Allocator.Persistent);
            var results = new NativeList<ScheduledWork>(8, Allocator.Persistent);
            try
            {
                var work = new ScheduledWork { Id = 42, Type = WorkType.Custom, Payload = 999 };
                var job = new EnqueueJob
                {
                    Writer = scheduler.AsParallelWriter(),
                    Work = work
                };

                job.Schedule().Complete();

                int count = scheduler.Drain(results);

                Assert.AreEqual(1, count);
                Assert.AreEqual(42, results[0].Id);
                Assert.AreEqual(999, results[0].Payload);
            }
            finally
            {
                results.Dispose();
                scheduler.Dispose();
            }
        }
    }
}
