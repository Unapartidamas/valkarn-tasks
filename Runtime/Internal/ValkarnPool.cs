// Copyright (c) Una Partida Mas. All rights reserved.
// Licensed under the MIT License. See LICENSE.md in the repository root.

using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;

namespace UnaPartidaMas.Valkarn.Tasks
{
    /// <summary>
    /// Non-generic holder for main thread ID. Shared by all ValkarnPool{T} instantiations.
    /// </summary>
    internal static class ValkarnPoolShared
    {
        // C-03 FIX (L-01): volatile ensures cross-thread visibility of initial publication.
        internal static volatile int MainThreadId;

        internal static void SetMainThreadId(int threadId)
        {
            MainThreadId = threadId;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static bool IsMainThread()
            => Thread.CurrentThread.ManagedThreadId == MainThreadId;
    }

    /// <summary>
    /// High-performance bounded object pool using fast slot + Treiber stack.
    /// IL2CPP-first: main thread operations use zero atomics.
    ///
    /// Design:
    /// - fastItem: single-slot cache for the common case (main thread rent/return). Plain read/write.
    /// - Treiber stack: overflow storage using intrusive linked list. CAS-based for cross-thread safety.
    /// - Main thread ID check: routes operations to atomic or non-atomic path.
    /// - Bounded: max size prevents unbounded growth after load spikes.
    /// </summary>
    internal sealed class ValkarnPool<T> : IPoolInfo where T : class, IPoolNode<T>
    {
        // ── Fast slot (main thread only, zero atomics) ──
        T fastItem;

        // ── Overflow stack (Treiber stack) ──
        // C-03 FIX: stackHead is accessed by both main thread and background threads.
        // Main thread uses Volatile.Write to publish changes; background threads use CAS.
        T stackHead;
        int stackSize;

        // ── Bounds ──
        int maxSize;
        int totalCreated;


        internal ValkarnPool(int maxSize = 0)
        {
            this.maxSize = maxSize > 0 ? maxSize : ValkarnTask.DefaultMaxPoolSize;
            PoolRegistry.Register(this);
        }

        /// <summary>
        /// Thread-safe lazy initialization of a pool field.
        /// Uses Interlocked.CompareExchange to ensure only one pool instance survives
        /// when multiple threads race to initialize the same static field.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static ValkarnPool<T> GetOrCreate(ref ValkarnPool<T> location)
        {
            var pool = Volatile.Read(ref location);
            if (pool != null) return pool;
            Interlocked.CompareExchange(ref location, new ValkarnPool<T>(), null);
            return Volatile.Read(ref location);
        }

        // ── IPoolInfo ──
        Type IPoolInfo.PooledType => typeof(T);
        int IPoolInfo.Size => Size;
        int IPoolInfo.MaxSize => maxSize;
        bool IPoolInfo.IsAlive => true;
        int IPoolInfo.Trim(int minPoolSize) => Trim(minPoolSize);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static bool IsMainThread() => ValkarnPoolShared.IsMainThread();

        /// <summary>
        /// Rents an object from the pool. Returns null if pool is empty.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal T TryRent()
        {
            // Fast path: main thread, check fast slot first
            if (IsMainThread())
            {
                var item = fastItem;
                if (item != null)
                {
                    fastItem = null;
                    return item;
                }
                return TryRentFromStackUnsafe();
            }

            return TryRentFromStackAtomic();
        }

        /// <summary>
        /// Returns an object to the pool. Returns false if pool is full (object should be GC'd).
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal bool TryReturn(T item)
        {
            if (IsMainThread())
            {
                if (fastItem == null)
                {
                    fastItem = item;
                    return true;
                }
                return TryReturnToStackUnsafe(item);
            }

            return TryReturnToStackAtomic(item);
        }

