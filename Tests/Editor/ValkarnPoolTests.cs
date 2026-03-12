// Copyright (c) Una Partida Mas. All rights reserved.
// Licensed under the MIT License. See LICENSE.md in the repository root.

using System.Threading;
using NUnit.Framework;

namespace UnaPartidaMas.Valkarn.Tasks.Tests
{
    [TestFixture]
    public class ValkarnPoolTests
    {
        sealed class TestNode : IPoolNode<TestNode>
        {
            TestNode nextNode;
            public ref TestNode NextNode => ref nextNode;
            public int Id;
        }

        [SetUp]
        public void SetUp()
        {
            TestHelper.EnsureInitialized();
        }

        [Test]
        public void TryRent_EmptyPool_ReturnsNull()
        {
            var pool = new ValkarnPool<TestNode>(maxSize: 4);
            Assert.IsNull(pool.TryRent());
        }

        [Test]
        public void TryReturn_ThenTryRent_ReturnsSameItem()
        {
            var pool = new ValkarnPool<TestNode>(maxSize: 4);
            var node = new TestNode { Id = 42 };

            Assert.IsTrue(pool.TryReturn(node));
            var rented = pool.TryRent();

            Assert.IsNotNull(rented);
            Assert.AreEqual(42, rented.Id);
        }

        [Test]
        public void Size_TracksCorrectly()
        {
            var pool = new ValkarnPool<TestNode>(maxSize: 4);
            Assert.AreEqual(0, pool.Size);

            var n1 = new TestNode();
            var n2 = new TestNode();
            pool.TryReturn(n1);
            Assert.AreEqual(1, pool.Size);

            pool.TryReturn(n2);
            Assert.AreEqual(2, pool.Size);

            pool.TryRent();
            Assert.AreEqual(1, pool.Size);
        }

        [Test]
        public void TryReturn_BeyondMaxSize_ReturnsFalse()
        {
            var pool = new ValkarnPool<TestNode>(maxSize: 2);

            // Fast slot + 2 in stack = we get 1 fast slot + 2 max in stack
            // Actually, maxSize controls the stack. Fast slot is separate.
            // So: fast slot (1) + stack (maxSize=2) = up to 3 items.
            // But TryReturn to stack checks stackSize >= maxSize.
            // Let's test:
            pool.TryReturn(new TestNode()); // goes to fast slot
            pool.TryReturn(new TestNode()); // goes to stack (stackSize=1)
            pool.TryReturn(new TestNode()); // goes to stack (stackSize=2, at max)
            var fourth = pool.TryReturn(new TestNode()); // should fail

            Assert.IsFalse(fourth);
        }

        [Test]
        public void FastSlot_UsedForMainThread()
        {
            // Ensure main thread ID is set
            ValkarnPoolShared.SetMainThreadId(Thread.CurrentThread.ManagedThreadId);

            var pool = new ValkarnPool<TestNode>(maxSize: 4);
            var node = new TestNode { Id = 99 };

            pool.TryReturn(node);
            // Should be in fast slot, size = 1
            Assert.AreEqual(1, pool.Size);

            var rented = pool.TryRent();
            Assert.AreEqual(99, rented.Id);
            Assert.AreEqual(0, pool.Size);
        }

        [Test]
        public void LIFO_Order_FromStack()
        {
            var pool = new ValkarnPool<TestNode>(maxSize: 10);
            var n1 = new TestNode { Id = 1 };
            var n2 = new TestNode { Id = 2 };
            var n3 = new TestNode { Id = 3 };

            // Fill fast slot first
            pool.TryReturn(n1); // fast slot
            pool.TryReturn(n2); // stack
            pool.TryReturn(n3); // stack

            // Rent: fast slot first, then stack (LIFO)
            var r1 = pool.TryRent();
            Assert.AreEqual(1, r1.Id); // fast slot

            var r2 = pool.TryRent();
            Assert.AreEqual(3, r2.Id); // stack top (LIFO)

            var r3 = pool.TryRent();
            Assert.AreEqual(2, r3.Id); // stack bottom
        }

        [Test]
        public void MaxSize_Property_ReflectsConstructor()
        {
            var pool = new ValkarnPool<TestNode>(maxSize: 16);
            Assert.AreEqual(16, pool.MaxSize);
        }

        [Test]
        public void Trim_ReleasesItems_WhenAboveMinSize()
        {
            var pool = new ValkarnPool<TestNode>(maxSize: 100);

            // Add many items and track creations
            for (int i = 0; i < 20; i++)
            {
                pool.TryReturn(new TestNode { Id = i });
                pool.IncrementCreated();
            }

            // With 20 items and 20 created, ratio = 1.0 > 0.5
            // First trim sets trimConsecutiveCount=1
            int released1 = pool.Trim(0);
            Assert.AreEqual(0, released1); // hysteresis: need 2 consecutive

            // Second trim actually releases
            int released2 = pool.Trim(0);
            Assert.Greater(released2, 0);
        }

        [Test]
        public void Trim_RespectsMinPoolSize()
        {
            var pool = new ValkarnPool<TestNode>(maxSize: 100);
            for (int i = 0; i < 5; i++)
            {
                pool.TryReturn(new TestNode());
                pool.IncrementCreated();
            }

            // If minPoolSize >= current size, no trimming
            int released = pool.Trim(minPoolSize: 10);
            Assert.AreEqual(0, released);
        }

        [Test]
        public void Pool_RegistersWithPoolRegistry()
        {
            var pool = new ValkarnPool<TestNode>(maxSize: 4);

            bool found = false;
            foreach (var info in PoolRegistry.GetAll())
            {
                if (info.type == typeof(TestNode))
                {
                    found = true;
                    break;
                }
            }

            Assert.IsTrue(found, "Pool should register with PoolRegistry");
        }

        [Test]
        public void IsMainThread_ReturnsTrueOnMainThread()
        {
            ValkarnPoolShared.SetMainThreadId(Thread.CurrentThread.ManagedThreadId);
            Assert.IsTrue(ValkarnPoolShared.IsMainThread());
        }

        [Test]
        public void IsMainThread_ReturnsFalseOnOtherThread()
        {
            ValkarnPoolShared.SetMainThreadId(Thread.CurrentThread.ManagedThreadId);

            bool result = true;
            var t = new Thread(() => { result = ValkarnPoolShared.IsMainThread(); });
            t.Start();
            t.Join();

            Assert.IsFalse(result);
        }
    }
}
