// Copyright (c) Una Partida Mas. All rights reserved.
// Licensed under the MIT License. See LICENSE.md in the repository root.

#if VTASKS_HAS_BURST && VTASKS_HAS_COLLECTIONS
using System;
using System.Collections.Generic;
using Unity.Collections;

namespace UnaPartidaMas.Valkarn.Tasks.Burst
{
    /// <summary>
    /// Bridges the Burst-compatible <see cref="NativeScheduler"/> and <see cref="NativeTimerHeap"/>
    /// to the managed world. Runs as an <see cref="IPlayerLoopItem"/> each frame, draining
    /// native queues and dispatching managed callbacks.
    /// </summary>
    public sealed class BurstSchedulerRunner : IPlayerLoopItem, IDisposable
    {
        NativeScheduler scheduler;
        NativeTimerHeap timerHeap;
        NativeList<ScheduledWork> drainBuffer;
        NativeList<int> expiredBuffer;

        readonly Dictionary<int, Action> workCallbacks = new Dictionary<int, Action>();
        readonly Dictionary<int, Action> timerCallbacks = new Dictionary<int, Action>();

        volatile bool disposed;

        BurstSchedulerRunner(int initialCapacity)
        {
            scheduler = new NativeScheduler(initialCapacity, Allocator.Persistent);
            timerHeap = new NativeTimerHeap(initialCapacity, Allocator.Persistent);
            drainBuffer = new NativeList<ScheduledWork>(initialCapacity, Allocator.Persistent);
            expiredBuffer = new NativeList<int>(initialCapacity, Allocator.Persistent);
        }

        /// <summary>
        /// Creates a <see cref="BurstSchedulerRunner"/> and registers it on the PlayerLoop
        /// at the specified <paramref name="timing"/>.
        /// </summary>
        /// <param name="timing">The PlayerLoop phase to run at.</param>
        /// <param name="initialCapacity">Initial capacity for native containers.</param>
        /// <returns>A new runner. Dispose when no longer needed.</returns>
        public static BurstSchedulerRunner Create(
            PlayerLoopTiming timing = PlayerLoopTiming.Update,
            int initialCapacity = 64)
        {
            var runner = new BurstSchedulerRunner(initialCapacity);
            PlayerLoopHelper.AddAction(timing, runner);
            return runner;
        }

        /// <summary>
        /// Gets the underlying <see cref="NativeScheduler"/> for direct enqueueing
        /// from Burst-compiled jobs.
        /// </summary>
        public NativeScheduler Scheduler => scheduler;

        /// <summary>
        /// Schedules a timer that fires <paramref name="callback"/> after
        /// <paramref name="delay"/>. The callback runs on the main thread.
        /// Must be called from the main thread only.
        /// </summary>
        /// <param name="delay">Time until the timer fires.</param>
        /// <param name="callback">Managed callback to invoke when the timer expires.</param>
        /// <returns>Timer ID.</returns>
        public int ScheduleTimer(TimeSpan delay, Action callback)
        {
            if (disposed) throw new ObjectDisposedException(nameof(BurstSchedulerRunner));
            if (callback == null) throw new ArgumentNullException(nameof(callback));

            long deadline = CurrentTimestampTicks() + delay.Ticks;
            int id = timerHeap.Schedule(deadline);
            timerCallbacks[id] = callback;
            return id;
        }

        /// <summary>
        /// Registers a managed callback for the given <paramref name="workId"/>.
        /// When a <see cref="ScheduledWork"/> with that ID is dequeued, the callback fires.
        /// Must be called from the main thread only.
        /// </summary>
        public void RegisterCallback(int workId, Action callback)
        {
            if (disposed) throw new ObjectDisposedException(nameof(BurstSchedulerRunner));
            if (callback == null) throw new ArgumentNullException(nameof(callback));

            workCallbacks[workId] = callback;
        }

        // ── IPlayerLoopItem ──

        public bool MoveNext()
        {
            if (disposed)
                return false;

            // Drain scheduler queue
            drainBuffer.Clear();
            int workCount = scheduler.Drain(drainBuffer);
            for (int i = 0; i < workCount; i++)
            {
                var work = drainBuffer[i];
                if (workCallbacks.TryGetValue(work.Id, out var cb))
                {
                    workCallbacks.Remove(work.Id);
                    try { cb(); }
                    catch (Exception ex) { ValkarnTask.PublishUnobservedException(ex); }
                }
            }

            // Drain expired timers
            expiredBuffer.Clear();
            long now = CurrentTimestampTicks();
            int timerCount = timerHeap.DrainExpired(now, expiredBuffer);
            for (int i = 0; i < timerCount; i++)
            {
                int id = expiredBuffer[i];
                if (timerCallbacks.TryGetValue(id, out var cb))
                {
                    timerCallbacks.Remove(id);
                    try { cb(); }
                    catch (Exception ex) { ValkarnTask.PublishUnobservedException(ex); }
                }
            }

            return !disposed;
        }

        static long CurrentTimestampTicks()
        {
#if UNITY_5_3_OR_NEWER
            return (long)(UnityEngine.Time.realtimeSinceStartupAsDouble * TimeSpan.TicksPerSecond);
#else
            return DateTime.UtcNow.Ticks;
#endif
        }

        /// <summary>
        /// Disposes all native containers and stops the runner.
        /// </summary>
        public void Dispose()
        {
            if (disposed)
                return;

            disposed = true;
            scheduler.Dispose();
            timerHeap.Dispose();
            drainBuffer.Dispose();
            expiredBuffer.Dispose();
            workCallbacks.Clear();
            timerCallbacks.Clear();
        }
    }
}
#endif
