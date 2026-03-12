// Copyright (c) Una Partida Mas. All rights reserved.
// Licensed under the MIT License. See LICENSE.md in the repository root.

using System;
using System.Buffers;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;

namespace UnaPartidaMas.Valkarn.Tasks
{
    public readonly partial struct ValkarnTask
    {
        /// <summary>
        /// Awaits both tasks concurrently, returning a tuple of results.
        /// Throws if any task faults or is canceled (first exception wins).
        /// Zero-alloc when both sync-completed.
        /// </summary>
        public static ValkarnTask<(T1, T2)> WhenAll<T1, T2>(
            ValkarnTask<T1> task1, ValkarnTask<T2> task2)
        {
            if (task1.source == null && task2.source == null)
                return new ValkarnTask<(T1, T2)>((task1.result, task2.result));

            return WhenAllPromise<T1, T2>.Create(task1, task2);
        }

        /// <summary>
        /// Awaits all tasks concurrently, returning an array of results.
        /// Throws if any task faults or is canceled.
        /// </summary>
        public static ValkarnTask<T[]> WhenAll<T>(IEnumerable<ValkarnTask<T>> tasks)
        {
            if (tasks == null) ThrowHelper.ThrowArgumentNull(nameof(tasks));
            var taskArray = tasks is ValkarnTask<T>[] arr ? arr : System.Linq.Enumerable.ToArray(tasks);
            if (taskArray.Length == 0)
                return FromResult(Array.Empty<T>());

            // Fast path: all tasks sync-completed
            bool allSync = true;
            for (int i = 0; i < taskArray.Length; i++)
            {
                if (taskArray[i].source != null) { allSync = false; break; }
            }
            if (allSync)
            {
                var results = new T[taskArray.Length];
                for (int i = 0; i < taskArray.Length; i++)
                    results[i] = taskArray[i].result;
                return FromResult(results);
            }

            return WhenAllArrayPromise<T>.Create(taskArray);
        }

        /// <summary>
        /// Awaits both void tasks concurrently.
        /// Throws if any task faults or is canceled.
        /// </summary>
        public static ValkarnTask WhenAll(ValkarnTask task1, ValkarnTask task2)
        {
            if (task1.source == null && task2.source == null)
                return CompletedTask;

            return WhenAllVoidPromise2.Create(task1, task2).AsNonGeneric();
        }

        /// <summary>
        /// Awaits three void tasks concurrently.
        /// Throws if any task faults or is canceled.
        /// </summary>
        public static ValkarnTask WhenAll(ValkarnTask task1, ValkarnTask task2, ValkarnTask task3)
        {
            if (task1.source == null && task2.source == null && task3.source == null)
                return CompletedTask;

            return WhenAllVoidPromise3.Create(task1, task2, task3).AsNonGeneric();
        }

        /// <summary>
        /// Awaits all void tasks concurrently.
        /// Throws if any task faults or is canceled.
        /// </summary>
        public static ValkarnTask WhenAll(IEnumerable<ValkarnTask> tasks)
        {
            if (tasks == null) ThrowHelper.ThrowArgumentNull(nameof(tasks));
            var taskArray = tasks is ValkarnTask[] arr ? arr : System.Linq.Enumerable.ToArray(tasks);
            if (taskArray.Length == 0)
                return CompletedTask;

            // Fast path: all sync-completed
            bool allSync = true;
            for (int i = 0; i < taskArray.Length; i++)
            {
                if (taskArray[i].source != null) { allSync = false; break; }
            }
            if (allSync)
                return CompletedTask;

            return WhenAllVoidArrayPromise.Create(taskArray).AsNonGeneric();
        }
    }

    // ════════════════════════════════════════════════════════════════
    //  WhenAllPromise<T1, T2> — Pooled, 2-arity typed combinator
    //  Returns raw (T1, T2). Throws first exception if any task fails.
    // ════════════════════════════════════════════════════════════════

