// Copyright (c) Una Partida Mas. All rights reserved.
// Licensed under the MIT License. See LICENSE.md in the repository root.

#if UNITY_5_3_OR_NEWER
using System;
using System.Buffers;
using System.Threading;
using Unity.Jobs;

namespace UnaPartidaMas.Valkarn.Tasks.Bridge
{
    /// <summary>
    /// Batch <see cref="JobHandle"/> WhenAll support — awaits multiple JobHandles concurrently.
    /// Partial extension of <see cref="JobHandleExtensions"/>.
    /// </summary>
    public static partial class JobHandleExtensions
    {
        /// <summary>
        /// Awaits all <paramref name="handles"/> concurrently, completing when every job finishes.
        /// Fast path: if ALL handles are already completed, calls <see cref="JobHandle.Complete"/>
        /// on each and returns <see cref="ValkarnTask.CompletedTask"/> immediately.
        /// </summary>
        public static ValkarnTask WhenAll(
            params JobHandle[] handles)
        {
            return WhenAll(handles, PlayerLoopTiming.Update, default);
        }

        /// <summary>
        /// Awaits all <paramref name="handles"/> concurrently with configurable timing and cancellation.
        /// </summary>
        public static ValkarnTask WhenAll(
            JobHandle[] handles,
            PlayerLoopTiming timing,
            CancellationToken cancellationToken = default)
        {
            if (handles == null) ThrowHelper.ThrowArgumentNull(nameof(handles));

            if (handles.Length == 0)
                return ValkarnTask.CompletedTask;

            // Fast path: all handles already completed
            bool allCompleted = true;
            for (int i = 0; i < handles.Length; i++)
            {
                if (!handles[i].IsCompleted)
                {
                    allCompleted = false;
                    break;
                }
            }

            if (allCompleted)
            {
                for (int i = 0; i < handles.Length; i++)
                    handles[i].Complete();
                return ValkarnTask.CompletedTask;
            }

            // Flush batched jobs so workers begin executing immediately.
            // Without this, jobs may stay queued until the next real
            // PlayerLoop tick, which never arrives in EditMode / tests.
            JobHandle.ScheduleBatchedJobs();

            var promise = JobHandleArrayPromise.Create(handles, timing, cancellationToken, out var token);
            return new ValkarnTask(promise, token);
        }

        /// <summary>
        /// Converts an array of <see cref="JobHandle"/> to a <see cref="ValkarnTask"/> that completes
        /// when all jobs finish. Polls each frame at the specified <paramref name="timing"/>.
        /// </summary>
        public static ValkarnTask ToValkarnTask(
            this JobHandle[] handles,
            PlayerLoopTiming timing = PlayerLoopTiming.Update,
            CancellationToken cancellationToken = default)
        {
            return WhenAll(handles, timing, cancellationToken);
        }
    }

    /// <summary>
    /// Pooled promise that polls an array of <see cref="JobHandle"/> each frame via
    /// <see cref="IPlayerLoopItem"/>. Completes the associated <see cref="ValkarnTaskCompletionCore{T}"/>
    /// when all jobs finish or cancellation is requested.
    /// Stores handles in a pooled array rented from <see cref="ArrayPool{T}"/>.
    /// </summary>
    public sealed class JobHandleArrayPromise : ValkarnTask.ISource, IPlayerLoopItem,
        IPoolNode<JobHandleArrayPromise>
    {
        static ValkarnPool<JobHandleArrayPromise> s_pool;
        static ValkarnPool<JobHandleArrayPromise> Pool => ValkarnPool<JobHandleArrayPromise>.GetOrCreate(ref s_pool);

        JobHandleArrayPromise nextNode;
        public ref JobHandleArrayPromise NextNode => ref nextNode;

        JobHandle[] handles;
        int handleCount;
        int pendingCount;
        ValkarnCompletionCore<AsyncUnit> core;
        int returned;
        CancellationToken cancellationToken;

        JobHandleArrayPromise() { }

        /// <summary>
        /// Creates a <see cref="JobHandleArrayPromise"/> that polls all given handles
        /// and registers on the PlayerLoop for per-frame polling.
        /// </summary>
        public static JobHandleArrayPromise Create(
            JobHandle[] sourceHandles,
            PlayerLoopTiming timing,
            CancellationToken ct,
            out uint token)
        {
            var p = Pool.TryRent() ?? Pool.TrackNew(new JobHandleArrayPromise());
            p.returned = 0;
            p.cancellationToken = ct;

            var count = sourceHandles.Length;
            p.handleCount = count;
            p.pendingCount = count;

            // Rent from ArrayPool to avoid per-call allocation
            var rented = ArrayPool<JobHandle>.Shared.Rent(count);
            Array.Copy(sourceHandles, 0, rented, 0, count);
            p.handles = rented;

            token = p.core.Token;

            PlayerLoopHelper.AddAction(timing, p);
            return p;
        }

        // ── IPlayerLoopItem ──

        public bool MoveNext()
        {
            // Check cancellation first
            if (cancellationToken.IsCancellationRequested)
            {
                CompleteAllRemainingHandles();
                core.TrySetCanceled(cancellationToken);
                return false;
            }

            // Ensure batched jobs are dispatched to worker threads so
            // IsCompleted can transition to true.  Without this call
            // jobs may remain queued indefinitely in EditMode / batch mode
            // because Unity only flushes at the end of a *real* frame.
            JobHandle.ScheduleBatchedJobs();

            // Dense-packing: only iterate pending handles [0..pendingCount).
            // When a handle completes, swap it with the last pending handle and
            // shrink the window. Iterate backwards so the swap never skips elements.
            for (int i = pendingCount - 1; i >= 0; i--)
            {
                if (handles[i].IsCompleted)
                {
                    handles[i].Complete();
                    pendingCount--;
                    handles[i] = handles[pendingCount];
                    handles[pendingCount] = default;
                }
            }

            if (pendingCount == 0)
            {
                core.TrySetResult(default);
                return false;
            }

            return true;
        }

        void CompleteAllRemainingHandles()
        {
            for (int i = 0; i < pendingCount; i++)
            {
                handles[i].Complete();
                handles[i] = default;
            }
            pendingCount = 0;
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

            var h = handles;
            handles = null;
            var count = handleCount;
            handleCount = 0;
            pendingCount = 0;
            if (h != null)
                ArrayPool<JobHandle>.Shared.Return(h, clearArray: true);

            cancellationToken = default;
            core.Reset();
            Pool.TryReturn(this);
        }
    }
}
#endif
