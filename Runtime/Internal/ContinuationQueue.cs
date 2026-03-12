// Copyright (c) Una Partida Mas. All rights reserved.
// Licensed under the MIT License. See LICENSE.md in the repository root.

using System;
using System.Runtime.CompilerServices;
using System.Threading;

namespace UnaPartidaMas.Valkarn.Tasks
{
    /// <summary>
    /// Double-buffered dynamic array for one-shot continuations, with Treiber stack for cross-thread.
    ///
    /// Flow:
    ///   1. Enqueue: under SpinLock, append to actionList (or waitingList if draining).
    ///   2. Cross-thread enqueue: push to crossThreadHead via CAS.
    ///   3. Drain (per PlayerLoop tick):
    ///      a. Drain crossThreadHead into actionList
    ///      b. Set isDraining = true
    ///      c. Execute actionList[0..count]
    ///      d. Swap (actionList, waitingList). Reuses arrays.
    ///      e. Set isDraining = false
    ///   4. Growth: double capacity when full (power-of-2).
    ///
    /// After warmup (~1-2 frames), arrays stabilize at high-water mark — zero allocation.
    /// SpinLock is held for nanoseconds during enqueue.
    /// </summary>
    internal sealed class ContinuationQueue
    {
        const int InitialCapacity = 256;

        // ── Double-buffered arrays ──
        Action<object>[] actionList;
        object[] stateList;
        int actionListCount;

        Action<object>[] waitingList;
        object[] waitingStateList;
        int waitingListCount;

        // ── Cross-thread Treiber stack ──
        volatile ContinuationNode crossThreadHead;

        // ── Synchronization ──
        SpinLock gate;
        bool isDraining;

        internal ContinuationQueue()
        {
            actionList = new Action<object>[InitialCapacity];
            stateList = new object[InitialCapacity];
            waitingList = new Action<object>[InitialCapacity];
            waitingStateList = new object[InitialCapacity];
#if UNITY_ASSERTIONS || DEBUG
            gate = new SpinLock(true); // detect re-entrancy in development builds
#else
            gate = new SpinLock(false);
#endif
        }

        /// <summary>
        /// Enqueues a continuation from the main thread.
        /// </summary>
        internal void Enqueue(Action<object> action, object state)
        {
            bool lockTaken = false;
            try
            {
                gate.Enter(ref lockTaken);

                if (isDraining)
                {
                    // Currently draining actionList — append to waiting list
                    if (waitingListCount == waitingList.Length)
                        GrowWaitingList();

                    waitingList[waitingListCount] = action;
                    waitingStateList[waitingListCount] = state;
                    waitingListCount++;
                }
                else
                {
                    if (actionListCount == actionList.Length)
                        GrowActionList();

                    actionList[actionListCount] = action;
                    stateList[actionListCount] = state;
                    actionListCount++;
                }
            }
            finally
            {
                if (lockTaken) gate.Exit(false);
            }
        }

        /// <summary>
        /// Enqueues a continuation from a background thread.
        /// Lock-free via Treiber stack.
        /// </summary>
        internal void EnqueueFromThread(Action<object> action, object state)
        {
            var node = ContinuationNode.Rent();
            node.Action = action;
            node.State = state;
            SpinWait spinner = default;
            while (true)
            {
                var head = crossThreadHead;
                node.Next = head;
                if (Interlocked.CompareExchange(ref crossThreadHead, node, head) == head)
                    return;
                spinner.SpinOnce();
            }
        }

        /// <summary>
        /// Drains and executes all queued continuations. Called once per PlayerLoop tick.
        /// </summary>
        internal void Run()
        {
            // 1. Drain cross-thread items into actionList
            DrainCrossThreadItems();

            // 2. Snapshot and mark as draining
            int count;
            Action<object>[] items;
            object[] states;

            bool lockTaken = false;
            try
            {
                gate.Enter(ref lockTaken);
                isDraining = true;
                items = actionList;
                states = stateList;
                count = actionListCount;
            }
            finally
            {
                if (lockTaken) gate.Exit(false);
            }

            // 3. Execute all items (outside lock)
            for (int i = 0; i < count; i++)
            {
                var action = items[i];
                var state = states[i];
                items[i] = null;    // clear for GC
                states[i] = null;

                try
                {
                    action(state);
                }
                catch (Exception ex)
                {
                    ValkarnTask.PublishUnobservedException(ex);
                }
            }

            // 4. Swap lists
            lockTaken = false;
            try
            {
                gate.Enter(ref lockTaken);

                // Swap: actionList ↔ waitingList
                var tempActions = actionList;
                var tempStates = stateList;

                actionList = waitingList;
                stateList = waitingStateList;
                actionListCount = waitingListCount;

                waitingList = tempActions;
                waitingStateList = tempStates;
                waitingListCount = 0;

                isDraining = false;
            }
            finally
            {
                if (lockTaken) gate.Exit(false);
            }
        }

