// Copyright (c) Una Partida Mas. All rights reserved.
// Licensed under the MIT License. See LICENSE.md in the repository root.

using System;
using NUnit.Framework;

namespace UnaPartidaMas.Valkarn.Tasks.Tests
{
    /// <summary>
    /// Tests for NativeTimerHeap and NativeScheduler logic.
    /// These test the algorithmic correctness of the min-heap and queue
    /// without requiring Burst or NativeContainers (which need Unity runtime).
    ///
    /// Full integration tests with NativeContainers require VTASKS_HAS_BURST
    /// and VTASKS_HAS_COLLECTIONS to be defined.
    /// </summary>
    [TestFixture]
    public class TimerHeapAlgorithmTests
    {
        [SetUp]
        public void SetUp()
        {
            TestHelper.EnsureInitialized();
        }

        // ════════════════════════════════════════════════════
        //  Min-heap correctness (verified via algorithm contract)
        // ════════════════════════════════════════════════════

        [Test]
        public void MinHeap_InsertAndExtract_MaintainsOrder()
        {
            // Test the min-heap property: extracting should yield elements
            // in ascending deadline order.
            // We use a managed replica of the SiftUp/SiftDown logic.

            var heap = new ManagedMinHeap();

            heap.Insert(50, 0);
            heap.Insert(20, 1);
            heap.Insert(80, 2);
            heap.Insert(10, 3);
            heap.Insert(40, 4);

            Assert.AreEqual(10, heap.PeekDeadline());
            Assert.AreEqual(3, heap.ExtractMinId());
            Assert.AreEqual(20, heap.PeekDeadline());
            Assert.AreEqual(1, heap.ExtractMinId());
            Assert.AreEqual(40, heap.PeekDeadline());
            Assert.AreEqual(4, heap.ExtractMinId());
            Assert.AreEqual(50, heap.PeekDeadline());
            Assert.AreEqual(0, heap.ExtractMinId());
            Assert.AreEqual(80, heap.PeekDeadline());
            Assert.AreEqual(2, heap.ExtractMinId());
            Assert.AreEqual(0, heap.Count);
        }

        [Test]
        public void MinHeap_DrainExpired_ReturnsOnlyExpired()
        {
            var heap = new ManagedMinHeap();

            heap.Insert(100, 0);
            heap.Insert(200, 1);
            heap.Insert(300, 2);
            heap.Insert(150, 3);
            heap.Insert(50, 4);

            // Drain at t=150: should get ids 4 (50), 0 (100), 3 (150)
            var expired = heap.DrainExpired(150);

            Assert.AreEqual(3, expired.Length);
            Assert.AreEqual(4, expired[0]); // 50
            Assert.AreEqual(0, expired[1]); // 100
            Assert.AreEqual(3, expired[2]); // 150

            Assert.AreEqual(2, heap.Count); // 200 and 300 remain
        }

        [Test]
        public void MinHeap_Empty_DrainReturnsNothing()
        {
            var heap = new ManagedMinHeap();
            var expired = heap.DrainExpired(1000);
            Assert.AreEqual(0, expired.Length);
        }

        [Test]
        public void MinHeap_SingleElement_Works()
        {
            var heap = new ManagedMinHeap();
            heap.Insert(42, 7);

            Assert.AreEqual(1, heap.Count);
            Assert.AreEqual(42, heap.PeekDeadline());
            Assert.AreEqual(7, heap.ExtractMinId());
            Assert.AreEqual(0, heap.Count);
        }

        [Test]
        public void MinHeap_DuplicateDeadlines_AllDrained()
        {
            var heap = new ManagedMinHeap();

            heap.Insert(100, 0);
            heap.Insert(100, 1);
            heap.Insert(100, 2);

            var expired = heap.DrainExpired(100);
            Assert.AreEqual(3, expired.Length);
            Assert.AreEqual(0, heap.Count);
        }

        [Test]
        public void MinHeap_LargeInsert_MaintainsHeapProperty()
        {
            var heap = new ManagedMinHeap();
            var rng = new Random(42);

            // Insert 1000 random deadlines
            for (int i = 0; i < 1000; i++)
                heap.Insert(rng.Next(0, 10000), i);

            // Extract all — should be in non-decreasing order
            long prev = long.MinValue;
            while (heap.Count > 0)
            {
                long deadline = heap.PeekDeadline();
                Assert.GreaterOrEqual(deadline, prev, "Heap property violated");
                prev = deadline;
                heap.ExtractMinId();
            }
        }

        // ════════════════════════════════════════════════════
        //  Managed replica of the min-heap (mirrors NativeTimerHeap)
        // ════════════════════════════════════════════════════

        struct Entry : IComparable<Entry>
        {
            public long Deadline;
            public int Id;
            public int CompareTo(Entry other) => Deadline.CompareTo(other.Deadline);
        }

        class ManagedMinHeap
        {
            readonly System.Collections.Generic.List<Entry> heap = new();

            public int Count => heap.Count;

            public void Insert(long deadline, int id)
            {
                heap.Add(new Entry { Deadline = deadline, Id = id });
                SiftUp(heap.Count - 1);
            }

            public long PeekDeadline() => heap[0].Deadline;

            public int ExtractMinId()
            {
                int id = heap[0].Id;
                int last = heap.Count - 1;
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
                return id;
            }

            public int[] DrainExpired(long currentTimestamp)
            {
                var result = new System.Collections.Generic.List<int>();
                while (heap.Count > 0 && heap[0].Deadline <= currentTimestamp)
                    result.Add(ExtractMinId());
                return result.ToArray();
            }

            void SiftUp(int index)
            {
                while (index > 0)
                {
                    int parent = (index - 1) / 2;
                    if (heap[index].CompareTo(heap[parent]) >= 0) break;
                    var tmp = heap[index];
                    heap[index] = heap[parent];
                    heap[parent] = tmp;
                    index = parent;
                }
            }

            void SiftDown(int index)
            {
                int count = heap.Count;
                while (true)
                {
                    int smallest = index;
                    int left = 2 * index + 1;
                    int right = 2 * index + 2;
                    if (left < count && heap[left].CompareTo(heap[smallest]) < 0) smallest = left;
                    if (right < count && heap[right].CompareTo(heap[smallest]) < 0) smallest = right;
                    if (smallest == index) break;
                    var tmp = heap[index];
                    heap[index] = heap[smallest];
                    heap[smallest] = tmp;
                    index = smallest;
                }
            }
        }
    }
}
