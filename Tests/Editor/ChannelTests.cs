// Copyright (c) Una Partida Mas. All rights reserved.
// Licensed under the MIT License. See LICENSE.md in the repository root.

using System;
using System.Collections.Generic;
using NUnit.Framework;

namespace UnaPartidaMas.Valkarn.Tasks.Tests
{
    [TestFixture]
    public class UnboundedChannelTests
    {
        [SetUp]
        public void SetUp()
        {
            TestHelper.EnsureInitialized();
        }

        [Test]
        public void TryWrite_ThenReadAsync_ReturnsItem()
        {
            var ch = ValkarnTask.Channel.CreateUnbounded<int>();
            Assert.IsTrue(ch.Writer.TryWrite(42));

            var task = ch.Reader.ReadAsync();
            Assert.IsTrue(task.IsCompleted);
            Assert.AreEqual(42, task.GetAwaiter().GetResult());
        }

        [Test]
        public void TryWrite_Multiple_MaintainsOrder()
        {
            var ch = ValkarnTask.Channel.CreateUnbounded<int>();
            ch.Writer.TryWrite(1);
            ch.Writer.TryWrite(2);
            ch.Writer.TryWrite(3);

            Assert.AreEqual(1, TestHelper.RunSync(ch.Reader.ReadAsync()));
            Assert.AreEqual(2, TestHelper.RunSync(ch.Reader.ReadAsync()));
            Assert.AreEqual(3, TestHelper.RunSync(ch.Reader.ReadAsync()));
        }

        [Test]
        public void ReadAsync_BeforeWrite_Pends()
        {
            var ch = ValkarnTask.Channel.CreateUnbounded<int>();

            var readTask = ch.Reader.ReadAsync();
            Assert.IsFalse(readTask.IsCompleted);

            ch.Writer.TryWrite(99);
            Assert.IsTrue(readTask.IsCompleted);
            Assert.AreEqual(99, readTask.GetAwaiter().GetResult());
        }

        [Test]
        public void TryRead_WithItem_ReturnsTrue()
        {
            var ch = ValkarnTask.Channel.CreateUnbounded<string>();
            ch.Writer.TryWrite("hello");

            Assert.IsTrue(ch.Reader.TryRead(out var item));
            Assert.AreEqual("hello", item);
        }

        [Test]
        public void TryRead_Empty_ReturnsFalse()
        {
            var ch = ValkarnTask.Channel.CreateUnbounded<int>();
            Assert.IsFalse(ch.Reader.TryRead(out _));
        }

        [Test]
        public void Complete_PreventsFurtherWrites()
        {
            var ch = ValkarnTask.Channel.CreateUnbounded<int>();
            ch.Writer.Complete();

            Assert.IsFalse(ch.Writer.TryWrite(1));
        }

        [Test]
        public void Complete_AllowsDrainingRemaining()
        {
            var ch = ValkarnTask.Channel.CreateUnbounded<int>();
            ch.Writer.TryWrite(1);
            ch.Writer.TryWrite(2);
            ch.Writer.Complete();

            Assert.AreEqual(1, TestHelper.RunSync(ch.Reader.ReadAsync()));
            Assert.AreEqual(2, TestHelper.RunSync(ch.Reader.ReadAsync()));
        }

        [Test]
        public void ReadAsync_AfterCompleteAndDrain_ThrowsChannelClosed()
        {
            var ch = ValkarnTask.Channel.CreateUnbounded<int>();
            ch.Writer.Complete();

            var task = ch.Reader.ReadAsync();
            Assert.IsTrue(task.IsCompleted);
            Assert.Throws<ChannelClosedException>(() => task.GetAwaiter().GetResult());
        }

        [Test]
        public void Completion_CompletesWhenDrained()
        {
            var ch = ValkarnTask.Channel.CreateUnbounded<int>();
            ch.Writer.TryWrite(1);

            var completion = ch.Reader.Completion;
            Assert.IsFalse(completion.IsCompleted);

            ch.Writer.Complete();
            Assert.IsFalse(completion.IsCompleted, "Still has unread items");

            ch.Reader.TryRead(out _);
            Assert.IsTrue(completion.IsCompleted, "Drained — should be complete");
        }

        [Test]
        public void Completion_EmptyComplete_CompletesImmediately()
        {
            var ch = ValkarnTask.Channel.CreateUnbounded<int>();
            ch.Writer.Complete();
            Assert.IsTrue(ch.Reader.Completion.IsCompleted);
        }

        [Test]
        public void Complete_FailsPendingReaders()
        {
            var ch = ValkarnTask.Channel.CreateUnbounded<int>();
            var readTask = ch.Reader.ReadAsync();
            Assert.IsFalse(readTask.IsCompleted);

            ch.Writer.Complete();
            Assert.IsTrue(readTask.IsCompleted);
            Assert.Throws<ChannelClosedException>(() => readTask.GetAwaiter().GetResult());
        }