        // ── Main thread stack operations ──
        // NEW-02 FIX: Main thread now delegates to atomic CAS paths for stack operations.
        // Previously, Volatile.Write on stackHead could overwrite a concurrent background
        // thread's CAS, losing a node. Using the same CAS loop for both paths is correct
        // and the perf difference is negligible (stack is the slow path; fast slot is the hot path).

        [MethodImpl(MethodImplOptions.NoInlining)]
        T TryRentFromStackUnsafe() => TryRentFromStackAtomic();

        [MethodImpl(MethodImplOptions.NoInlining)]
        bool TryReturnToStackUnsafe(T item) => TryReturnToStackAtomic(item);

        // ── Cross-thread stack operations (CAS-based Treiber stack) ──

        [MethodImpl(MethodImplOptions.NoInlining)]
        T TryRentFromStackAtomic()
        {
            SpinWait spinner = default;
            while (true)
            {
                var head = Volatile.Read(ref stackHead);
                if (head == null)
                    return null;

                var next = head.NextNode;
                if (Interlocked.CompareExchange(ref stackHead, next, head) == head)
                {
                    head.NextNode = null;
                    Interlocked.Decrement(ref stackSize);
                    return head;
                }

                spinner.SpinOnce();
            }
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        bool TryReturnToStackAtomic(T item)
        {
            if (Volatile.Read(ref stackSize) >= maxSize)
                return false;

            SpinWait spinner = default;
            while (true)
            {
                var head = Volatile.Read(ref stackHead);
                item.NextNode = head;

                if (Interlocked.CompareExchange(ref stackHead, item, head) == head)
                {
                    Interlocked.Increment(ref stackSize);
                    return true;
                }

                spinner.SpinOnce();
            }
        }

        // ── Monitoring ──

        internal int Size => (fastItem != null ? 1 : 0) + Math.Max(0, stackSize);
        internal int MaxSize => maxSize;

        // ── Trimming (called by PlayerLoop, Phase 2) ──

        int trimConsecutiveCount;

        /// <summary>
        /// Called periodically by PlayerLoop to trim excess pooled objects.
        /// Returns the number of items released.
        /// </summary>
        internal int Trim(int minPoolSize)
        {
            System.Diagnostics.Debug.Assert(IsMainThread(), "ValkarnPool.Trim must be called from the main thread.");
            var currentSize = Size;
            if (currentSize <= minPoolSize)
            {
                trimConsecutiveCount = 0;
                return 0;
            }

            var ratio = (float)currentSize / Math.Max(totalCreated, 1);
            if (ratio > 0.5f)
            {
                trimConsecutiveCount++;
                var hysteresis = SettingsHelper.GetTrimHysteresisCount();
                if (trimConsecutiveCount >= hysteresis)
                {
                    var releaseRatio = SettingsHelper.GetTrimReleaseRatio();
                    var toRelease = Math.Max(1, (int)(currentSize * releaseRatio));
                    var released = 0;
                    for (int i = 0; i < toRelease && Size > minPoolSize; i++)
                    {
                        // M-07 FIX: Rent from stack only, preserving the fast slot
                        // (which is the most cache-friendly item for the main thread).
                        var item = TryRentFromStackUnsafe();
                        if (item == null) break;
                        // Don't return to pool — let GC collect
                        released++;
                    }
                    return released;
                }
            }
            else
            {
                trimConsecutiveCount = 0;
            }

            return 0;
        }

        internal void IncrementCreated()
        {
            Interlocked.Increment(ref totalCreated);
        }

        /// <summary>
        /// Tracks a newly created instance for trim ratio calculation.
        /// Usage: <c>var p = Pool.TryRent() ?? Pool.TrackNew(new Foo());</c>
        /// The ?? operator lazily evaluates the RHS, so allocation only happens when pool is empty.
        /// </summary>
        [MethodImpl(MethodImplOptions.NoInlining)]
        internal T TrackNew(T item)
        {
            Interlocked.Increment(ref totalCreated);
            return item;
        }

    }
}
