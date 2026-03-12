// Copyright (c) Una Partida Mas. All rights reserved.
// Licensed under the MIT License. See LICENSE.md in the repository root.

using System;
using System.Collections.Generic;

namespace UnaPartidaMas.Valkarn.Tasks
{
    /// <summary>
    /// Unbounded channel backed by Queue (all operations are inside lock(gate)).
    /// Zero-alloc in steady state (items in queue, no waiting readers).
    ///
    /// Single-consumer optimization: embeds a reusable ValkarnTaskCompletionCore in the
    /// channel itself, avoiding PooledPromise rent/return overhead per ReadAsync.
    /// Multi-consumer: still uses Queue of PooledPromise (N concurrent readers).
    /// </summary>
    internal sealed class UnboundedChannelImpl<T> : ValkarnTask.ISource<T>
    {
        readonly Queue<T> items = new();
        readonly object gate = new();
        readonly bool multiConsumer;

        // Multi-consumer pending readers (null for single-consumer — avoids Queue alloc)
        readonly Queue<PendingReader> pendingReaders;

        // Single-consumer embedded reader core (avoids PooledPromise alloc per read)
        ValkarnCompletionCore<T> readerCore;
        bool hasPendingReader;

        // Completion
        bool completed;
        ValkarnTask.Promise completionPromise;

        internal UnboundedChannelImpl(bool multiConsumer)
        {
            this.multiConsumer = multiConsumer;
            if (multiConsumer)
                pendingReaders = new Queue<PendingReader>();
            completionPromise = new ValkarnTask.Promise();
        }

        internal static Channel<T> Create(bool multiConsumer)
        {
            var impl = new UnboundedChannelImpl<T>(multiConsumer);
            return new Channel<T>(new ReaderImpl(impl), new WriterImpl(impl));
        }

        struct PendingReader
        {
            public ValkarnTask.PooledPromise<T> Promise;
        }

        // ── Write Operations ──

        bool TryWriteCore(T item)
        {
            lock (gate)
            {
                if (completed)
                    return false;

                // Hand off directly to a waiting reader if any
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

                // No waiting reader — enqueue
                items.Enqueue(item);
                return true;
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

                // If queue is already empty, signal completion
                if (items.Count == 0)
                    completionPromise.TrySetResult();
            }
        }

        // ── Read Operations ──

        ValkarnTask<T> ReadAsyncCore()
        {
            lock (gate)
            {
                // Try dequeue first
                if (items.Count > 0)
                {
                    var item = items.Dequeue();

                    // Check if this was the last item and channel is completed
                    if (completed && items.Count == 0)
                        completionPromise.TrySetResult();

                    return ValkarnTask.FromResult(item);
                }

                // Queue is empty
                if (completed)
                    return ValkarnTask.FromException<T>(new ChannelClosedException());

                // No items available — pend
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
                if (items.Count > 0)
                {
                    item = items.Dequeue();

                    if (completed && items.Count == 0)
                        completionPromise.TrySetResult();

                    return true;
                }

                item = default;
                return false;
            }
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
            readonly UnboundedChannelImpl<T> parent;
            internal ReaderImpl(UnboundedChannelImpl<T> parent) => this.parent = parent;

            public override ValkarnTask<T> ReadAsync() => parent.ReadAsyncCore();
            public override bool TryRead(out T item) => parent.TryReadCore(out item);
            public override ValkarnTask Completion => parent.completionPromise.Task;
        }

        internal sealed class WriterImpl : ChannelWriter<T>
        {
            readonly UnboundedChannelImpl<T> parent;
            internal WriterImpl(UnboundedChannelImpl<T> parent) => this.parent = parent;

            public override bool TryWrite(T item) => parent.TryWriteCore(item);

            public override ValkarnTask WriteAsync(T item)
            {
                if (parent.TryWriteCore(item))
                    return ValkarnTask.CompletedTask;

                return ValkarnTask.FromException(new ChannelClosedException());
            }

            public override void Complete() => parent.CompleteCore();
        }
    }
}