    internal sealed class WhenAllPromise<T1, T2>
        : ValkarnTask.ISource<(T1, T2)>,
          IPoolNode<WhenAllPromise<T1, T2>>
    {
        static ValkarnPool<WhenAllPromise<T1, T2>> s_pool;
        static ValkarnPool<WhenAllPromise<T1, T2>> Pool => ValkarnPool<WhenAllPromise<T1, T2>>.GetOrCreate(ref s_pool);

        WhenAllPromise<T1, T2> nextNode;
        public ref WhenAllPromise<T1, T2> NextNode => ref nextNode;

        ValkarnCompletionCore<(T1, T2)> core;
        int returned;
        ValkarnTask.ISource<T1> source1;
        ValkarnTask.ISource<T2> source2;
        uint token1, token2;
        T1 result1;
        T2 result2;
        Exception firstException;
        OperationCanceledException firstCancellation;
        int remainingCount;

        static readonly Action<object> s_callback1 = static state =>
        {
            var self = (WhenAllPromise<T1, T2>)state;
            try { self.result1 = self.source1.GetResult(self.token1); }
            catch (OperationCanceledException oce) { Interlocked.CompareExchange(ref self.firstCancellation, oce, null); }
            catch (Exception ex)
            {
                if (Interlocked.CompareExchange(ref self.firstException, ex, null) != null)
                    ValkarnTask.PublishUnobservedException(ex);
            }
            self.source1 = null;
            if (Interlocked.Decrement(ref self.remainingCount) == 0)
                self.Complete();
        };

        static readonly Action<object> s_callback2 = static state =>
        {
            var self = (WhenAllPromise<T1, T2>)state;
            try { self.result2 = self.source2.GetResult(self.token2); }
            catch (OperationCanceledException oce) { Interlocked.CompareExchange(ref self.firstCancellation, oce, null); }
            catch (Exception ex)
            {
                if (Interlocked.CompareExchange(ref self.firstException, ex, null) != null)
                    ValkarnTask.PublishUnobservedException(ex);
            }
            self.source2 = null;
            if (Interlocked.Decrement(ref self.remainingCount) == 0)
                self.Complete();
        };

        WhenAllPromise() { }

        void Complete()
        {
            if (firstException != null)
                core.TrySetException(firstException);
            else if (firstCancellation != null)
                core.TrySetCanceled(firstCancellation.CancellationToken);
            else
                core.TrySetResult((result1, result2));
        }

        internal static ValkarnTask<(T1, T2)> Create(
            ValkarnTask<T1> task1, ValkarnTask<T2> task2)
        {
            var p = Pool.TryRent() ?? Pool.TrackNew(new WhenAllPromise<T1, T2>());
            var outToken = p.core.Token;
            p.returned = 0;
            p.remainingCount = 3; // 2 tasks + 1 guard
            p.firstException = null;
            p.firstCancellation = null;

            if (task1.source == null)
            {
                p.result1 = task1.result;
                Interlocked.Decrement(ref p.remainingCount);
            }
            else
            {
                p.source1 = task1.source;
                p.token1 = task1.token;
                task1.source.OnCompleted(s_callback1, p, task1.token);
            }

            if (task2.source == null)
            {
                p.result2 = task2.result;
                Interlocked.Decrement(ref p.remainingCount);
            }
            else
            {
                p.source2 = task2.source;
                p.token2 = task2.token;
                task2.source.OnCompleted(s_callback2, p, task2.token);
            }

            if (Interlocked.Decrement(ref p.remainingCount) == 0)
                p.Complete();

            return new ValkarnTask<(T1, T2)>(p, outToken);
        }

        // ── ISource ──

        ValkarnTask.Status ValkarnTask.ISource.GetStatus(uint token) => core.GetStatus(token);
        ValkarnTask.Status ValkarnTask.ISource.UnsafeGetStatus() => core.UnsafeGetStatus();

        (T1, T2) ValkarnTask.ISource<(T1, T2)>.GetResult(uint token)
        {
            try { return core.GetResult(token); }
            finally { TryReturn(); }
        }

        void ValkarnTask.ISource.GetResult(uint token)
        {
            try { core.GetResult(token); }
            finally { TryReturn(); }
        }

        void ValkarnTask.ISource.OnCompleted(Action<object> continuation, object state, uint token)
            => core.OnCompleted(continuation, state, token);

        void TryReturn()
        {
            if (Interlocked.Exchange(ref returned, 1) != 0) return;
            source1 = null;
            source2 = null;
            result1 = default;
            result2 = default;
            firstException = null;
            firstCancellation = null;
            core.Reset();
            Pool.TryReturn(this);
        }
    }

