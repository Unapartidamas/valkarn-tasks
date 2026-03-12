// Copyright (c) Una Partida Mas. All rights reserved.
// Licensed under the MIT License. See LICENSE.md in the repository root.

using System;
using System.Collections.Generic;

namespace UnaPartidaMas.Valkarn.Tasks
{
    /// <summary>
    /// Bounded channel with fixed-capacity ring buffer.
    /// WriteAsync provides backpressure when buffer is full.
    ///
    /// Single-consumer optimization: embeds a reusable ValkarnTaskCompletionCore for the
    /// reader, avoiding PooledPromise rent/return per ReadAsync.
    /// Writers still use PooledPromise (multiple writers can be blocked simultaneously).
    /// </summary>
    internal sealed class BoundedChannelImpl<T> : ValkarnTask.ISource<T>
    {
        readonly object gate = new();
        readonly bool multiConsumer;

        // Ring buffer
        readonly T[] buffer;
        readonly int capacity;
        int head;   // next read position
        int tail;   // next write position
        int count;  // current items in buffer

        // Multi-consumer pending readers (null for single-consumer)
        readonly Queue<PendingReader> pendingReaders;

        // Single-consumer embedded reader core
        ValkarnCompletionCore<T> readerCore;
        bool hasPendingReader;

        // Pending writers (waiting for space — backpressure)
        readonly Queue<PendingWriter> pendingWriters = new();

        // Completion
        bool completed;
        ValkarnTask.Promise completionPromise;

        internal BoundedChannelImpl(int capacity, bool multiConsumer)
        {
            if (capacity <= 0) throw new ArgumentOutOfRangeException(nameof(capacity));

            this.capacity = capacity;
            this.multiConsumer = multiConsumer;
            this.buffer = new T[capacity];
            if (multiConsumer)
                this.pendingReaders = new Queue<PendingReader>();
            this.completionPromise = new ValkarnTask.Promise();
        }

        internal static Channel<T> Create(int capacity, bool multiConsumer)
        {
            var impl = new BoundedChannelImpl<T>(capacity, multiConsumer);
            return new Channel<T>(new ReaderImpl(impl), new WriterImpl(impl));
        }

        struct PendingReader
        {
            public ValkarnTask.PooledPromise<T> Promise;
        }

        struct PendingWriter
        {
            public T Item;
            public ValkarnTask.PooledPromise<AsyncUnit> Promise;
        }

        // ── Write Operations ──

        bool TryWriteCore(T item)
        {
            lock (gate)
            {
                if (completed)
                    return false;

                // Hand off to waiting reader
                if (multiConsumer)
                {
                    if (pendingReaders.Count > 0)
                    {
                        var reader = pendingReaders.Dequeue();
                        reader.Promise.TrySetResult(item);
                        return true;
                    }
                }
                else if (hasPendingReader)
                {
                    readerCore.TrySetResult(item);
                    hasPendingReader = false;
                    return true;
                }

                // Space in buffer
                if (count < capacity)
                {
                    Enqueue(item);
                    return true;
                }

                // Full — no room
                return false;
            }
        }

        ValkarnTask WriteAsyncCore(T item)
        {
            lock (gate)
            {
                if (completed)
                    return ValkarnTask.FromException(new ChannelClosedException());

                // Hand off to waiting reader
                if (multiConsumer)
                {
                    if (pendingReaders.Count > 0)
                    {
                        var reader = pendingReaders.Dequeue();
                        reader.Promise.TrySetResult(item);
                        return ValkarnTask.CompletedTask;
                    }
                }
                else if (hasPendingReader)
                {
                    readerCore.TrySetResult(item);
                    hasPendingReader = false;
                    return ValkarnTask.CompletedTask;
                }

                // Space in buffer
                if (count < capacity)
                {
                    Enqueue(item);
                    return ValkarnTask.CompletedTask;
                }

                // Full — block writer (backpressure)
                var promise = ValkarnTask.PooledPromise<AsyncUnit>.Create(out var token);
                pendingWriters.Enqueue(new PendingWriter
                {
                    Item = item,
                    Promise = promise
                });
                return new ValkarnTask<AsyncUnit>(promise, token).AsNonGeneric();
            }
        }

        void CompleteCore()
        {
            lock (gate)
            {
                if (completed) return;
                completed = true;

                // Fail all pending readers
                if (multiConsumer)
                {
                    while (pendingReaders.Count > 0)
                    {
                        var reader = pendingReaders.Dequeue();
                        reader.Promise.TrySetException(new ChannelClosedException());
                    }
                }
                else if (hasPendingReader)
                {
                    readerCore.TrySetException(new ChannelClosedException());
                    hasPendingReader = false;
                }

                // Fail all pending writers
                while (pendingWriters.Count > 0)
                {
                    var writer = pendingWriters.Dequeue();
                    writer.Promise.TrySetException(new ChannelClosedException());
                }

                // If buffer is empty, signal completion immediately
                if (count == 0)
                    completionPromise.TrySetResult();
            }
        }

