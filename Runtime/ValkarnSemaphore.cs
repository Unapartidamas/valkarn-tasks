// Copyright (c) Una Partida Mas. All rights reserved.
// Licensed under the MIT License. See LICENSE.md in the repository root.

using System;
using System.Runtime.CompilerServices;
using System.Threading;

namespace UnaPartidaMas.Valkarn.Tasks
{
    /// <summary>
    /// Zero-allocation async semaphore built on ValkarnTask.
    /// <para>
    /// Fast path (uncontended): returns <see cref="ValkarnTask.CompletedTask"/> — zero heap allocation.
    /// Slow path (contended): waiter nodes are rented from a bounded pool (Fast Slot + Treiber Stack)
    /// and returned on completion — zero GC in steady state.
    /// </para>
    /// <para>
    /// Thread safety: all state is protected by a single monitor lock.
    /// Completions are dispatched <em>outside</em> the lock so continuations
    /// never run with the lock held, preventing deadlocks if a continuation
    /// re-enters the semaphore.
    /// </para>
    /// </summary>
    public sealed class ValkarnSemaphore : IDisposable
    {
        private int _count;
        private readonly int _maxCount;
        private WaiterNode _head;
        private WaiterNode _tail;
        private readonly object _gate = new object();
        private bool _disposed;

        /// <param name="initialCount">Initial number of available slots.</param>
        /// <param name="maxCount">Maximum number of slots (upper bound for <see cref="Release"/>).</param>
        public ValkarnSemaphore(int initialCount, int maxCount)
        {
            if (maxCount <= 0)
                throw new ArgumentOutOfRangeException(nameof(maxCount), "maxCount must be > 0.");
            if (initialCount < 0 || initialCount > maxCount)
                throw new ArgumentOutOfRangeException(nameof(initialCount),
                    "initialCount must be in [0, maxCount].");

            _count = initialCount;
            _maxCount = maxCount;
        }

        /// <summary>Current number of available slots.</summary>
        public int CurrentCount
        {
            get { lock (_gate) return _count; }
        }

        /// <summary>
        /// Acquires one slot synchronously, blocking the calling thread if necessary.
        /// Fast path (uncontended) is allocation-free.
        /// Slow path parks the thread via <see cref="ManualResetEventSlim"/>.
        /// </summary>
        public void Wait(CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();

            lock (_gate)
            {
                if (_disposed) ThrowObjectDisposed();

                if (_count > 0)
                {
                    _count--;
                    return; // zero allocation fast path
                }
            }

            // Slow path: enqueue an async waiter, then block the thread until it completes.
            // ManualResetEventSlim allocation is acceptable here — this is a contended, infrequent path.
            using var mre = new ManualResetEventSlim(false);
            ValkarnTask task = WaitAsync(ct);
            ValkarnTask.Awaiter awaiter = task.GetAwaiter();

            if (!awaiter.IsCompleted)
                awaiter.UnsafeOnCompleted(() => mre.Set());

            if (!awaiter.IsCompleted)
                mre.Wait(ct);

            awaiter.GetResult(); // propagates exceptions, returns WaiterNode to pool
        }

        /// <summary>
        /// Acquires one slot asynchronously.
        /// Returns a completed <see cref="ValkarnTask"/> immediately (zero allocation) when
        /// a slot is available; otherwise suspends until <see cref="Release"/> is called.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ValkarnTask WaitAsync(CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();

            lock (_gate)
            {
                if (_disposed) ThrowObjectDisposed();

                // ── Fast path: slot available ──────────────────────────────
                if (_count > 0)
                {
                    _count--;
                    return ValkarnTask.CompletedTask; // zero allocation
                }

                // ── Slow path: enqueue a pooled waiter ─────────────────────
                var node = WaiterNode.Rent(this, ct);
                if (_tail == null)
                    _head = _tail = node;
                else
                {
                    _tail.NextWaiter = node;
                    _tail = node;
                }
                return node.GetTask();
            }
        }

        /// <summary>
        /// Releases <paramref name="releaseCount"/> slots.
        /// Pending waiters are completed in FIFO order.
        /// </summary>
        public void Release(int releaseCount = 1)
        {
            if (releaseCount < 1)
                throw new ArgumentOutOfRangeException(nameof(releaseCount),
                    "releaseCount must be >= 1.");

            // Collect nodes to complete outside the lock.
            WaiterNode toComplete = null;

            lock (_gate)
            {
                if (_disposed) ThrowObjectDisposed();

                if (checked(_count + releaseCount) > _maxCount)
                    throw new InvalidOperationException(
                        $"Release would exceed maxCount ({_maxCount}). " +
                        "Ensure every WaitAsync is matched by exactly one Release.");

                // Satisfy waiting callers before incrementing the counter.
                while (releaseCount > 0 && _head != null)
                {
                    WaiterNode node = Dequeue();
                    // Mark as acquired under the lock — cancellation callback
                    // checks this flag and backs off if set.
                    node.SetAcquired();
                    // Chain nodes to complete outside the lock.
                    node.NextWaiter = toComplete;
                    toComplete = node;
                    releaseCount--;
                }

                _count += releaseCount;
            }

            // Complete waiters outside the lock (principle: never hold lock across continuation).
            while (toComplete != null)
            {
                WaiterNode next = toComplete.NextWaiter;
                toComplete.NextWaiter = null;
                toComplete.Complete();
                toComplete = next;
            }
        }