    // ════════════════════════════════════════════════════════════════
    //  WhenAllArrayPromise<T> — IEnumerable typed combinator
    //  Returns T[]. Throws first exception if any task fails.
    // ════════════════════════════════════════════════════════════════

    internal sealed class WhenAllArrayPromise<T>
        : ValkarnTask.ISource<T[]>,
          IPoolNode<WhenAllArrayPromise<T>>
    {
        static ValkarnPool<WhenAllArrayPromise<T>> s_pool;
        static ValkarnPool<WhenAllArrayPromise<T>> Pool => ValkarnPool<WhenAllArrayPromise<T>>.GetOrCreate(ref s_pool);

        WhenAllArrayPromise<T> nextNode;
        public ref WhenAllArrayPromise<T> NextNode => ref nextNode;

        ValkarnCompletionCore<T[]> core;
        int returned;
        ValkarnTask.ISource<T>[] sources;
        uint[] tokens;
        T[] results;
        Exception firstException;
        OperationCanceledException firstCancellation;
        int remainingCount;
        int rentedLength;

        // ── Pooled Slot (simple Treiber stack) ──

        sealed class Slot
        {
            internal WhenAllArrayPromise<T> Parent;
            internal int Index;
            internal Slot NextSlot;

            static Slot s_slotHead;
            static int s_slotCount;
            const int MaxPooled = 256;

            internal static void ResetPool() { s_slotHead = null; s_slotCount = 0; }

            internal static Slot Rent(WhenAllArrayPromise<T> parent, int index)
            {
                Slot slot;
                do { slot = Volatile.Read(ref s_slotHead); }
                while (slot != null && Interlocked.CompareExchange(ref s_slotHead, slot.NextSlot, slot) != slot);

                if (slot != null)
                    Interlocked.Decrement(ref s_slotCount);
                else
                    slot = new Slot();

                slot.Parent = parent;
                slot.Index = index;
                return slot;
            }

            internal void Return()
            {
                Parent = null;
                Index = 0;
                if (Volatile.Read(ref s_slotCount) >= MaxPooled) return;
                Slot head;
                do { head = Volatile.Read(ref s_slotHead); NextSlot = head; }
                while (Interlocked.CompareExchange(ref s_slotHead, this, head) != head);
                Interlocked.Increment(ref s_slotCount);
            }
        }

        static readonly Action<object> s_callback = static state =>
        {
            var slot = (Slot)state;
            var self = slot.Parent;
            var i = slot.Index;
            slot.Return();
            try { self.results[i] = self.sources[i].GetResult(self.tokens[i]); }
            catch (OperationCanceledException oce) { Interlocked.CompareExchange(ref self.firstCancellation, oce, null); }
            catch (Exception ex)
            {
                if (Interlocked.CompareExchange(ref self.firstException, ex, null) != null)
                    ValkarnTask.PublishUnobservedException(ex);
            }
            self.sources[i] = null;
            if (Interlocked.Decrement(ref self.remainingCount) == 0)
                self.Complete();
        };

        WhenAllArrayPromise() { }

        void Complete()
        {
            if (firstException != null)
                core.TrySetException(firstException);
            else if (firstCancellation != null)
                core.TrySetCanceled(firstCancellation.CancellationToken);
            else
                core.TrySetResult(results);
        }

        internal static ValkarnTask<T[]> Create(ValkarnTask<T>[] tasks)
        {
            var n = tasks.Length;
            var p = Pool.TryRent() ?? Pool.TrackNew(new WhenAllArrayPromise<T>());
            var outToken = p.core.Token;

            var pooledSources = ArrayPool<ValkarnTask.ISource<T>>.Shared.Rent(n);
            var pooledTokens = ArrayPool<uint>.Shared.Rent(n);
            p.sources = pooledSources;
            p.tokens = pooledTokens;
            p.returned = 0;
            p.rentedLength = n;
            p.results = new T[n];
            p.firstException = null;
            p.firstCancellation = null;
            p.remainingCount = n + 1;

            for (int i = 0; i < n; i++)
            {
                if (tasks[i].source == null)
                {
                    p.results[i] = tasks[i].result;
                    Interlocked.Decrement(ref p.remainingCount);
                }
                else
                {
                    p.sources[i] = tasks[i].source;
                    p.tokens[i] = tasks[i].token;
                    tasks[i].source.OnCompleted(s_callback,
                        Slot.Rent(p, i), tasks[i].token);
                }
            }

            if (Interlocked.Decrement(ref p.remainingCount) == 0)
                p.Complete();

            return new ValkarnTask<T[]>(p, outToken);
        }

        ValkarnTask.Status ValkarnTask.ISource.GetStatus(uint token) => core.GetStatus(token);
        ValkarnTask.Status ValkarnTask.ISource.UnsafeGetStatus() => core.UnsafeGetStatus();

        T[] ValkarnTask.ISource<T[]>.GetResult(uint token)
        {
            try { return core.GetResult(token); }
            finally { TryReturn(); }
        }

        void ValkarnTask.ISource.GetResult(uint token)
        {
            try { core.GetResult(token); }
            finally { TryReturn(); }
        }

        void ValkarnTask.ISource.OnCompleted(Action<object> continuation, object state, uint token)
            => core.OnCompleted(continuation, state, token);

        void TryReturn()
        {
            if (Interlocked.Exchange(ref returned, 1) != 0) return;
            var len = rentedLength;
            if (len > 0)
            {
                var s = sources;
                var t = tokens;
                sources = null;
                tokens = null;
                rentedLength = 0;
                if (s != null) ArrayPool<ValkarnTask.ISource<T>>.Shared.Return(s, clearArray: true);
                if (t != null) ArrayPool<uint>.Shared.Return(t);
            }
            results = null;
            firstException = null;
            firstCancellation = null;
            core.Reset();
            Pool.TryReturn(this);
        }
    }

