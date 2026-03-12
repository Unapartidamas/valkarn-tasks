// Copyright (c) Una Partida Mas. All rights reserved.
// Licensed under the MIT License. See LICENSE.md in the repository root.

using System;
using NUnit.Framework;
using Unity.Collections;
using UnaPartidaMas.Valkarn.Tasks.Burst;

namespace UnaPartidaMas.Valkarn.Tasks.Tests.Burst
{
    /// <summary>
    /// Integration tests for <see cref="NativeTimerHeap"/>
    /// exercising real <see cref="NativeList{T}"/>-backed binary min-heap.
    /// </summary>
    [TestFixture]
    public class NativeTimerHeapIntegrationTests
    {
        // ════════════════════════════════════════════════════
        //  Create → IsCreated true, Count 0
        // ════════════════════════════════════════════════════

        [Test]
        public void Create_IsCreatedTrue_CountZero()
        {
            var heap = new NativeTimerHeap(8, Allocator.Persistent);
            try
            {
                Assert.IsTrue(heap.IsCreated);
                Assert.AreEqual(0, heap.Count);
            }
            finally
            {
                heap.Dispose();
            }
        }

        // ════════════════════════════════════════════════════
        //  Schedule → sequential IDs, Count increases
        // ════════════════════════════════════════════════════

        [Test]
        public void Schedule_ReturnsSequentialIds_CountIncreases()
        {
            var heap = new NativeTimerHeap(8, Allocator.Persistent);
            try
            {
                int id0 = heap.Schedule(100);
                int id1 = heap.Schedule(200);
                int id2 = heap.Schedule(300);

                Assert.AreEqual(0, id0);
                Assert.AreEqual(1, id1);
                Assert.AreEqual(2, id2);
                Assert.AreEqual(3, heap.Count);
            }
            finally
            {
                heap.Dispose();
            }
        }

        // ════════════════════════════════════════════════════
        //  DrainExpired → only expired timers drained
        // ════════════════════════════════════════════════════

        [Test]
        public void DrainExpired_OnlyExpiredTimersDrained()
        {
            var heap = new NativeTimerHeap(8, Allocator.Persistent);
            var expired = new NativeList<int>(8, Allocator.Persistent);
            try
            {
                heap.Schedule(100); // id 0
                heap.Schedule(200); // id 1
                heap.Schedule(300); // id 2
                heap.Schedule(150); // id 3

                int count = heap.DrainExpired(150, expired);

                Assert.AreEqual(2, count);       // deadlines 100 and 150
                Assert.AreEqual(2, expired.Length);
                Assert.AreEqual(2, heap.Count);  // deadlines 200 and 300 remain
            }
            finally
            {
                expired.Dispose();
                heap.Dispose();
            }
        }

        // ════════════════════════════════════════════════════
        //  DrainExpired with no expired → returns 0
        // ════════════════════════════════════════════════════

        [Test]
        public void DrainExpired_NoExpired_ReturnsZero()
        {
            var heap = new NativeTimerHeap(8, Allocator.Persistent);
            var expired = new NativeList<int>(8, Allocator.Persistent);
            try
            {
                heap.Schedule(500);
                heap.Schedule(600);

                int count = heap.DrainExpired(100, expired);

                Assert.AreEqual(0, count);
                Assert.AreEqual(0, expired.Length);
                Assert.AreEqual(2, heap.Count);
            }
            finally
            {
                expired.Dispose();
                heap.Dispose();
            }
        }

        // ════════════════════════════════════════════════════
        //  Duplicate deadlines → all drained
        // ════════════════════════════════════════════════════

        [Test]
        public void DuplicateDeadlines_AllDrained()
        {
            var heap = new NativeTimerHeap(8, Allocator.Persistent);
            var expired = new NativeList<int>(8, Allocator.Persistent);
            try
            {
                heap.Schedule(100);
                heap.Schedule(100);
                heap.Schedule(100);

                int count = heap.DrainExpired(100, expired);

                Assert.AreEqual(3, count);
                Assert.AreEqual(3, expired.Length);
                Assert.AreEqual(0, heap.Count);
            }
            finally
            {
                expired.Dispose();
                heap.Dispose();
            }
        }

        // ════════════════════════════════════════════════════
        //  Single element → works correctly
        // ════════════════════════════════════════════════════

        [Test]
        public void SingleElement_WorksCorrectly()
        {
            var heap = new NativeTimerHeap(8, Allocator.Persistent);
            var expired = new NativeList<int>(8, Allocator.Persistent);
            try
            {
                int id = heap.Schedule(50);

                Assert.AreEqual(0, id);
                Assert.AreEqual(1, heap.Count);

                int count = heap.DrainExpired(50, expired);

                Assert.AreEqual(1, count);
                Assert.AreEqual(0, expired[0]);
                Assert.AreEqual(0, heap.Count);
            }
            finally
            {
                expired.Dispose();
                heap.Dispose();
            }
        }

        // ════════════════════════════════════════════════════
        //  Large scale (1000 timers) → heap property maintained
        // ════════════════════════════════════════════════════

        [Test]
        public void LargeScale_HeapPropertyMaintained()
        {
            var heap = new NativeTimerHeap(64, Allocator.Persistent);
            var expired = new NativeList<int>(1024, Allocator.Persistent);
            try
            {
                var rng = new Random(42);
                for (int i = 0; i < 1000; i++)
                    heap.Schedule(rng.Next(0, 10000));

                Assert.AreEqual(1000, heap.Count);

                // Drain all by using max deadline
                int count = heap.DrainExpired(10000, expired);

                Assert.AreEqual(1000, count);
                Assert.AreEqual(0, heap.Count);
            }
            finally
            {
                expired.Dispose();
                heap.Dispose();
            }
        }

        // ════════════════════════════════════════════════════
        //  Dispose → IsCreated false
        // ════════════════════════════════════════════════════

        [Test]
        public void Dispose_IsCreatedFalse()
        {
            var heap = new NativeTimerHeap(8, Allocator.Persistent);
            heap.Dispose();

            Assert.IsFalse(heap.IsCreated);
        }
    }
}