        /// <summary>
        /// Called by a <see cref="WaiterNode"/> whose <see cref="CancellationToken"/> fired.
        /// Removes the node from the queue (if still present) and cancels its task.
        /// </summary>
        internal void OnCancellation(WaiterNode node, CancellationToken ct)
        {
            bool removed;
            lock (_gate)
            {
                // If Release already marked the node as acquired, it owns it — back off.
                if (node.IsAcquired)
                    return;
                removed = TryRemove(node);
            }
            if (removed)
                node.Cancel(ct);
        }

        // ── Queue helpers ──────────────────────────────────────────────────

        private WaiterNode Dequeue()
        {
            WaiterNode node = _head;
            _head = node.NextWaiter;
            if (_head == null) _tail = null;
            node.NextWaiter = null;
            return node;
        }

        private bool TryRemove(WaiterNode target)
        {
            WaiterNode prev = null;
            WaiterNode curr = _head;
            while (curr != null)
            {
                if (ReferenceEquals(curr, target))
                {
                    if (prev == null) _head = curr.NextWaiter;
                    else prev.NextWaiter = curr.NextWaiter;
                    if (ReferenceEquals(_tail, curr)) _tail = prev;
                    curr.NextWaiter = null;
                    return true;
                }
                prev = curr;
                curr = curr.NextWaiter;
            }
            return false;
        }

        // ── IDisposable ────────────────────────────────────────────────────

        /// <summary>
        /// Cancels all pending waiters and releases resources.
        /// </summary>
        public void Dispose()
        {
            WaiterNode toCancel = null;
            lock (_gate)
            {
                if (_disposed) return;
                _disposed = true;
                toCancel = _head;
                _head = _tail = null;
            }

            while (toCancel != null)
            {
                WaiterNode next = toCancel.NextWaiter;
                toCancel.NextWaiter = null;
                toCancel.Cancel(default);
                toCancel = next;
            }
        }

        private static void ThrowObjectDisposed() =>
            throw new ObjectDisposedException(nameof(ValkarnSemaphore));
    }

    // ── WaiterNode ─────────────────────────────────────────────────────────────
    // Implements ValkarnTask.ISource directly so the task is the node itself —
    // no extra heap allocation beyond the pooled node.

    internal sealed class WaiterNode : ValkarnTask.ISource, IPoolNode<WaiterNode>
    {
        // ── Pool intrusive link ────────────────────────────────────────────
        private static ValkarnPool<WaiterNode> s_pool;
        private WaiterNode _poolNext;
        public ref WaiterNode NextNode => ref _poolNext;

        // ── Semaphore queue link (reused as completion chain in Release) ───
        internal WaiterNode NextWaiter;

        // ── Async state ───────────────────────────────────────────────────
        private ValkarnCompletionCore<AsyncUnit> _core;
        private CancellationTokenRegistration _ctr;
        private ValkarnSemaphore _owner;

        // ── Acquired flag: set by Release under the semaphore lock ─────────
        // Prevents the cancellation callback from firing after Release claimed this node.
        private int _acquired;  // 0 = pending, 1 = acquired by Release
        private int _returned;  // 0 = live, 1 = returned to pool

        internal bool IsAcquired =>
            Volatile.Read(ref _acquired) == 1;

        // ── Rent / Return ─────────────────────────────────────────────────

        internal static WaiterNode Rent(ValkarnSemaphore owner, CancellationToken ct)
        {
            var pool = ValkarnPool<WaiterNode>.GetOrCreate(ref s_pool);
            WaiterNode node = pool.TryRent() ?? pool.TrackNew(new WaiterNode());
            node._owner = owner;
            node._acquired = 0;
            node._returned = 0;
            node.NextWaiter = null;

            if (ct.CanBeCanceled)
            {
                // Store ct for the callback. Capture node as state to avoid closure alloc.
                node._ctr = ct.Register(
                    static state =>
                    {
                        var (n, token) = ((WaiterNode, CancellationToken))state;
                        n._owner.OnCancellation(n, token);
                    },
                    (node, ct));
            }

            return node;
        }

        internal ValkarnTask GetTask()
        {
            return new ValkarnTask(this, _core.Token);
        }

        // ── Called by Release (outside the semaphore lock) ─────────────────

        internal void SetAcquired() => Volatile.Write(ref _acquired, 1);

        internal void Complete()
        {
            _ctr.Dispose();
            _core.TrySetResult(AsyncUnit.Default);
        }

        internal void Cancel(CancellationToken ct)
        {
            _ctr.Dispose();
            _core.TrySetCanceled(ct);
        }

        // ── ValkarnTask.ISource ────────────────────────────────────────────

        public ValkarnTask.Status GetStatus(uint token) => _core.GetStatus(token);

        public ValkarnTask.Status UnsafeGetStatus() => _core.UnsafeGetStatus();

        public void GetResult(uint token)
        {
            try
            {
                _core.GetResult(token);
            }
            finally
            {
                TryReturn();
            }
        }

        public void OnCompleted(Action<object> continuation, object state, uint token)
            => _core.OnCompleted(continuation, state, token);

        // ── Pool return ────────────────────────────────────────────────────

        private void TryReturn()
        {
            if (Interlocked.Exchange(ref _returned, 1) != 0) return;
            _core.Reset();
            _owner = null;
            _ctr = default;
            ValkarnPool<WaiterNode>.GetOrCreate(ref s_pool).TryReturn(this);
        }
    }
}
