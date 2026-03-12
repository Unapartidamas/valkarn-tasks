// Copyright (c) Una Partida Mas. All rights reserved.
// Licensed under the MIT License. See LICENSE.md in the repository root.

#if UNITY_5_3_OR_NEWER
using System;
using System.Threading;
using Unity.Jobs;

namespace UnaPartidaMas.Valkarn.Tasks.Bridge
{
    /// <summary>
    /// Extension methods to bridge raw <see cref="JobHandle"/> to <see cref="ValkarnTask"/>.
    /// </summary>
    public static partial class JobHandleExtensions
    {
        /// <summary>
        /// Converts a <see cref="JobHandle"/> to a <see cref="ValkarnTask"/> that completes when
        /// the job finishes. Polls each frame at the specified <paramref name="timing"/>.
        /// Fast path: if the handle is already completed, returns <see cref="ValkarnTask.CompletedTask"/>.
        /// </summary>
        public static ValkarnTask ToValkarnTask(
            this JobHandle handle,
            PlayerLoopTiming timing = PlayerLoopTiming.Update,
            CancellationToken cancellationToken = default)
        {
            // Fast path: already done
            if (handle.IsCompleted)
            {
                handle.Complete();
                return ValkarnTask.CompletedTask;
            }

            // Flush batched jobs so workers begin executing immediately.
            // Without this, jobs may stay queued until the next real
            // PlayerLoop tick, which never arrives in EditMode / tests.
            JobHandle.ScheduleBatchedJobs();

            var promise = JobHandlePromise.Create(handle, timing, cancellationToken, out var token);
            return new ValkarnTask(promise, token);
        }
    }

    /// <summary>
    /// Non-generic promise that polls a <see cref="JobHandle"/> each frame.
    /// Follows the same pattern as <see cref="JobPromise{TJob}"/> but works
    /// with raw handles and supports configurable <see cref="PlayerLoopTiming"/>.
    /// </summary>
    public sealed class JobHandlePromise : ValkarnTask.ISource, IPlayerLoopItem,
        IPoolNode<JobHandlePromise>
    {
        static ValkarnPool<JobHandlePromise> s_pool;
        static ValkarnPool<JobHandlePromise> Pool => ValkarnPool<JobHandlePromise>.GetOrCreate(ref s_pool);

        JobHandlePromise nextNode;
        public ref JobHandlePromise NextNode => ref nextNode;

        JobHandle handle;
        ValkarnCompletionCore<AsyncUnit> core;
        int returned;
        CancellationToken cancellationToken;

        JobHandlePromise() { }

        public static JobHandlePromise Create(
            JobHandle handle,
            PlayerLoopTiming timing,
            CancellationToken ct,
            out uint token)
        {
            var p = Pool.TryRent() ?? Pool.TrackNew(new JobHandlePromise());
            p.returned = 0;
            p.handle = handle;
            p.cancellationToken = ct;
            token = p.core.Token;

            PlayerLoopHelper.AddAction(timing, p);
            return p;
        }

        // ── IPlayerLoopItem ──

        public bool MoveNext()
        {
            if (cancellationToken.IsCancellationRequested)
            {
                handle.Complete();
                core.TrySetCanceled(cancellationToken);
                return false;
            }

            // Ensure batched jobs are dispatched to worker threads so
            // IsCompleted can transition to true.  Without this call
            // jobs may remain queued indefinitely in EditMode / batch mode
            // because Unity only flushes at the end of a *real* frame.
            JobHandle.ScheduleBatchedJobs();

            if (handle.IsCompleted)
            {
                handle.Complete();
                core.TrySetResult(default);
                return false;
            }

            return true;
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