        [Test]
        public void WriteAsync_Unbounded_CompletesImmediately()
        {
            var ch = ValkarnTask.Channel.CreateUnbounded<int>();
            var writeTask = ch.Writer.WriteAsync(42);
            Assert.IsTrue(writeTask.IsCompleted);
        }

        [Test]
        public void WriteAsync_AfterComplete_Throws()
        {
            var ch = ValkarnTask.Channel.CreateUnbounded<int>();
            ch.Writer.Complete();

            var task = ch.Writer.WriteAsync(1);
            Assert.IsTrue(task.IsCompleted);
            Assert.Throws<ChannelClosedException>(() => task.GetAwaiter().GetResult());
        }

        // ── Multi-consumer ──

        [Test]
        public void MultiConsumer_MultiplePendingReaders()
        {
            var ch = ValkarnTask.Channel.CreateUnbounded<int>(multiConsumer: true);

            var r1 = ch.Reader.ReadAsync();
            var r2 = ch.Reader.ReadAsync();
            Assert.IsFalse(r1.IsCompleted);
            Assert.IsFalse(r2.IsCompleted);

            ch.Writer.TryWrite(10);
            ch.Writer.TryWrite(20);

            Assert.IsTrue(r1.IsCompleted);
            Assert.IsTrue(r2.IsCompleted);
            Assert.AreEqual(10, r1.GetAwaiter().GetResult());
            Assert.AreEqual(20, r2.GetAwaiter().GetResult());
        }

        [Test]
        public void SingleConsumer_SecondPendingReader_Throws()
        {
            var ch = ValkarnTask.Channel.CreateUnbounded<int>(multiConsumer: false);

            var _ = ch.Reader.ReadAsync(); // first reader
            Assert.Throws<InvalidOperationException>(() => ch.Reader.ReadAsync());
        }

        // ── Direct handoff ──

        [Test]
        public void Write_DirectHandoff_ToWaitingReader()
        {
            var ch = ValkarnTask.Channel.CreateUnbounded<int>();
            var readTask = ch.Reader.ReadAsync();

            ch.Writer.TryWrite(77);

            // Item handed off directly, not queued
            Assert.IsTrue(readTask.IsCompleted);
            Assert.AreEqual(77, readTask.GetAwaiter().GetResult());

            // Queue should be empty
            Assert.IsFalse(ch.Reader.TryRead(out _));
        }
    }

    [TestFixture]
    public class BoundedChannelTests
    {
        [SetUp]
        public void SetUp()
        {
            TestHelper.EnsureInitialized();
        }

        [Test]
        public void TryWrite_WithinCapacity_Succeeds()
        {
            var ch = ValkarnTask.Channel.CreateBounded<int>(capacity: 3);
            Assert.IsTrue(ch.Writer.TryWrite(1));
            Assert.IsTrue(ch.Writer.TryWrite(2));
            Assert.IsTrue(ch.Writer.TryWrite(3));
        }

        [Test]
        public void TryWrite_AtCapacity_ReturnsFalse()
        {
            var ch = ValkarnTask.Channel.CreateBounded<int>(capacity: 2);
            ch.Writer.TryWrite(1);
            ch.Writer.TryWrite(2);
            Assert.IsFalse(ch.Writer.TryWrite(3));
        }

        [Test]
        public void ReadAsync_ReturnsInOrder()
        {
            var ch = ValkarnTask.Channel.CreateBounded<int>(capacity: 3);
            ch.Writer.TryWrite(10);
            ch.Writer.TryWrite(20);
            ch.Writer.TryWrite(30);

            Assert.AreEqual(10, TestHelper.RunSync(ch.Reader.ReadAsync()));
            Assert.AreEqual(20, TestHelper.RunSync(ch.Reader.ReadAsync()));
            Assert.AreEqual(30, TestHelper.RunSync(ch.Reader.ReadAsync()));
        }

        [Test]
        public void WriteAsync_WhenFull_Blocks()
        {
            var ch = ValkarnTask.Channel.CreateBounded<int>(capacity: 1);
            ch.Writer.TryWrite(1); // fills buffer

            var writeTask = ch.Writer.WriteAsync(2);
            Assert.IsFalse(writeTask.IsCompleted, "Should block — buffer is full");

            // Read frees a slot
            Assert.AreEqual(1, TestHelper.RunSync(ch.Reader.ReadAsync()));

            // Write should now complete
            Assert.IsTrue(writeTask.IsCompleted);
            Assert.DoesNotThrow(() => writeTask.GetAwaiter().GetResult());

            // Item 2 should be in the buffer now
            Assert.AreEqual(2, TestHelper.RunSync(ch.Reader.ReadAsync()));
        }