        void DrainCrossThreadItems()
        {
            // Atomically take the entire cross-thread list
            var head = Interlocked.Exchange(ref crossThreadHead, null);
            if (head == null) return;

            // Reverse to preserve FIFO order
            ContinuationNode reversed = null;
            while (head != null)
            {
                var next = head.Next;
                head.Next = reversed;
                reversed = head;
                head = next;
            }

            // Enqueue into actionList under lock (caller does NOT hold the lock)
            bool lockTaken = false;
            try
            {
                gate.Enter(ref lockTaken);
                while (reversed != null)
                {
                    if (actionListCount == actionList.Length)
                        GrowActionList();

                    actionList[actionListCount] = reversed.Action;
                    stateList[actionListCount] = reversed.State;
                    actionListCount++;
                    var next = reversed.Next;
                    ContinuationNode.Return(reversed);
                    reversed = next;
                }
            }
            finally
            {
                if (lockTaken) gate.Exit(false);
            }
        }

        // Maximum capacity to prevent unbounded memory growth from runaway producers.
        const int MaxCapacity = 1 << 20; // 1,048,576

        void GrowActionList()
        {
            int currentLength = actionList.Length;
            if (currentLength >= MaxCapacity)
                throw new InvalidOperationException(
                    $"ContinuationQueue exceeded maximum capacity ({MaxCapacity}). This likely indicates a runaway producer.");
            var newSize = currentLength * 2;
            var newActions = new Action<object>[newSize];
            var newStates = new object[newSize];
            Array.Copy(actionList, newActions, actionListCount);
            Array.Copy(stateList, newStates, actionListCount);
            actionList = newActions;
            stateList = newStates;
        }

        void GrowWaitingList()
        {
            int currentLength = waitingList.Length;
            if (currentLength >= MaxCapacity)
                throw new InvalidOperationException(
                    $"ContinuationQueue exceeded maximum capacity ({MaxCapacity}). This likely indicates a runaway producer.");
            var newSize = currentLength * 2;
            var newActions = new Action<object>[newSize];
            var newStates = new object[newSize];
            Array.Copy(waitingList, newActions, waitingListCount);
            Array.Copy(waitingStateList, newStates, waitingListCount);
            waitingList = newActions;
            waitingStateList = newStates;
        }

        /// <summary>
        /// Intrusive linked list node for cross-thread continuations.
        /// Pooled to avoid allocation on every cross-thread enqueue.
        /// </summary>
        internal class ContinuationNode
        {
            internal Action<object> Action;
            internal object State;
            internal ContinuationNode Next;

            // ── Simple lock-free bounded pool for ContinuationNode ──
            const int MaxPoolSize = 1024;
            static ContinuationNode s_poolHead;
            static int s_poolCount;

            internal static void ResetPool()
            {
                s_poolHead = null;
                s_poolCount = 0;
            }

            internal static ContinuationNode Rent()
            {
                SpinWait spinner = default;
                while (true)
                {
                    var head = Volatile.Read(ref s_poolHead);
                    if (head == null)
                        return new ContinuationNode();

                    if (Interlocked.CompareExchange(ref s_poolHead, head.Next, head) == head)
                    {
                        Interlocked.Decrement(ref s_poolCount);
                        head.Next = null;
                        return head;
                    }
                    spinner.SpinOnce();
                }
            }

            internal static void Return(ContinuationNode node)
            {
                node.Action = null;
                node.State = null;

                // Drop excess nodes instead of pooling unboundedly
                if (Volatile.Read(ref s_poolCount) >= MaxPoolSize)
                    return;

                SpinWait spinner = default;
                while (true)
                {
                    var head = Volatile.Read(ref s_poolHead);
                    node.Next = head;
                    if (Interlocked.CompareExchange(ref s_poolHead, node, head) == head)
                    {
                        Interlocked.Increment(ref s_poolCount);
                        return;
                    }
                    spinner.SpinOnce();
                }
            }
        }
    }
}