    // ════════════════════════════════════════════════════════════════
    //  WhenAllVoidPromise2 — Pooled, 2-arity void combinator
    //  Returns AsyncUnit. Throws first exception if any task fails.
    // ════════════════════════════════════════════════════════════════

    internal sealed class WhenAllVoidPromise2
        : ValkarnTask.ISource<AsyncUnit>,
          IPoolNode<WhenAllVoidPromise2>
    {
        static ValkarnPool<WhenAllVoidPromise2> s_pool;
        static ValkarnPool<WhenAllVoidPromise2> Pool => ValkarnPool<WhenAllVoidPromise2>.GetOrCreate(ref s_pool);

        WhenAllVoidPromise2 nextNode;
        public ref WhenAllVoidPromise2 NextNode => ref nextNode;

        ValkarnCompletionCore<AsyncUnit> core;
        int returned;
        ValkarnTask.ISource source1, source2;
        uint token1, token2;
        Exception firstException;
        OperationCanceledException firstCancellation;
        int remainingCount;

        static readonly Action<object> s_callback1 = static state =>
        {
            var self = (WhenAllVoidPromise2)state;
            try { self.source1.GetResult(self.token1); }
            catch (OperationCanceledException oce) { Interlocked.CompareExchange(ref self.firstCancellation, oce, null); }
            catch (Exception ex)
            {
                if (Interlocked.CompareExchange(ref self.firstException, ex, null) != null)
                    ValkarnTask.PublishUnobservedException(ex);
            }
            self.source1 = null;
            if (Interlocked.Decrement(ref self.remainingCount) == 0)
                self.Complete();
        };

        static readonly Action<object> s_callback2 = static state =>
        {
            var self = (WhenAllVoidPromise2)state;
            try { self.source2.GetResult(self.token2); }
            catch (OperationCanceledException oce) { Interlocked.CompareExchange(ref self.firstCancellation, oce, null); }
            catch (Exception ex)
            {
                if (Interlocked.CompareExchange(ref self.firstException, ex, null) != null)
                    ValkarnTask.PublishUnobservedException(ex);
            }
            self.source2 = null;
            if (Interlocked.Decrement(ref self.remainingCount) == 0)
                self.Complete();
        };

        WhenAllVoidPromise2() { }

        void Complete()
        {
            if (firstException != null)
                core.TrySetException(firstException);
            else if (firstCancellation != null)
                core.TrySetCanceled(firstCancellation.CancellationToken);
            else
                core.TrySetResult(AsyncUnit.Default);
        }

        internal static ValkarnTask<AsyncUnit> Create(ValkarnTask task1, ValkarnTask task2)
        {
            var p = Pool.TryRent() ?? Pool.TrackNew(new WhenAllVoidPromise2());
            var outToken = p.core.Token;
            p.returned = 0;
            p.remainingCount = 3;
            p.firstException = null;
            p.firstCancellation = null;

            if (task1.source == null)
            {
                Interlocked.Decrement(ref p.remainingCount);
            }
            else
            {
                p.source1 = task1.source;
                p.token1 = task1.token;
                task1.source.OnCompleted(s_callback1, p, task1.token);
            }

            if (task2.source == null)
            {
                Interlocked.Decrement(ref p.remainingCount);
            }
            else
            {
                p.source2 = task2.source;
                p.token2 = task2.token;
                task2.source.OnCompleted(s_callback2, p, task2.token);
            }

            if (Interlocked.Decrement(ref p.remainingCount) == 0)
                p.Complete();

            return new ValkarnTask<AsyncUnit>(p, outToken);
        }

        ValkarnTask.Status ValkarnTask.ISource.GetStatus(uint token) => core.GetStatus(token);
        ValkarnTask.Status ValkarnTask.ISource.UnsafeGetStatus() => core.UnsafeGetStatus();

        AsyncUnit ValkarnTask.ISource<AsyncUnit>.GetResult(uint token)
        {
            try { return core.GetResult(token); }
            finally { TryReturn(); }
        }

        void ValkarnTask.ISource.GetResult(uint token)
        {
            try { core.GetResult(token); }
            finally { TryReturn(); }
        }

        void ValkarnTask.ISource.OnCompleted(Action<object> continuation, object state, uint token)
            => core.OnCompleted(continuation, state, token);

        void TryReturn()
        {
            if (Interlocked.Exchange(ref returned, 1) != 0) return;
            source1 = null;
            source2 = null;
            firstException = null;
            firstCancellation = null;
            core.Reset();
            Pool.TryReturn(this);
        }
    }

