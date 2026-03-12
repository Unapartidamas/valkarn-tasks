// Copyright (c) Una Partida Mas. All rights reserved.
// Licensed under the MIT License. See LICENSE.md in the repository root.

#if VTASKS_HAS_BURST && VTASKS_HAS_COLLECTIONS
using System;
using Unity.Burst;
using Unity.Collections;

namespace UnaPartidaMas.Valkarn.Tasks.Burst
{
    /// <summary>
    /// Entry in the timer min-heap. Ordered by <see cref="Deadline"/>.
    /// </summary>
    public struct TimerEntry : IComparable<TimerEntry>
    {
        public long Deadline;
        public int Id;

        public int CompareTo(TimerEntry other) => Deadline.CompareTo(other.Deadline);
    }

    /// <summary>
    /// Burst-compatible binary min-heap for scheduling timers.
    /// Supports O(log n) insert and O(log n) per-expired-timer drain.
    /// </summary>
    public struct NativeTimerHeap : IDisposable
    {
        NativeList<TimerEntry> heap;
        int nextId;

        /// <summary>
        /// Creates a timer heap with the given initial capacity.
        /// </summary>
        public NativeTimerHeap(int initialCapacity, AllocatorManager.AllocatorHandle allocator)
        {
            heap = new NativeList<TimerEntry>(initialCapacity, allocator);
            nextId = 0;
        }

        /// <summary>Whether this heap has been created and not yet disposed.</summary>
        public bool IsCreated => heap.IsCreated;

        /// <summary>Current number of timers in the heap.</summary>
        public int Count => heap.Length;

        /// <summary>
        /// Schedules a new timer with the given deadline (ticks or milliseconds,
        /// as long as the unit is consistent with <see cref="DrainExpired"/>).
        /// </summary>
        /// <param name="deadline">The absolute time at which this timer expires.</param>
        /// <returns>Timer ID that can be used for identification in callbacks.</returns>
        [BurstCompile]
        public int Schedule(long deadline)
        {
            int id = nextId++;
            var entry = new TimerEntry { Deadline = deadline, Id = id };
            heap.Add(entry);
            SiftUp(heap.Length - 1);
            return id;
        }

        /// <summary>
        /// Drains all timers whose deadline is &lt;= <paramref name="currentTimestamp"/>
        /// and appends their IDs to <paramref name="expiredIds"/>.
        /// </summary>
        /// <param name="currentTimestamp">The current time in the same unit as deadlines.</param>
        /// <param name="expiredIds">List to append expired timer IDs to.</param>
        /// <returns>Number of expired timers drained.</returns>
        [BurstCompile]
        public int DrainExpired(long currentTimestamp, NativeList<int> expiredIds)
        {
            int count = 0;
            while (heap.Length > 0 && heap[0].Deadline <= currentTimestamp)
            {
                expiredIds.Add(heap[0].Id);
                count++;

                // Remove root: move last to root and sift down
                int last = heap.Length - 1;
                if (last > 0)
                {
                    heap[0] = heap[last];
                    heap.RemoveAt(last);
                    SiftDown(0);
                }
                else
                {
                    heap.RemoveAt(0);
                }
            }
            return count;
        }

        void SiftUp(int index)
        {
            while (index > 0)
            {
                int parent = (index - 1) / 2;
                if (heap[index].CompareTo(heap[parent]) >= 0)
                    break;

                // Swap
                var tmp = heap[index];
                heap[index] = heap[parent];
                heap[parent] = tmp;
                index = parent;
            }
        }

        void SiftDown(int index)
        {
            int count = heap.Length;
            while (true)
            {
                int smallest = index;
                int left = 2 * index + 1;
                int right = 2 * index + 2;

                if (left < count && heap[left].CompareTo(heap[smallest]) < 0)
                    smallest = left;
                if (right < count && heap[right].CompareTo(heap[smallest]) < 0)
                    smallest = right;

                if (smallest == index)
                    break;

                var tmp = heap[index];
                heap[index] = heap[smallest];
                heap[smallest] = tmp;
                index = smallest;
            }
        }

        /// <summary>
        /// Disposes the underlying native list.
        /// </summary>
        public void Dispose()
        {
            if (heap.IsCreated)
                heap.Dispose();
        }
    }
}
#endif
