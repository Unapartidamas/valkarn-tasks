// Copyright (c) Una Partida Mas. All rights reserved.
// Licensed under the MIT License. See LICENSE.md in the repository root.

using System;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Security;

namespace UnaPartidaMas.Valkarn.Tasks.CompilerServices
{
    /// <summary>
    /// Custom async method builder for ValkarnTask{TResult} (generic).
    /// Same architecture as AsyncValkarnMethodBuilder but produces a typed result.
    ///
    /// Optimized for the sync-completion fast path (3 fields only):
    ///   - runner: null until first suspension (lazy allocation)
    ///   - syncException: stored directly (no ISource wrapper) for the sync error path
    ///   - result: stored inline for the sync success path
    /// Token is read from the runner on demand (async path only).
    /// </summary>
    [StructLayout(LayoutKind.Auto)]
    public struct AsyncValkarnMethodBuilder<TResult>
    {
        IStateMachineRunnerPromise<TResult> runner;
        Exception syncException;
        TResult result;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static AsyncValkarnMethodBuilder<TResult> Create()
            => default;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        [DebuggerStepThrough]
        public void Start<TStateMachine>(ref TStateMachine stateMachine)
            where TStateMachine : IAsyncStateMachine
        {
            stateMachine.MoveNext();
        }

        public void AwaitOnCompleted<TAwaiter, TStateMachine>(
            ref TAwaiter awaiter, ref TStateMachine stateMachine)
            where TAwaiter : INotifyCompletion
            where TStateMachine : IAsyncStateMachine
        {
            if (runner == null)
                AsyncValkarnRunner<TStateMachine, TResult>.SetStateMachine(ref stateMachine, ref runner);

            awaiter.OnCompleted(runner.MoveNextAction);
        }

        [SecuritySafeCritical]
        public void AwaitUnsafeOnCompleted<TAwaiter, TStateMachine>(
            ref TAwaiter awaiter, ref TStateMachine stateMachine)
            where TAwaiter : ICriticalNotifyCompletion
            where TStateMachine : IAsyncStateMachine
        {
            if (runner == null)
                AsyncValkarnRunner<TStateMachine, TResult>.SetStateMachine(ref stateMachine, ref runner);

            awaiter.UnsafeOnCompleted(runner.MoveNextAction);
        }

        public void SetResult(TResult result)
        {
            if (runner != null)
            {
                runner.SetResult(result);
                return;
            }

            // Sync path: store result for Task property
            this.result = result;
        }

        public void SetException(Exception exception)
        {
            if (runner != null)
            {
                runner.SetException(exception);
                return;
            }

            // Sync path: store exception directly (no ISource wrapper needed).
            syncException = exception;
        }

        public ValkarnTask<TResult> Task
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get
            {
                // Hot path: sync success (runner and syncException both null).
                // Checking both before returning prevents the JIT from merging
                // this exit with the async/exception exits, avoiding redundant
                // source/token register writes on the fast path.
                if (runner == null && syncException == null)
                    return new ValkarnTask<TResult>(result);

                return TaskSlowPath();
            }
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        ValkarnTask<TResult> TaskSlowPath()
        {
            if (runner != null)
                return new ValkarnTask<TResult>(runner, runner.Token);

            return ValkarnTask.FromException<TResult>(syncException);
        }

        public void SetStateMachine(IAsyncStateMachine stateMachine) { }
    }
}
