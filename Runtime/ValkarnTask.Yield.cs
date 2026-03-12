// Copyright (c) Una Partida Mas. All rights reserved.
// Licensed under the MIT License. See LICENSE.md in the repository root.

using System;
using System.Runtime.CompilerServices;
using System.Threading;

namespace UnaPartidaMas.Valkarn.Tasks
{
    public readonly partial struct ValkarnTask
    {
        /// <summary>
        /// Returns an awaitable that completes on the NEXT tick of the specified PlayerLoop timing.
        /// IsCompleted always returns false — forces at least one suspension.
        /// </summary>
        public static YieldAwaitable Yield(PlayerLoopTiming timing = PlayerLoopTiming.Update)
            => new YieldAwaitable(timing);

        /// <summary>
        /// Waits until the next frame (guarantees Time.frameCount advances).
        /// </summary>
        public static ValkarnTask NextFrame(PlayerLoopTiming timing = PlayerLoopTiming.Update,
            System.Threading.CancellationToken ct = default)
        {
            return new ValkarnTask(
                NextFramePromise.Create(timing, ct, out var token), token);
        }

        /// <summary>
        /// Waits for the specified number of frames to pass.
        /// </summary>
        public static ValkarnTask DelayFrame(int frameCount, PlayerLoopTiming timing = PlayerLoopTiming.Update,
            System.Threading.CancellationToken ct = default)
        {
            if (frameCount <= 0)
            {
                ct.ThrowIfCancellationRequested();
                return CompletedTask;
            }
            return new ValkarnTask(
                DelayFramePromise.Create(frameCount, timing, ct, out var token), token);
        }
    }

    // ── YieldAwaitable (zero-alloc struct awaitable) ──

    public readonly struct YieldAwaitable
    {
        readonly PlayerLoopTiming timing;

        internal YieldAwaitable(PlayerLoopTiming timing) => this.timing = timing;

        public Awaiter GetAwaiter() => new Awaiter(timing);

        public readonly struct Awaiter : ICriticalNotifyCompletion
        {
            readonly PlayerLoopTiming timing;

            internal Awaiter(PlayerLoopTiming timing) => this.timing = timing;

            public bool IsCompleted => false; // always suspend

            public void GetResult() { }

            public void OnCompleted(Action continuation)
            {
                PlayerLoopHelper.AddContinuation(timing, InvokeAction, continuation);
            }

            public void UnsafeOnCompleted(Action continuation)
            {
                PlayerLoopHelper.AddContinuation(timing, InvokeAction, continuation);
            }

            static readonly Action<object> InvokeAction = static state => ((Action)state)();
        }
    }

    // ── NextFramePromise ──

    internal sealed class NextFramePromise
        : ValkarnTask.ISource, IPlayerLoopItem, IPoolNode<NextFramePromise>
    {
        static ValkarnPool<NextFramePromise> s_pool;
        static ValkarnPool<NextFramePromise> Pool => ValkarnPool<NextFramePromise>.GetOrCreate(ref s_pool);

        NextFramePromise nextNode;
        public ref NextFramePromise NextNode => ref nextNode;

        ValkarnCompletionCore<AsyncUnit> core;
        int returned;
        int capturedFrame;
        System.Threading.CancellationToken ct;

        internal static NextFramePromise Create(PlayerLoopTiming timing,
            System.Threading.CancellationToken ct, out uint token)
        {
            var p = Pool.TryRent() ?? Pool.TrackNew(new NextFramePromise());
            p.returned = 0;
            p.capturedFrame = TimeProvider.Current.FrameCount;
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
                return false; // pool return happens in GetResult
            }

            // Subtraction-based comparison handles int wrap-around safely
            if (TimeProvider.Current.FrameCount - capturedFrame > 0)
            {
                core.TrySetResult(AsyncUnit.Default);
                return false; // pool return happens in GetResult
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
            core.Reset(); ct = default; Pool.TryReturn(this);
        }
    }

    // ── DelayFramePromise ──

    internal sealed class DelayFramePromise
        : ValkarnTask.ISource, IPlayerLoopItem, IPoolNode<DelayFramePromise>
    {
        static ValkarnPool<DelayFramePromise> s_pool;
        static ValkarnPool<DelayFramePromise> Pool => ValkarnPool<DelayFramePromise>.GetOrCreate(ref s_pool);

        DelayFramePromise nextNode;
        public ref DelayFramePromise NextNode => ref nextNode;

        ValkarnCompletionCore<AsyncUnit> core;
        int returned;
        int remaining;
        System.Threading.CancellationToken ct;

        internal static DelayFramePromise Create(int frameCount, PlayerLoopTiming timing,
            System.Threading.CancellationToken ct, out uint token)
        {
            var p = Pool.TryRent() ?? Pool.TrackNew(new DelayFramePromise());
            p.returned = 0;
            p.remaining = frameCount;
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

            if (--remaining <= 0)
            {
                core.TrySetResult(AsyncUnit.Default);
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
            core.Reset(); ct = default; Pool.TryReturn(this);
        }
    }
}
