// Copyright (c) Una Partida Mas. All rights reserved.
// Licensed under the MIT License. See LICENSE.md in the repository root.

using System;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading;

namespace UnaPartidaMas.Valkarn.Tasks
{
    public readonly partial struct ValkarnTask
    {
        /// <summary>Delays for the specified number of milliseconds.</summary>
        public static ValkarnTask Delay(int millisecondsDelay,
            DelayType delayType = DelayType.DeltaTime,
            PlayerLoopTiming timing = PlayerLoopTiming.Update,
            CancellationToken ct = default)
        {
            if (millisecondsDelay <= 0)
            {
                ct.ThrowIfCancellationRequested();
                return CompletedTask;
            }
            var duration = TimeSpan.FromMilliseconds(millisecondsDelay);
            return Delay(duration, delayType, timing, ct);
        }

        /// <summary>Delays for the specified TimeSpan.</summary>
        public static ValkarnTask Delay(TimeSpan delay,
            DelayType delayType = DelayType.DeltaTime,
            PlayerLoopTiming timing = PlayerLoopTiming.Update,
            CancellationToken ct = default)
        {
            if (delay <= TimeSpan.Zero)
            {
                ct.ThrowIfCancellationRequested();
                return CompletedTask;
            }

            return delayType switch
            {
                DelayType.DeltaTime => new ValkarnTask(
                    DeltaTimeDelayPromise.Create(delay, timing, ct, out var t1), t1),
                DelayType.UnscaledDeltaTime => new ValkarnTask(
                    UnscaledDeltaTimeDelayPromise.Create(delay, timing, ct, out var t2), t2),
                DelayType.Realtime => new ValkarnTask(
                    RealtimeDelayPromise.Create(delay, timing, ct, out var t3), t3),
                _ => throw new ArgumentOutOfRangeException(nameof(delayType))
            };
        }

        // Overloads with cancellationToken named parameter for UniTask compatibility
        public static ValkarnTask Delay(int millisecondsDelay, CancellationToken cancellationToken)
            => Delay(millisecondsDelay, DelayType.DeltaTime, PlayerLoopTiming.Update, cancellationToken);

        public static ValkarnTask Delay(TimeSpan delay, CancellationToken cancellationToken)
            => Delay(delay, DelayType.DeltaTime, PlayerLoopTiming.Update, cancellationToken);
    }

    // ── DeltaTime Delay ──

    internal sealed class DeltaTimeDelayPromise
        : ValkarnTask.ISource, IPlayerLoopItem, IPoolNode<DeltaTimeDelayPromise>
    {
        static ValkarnPool<DeltaTimeDelayPromise> s_pool;
        static ValkarnPool<DeltaTimeDelayPromise> Pool => ValkarnPool<DeltaTimeDelayPromise>.GetOrCreate(ref s_pool);

        DeltaTimeDelayPromise nextNode;
        public ref DeltaTimeDelayPromise NextNode => ref nextNode;

        ValkarnCompletionCore<AsyncUnit> core;
        int returned;
        float elapsed;
        float targetSeconds;
        CancellationToken ct;

        internal static DeltaTimeDelayPromise Create(TimeSpan delay, PlayerLoopTiming timing,
            CancellationToken ct, out uint token)
        {
            var p = Pool.TryRent() ?? Pool.TrackNew(new DeltaTimeDelayPromise());
            p.returned = 0;
            p.elapsed = 0f;
            p.targetSeconds = (float)delay.TotalSeconds;
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

            elapsed += TimeProvider.Current.DeltaTime;
            if (elapsed >= targetSeconds)
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

    // ── Unscaled DeltaTime Delay ──

    internal sealed class UnscaledDeltaTimeDelayPromise
        : ValkarnTask.ISource, IPlayerLoopItem, IPoolNode<UnscaledDeltaTimeDelayPromise>
    {
        static ValkarnPool<UnscaledDeltaTimeDelayPromise> s_pool;
        static ValkarnPool<UnscaledDeltaTimeDelayPromise> Pool => ValkarnPool<UnscaledDeltaTimeDelayPromise>.GetOrCreate(ref s_pool);

        UnscaledDeltaTimeDelayPromise nextNode;
        public ref UnscaledDeltaTimeDelayPromise NextNode => ref nextNode;

        ValkarnCompletionCore<AsyncUnit> core;
        int returned;
        float elapsed;
        float targetSeconds;
        CancellationToken ct;

        internal static UnscaledDeltaTimeDelayPromise Create(TimeSpan delay, PlayerLoopTiming timing,
            CancellationToken ct, out uint token)
        {
            var p = Pool.TryRent() ?? Pool.TrackNew(new UnscaledDeltaTimeDelayPromise());
            p.returned = 0;
            p.elapsed = 0f;
            p.targetSeconds = (float)delay.TotalSeconds;
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

            elapsed += TimeProvider.Current.UnscaledDeltaTime;
            if (elapsed >= targetSeconds)
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

    // ── Realtime Delay (Stopwatch-based) ──

    internal sealed class RealtimeDelayPromise
        : ValkarnTask.ISource, IPlayerLoopItem, IPoolNode<RealtimeDelayPromise>
    {
        static ValkarnPool<RealtimeDelayPromise> s_pool;
        static ValkarnPool<RealtimeDelayPromise> Pool => ValkarnPool<RealtimeDelayPromise>.GetOrCreate(ref s_pool);

        RealtimeDelayPromise nextNode;
        public ref RealtimeDelayPromise NextNode => ref nextNode;

        ValkarnCompletionCore<AsyncUnit> core;
        int returned;
        long startTimestamp;
        long targetTicks;
        CancellationToken ct;

        internal static RealtimeDelayPromise Create(TimeSpan delay, PlayerLoopTiming timing,
            CancellationToken ct, out uint token)
        {
            var p = Pool.TryRent() ?? Pool.TrackNew(new RealtimeDelayPromise());
            p.returned = 0;
            p.startTimestamp = TimeProvider.Current.GetTimestamp();
            p.targetTicks = (long)(delay.TotalSeconds * Stopwatch.Frequency);
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

            var elapsed = TimeProvider.Current.GetTimestamp() - startTimestamp;
            if (elapsed >= targetTicks)
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