        [Test]
        public void Backpressure_MultipleWriters()
        {
            var ch = ValkarnTask.Channel.CreateBounded<int>(capacity: 1);
            ch.Writer.TryWrite(1); // fills buffer

            var w1 = ch.Writer.WriteAsync(2);
            var w2 = ch.Writer.WriteAsync(3);
            Assert.IsFalse(w1.IsCompleted);
            Assert.IsFalse(w2.IsCompleted);

            // Read 1 → frees slot → writer 1 unblocks → item 2 enters buffer
            Assert.AreEqual(1, TestHelper.RunSync(ch.Reader.ReadAsync()));
            Assert.IsTrue(w1.IsCompleted);

            // Read 2 → frees slot → writer 2 unblocks → item 3 enters buffer
            Assert.AreEqual(2, TestHelper.RunSync(ch.Reader.ReadAsync()));
            Assert.IsTrue(w2.IsCompleted);

            Assert.AreEqual(3, TestHelper.RunSync(ch.Reader.ReadAsync()));
        }

        [Test]
        public void ReadAsync_BeforeWrite_Pends()
        {
            var ch = ValkarnTask.Channel.CreateBounded<int>(capacity: 5);

            var readTask = ch.Reader.ReadAsync();
            Assert.IsFalse(readTask.IsCompleted);

            ch.Writer.TryWrite(42);
            Assert.IsTrue(readTask.IsCompleted);
            Assert.AreEqual(42, readTask.GetAwaiter().GetResult());
        }

        [Test]
        public void Complete_FailsPendingWriters()
        {
            var ch = ValkarnTask.Channel.CreateBounded<int>(capacity: 1);
            ch.Writer.TryWrite(1);

            var writeTask = ch.Writer.WriteAsync(2);
            Assert.IsFalse(writeTask.IsCompleted);

            ch.Writer.Complete();
            Assert.IsTrue(writeTask.IsCompleted);
            Assert.Throws<ChannelClosedException>(() => writeTask.GetAwaiter().GetResult());
        }

        [Test]
        public void Complete_AllowsDraining()
        {
            var ch = ValkarnTask.Channel.CreateBounded<int>(capacity: 3);
            ch.Writer.TryWrite(1);
            ch.Writer.TryWrite(2);
            ch.Writer.Complete();

            Assert.AreEqual(1, TestHelper.RunSync(ch.Reader.ReadAsync()));
            Assert.AreEqual(2, TestHelper.RunSync(ch.Reader.ReadAsync()));
            Assert.Throws<ChannelClosedException>(() => ch.Reader.ReadAsync().GetAwaiter().GetResult());
        }

        [Test]
        public void Completion_CompletesAfterDrain()
        {
            var ch = ValkarnTask.Channel.CreateBounded<int>(capacity: 3);
            ch.Writer.TryWrite(1);
            ch.Writer.Complete();

            var completion = ch.Reader.Completion;
            Assert.IsFalse(completion.IsCompleted);

            ch.Reader.TryRead(out _);
            Assert.IsTrue(completion.IsCompleted);
        }

        [Test]
        public void TryRead_DirectHandoff_FromPendingWriter()
        {
            var ch = ValkarnTask.Channel.CreateBounded<int>(capacity: 1);
            ch.Writer.TryWrite(1); // fills buffer
            var writeTask = ch.Writer.WriteAsync(2); // blocks

            // TryRead dequeues 1, and moves writer's item 2 into buffer
            Assert.IsTrue(ch.Reader.TryRead(out var item));
            Assert.AreEqual(1, item);
            Assert.IsTrue(writeTask.IsCompleted);

            // Item 2 is now in buffer
            Assert.IsTrue(ch.Reader.TryRead(out var item2));
            Assert.AreEqual(2, item2);
        }

        [Test]
        public void InvalidCapacity_Throws()
        {
            Assert.Throws<ArgumentOutOfRangeException>(
                () => ValkarnTask.Channel.CreateBounded<int>(capacity: 0));
            Assert.Throws<ArgumentOutOfRangeException>(
                () => ValkarnTask.Channel.CreateBounded<int>(capacity: -1));
        }

        [Test]
        public void RingBuffer_Wraps_Correctly()
        {
            var ch = ValkarnTask.Channel.CreateBounded<int>(capacity: 2);

            // Fill and drain multiple times to exercise ring buffer wrap-around
            for (int cycle = 0; cycle < 5; cycle++)
            {
                ch.Writer.TryWrite(cycle * 10 + 1);
                ch.Writer.TryWrite(cycle * 10 + 2);

                Assert.AreEqual(cycle * 10 + 1, TestHelper.RunSync(ch.Reader.ReadAsync()));
                Assert.AreEqual(cycle * 10 + 2, TestHelper.RunSync(ch.Reader.ReadAsync()));
            }
        }

        // ── Multi-consumer ──

        [Test]
        public void MultiConsumer_MultiplePendingReaders()
        {
            var ch = ValkarnTask.Channel.CreateBounded<int>(capacity: 5, multiConsumer: true);

            var r1 = ch.Reader.ReadAsync();
            var r2 = ch.Reader.ReadAsync();

            ch.Writer.TryWrite(10);
            ch.Writer.TryWrite(20);

            Assert.IsTrue(r1.IsCompleted);
            Assert.IsTrue(r2.IsCompleted);
            Assert.AreEqual(10, r1.GetAwaiter().GetResult());
            Assert.AreEqual(20, r2.GetAwaiter().GetResult());
        }
    }
}
