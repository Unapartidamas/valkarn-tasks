// Copyright (c) Una Partida Mas. All rights reserved.
// Licensed under the MIT License. See LICENSE.md in the repository root.

using System;
using System.Runtime.CompilerServices;
using System.Threading;

namespace UnaPartidaMas.Valkarn.Tasks
{
    /// <summary>
    /// A simple concurrency limiter for fire-and-forget async operations.
    /// Tracks how many tasks are currently in-flight and rejects new work once the
    /// configured <see cref="MaxConcurrency"/> cap is reached.
    /// Thread-safe: <see cref="Release"/> may be called from background threads.
    /// Designed for use in ECS system loops to prevent unbounded task storms.
    /// </summary>
    public sealed class AsyncThrottle
    {
        int inFlight;
        readonly int maxConcurrency;

        /// <summary>
        /// Creates a new <see cref="AsyncThrottle"/> with the specified maximum concurrency.
        /// </summary>
        /// <param name="maxConcurrency">
        /// The maximum number of tasks allowed in-flight simultaneously. Must be greater than zero.
        /// </param>
        /// <exception cref="ArgumentOutOfRangeException">
        /// Thrown when <paramref name="maxConcurrency"/> is zero or negative.
        /// </exception>
        public AsyncThrottle(int maxConcurrency)
        {
            if (maxConcurrency <= 0)
                throw new ArgumentOutOfRangeException(nameof(maxConcurrency),
                    maxConcurrency, "maxConcurrency must be greater than zero.");
            this.maxConcurrency = maxConcurrency;
        }

        /// <summary>
        /// The number of tasks currently in-flight.
        /// </summary>
        public int InFlight
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => Volatile.Read(ref inFlight);
        }

        /// <summary>
        /// The maximum number of concurrent tasks allowed.
        /// </summary>
        public int MaxConcurrency
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => maxConcurrency;
        }

        /// <summary>
        /// Attempts to acquire a slot. Returns <c>true</c> and increments the in-flight
        /// counter if under the limit; returns <c>false</c> otherwise.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool TryAcquire()
        {
            while (true)
            {
                int current = Volatile.Read(ref inFlight);
                if (current >= maxConcurrency)
                    return false;

                if (Interlocked.CompareExchange(ref inFlight, current + 1, current) == current)
                    return true;
            }
        }

        /// <summary>
        /// Releases a slot, decrementing the in-flight counter.
        /// Called when an in-flight task completes (successfully or with a fault).
        /// Safe to call from any thread.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Release()
        {
            // Guard against underflow from unmatched Release() calls.
            // SpinWait not needed: contention on Release is rare (one call per task completion).
            while (true)
            {
                int current = Volatile.Read(ref inFlight);
                if (current <= 0) return;
                if (Interlocked.CompareExchange(ref inFlight, current - 1, current) == current)
                    return;
            }
        }
    }

    /// <summary>
    /// Extension methods for throttled fire-and-forget patterns.
    /// </summary>
    public static class AsyncThrottleExtensions
    {
        /// <summary>Pooled state for pending throttled-discard callbacks. Avoids per-call allocation.</summary>
        sealed class ThrottleForgetState : IPoolNode<ThrottleForgetState>
        {
            static ValkarnPool<ThrottleForgetState> s_pool;
            static ValkarnPool<ThrottleForgetState> Pool => ValkarnPool<ThrottleForgetState>.GetOrCreate(ref s_pool);

            ThrottleForgetState nextNode;
            public ref ThrottleForgetState NextNode => ref nextNode;

            internal ValkarnTask.ISource Source;
            internal uint Token;
            internal AsyncThrottle Throttle;

            internal static ThrottleForgetState Rent(ValkarnTask.ISource source, uint token, AsyncThrottle throttle)
            {
                var s = Pool.TryRent() ?? Pool.TrackNew(new ThrottleForgetState());
                s.Source = source;
                s.Token = token;
                s.Throttle = throttle;
                return s;
            }

            internal void Return()
            {
                Source = null;
                Token = 0;
                Throttle = null;
                Pool.TryReturn(this);
            }
        }

        static readonly Action<object> s_throttleContinuation = static state =>
        {
            var s = (ThrottleForgetState)state;
            var source = s.Source;
            var token = s.Token;
            var throttle = s.Throttle;
            s.Return(); // Return to pool immediately

            try
            {
                var status = source.UnsafeGetStatus();
                if (status == ValkarnTask.Status.Faulted)
                {
                    try { source.GetResult(token); }
                    catch (Exception ex) { ValkarnTask.PublishUnobservedException(ex); }
                }
                else
                {
                    try { source.GetResult(token); } catch { }
                }
            }
            finally
            {
                throttle.Release();
            }
        };

        /// <summary>
        /// Fire-and-forget with throttle release: calls <see cref="AsyncThrottle.Release"/>
        /// when the task completes (whether it succeeds, faults, or cancels).
        /// Faulted exceptions are published via <see cref="ValkarnTask.PublishUnobservedException"/>.
        /// Zero allocation on sync-completed tasks. Pooled allocation on pending tasks.
        /// </summary>
        /// <param name="task">The task to observe.</param>
        /// <param name="throttle">The throttle whose slot to release on completion.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void ThrottledForget(this ValkarnTask task, AsyncThrottle throttle)
        {
            if (task.source == null)
            {
                throttle.Release();
                return;
            }

            var status = task.source.UnsafeGetStatus();
            if (status.IsCompleted())
            {
                if (status == ValkarnTask.Status.Faulted)
                {
                    try { task.source.GetResult(task.token); }
                    catch (Exception ex) { ValkarnTask.PublishUnobservedException(ex); }
                }
                else
                {
                    try { task.source.GetResult(task.token); } catch { }
                }

                throttle.Release();
                return;
            }

            // Pending — register for observation when complete (pooled state)
            task.source.OnCompleted(s_throttleContinuation,
                ThrottleForgetState.Rent(task.source, task.token, throttle), task.token);
        }

        /// <summary>
        /// Generic variant of <see cref="ThrottledForget(ValkarnTask, AsyncThrottle)"/>.
        /// Fire-and-forget with throttle release for <see cref="ValkarnTask{T}"/>.
        /// Faulted exceptions are published via <see cref="ValkarnTask.PublishUnobservedException"/>.
        /// Zero allocation on sync-completed tasks. Pooled allocation on pending tasks.
        /// </summary>
        /// <param name="task">The task to observe.</param>
        /// <param name="throttle">The throttle whose slot to release on completion.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void ThrottledForget<T>(this ValkarnTask<T> task, AsyncThrottle throttle)
        {
            if (task.source == null)
            {
                throttle.Release();
                return;
            }

            var status = task.source.UnsafeGetStatus();
            if (status.IsCompleted())
            {
                if (status == ValkarnTask.Status.Faulted)
                {
                    try { ((ValkarnTask.ISource)task.source).GetResult(task.token); }
                    catch (Exception ex) { ValkarnTask.PublishUnobservedException(ex); }
                }
                else
                {
                    try { ((ValkarnTask.ISource)task.source).GetResult(task.token); } catch { }
                }

                throttle.Release();
                return;
            }

            // Pending — register for observation when complete (pooled state)
            ((ValkarnTask.ISource)task.source).OnCompleted(s_throttleContinuation,
                ThrottleForgetState.Rent(task.source, task.token, throttle), task.token);
        }
    }
}
