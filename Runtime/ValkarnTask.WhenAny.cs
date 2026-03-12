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
        /// Returns the first completed result. Losing tasks continue running.
        /// Winner index is 0-based. Loser errors go to UnobservedException.
        /// Loser cancellations are intentionally not reported (cancellation is not an error).
        /// </summary>
        public static ValkarnTask<(int winnerIndex, T result)> WhenAny<T>(
            ValkarnTask<T> task1, ValkarnTask<T> task2)
        {
            // Fast path: task1 sync-completed success (source==null) — zero alloc, guaranteed first-order winner
            if (task1.source == null)
                return new ValkarnTask<(int, T)>((0, task1.result));
            // Cannot fast-path task2: task1 has source != null which may be completed (faulted/canceled)
            // and should win by first-in-order rule. Fall to promise path.
            return WhenAnyPromise2<T>.Create(task1, task2);
        }

        /// <summary>
        /// Returns the first completed result from a collection.
        /// Loser cancellations are intentionally not reported.
        /// </summary>
        public static ValkarnTask<(int winnerIndex, T result)> WhenAny<T>(
            IEnumerable<ValkarnTask<T>> tasks)
        {
            if (tasks == null) ThrowHelper.ThrowArgumentNull(nameof(tasks));
            var taskArray = tasks is ValkarnTask<T>[] arr ? arr : System.Linq.Enumerable.ToArray(tasks);
            if (taskArray.Length == 0) throw new ArgumentException("WhenAny requires at least one task.", nameof(tasks));

            // Fast path: first task is sync-completed success
            if (taskArray[0].source == null)
                return new ValkarnTask<(int, T)>((0, taskArray[0].result));

            return WhenAnyArrayPromise<T>.Create(taskArray);
        }

        /// <summary>
        /// Returns the index of the first completed void task.
        /// Loser cancellations are intentionally not reported.
        /// </summary>
        public static ValkarnTask<int> WhenAny(ValkarnTask task1, ValkarnTask task2)
        {
            // Fast path: task1 sync-completed success — zero alloc
            if (task1.source == null)
                return new ValkarnTask<int>(0);
            // Cannot fast-path task2: task1 may be completed (faulted/canceled) first-in-order
            return WhenAnyVoidPromise2.Create(task1, task2);
        }

        /// <summary>
        /// Returns the index of the first completed void task from a collection.
        /// Loser cancellations are intentionally not reported.
        /// </summary>
        public static ValkarnTask<int> WhenAny(IEnumerable<ValkarnTask> tasks)
        {
            if (tasks == null) ThrowHelper.ThrowArgumentNull(nameof(tasks));
            var taskArray = tasks is ValkarnTask[] arr ? arr : System.Linq.Enumerable.ToArray(tasks);
            if (taskArray.Length == 0) throw new ArgumentException("WhenAny requires at least one task.", nameof(tasks));

            // Fast path: first task is sync-completed success
            if (taskArray[0].source == null)
                return new ValkarnTask<int>(0);

            return WhenAnyVoidArrayPromise.Create(taskArray);
        }
    }

    // ════════════════════════════════════════════════════════════════
    //  WhenAnyPromise2<T> — Pooled, 2-arity typed combinator
    //  returnGuard ensures pool return only after ALL callbacks + GetResult.
    // ════════════════════════════════════════════════════════════════

    internal sealed class WhenAnyPromise2<T>
        : ValkarnTask.ISource<(int, T)>,
          IPoolNode<WhenAnyPromise2<T>>
    {
        static ValkarnPool<WhenAnyPromise2<T>> s_pool;
        static ValkarnPool<WhenAnyPromise2<T>> Pool => ValkarnPool<WhenAnyPromise2<T>>.GetOrCreate(ref s_pool);

        WhenAnyPromise2<T> nextNode;
        public ref WhenAnyPromise2<T> NextNode => ref nextNode;

        ValkarnCompletionCore<(int, T)> core;
        int returned;
        ValkarnTask.ISource<T> source1, source2;
        uint token1, token2;
        int winnerIndex;
        int returnGuard; // registeredCallbacks + 1 (GetResult); pool-return when 0

        static readonly Action<object> s_callback1 = static state =>
        {
            var self = (WhenAnyPromise2<T>)state;
            bool isWinner = Interlocked.CompareExchange(ref self.winnerIndex, 0, -1) == -1;
            try
            {
                var value = self.source1.GetResult(self.token1);
                if (isWinner)
                    self.core.TrySetResult((0, value));
            }
            catch (OperationCanceledException oce)
            {
                if (isWinner) self.core.TrySetCanceled(oce.CancellationToken);
                // Loser cancellation intentionally not reported (cancellation is not an error)
            }
            catch (Exception ex)
            {
                if (isWinner) self.core.TrySetException(ex);
                else ValkarnTask.PublishUnobservedException(ex);
            }
            self.source1 = null;
            if (Interlocked.Decrement(ref self.returnGuard) == 0)
                self.TryReturn();
        };

        static readonly Action<object> s_callback2 = static state =>
        {
            var self = (WhenAnyPromise2<T>)state;
            bool isWinner = Interlocked.CompareExchange(ref self.winnerIndex, 1, -1) == -1;
            try
            {
                var value = self.source2.GetResult(self.token2);
                if (isWinner)
                    self.core.TrySetResult((1, value));
            }
            catch (OperationCanceledException oce)
            {
                if (isWinner) self.core.TrySetCanceled(oce.CancellationToken);
                // Loser cancellation intentionally not reported (cancellation is not an error)
            }
            catch (Exception ex)
            {
                if (isWinner) self.core.TrySetException(ex);
                else ValkarnTask.PublishUnobservedException(ex);
            }
            self.source2 = null;
            if (Interlocked.Decrement(ref self.returnGuard) == 0)
                self.TryReturn();
        };

        WhenAnyPromise2() { }

        internal static ValkarnTask<(int, T)> Create(ValkarnTask<T> task1, ValkarnTask<T> task2)
        {
            var p = Pool.TryRent() ?? Pool.TrackNew(new WhenAnyPromise2<T>());
            var outToken = p.core.Token;
            p.returned = 0;
            p.winnerIndex = -1;
            p.returnGuard = 3; // 2 potential callbacks + 1 GetResult

            if (task1.source == null)
            {
                if (Interlocked.CompareExchange(ref p.winnerIndex, 0, -1) == -1)
                    p.core.TrySetResult((0, task1.result));
                Interlocked.Decrement(ref p.returnGuard); // no callback registered
            }
            else
            {
                p.source1 = task1.source;
                p.token1 = task1.token;
                task1.source.OnCompleted(s_callback1, p, task1.token);
            }

            if (task2.source == null)
            {
                if (Interlocked.CompareExchange(ref p.winnerIndex, 1, -1) == -1)
                    p.core.TrySetResult((1, task2.result));
                Interlocked.Decrement(ref p.returnGuard); // no callback registered
            }
            else
            {
                p.source2 = task2.source;
                p.token2 = task2.token;
                task2.source.OnCompleted(s_callback2, p, task2.token);
            }

            return new ValkarnTask<(int, T)>(p, outToken);
        }

        // ── ISource ──

        ValkarnTask.Status ValkarnTask.ISource.GetStatus(uint token) => core.GetStatus(token);
        ValkarnTask.Status ValkarnTask.ISource.UnsafeGetStatus() => core.UnsafeGetStatus();

        (int, T) ValkarnTask.ISource<(int, T)>.GetResult(uint token)
        {
            try { return core.GetResult(token); }
            finally
            {
                if (Interlocked.Decrement(ref returnGuard) == 0)
                    TryReturn();
            }
        }

        void ValkarnTask.ISource.GetResult(uint token)
        {
            try { core.GetResult(token); }
            finally
            {
                if (Interlocked.Decrement(ref returnGuard) == 0)
                    TryReturn();
            }
        }

        void ValkarnTask.ISource.OnCompleted(Action<object> continuation, object state, uint token)
            => core.OnCompleted(continuation, state, token);

        void TryReturn()
        {
            if (Interlocked.Exchange(ref returned, 1) != 0) return;
            source1 = null;
            source2 = null;
            winnerIndex = 0;
            core.Reset();
            Pool.TryReturn(this);
        }
    }

    // ════════════════════════════════════════════════════════════════
    //  WhenAnyArrayPromise<T> — Pooled, IEnumerable typed combinator
    //  Uses ArrayPool for sources/tokens arrays.
    // ════════════════════════════════════════════════════════════════

    internal sealed class WhenAnyArrayPromise<T>
        : ValkarnTask.ISource<(int, T)>,
          IPoolNode<WhenAnyArrayPromise<T>>
    {
        static ValkarnPool<WhenAnyArrayPromise<T>> s_pool;
        static ValkarnPool<WhenAnyArrayPromise<T>> Pool => ValkarnPool<WhenAnyArrayPromise<T>>.GetOrCreate(ref s_pool);

        WhenAnyArrayPromise<T> nextNode;
        public ref WhenAnyArrayPromise<T> NextNode => ref nextNode;

        ValkarnCompletionCore<(int, T)> core;
        int returned;
        ValkarnTask.ISource<T>[] sources;
        uint[] tokens;
        int winnerIndex;
        int returnGuard;

        // ── Pooled Slot (simple Treiber stack) ──

        sealed class Slot
        {
            internal WhenAnyArrayPromise<T> Parent;
            internal int Index;
            internal Slot NextSlot;

            static Slot s_slotHead;
            static int s_slotCount;
            const int MaxPooled = 256;

            internal static void ResetPool() { s_slotHead = null; s_slotCount = 0; }

            internal static Slot Rent(WhenAnyArrayPromise<T> parent, int index)
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
            bool isWinner = Interlocked.CompareExchange(ref self.winnerIndex, i, -1) == -1;
            try
            {
                var value = self.sources[i].GetResult(self.tokens[i]);
                if (isWinner)
                    self.core.TrySetResult((i, value));
            }
            catch (OperationCanceledException oce)
            {
                if (isWinner) self.core.TrySetCanceled(oce.CancellationToken);
                // Loser cancellation intentionally not reported
            }
            catch (Exception ex)
            {
                if (isWinner) self.core.TrySetException(ex);
                else ValkarnTask.PublishUnobservedException(ex);
            }
            self.sources[i] = null;
            if (Interlocked.Decrement(ref self.returnGuard) == 0)
                self.TryReturn();
        };

        WhenAnyArrayPromise() { }

        internal static ValkarnTask<(int, T)> Create(ValkarnTask<T>[] tasks)
        {
            var n = tasks.Length;
            var p = Pool.TryRent() ?? Pool.TrackNew(new WhenAnyArrayPromise<T>());
            var outToken = p.core.Token;
            p.returned = 0;
            p.sources = ArrayPool<ValkarnTask.ISource<T>>.Shared.Rent(n);
            p.tokens = ArrayPool<uint>.Shared.Rent(n);
            p.winnerIndex = -1;
            p.returnGuard = n + 1; // n potential callbacks + 1 GetResult

            for (int i = 0; i < n; i++)
            {
                if (tasks[i].source == null)
                {
                    if (Interlocked.CompareExchange(ref p.winnerIndex, i, -1) == -1)
                        p.core.TrySetResult((i, tasks[i].result));
                    p.sources[i] = null;
                    Interlocked.Decrement(ref p.returnGuard); // no callback registered
                }
                else
                {
                    p.sources[i] = tasks[i].source;
                    p.tokens[i] = tasks[i].token;
                    tasks[i].source.OnCompleted(s_callback,
                        Slot.Rent(p, i), tasks[i].token);
                }
            }

            return new ValkarnTask<(int, T)>(p, outToken);
        }

        ValkarnTask.Status ValkarnTask.ISource.GetStatus(uint token) => core.GetStatus(token);
        ValkarnTask.Status ValkarnTask.ISource.UnsafeGetStatus() => core.UnsafeGetStatus();

        (int, T) ValkarnTask.ISource<(int, T)>.GetResult(uint token)
        {
            try { return core.GetResult(token); }
            finally
            {
                if (Interlocked.Decrement(ref returnGuard) == 0)
                    TryReturn();
            }
        }

        void ValkarnTask.ISource.GetResult(uint token)
        {
            try { core.GetResult(token); }
            finally
            {
                if (Interlocked.Decrement(ref returnGuard) == 0)
                    TryReturn();
            }
        }

        void ValkarnTask.ISource.OnCompleted(Action<object> continuation, object state, uint token)
            => core.OnCompleted(continuation, state, token);

        void TryReturn()
        {
            if (Interlocked.Exchange(ref returned, 1) != 0) return;
            var s = sources;
            var t = tokens;
            sources = null;
            tokens = null;
            if (s != null) ArrayPool<ValkarnTask.ISource<T>>.Shared.Return(s, clearArray: true);
            if (t != null) ArrayPool<uint>.Shared.Return(t);
            winnerIndex = 0;
            core.Reset();
            Pool.TryReturn(this);
        }
    }

    // ════════════════════════════════════════════════════════════════
    //  WhenAnyVoidPromise2 — Pooled, 2-arity void combinator
    // ════════════════════════════════════════════════════════════════

    internal sealed class WhenAnyVoidPromise2
        : ValkarnTask.ISource<int>,
          IPoolNode<WhenAnyVoidPromise2>
    {
        static ValkarnPool<WhenAnyVoidPromise2> s_pool;
        static ValkarnPool<WhenAnyVoidPromise2> Pool => ValkarnPool<WhenAnyVoidPromise2>.GetOrCreate(ref s_pool);

        WhenAnyVoidPromise2 nextNode;
        public ref WhenAnyVoidPromise2 NextNode => ref nextNode;

        ValkarnCompletionCore<int> core;
        int returned;
        ValkarnTask.ISource source1, source2;
        uint token1, token2;
        int winnerIndex;
        int returnGuard;

        static readonly Action<object> s_callback1 = static state =>
        {
            var self = (WhenAnyVoidPromise2)state;
            bool isWinner = Interlocked.CompareExchange(ref self.winnerIndex, 0, -1) == -1;
            try
            {
                self.source1.GetResult(self.token1);
                if (isWinner)
                    self.core.TrySetResult(0);
            }
            catch (OperationCanceledException oce)
            {
                if (isWinner) self.core.TrySetCanceled(oce.CancellationToken);
                // Loser cancellation intentionally not reported
            }
            catch (Exception ex)
            {
                if (isWinner) self.core.TrySetException(ex);
                else ValkarnTask.PublishUnobservedException(ex);
            }
            self.source1 = null;
            if (Interlocked.Decrement(ref self.returnGuard) == 0)
                self.TryReturn();
        };

        static readonly Action<object> s_callback2 = static state =>
        {
            var self = (WhenAnyVoidPromise2)state;
            bool isWinner = Interlocked.CompareExchange(ref self.winnerIndex, 1, -1) == -1;
            try
            {
                self.source2.GetResult(self.token2);
                if (isWinner)
                    self.core.TrySetResult(1);
            }
            catch (OperationCanceledException oce)
            {
                if (isWinner) self.core.TrySetCanceled(oce.CancellationToken);
                // Loser cancellation intentionally not reported (cancellation is not an error)
            }
            catch (Exception ex)
            {
                if (isWinner) self.core.TrySetException(ex);
                else ValkarnTask.PublishUnobservedException(ex);
            }
            self.source2 = null;
            if (Interlocked.Decrement(ref self.returnGuard) == 0)
                self.TryReturn();
        };

        WhenAnyVoidPromise2() { }

        internal static ValkarnTask<int> Create(ValkarnTask task1, ValkarnTask task2)
        {
            var p = Pool.TryRent() ?? Pool.TrackNew(new WhenAnyVoidPromise2());
            var outToken = p.core.Token;
            p.returned = 0;
            p.winnerIndex = -1;
            p.returnGuard = 3; // 2 potential callbacks + 1 GetResult

            if (task1.source == null)
            {
                if (Interlocked.CompareExchange(ref p.winnerIndex, 0, -1) == -1)
                    p.core.TrySetResult(0);
                Interlocked.Decrement(ref p.returnGuard);
            }
            else if (task1.source is NeverSource)
            {
                // NeverSource.OnCompleted is a no-op; pre-decrement to avoid permanent pool leak
                Interlocked.Decrement(ref p.returnGuard);
            }
            else
            {
                p.source1 = task1.source;
                p.token1 = task1.token;
                task1.source.OnCompleted(s_callback1, p, task1.token);
            }

            if (task2.source == null)
            {
                if (Interlocked.CompareExchange(ref p.winnerIndex, 1, -1) == -1)
                    p.core.TrySetResult(1);
                Interlocked.Decrement(ref p.returnGuard);
            }
            else if (task2.source is NeverSource)
            {
                Interlocked.Decrement(ref p.returnGuard);
            }
            else
            {
                p.source2 = task2.source;
                p.token2 = task2.token;
                task2.source.OnCompleted(s_callback2, p, task2.token);
            }

            return new ValkarnTask<int>(p, outToken);
        }

        // ── ISource ──

        ValkarnTask.Status ValkarnTask.ISource.GetStatus(uint token) => core.GetStatus(token);
        ValkarnTask.Status ValkarnTask.ISource.UnsafeGetStatus() => core.UnsafeGetStatus();

        int ValkarnTask.ISource<int>.GetResult(uint token)
        {
            try { return core.GetResult(token); }
            finally
            {
                if (Interlocked.Decrement(ref returnGuard) == 0)
                    TryReturn();
            }
        }

        void ValkarnTask.ISource.GetResult(uint token)
        {
            try { core.GetResult(token); }
            finally
            {
                if (Interlocked.Decrement(ref returnGuard) == 0)
                    TryReturn();
            }
        }

        void ValkarnTask.ISource.OnCompleted(Action<object> continuation, object state, uint token)
            => core.OnCompleted(continuation, state, token);

        void TryReturn()
        {
            if (Interlocked.Exchange(ref returned, 1) != 0) return;
            source1 = null;
            source2 = null;
            winnerIndex = 0;
            core.Reset();
            Pool.TryReturn(this);
        }
    }

    // ════════════════════════════════════════════════════════════════
    //  WhenAnyVoidArrayPromise — Pooled, IEnumerable void combinator
    //  Uses ArrayPool for sources/tokens arrays.
    // ════════════════════════════════════════════════════════════════

    internal sealed class WhenAnyVoidArrayPromise
        : ValkarnTask.ISource<int>,
          IPoolNode<WhenAnyVoidArrayPromise>
    {
        static ValkarnPool<WhenAnyVoidArrayPromise> s_pool;
        static ValkarnPool<WhenAnyVoidArrayPromise> Pool => ValkarnPool<WhenAnyVoidArrayPromise>.GetOrCreate(ref s_pool);

        WhenAnyVoidArrayPromise nextNode;
        public ref WhenAnyVoidArrayPromise NextNode => ref nextNode;

        ValkarnCompletionCore<int> core;
        int returned;
        ValkarnTask.ISource[] sources;
        uint[] tokens;
        int winnerIndex;
        int returnGuard;

        // ── Pooled Slot (simple Treiber stack) ──

        sealed class Slot
        {
            internal WhenAnyVoidArrayPromise Parent;
            internal int Index;
            internal Slot NextSlot;

            static Slot s_slotHead;
            static int s_slotCount;
            const int MaxPooled = 256;

            internal static void ResetPool() { s_slotHead = null; s_slotCount = 0; }

            internal static Slot Rent(WhenAnyVoidArrayPromise parent, int index)
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
            bool isWinner = Interlocked.CompareExchange(ref self.winnerIndex, i, -1) == -1;
            try
            {
                self.sources[i].GetResult(self.tokens[i]);
                if (isWinner)
                    self.core.TrySetResult(i);
            }
            catch (OperationCanceledException oce)
            {
                if (isWinner) self.core.TrySetCanceled(oce.CancellationToken);
                // Loser cancellation intentionally not reported
            }
            catch (Exception ex)
            {
                if (isWinner) self.core.TrySetException(ex);
                else ValkarnTask.PublishUnobservedException(ex);
            }
            self.sources[i] = null;
            if (Interlocked.Decrement(ref self.returnGuard) == 0)
                self.TryReturn();
        };

        WhenAnyVoidArrayPromise() { }

        internal static ValkarnTask<int> Create(ValkarnTask[] tasks)
        {
            var n = tasks.Length;
            var p = Pool.TryRent() ?? Pool.TrackNew(new WhenAnyVoidArrayPromise());
            var outToken = p.core.Token;
            p.sources = ArrayPool<ValkarnTask.ISource>.Shared.Rent(n);
            p.tokens = ArrayPool<uint>.Shared.Rent(n);
            p.returned = 0;
            p.winnerIndex = -1;
            p.returnGuard = n + 1; // n potential callbacks + 1 GetResult

            for (int i = 0; i < n; i++)
            {
                if (tasks[i].source == null)
                {
                    if (Interlocked.CompareExchange(ref p.winnerIndex, i, -1) == -1)
                        p.core.TrySetResult(i);
                    p.sources[i] = null;
                    Interlocked.Decrement(ref p.returnGuard);
                }
                else if (tasks[i].source is NeverSource)
                {
                    // NeverSource.OnCompleted is a no-op; pre-decrement to avoid permanent pool leak
                    p.sources[i] = null;
                    Interlocked.Decrement(ref p.returnGuard);
                }
                else
                {
                    p.sources[i] = tasks[i].source;
                    p.tokens[i] = tasks[i].token;
                    tasks[i].source.OnCompleted(s_callback,
                        Slot.Rent(p, i), tasks[i].token);
                }
            }

            return new ValkarnTask<int>(p, outToken);
        }

        ValkarnTask.Status ValkarnTask.ISource.GetStatus(uint token) => core.GetStatus(token);
        ValkarnTask.Status ValkarnTask.ISource.UnsafeGetStatus() => core.UnsafeGetStatus();

        int ValkarnTask.ISource<int>.GetResult(uint token)
        {
            try { return core.GetResult(token); }
            finally
            {
                if (Interlocked.Decrement(ref returnGuard) == 0)
                    TryReturn();
            }
        }

        void ValkarnTask.ISource.GetResult(uint token)
        {
            try { core.GetResult(token); }
            finally
            {
                if (Interlocked.Decrement(ref returnGuard) == 0)
                    TryReturn();
            }
        }

        void ValkarnTask.ISource.OnCompleted(Action<object> continuation, object state, uint token)
            => core.OnCompleted(continuation, state, token);

        void TryReturn()
        {
            if (Interlocked.Exchange(ref returned, 1) != 0) return;
            var s = sources;
            var t = tokens;
            sources = null;
            tokens = null;
            if (s != null) ArrayPool<ValkarnTask.ISource>.Shared.Return(s, clearArray: true);
            if (t != null) ArrayPool<uint>.Shared.Return(t);
            winnerIndex = 0;
            core.Reset();
            Pool.TryReturn(this);
        }
    }
}