    // ════════════════════════════════════════════════════════════════
    //  WhenAllVoidPromise3 — Pooled, 3-arity void combinator
    // ════════════════════════════════════════════════════════════════

    internal sealed class WhenAllVoidPromise3
        : ValkarnTask.ISource<AsyncUnit>,
          IPoolNode<WhenAllVoidPromise3>
    {
        static ValkarnPool<WhenAllVoidPromise3> s_pool;
        static ValkarnPool<WhenAllVoidPromise3> Pool => ValkarnPool<WhenAllVoidPromise3>.GetOrCreate(ref s_pool);

        WhenAllVoidPromise3 nextNode;
        public ref WhenAllVoidPromise3 NextNode => ref nextNode;

        ValkarnCompletionCore<AsyncUnit> core;
        int returned;
        ValkarnTask.ISource source1, source2, source3;
        uint token1, token2, token3;
        Exception firstException;
        OperationCanceledException firstCancellation;
        int remainingCount;

        static readonly Action<object> s_callback1 = static state =>
        {
            var self = (WhenAllVoidPromise3)state;
            try { self.source1.GetResult(self.token1); }
            catch (OperationCanceledException oce) { Interlocked.CompareExchange(ref self.firstCancellation, oce, null); }
            catch (Exception ex)
            {
                if (Interlocked.CompareExchange(ref self.firstException, ex, null) != null)
                    ValkarnTask.PublishUnobservedException(ex);
            }
            self.source1 = null;
            if (Interlocked.Decrement(ref self.remainingCount) == 0)
                self.Complete();
        };

        static readonly Action<object> s_callback2 = static state =>
        {
            var self = (WhenAllVoidPromise3)state;
            try { self.source2.GetResult(self.token2); }
            catch (OperationCanceledException oce) { Interlocked.CompareExchange(ref self.firstCancellation, oce, null); }
            catch (Exception ex)
            {
                if (Interlocked.CompareExchange(ref self.firstException, ex, null) != null)
                    ValkarnTask.PublishUnobservedException(ex);
            }
            self.source2 = null;
            if (Interlocked.Decrement(ref self.remainingCount) == 0)
                self.Complete();
        };

        static readonly Action<object> s_callback3 = static state =>
        {
            var self = (WhenAllVoidPromise3)state;
            try { self.source3.GetResult(self.token3); }
            catch (OperationCanceledException oce) { Interlocked.CompareExchange(ref self.firstCancellation, oce, null); }
            catch (Exception ex)
            {
                if (Interlocked.CompareExchange(ref self.firstException, ex, null) != null)
                    ValkarnTask.PublishUnobservedException(ex);
            }
            self.source3 = null;
            if (Interlocked.Decrement(ref self.remainingCount) == 0)
                self.Complete();
        };

        WhenAllVoidPromise3() { }

        void Complete()
        {
            if (firstException != null)
                core.TrySetException(firstException);
            else if (firstCancellation != null)
                core.TrySetCanceled(firstCancellation.CancellationToken);
            else
                core.TrySetResult(AsyncUnit.Default);
        }

        internal static ValkarnTask<AsyncUnit> Create(
            ValkarnTask task1, ValkarnTask task2, ValkarnTask task3)
        {
            var p = Pool.TryRent() ?? Pool.TrackNew(new WhenAllVoidPromise3());
            var outToken = p.core.Token;
            p.returned = 0;
            p.remainingCount = 4;
            p.firstException = null;
            p.firstCancellation = null;

            if (task1.source == null) { Interlocked.Decrement(ref p.remainingCount); }
            else { p.source1 = task1.source; p.token1 = task1.token; task1.source.OnCompleted(s_callback1, p, task1.token); }

            if (task2.source == null) { Interlocked.Decrement(ref p.remainingCount); }
            else { p.source2 = task2.source; p.token2 = task2.token; task2.source.OnCompleted(s_callback2, p, task2.token); }

            if (task3.source == null) { Interlocked.Decrement(ref p.remainingCount); }
            else { p.source3 = task3.source; p.token3 = task3.token; task3.source.OnCompleted(s_callback3, p, task3.token); }

            if (Interlocked.Decrement(ref p.remainingCount) == 0)
                p.Complete();

            return new ValkarnTask<AsyncUnit>(p, outToken);
        }

        ValkarnTask.Status ValkarnTask.ISource.GetStatus(uint token) => core.GetStatus(token);
        ValkarnTask.Status ValkarnTask.ISource.UnsafeGetStatus() => core.UnsafeGetStatus();

        AsyncUnit ValkarnTask.ISource<AsyncUnit>.GetResult(uint token)
        {
            try { return core.GetResult(token); }
            finally { TryReturn(); }
        }

        void ValkarnTask.ISource.GetResult(uint token)
        {
            try { core.GetResult(token); }
            finally { TryReturn(); }
        }

        void ValkarnTask.ISource.OnCompleted(Action<object> continuation, object state, uint token)
            => core.OnCompleted(continuation, state, token);

        void TryReturn()
        {
            if (Interlocked.Exchange(ref returned, 1) != 0) return;
            source1 = null;
            source2 = null;
            source3 = null;
            firstException = null;
            firstCancellation = null;
            core.Reset();
            Pool.TryReturn(this);
        }
    }

