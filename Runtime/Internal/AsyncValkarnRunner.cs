// Copyright (c) Una Partida Mas. All rights reserved.
// Licensed under the MIT License. See LICENSE.md in the repository root.

using System;
using System.Runtime.CompilerServices;
using System.Threading;

namespace UnaPartidaMas.Valkarn.Tasks
{
    /// <summary>
    /// Internal interface for the method builder to interact with the runner
    /// without knowing the TStateMachine type parameter.
    /// Extends ISource so the same object serves as both runner and task source.
    /// </summary>
    internal interface IStateMachineRunnerPromise : ValkarnTask.ISource
    {
        Action MoveNextAction { get; }
        uint Token { get; }
        void SetResult();
        void SetException(Exception exception);
    }

    /// <summary>
    /// Pre-written generic pooled runner for non-generic async ValkarnTask methods.
    /// One pool per TStateMachine type (= per async method) via generic specialization.
    ///
    /// Lifecycle:
    ///   1. Rented from pool (or created) by AsyncValkarnMethodBuilder on first suspension
    ///   2. Stores the compiler-generated state machine by value (no boxing)
    ///   3. MoveNext delegate is cached once per pool instance (no allocation per await)
    ///   4. Returns to pool when GetResult() is called by the awaiter
    /// </summary>
    internal sealed class AsyncValkarnRunner<TStateMachine>
        : IStateMachineRunnerPromise, IPoolNode<AsyncValkarnRunner<TStateMachine>>
        where TStateMachine : IAsyncStateMachine
    {
        static ValkarnPool<AsyncValkarnRunner<TStateMachine>> s_pool;

        // ── Pool Node ──
        AsyncValkarnRunner<TStateMachine> nextNode;
        public ref AsyncValkarnRunner<TStateMachine> NextNode => ref nextNode;

        // ── Core ──
        TStateMachine stateMachine;
        ValkarnCompletionCore<AsyncUnit> core;
        Action moveNext;
        int returned;

        AsyncValkarnRunner()
        {
            moveNext = Run;
        }

        static ValkarnPool<AsyncValkarnRunner<TStateMachine>> Pool
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => ValkarnPool<AsyncValkarnRunner<TStateMachine>>.GetOrCreate(ref s_pool);
        }

        /// <summary>
        /// Creates/rents a runner and wires it into the method builder.
        ///
        /// CRITICAL ORDERING:
        ///   1. Set the builder's runner field (via runnerFieldRef) FIRST
        ///   2. THEN copy the state machine into the runner
        ///
        /// This ensures the copy of the state machine inside the runner
        /// has the builder.runner field already pointing to the runner.
        /// When MoveNext is called later and the state machine calls
        /// builder.SetResult(), builder.runner is not null.
        /// </summary>
        internal static void SetStateMachine(
            ref TStateMachine stateMachine,
            ref IStateMachineRunnerPromise runnerFieldRef)
        {
            var runner = Pool.TryRent();
            if (runner == null)
            {
                runner = new AsyncValkarnRunner<TStateMachine>();
                Pool.IncrementCreated();
            }

            runner.returned = 0;

            // Step 1: Store runner reference in the builder's field
            // (this modifies the stateMachine struct because builder is a field of it)
            runnerFieldRef = runner;

            // Step 2: Copy the state machine (now includes builder.runner = runner)
            runner.stateMachine = stateMachine;
        }

        void Run()
        {
            stateMachine.MoveNext();
        }

        public Action MoveNextAction
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => moveNext;
        }

        public uint Token
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => core.Token;
        }

        // ── IStateMachineRunnerPromise (completion) ──

        public void SetResult()
            => core.TrySetResult(AsyncUnit.Default);

        public void SetException(Exception exception)
            => core.TrySetException(exception);

        // ── ISource Implementation ──

        public ValkarnTask.Status GetStatus(uint token)
            => core.GetStatus(token);

        public void GetResult(uint token)
        {
            try
            {
                core.GetResult(token);
            }
            finally
            {
                TryReturn();
            }
        }

        public void OnCompleted(Action<object> continuation, object state, uint token)
            => core.OnCompleted(continuation, state, token);

        public ValkarnTask.Status UnsafeGetStatus()
            => core.UnsafeGetStatus();

        // ── Pool Return ──

        void TryReturn()
        {
            if (Interlocked.Exchange(ref returned, 1) != 0) return;
            // Clear state machine BEFORE Reset: Reset publishes new generation,
            // making this slot rentable. If we cleared SM after, a new renter
            // on another thread could have its SM clobbered.
            stateMachine = default;
            core.Reset();
            Pool.TryReturn(this);
        }
    }
}
