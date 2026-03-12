// Copyright (c) Una Partida Mas. All rights reserved.
// Licensed under the MIT License. See LICENSE.md in the repository root.

#if VTASKS_HAS_BURST && VTASKS_HAS_COLLECTIONS
using System;
using Unity.Burst;
using Unity.Collections;

namespace UnaPartidaMas.Valkarn.Tasks.Burst
{
    /// <summary>
    /// Describes the type of scheduled work.
    /// </summary>
    public enum WorkType : byte
    {
        TimerExpired = 0,
        JobCompleted = 1,
        Custom = 2
    }

    /// <summary>
    /// Unmanaged descriptor for a unit of scheduled work.
    /// </summary>
    public struct ScheduledWork
    {
        public int Id;
        public WorkType Type;
        public long Payload;
    }

    /// <summary>
    /// Burst-compatible work queue backed by <see cref="NativeQueue{T}"/>.
    /// Supports enqueueing from Burst-compiled jobs and draining from the main thread.
    /// </summary>
    public struct NativeScheduler : IDisposable
    {
        NativeQueue<ScheduledWork> queue;

        /// <summary>
        /// Creates a new scheduler with the given allocator.
        /// </summary>
        public NativeScheduler(int initialCapacity, AllocatorManager.AllocatorHandle allocator)
        {
            queue = new NativeQueue<ScheduledWork>(allocator);
        }

        /// <summary>Whether this scheduler has been created and not yet disposed.</summary>
        public bool IsCreated => queue.IsCreated;

        /// <summary>
        /// Enqueues a work item. Burst-compatible.
        /// </summary>
        [BurstCompile]
        public void Enqueue(ScheduledWork work)
        {
            queue.Enqueue(work);
        }

        /// <summary>
        /// Drains all pending work into <paramref name="results"/>.
        /// Call from main thread only.
        /// </summary>
        /// <param name="results">List to append drained items to. Must be created.</param>
        /// <returns>Number of items drained.</returns>
        public int Drain(NativeList<ScheduledWork> results)
        {
            int count = 0;
            while (queue.TryDequeue(out var work))
            {
                results.Add(work);
                count++;
            }
            return count;
        }

        /// <summary>
        /// Returns a parallel writer for use in Burst-compiled jobs.
        /// </summary>
        public NativeQueue<ScheduledWork>.ParallelWriter AsParallelWriter()
            => queue.AsParallelWriter();

        /// <summary>
        /// Disposes the underlying native queue.
        /// </summary>
        public void Dispose()
        {
            if (queue.IsCreated)
                queue.Dispose();
        }
    }
}
#endif