    // ════════════════════════════════════════════════════════════════
    //  WhenAllVoidArrayPromise — IEnumerable void combinator
    // ════════════════════════════════════════════════════════════════

    internal sealed class WhenAllVoidArrayPromise
        : ValkarnTask.ISource<AsyncUnit>,
          IPoolNode<WhenAllVoidArrayPromise>
    {
        static ValkarnPool<WhenAllVoidArrayPromise> s_pool;
        static ValkarnPool<WhenAllVoidArrayPromise> Pool => ValkarnPool<WhenAllVoidArrayPromise>.GetOrCreate(ref s_pool);

        WhenAllVoidArrayPromise nextNode;
        public ref WhenAllVoidArrayPromise NextNode => ref nextNode;

        ValkarnCompletionCore<AsyncUnit> core;
        int returned;
        ValkarnTask.ISource[] sources;
        uint[] tokens;
        Exception firstException;
        OperationCanceledException firstCancellation;
        int remainingCount;
        int rentedLength;

        // ── Pooled Slot (simple Treiber stack) ──

        sealed class Slot
        {
            internal WhenAllVoidArrayPromise Parent;
            internal int Index;
            internal Slot NextSlot;

            static Slot s_slotHead;
            static int s_slotCount;
            const int MaxPooled = 256;

            internal static void ResetPool() { s_slotHead = null; s_slotCount = 0; }

            internal static Slot Rent(WhenAllVoidArrayPromise parent, int index)
            {
                Slot slot;
                do { slot = Volatile.Read(ref s_slotHead); }
                while (slot != null && Interlocked.CompareExchange(ref s_slotHead, slot.NextSlot, slot) != slot);

                if (slot != null)
                    Interlocked.Decrement(ref s_slotCount);
                else
                    slot = new Slot();

                slot.Parent = parent;
                slot.Index = index;
                return slot;
            }

            internal void Return()
            {
                Parent = null;
                Index = 0;
                if (Volatile.Read(ref s_slotCount) >= MaxPooled) return;
                Slot head;
                do { head = Volatile.Read(ref s_slotHead); NextSlot = head; }
                while (Interlocked.CompareExchange(ref s_slotHead, this, head) != head);
                Interlocked.Increment(ref s_slotCount);
            }
        }

        static readonly Action<object> s_callback = static state =>
        {
            var slot = (Slot)state;
            var self = slot.Parent;
            var i = slot.Index;
            slot.Return();
            try { self.sources[i].GetResult(self.tokens[i]); }
            catch (OperationCanceledException oce) { Interlocked.CompareExchange(ref self.firstCancellation, oce, null); }
            catch (Exception ex)
            {
                if (Interlocked.CompareExchange(ref self.firstException, ex, null) != null)
                    ValkarnTask.PublishUnobservedException(ex);
            }
            self.sources[i] = null;
            if (Interlocked.Decrement(ref self.remainingCount) == 0)
                self.Complete();
        };

        WhenAllVoidArrayPromise() { }

        void Complete()
        {
            if (firstException != null)
                core.TrySetException(firstException);
            else if (firstCancellation != null)
                core.TrySetCanceled(firstCancellation.CancellationToken);
            else
                core.TrySetResult(AsyncUnit.Default);
        }

        internal static ValkarnTask<AsyncUnit> Create(ValkarnTask[] tasks)
        {
            var n = tasks.Length;
            var p = Pool.TryRent() ?? Pool.TrackNew(new WhenAllVoidArrayPromise());
            var outToken = p.core.Token;

            var pooledSources = ArrayPool<ValkarnTask.ISource>.Shared.Rent(n);
            var pooledTokens = ArrayPool<uint>.Shared.Rent(n);
            p.sources = pooledSources;
            p.tokens = pooledTokens;
            p.returned = 0;
            p.rentedLength = n;
            p.firstException = null;
            p.firstCancellation = null;
            p.remainingCount = n + 1;

            for (int i = 0; i < n; i++)
            {
                if (tasks[i].source == null)
                {
                    Interlocked.Decrement(ref p.remainingCount);
                }
                else
                {
                    p.sources[i] = tasks[i].source;
                    p.tokens[i] = tasks[i].token;
                    tasks[i].source.OnCompleted(s_callback,
                        Slot.Rent(p, i), tasks[i].token);
                }
            }

            if (Interlocked.Decrement(ref p.remainingCount) == 0)
                p.Complete();

            return new ValkarnTask<AsyncUnit>(p, outToken);
        }

        ValkarnTask.Status ValkarnTask.ISource.GetStatus(uint token) => core.GetStatus(token);
        ValkarnTask.Status ValkarnTask.ISource.UnsafeGetStatus() => core.UnsafeGetStatus();

        AsyncUnit ValkarnTask.ISource<AsyncUnit>.GetResult(uint token)
        {
            try { return core.GetResult(token); }
            finally { TryReturn(); }
        }

        void ValkarnTask.ISource.GetResult(uint token)
        {
            try { core.GetResult(token); }
            finally { TryReturn(); }
        }

        void ValkarnTask.ISource.OnCompleted(Action<object> continuation, object state, uint token)
            => core.OnCompleted(continuation, state, token);

        void TryReturn()
        {
            if (Interlocked.Exchange(ref returned, 1) != 0) return;
            var len = rentedLength;
            if (len > 0)
            {
                var s = sources;
                var t = tokens;
                sources = null;
                tokens = null;
                rentedLength = 0;
                if (s != null) ArrayPool<ValkarnTask.ISource>.Shared.Return(s, clearArray: true);
                if (t != null) ArrayPool<uint>.Shared.Return(t);
            }
            firstException = null;
            firstCancellation = null;
            core.Reset();
            Pool.TryReturn(this);
        }
    }
}
