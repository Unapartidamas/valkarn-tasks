// Copyright (c) Una Partida Mas. All rights reserved.
// Licensed under the MIT License. See LICENSE.md in the repository root.

using System;
using System.Runtime.CompilerServices;
using System.Threading;

namespace UnaPartidaMas.Valkarn.Tasks
{
    /// <summary>
    /// Generic variant of IStateMachineRunnerPromise for ValkarnTask{TResult}.
    /// </summary>
    internal interface IStateMachineRunnerPromise<TResult> : ValkarnTask.ISource<TResult>
    {
        Action MoveNextAction { get; }
        uint Token { get; }
        void SetResult(TResult result);
        void SetException(Exception exception);
    }

    /// <summary>
    /// Pre-written generic pooled runner for generic async ValkarnTask{TResult} methods.
    /// Same architecture as AsyncValkarnRunner{TStateMachine} but produces a typed result.
    /// </summary>
    internal sealed class AsyncValkarnRunner<TStateMachine, TResult>
        : IStateMachineRunnerPromise<TResult>, IPoolNode<AsyncValkarnRunner<TStateMachine, TResult>>
        where TStateMachine : IAsyncStateMachine
    {
        static ValkarnPool<AsyncValkarnRunner<TStateMachine, TResult>> s_pool;

        // ── Pool Node ──
        AsyncValkarnRunner<TStateMachine, TResult> nextNode;
        public ref AsyncValkarnRunner<TStateMachine, TResult> NextNode => ref nextNode;

        // ── Core ──
        TStateMachine stateMachine;
        ValkarnCompletionCore<TResult> core;
        Action moveNext;
        int returned;

        AsyncValkarnRunner()
        {
            moveNext = Run;
        }

        static ValkarnPool<AsyncValkarnRunner<TStateMachine, TResult>> Pool
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => ValkarnPool<AsyncValkarnRunner<TStateMachine, TResult>>.GetOrCreate(ref s_pool);
        }

        /// <summary>
        /// Creates/rents a runner. Same critical ordering as non-generic variant:
        /// set builder field FIRST, then copy state machine.
        /// </summary>
        internal static void SetStateMachine(
            ref TStateMachine stateMachine,
            ref IStateMachineRunnerPromise<TResult> runnerFieldRef)
        {
            var runner = Pool.TryRent();
            if (runner == null)
            {
                runner = new AsyncValkarnRunner<TStateMachine, TResult>();
                Pool.IncrementCreated();
            }

            runner.returned = 0;
            runnerFieldRef = runner;
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

        // ── IStateMachineRunnerPromise<TResult> (completion) ──

        public void SetResult(TResult result)
            => core.TrySetResult(result);

        public void SetException(Exception exception)
            => core.TrySetException(exception);

        // ── ISource<TResult> Implementation ──

        public ValkarnTask.Status GetStatus(uint token)
            => core.GetStatus(token);

        TResult ValkarnTask.ISource<TResult>.GetResult(uint token)
        {
            try
            {
                return core.GetResult(token);
            }
            finally
            {
                TryReturn();
            }
        }

        void ValkarnTask.ISource.GetResult(uint token)
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