        // ── Read Operations ──

        ValkarnTask<T> ReadAsyncCore()
        {
            lock (gate)
            {
                // Items in buffer
                if (count > 0)
                {
                    var item = Dequeue();

                    // Unblock a pending writer if any
                    if (pendingWriters.Count > 0)
                    {
                        var writer = pendingWriters.Dequeue();
                        Enqueue(writer.Item);
                        writer.Promise.TrySetResult(AsyncUnit.Default);
                    }

                    // Check completion
                    if (completed && count == 0)
                        completionPromise.TrySetResult();

                    return ValkarnTask.FromResult(item);
                }

                // Buffer empty — check for pending writers to hand off directly
                if (pendingWriters.Count > 0)
                {
                    var writer = pendingWriters.Dequeue();
                    writer.Promise.TrySetResult(AsyncUnit.Default);

                    if (completed && count == 0 && pendingWriters.Count == 0)
                        completionPromise.TrySetResult();

                    return ValkarnTask.FromResult(writer.Item);
                }

                // Buffer empty, no writers
                if (completed)
                    return ValkarnTask.FromException<T>(new ChannelClosedException());

                // Wait for an item
                if (multiConsumer)
                {
                    var promise = ValkarnTask.PooledPromise<T>.Create(out var token);
                    pendingReaders.Enqueue(new PendingReader { Promise = promise });
                    return new ValkarnTask<T>(promise, token);
                }
                else
                {
                    if (hasPendingReader)
                        ThrowHelper.ThrowMultipleAwaiters();

                    hasPendingReader = true;
                    return new ValkarnTask<T>(this, readerCore.Token);
                }
            }
        }

        bool TryReadCore(out T item)
        {
            lock (gate)
            {
                if (count > 0)
                {
                    item = Dequeue();

                    // Unblock a pending writer
                    if (pendingWriters.Count > 0)
                    {
                        var writer = pendingWriters.Dequeue();
                        Enqueue(writer.Item);
                        writer.Promise.TrySetResult(AsyncUnit.Default);
                    }

                    if (completed && count == 0)
                        completionPromise.TrySetResult();

                    return true;
                }

                // Check pending writers for direct handoff
                if (pendingWriters.Count > 0)
                {
                    var writer = pendingWriters.Dequeue();
                    item = writer.Item;
                    writer.Promise.TrySetResult(AsyncUnit.Default);

                    if (completed && count == 0 && pendingWriters.Count == 0)
                        completionPromise.TrySetResult();

                    return true;
                }

                item = default;
                return false;
            }
        }

        // ── Ring Buffer ──

        void Enqueue(T item)
        {
            buffer[tail] = item;
            tail = (tail + 1) % capacity;
            count++;
        }

        T Dequeue()
        {
            var item = buffer[head];
            buffer[head] = default; // clear reference for GC
            head = (head + 1) % capacity;
            count--;
            return item;
        }

        // ── ISource<T> (single-consumer embedded core) ──

        ValkarnTask.Status ValkarnTask.ISource.GetStatus(uint token) => readerCore.GetStatus(token);

        T ValkarnTask.ISource<T>.GetResult(uint token)
        {
            try { return readerCore.GetResult(token); }
            finally { lock (gate) { readerCore.Reset(); } }
        }

        void ValkarnTask.ISource.GetResult(uint token)
        {
            try { readerCore.GetResult(token); }
            finally { lock (gate) { readerCore.Reset(); } }
        }

        void ValkarnTask.ISource.OnCompleted(Action<object> continuation, object state, uint token)
            => readerCore.OnCompleted(continuation, state, token);

        ValkarnTask.Status ValkarnTask.ISource.UnsafeGetStatus() => readerCore.UnsafeGetStatus();

        // ── Reader / Writer ──

        internal sealed class ReaderImpl : ChannelReader<T>
        {
            readonly BoundedChannelImpl<T> parent;
            internal ReaderImpl(BoundedChannelImpl<T> parent) => this.parent = parent;

            public override ValkarnTask<T> ReadAsync() => parent.ReadAsyncCore();
            public override bool TryRead(out T item) => parent.TryReadCore(out item);
            public override ValkarnTask Completion => parent.completionPromise.Task;
        }

        internal sealed class WriterImpl : ChannelWriter<T>
        {
            readonly BoundedChannelImpl<T> parent;
            internal WriterImpl(BoundedChannelImpl<T> parent) => this.parent = parent;

            public override bool TryWrite(T item) => parent.TryWriteCore(item);
            public override ValkarnTask WriteAsync(T item) => parent.WriteAsyncCore(item);
            public override void Complete() => parent.CompleteCore();
        }
    }
}
