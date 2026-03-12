// Copyright (c) Una Partida Mas. All rights reserved.
// Licensed under the MIT License. See LICENSE.md in the repository root.

using System;
using System.Threading;

namespace UnaPartidaMas.Valkarn.Tasks
{
    public readonly partial struct ValkarnTask
    {
        /// <summary>
        /// Waits until the predicate returns true. Checked each PlayerLoop tick.
        /// </summary>
        public static ValkarnTask WaitUntil(Func<bool> predicate,
            PlayerLoopTiming timing = PlayerLoopTiming.Update,
            CancellationToken ct = default)
        {
            if (predicate == null) ThrowHelper.ThrowArgumentNull(nameof(predicate));
            return new ValkarnTask(
                WaitUntilPromise.Create(predicate, timing, ct, out var token), token);
        }

        /// <summary>
        /// Waits while the predicate returns true. Completes when it returns false.
        /// </summary>
        public static ValkarnTask WaitWhile(Func<bool> predicate,
            PlayerLoopTiming timing = PlayerLoopTiming.Update,
            CancellationToken ct = default)
        {
            if (predicate == null) ThrowHelper.ThrowArgumentNull(nameof(predicate));
            return new ValkarnTask(
                WaitWhilePromise.Create(predicate, timing, ct, out var token), token);
        }
    }

    // ── WaitUntilPromise ──

    internal sealed class WaitUntilPromise
        : ValkarnTask.ISource, IPlayerLoopItem, IPoolNode<WaitUntilPromise>
    {
        static ValkarnPool<WaitUntilPromise> s_pool;
        static ValkarnPool<WaitUntilPromise> Pool => ValkarnPool<WaitUntilPromise>.GetOrCreate(ref s_pool);

        WaitUntilPromise nextNode;
        public ref WaitUntilPromise NextNode => ref nextNode;

        ValkarnCompletionCore<AsyncUnit> core;
        int returned;
        Func<bool> predicate;
        CancellationToken ct;

        internal static WaitUntilPromise Create(Func<bool> predicate, PlayerLoopTiming timing,
            CancellationToken ct, out uint token)
        {
            var p = Pool.TryRent() ?? Pool.TrackNew(new WaitUntilPromise());
            p.returned = 0;
            p.predicate = predicate;
            p.ct = ct;
            token = p.core.Token;
            PlayerLoopHelper.AddAction(timing, p);
            return p;
        }

        public bool MoveNext()
        {
            if (ct.IsCancellationRequested)
            {
                core.TrySetCanceled(ct);
                return false;
            }

            try
            {
                if (predicate())
                {
                    core.TrySetResult(AsyncUnit.Default);
                    return false;
                }
            }
            catch (Exception ex)
            {
                core.TrySetException(ex);
                return false;
            }

            return true;
        }

        public ValkarnTask.Status GetStatus(uint token) => core.GetStatus(token);
        public void GetResult(uint token) { try { core.GetResult(token); } finally { TryReturn(); } }
        public void OnCompleted(Action<object> continuation, object state, uint token)
            => core.OnCompleted(continuation, state, token);
        public ValkarnTask.Status UnsafeGetStatus() => core.UnsafeGetStatus();

        void TryReturn()
        {
            if (Interlocked.Exchange(ref returned, 1) != 0) return;
            core.Reset();
            predicate = null;
            ct = default;
            Pool.TryReturn(this);
        }
    }

    // ── WaitWhilePromise ──

    internal sealed class WaitWhilePromise
        : ValkarnTask.ISource, IPlayerLoopItem, IPoolNode<WaitWhilePromise>
    {
        static ValkarnPool<WaitWhilePromise> s_pool;
        static ValkarnPool<WaitWhilePromise> Pool => ValkarnPool<WaitWhilePromise>.GetOrCreate(ref s_pool);

        WaitWhilePromise nextNode;
        public ref WaitWhilePromise NextNode => ref nextNode;

        ValkarnCompletionCore<AsyncUnit> core;
        int returned;
        Func<bool> predicate;
        CancellationToken ct;

        internal static WaitWhilePromise Create(Func<bool> predicate, PlayerLoopTiming timing,
            CancellationToken ct, out uint token)
        {
            var p = Pool.TryRent() ?? Pool.TrackNew(new WaitWhilePromise());
            p.returned = 0;
            p.predicate = predicate;
            p.ct = ct;
            token = p.core.Token;
            PlayerLoopHelper.AddAction(timing, p);
            return p;
        }

        public bool MoveNext()
        {
            if (ct.IsCancellationRequested)
            {
                core.TrySetCanceled(ct);
                return false;
            }

            try
            {
                if (!predicate())
                {
                    core.TrySetResult(AsyncUnit.Default);
                    return false;
                }
            }
            catch (Exception ex)
            {
                core.TrySetException(ex);
                return false;
            }

            return true;
        }

        public ValkarnTask.Status GetStatus(uint token) => core.GetStatus(token);
        public void GetResult(uint token) { try { core.GetResult(token); } finally { TryReturn(); } }
        public void OnCompleted(Action<object> continuation, object state, uint token)
            => core.OnCompleted(continuation, state, token);
        public ValkarnTask.Status UnsafeGetStatus() => core.UnsafeGetStatus();

        void TryReturn()
        {
            if (Interlocked.Exchange(ref returned, 1) != 0) return;
            core.Reset();
            predicate = null;
            ct = default;
            Pool.TryReturn(this);
        }
    }
}
