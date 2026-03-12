// Copyright (c) Una Partida Mas. All rights reserved.
// Licensed under the MIT License. See LICENSE.md in the repository root.

#if UNITY_5_3_OR_NEWER
using System;
using System.Threading;
using Unity.Jobs;

namespace UnaPartidaMas.Valkarn.Tasks.Bridge
{
    /// <summary>
    /// Polls a JobHandle each frame via IPlayerLoopItem. Completes the associated
    /// ValkarnTaskCompletionCore when the job finishes or cancellation is requested.
    /// Registered on PlayerLoopTiming.Update by default.
    ///
    /// Design: architecture.md §11.1
    /// </summary>
    public sealed class JobPromise<TJob> : ValkarnTask.ISource, IPlayerLoopItem,
        IPoolNode<JobPromise<TJob>>
        where TJob : struct
    {
        static ValkarnPool<JobPromise<TJob>> s_pool;
        static ValkarnPool<JobPromise<TJob>> Pool => ValkarnPool<JobPromise<TJob>>.GetOrCreate(ref s_pool);

        JobPromise<TJob> nextNode;
        public ref JobPromise<TJob> NextNode => ref nextNode;

        JobHandle handle;
        ValkarnCompletionCore<AsyncUnit> core;
        int returned;
        CancellationToken cancellationToken;

        JobPromise() { }

        /// <summary>
        /// Creates a JobPromise that polls the given handle and registers it
        /// on the PlayerLoop for per-frame polling.
        /// </summary>
        public static JobPromise<TJob> Create(JobHandle handle, CancellationToken ct, out uint token)
        {
            var p = Pool.TryRent() ?? Pool.TrackNew(new JobPromise<TJob>());
            p.returned = 0;
            p.handle = handle;
            p.cancellationToken = ct;
            token = p.core.Token;

            // Register on Update timing for per-frame polling
            PlayerLoopHelper.AddAction(PlayerLoopTiming.Update, p);
            return p;
        }

        // ── IPlayerLoopItem ──

        public bool MoveNext()
        {
            if (cancellationToken.IsCancellationRequested)
            {
                handle.Complete(); // must complete to avoid job leak
                core.TrySetCanceled(cancellationToken);
                return false;
            }

            if (handle.IsCompleted)
            {
                handle.Complete(); // finalize
                core.TrySetResult(default);
                return false;
            }

            return true; // keep polling
        }

        // ── ISource ──

        public ValkarnTask.Status GetStatus(uint token) => core.GetStatus(token);
        public ValkarnTask.Status UnsafeGetStatus() => core.UnsafeGetStatus();

        void ValkarnTask.ISource.GetResult(uint token)
        {
            try { core.GetResult(token); }
            finally { TryReturn(); }
        }

        public void OnCompleted(Action<object> continuation, object state, uint token)
            => core.OnCompleted(continuation, state, token);

        void TryReturn()
        {
            if (Interlocked.Exchange(ref returned, 1) != 0) return;
            core.Reset();
            handle = default;
            cancellationToken = default;
            Pool.TryReturn(this);
        }
    }
}
#endif
