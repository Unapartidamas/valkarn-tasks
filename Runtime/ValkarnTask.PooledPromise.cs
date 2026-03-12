// Copyright (c) Una Partida Mas. All rights reserved.
// Licensed under the MIT License. See LICENSE.md in the repository root.

using System;
using System.Runtime.CompilerServices;
using System.Threading;

// Double-return guards prevent pool corruption if GetResult is called twice.

namespace UnaPartidaMas.Valkarn.Tasks
{
    public readonly partial struct ValkarnTask
    {
        /// <summary>
        /// Non-generic auto-reset pooled completion source for void ValkarnTask.
        /// After GetResult is called, the source resets and returns to pool.
        /// </summary>
        public sealed class PooledPromise
            : ValkarnTask.ISource, IPoolNode<PooledPromise>
        {
            static ValkarnPool<PooledPromise> s_pool;
            static ValkarnPool<PooledPromise> Pool => ValkarnPool<PooledPromise>.GetOrCreate(ref s_pool);

            PooledPromise nextNode;
            public ref PooledPromise NextNode => ref nextNode;

            ValkarnCompletionCore<AsyncUnit> core;
            uint cachedToken;
            int returned;

            PooledPromise() { }

            /// <summary>Creates a pending pooled promise.</summary>
            public static PooledPromise Create(out uint token)
            {
                var p = Pool.TryRent() ?? Pool.TrackNew(new PooledPromise());
                p.returned = 0;
                token = p.core.Token;
                p.cachedToken = token;
                return p;
            }

            /// <summary>Creates a pre-completed pooled promise.</summary>
            public static PooledPromise CreateCompleted(out uint token)
            {
                var p = Pool.TryRent() ?? Pool.TrackNew(new PooledPromise());
                p.returned = 0;
                token = p.core.Token;
                p.cachedToken = token;
                p.core.TrySetResult(AsyncUnit.Default);
                return p;
            }

            public bool TrySetResult() => core.TrySetResult(AsyncUnit.Default);
            public bool TrySetException(Exception ex) => core.TrySetException(ex);
            public bool TrySetCanceled(CancellationToken ct = default) => core.TrySetCanceled(ct);

            /// <summary>Gets a ValkarnTask backed by this pooled promise.</summary>
            public ValkarnTask Task => new ValkarnTask(this, cachedToken);

            // ── ISource ──

            public Status GetStatus(uint token) => core.GetStatus(token);

            void ValkarnTask.ISource.GetResult(uint token)
            {
                try { core.GetResult(token); }
                finally { TryReturn(); }
            }

            public void OnCompleted(Action<object> continuation, object state, uint token)
                => core.OnCompleted(continuation, state, token);

            public Status UnsafeGetStatus() => core.UnsafeGetStatus();

            void TryReturn()
            {
                if (Interlocked.Exchange(ref returned, 1) != 0) return;
                core.Reset();
                Pool.TryReturn(this);
            }
        }

        /// <summary>
        /// Auto-reset pooled completion source. After GetResult is called,
        /// the source resets and returns to pool. Used by channels and repeating patterns.
        /// </summary>
        public sealed class PooledPromise<T>
            : ValkarnTask.ISource<T>, IPoolNode<PooledPromise<T>>
        {
            static ValkarnPool<PooledPromise<T>> s_pool;
            static ValkarnPool<PooledPromise<T>> Pool => ValkarnPool<PooledPromise<T>>.GetOrCreate(ref s_pool);

            PooledPromise<T> nextNode;
            public ref PooledPromise<T> NextNode => ref nextNode;

            ValkarnCompletionCore<T> core;
            uint cachedToken;
            int returned;

            PooledPromise() { }

            /// <summary>Creates a pending pooled promise.</summary>
            public static PooledPromise<T> Create(out uint token)
            {
                var p = Pool.TryRent() ?? Pool.TrackNew(new PooledPromise<T>());
                p.returned = 0;
                token = p.core.Token;
                p.cachedToken = token;
                return p;
            }

            /// <summary>Creates a pre-completed pooled promise.</summary>
            public static PooledPromise<T> CreateCompleted(T result, out uint token)
            {
                var p = Pool.TryRent() ?? Pool.TrackNew(new PooledPromise<T>());
                p.returned = 0;
                token = p.core.Token;
                p.cachedToken = token;
                p.core.TrySetResult(result);
                return p;
            }

            public bool TrySetResult(T result) => core.TrySetResult(result);
            public bool TrySetException(Exception ex) => core.TrySetException(ex);
            public bool TrySetCanceled(CancellationToken ct = default) => core.TrySetCanceled(ct);

            /// <summary>Gets a ValkarnTask{T} backed by this pooled promise.</summary>
            public ValkarnTask<T> Task => new ValkarnTask<T>(this, cachedToken);

            // ── ISource<T> ──

            public Status GetStatus(uint token) => core.GetStatus(token);

            T ValkarnTask.ISource<T>.GetResult(uint token)
            {
                try { return core.GetResult(token); }
                finally { TryReturn(); }
            }

            void ValkarnTask.ISource.GetResult(uint token)
            {
                try { core.GetResult(token); }
                finally { TryReturn(); }
            }

            public void OnCompleted(Action<object> continuation, object state, uint token)
                => core.OnCompleted(continuation, state, token);

            public Status UnsafeGetStatus() => core.UnsafeGetStatus();

            void TryReturn()
            {
                if (Interlocked.Exchange(ref returned, 1) != 0) return;
                core.Reset();
                Pool.TryReturn(this);
            }
        }
    }
}
