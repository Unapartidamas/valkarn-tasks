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
    /// Custom async method builder for ValkarnTask (non-generic).
    /// The compiler uses this to convert async methods returning ValkarnTask.
    ///
    /// Optimized for the sync-completion fast path (2 fields only):
    ///   - runner: null until first suspension (lazy allocation)
    ///   - syncException: stored directly for the sync error path
    /// Token is read from the runner on demand (async path only).
    ///
    /// Contract:
    ///   1. Create() → new builder (all fields default)
    ///   2. Start(ref stateMachine) → runs MoveNext synchronously
    ///   3. If completes sync → SetResult/SetException, Task returns CompletedTask
    ///   4. If suspends → AwaitUnsafeOnCompleted → creates pooled runner
    ///   5. Task property → returns ValkarnTask backed by the runner
    /// </summary>
    [StructLayout(LayoutKind.Auto)]
    public struct AsyncValkarnMethodBuilder
    {
        IStateMachineRunnerPromise runner;
        Exception syncException;

        // ── Create ──

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static AsyncValkarnMethodBuilder Create()
            => default;

        // ── Start ──

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        [DebuggerStepThrough]
        public void Start<TStateMachine>(ref TStateMachine stateMachine)
            where TStateMachine : IAsyncStateMachine
        {
            stateMachine.MoveNext();
        }

        // ── Await ──

        public void AwaitOnCompleted<TAwaiter, TStateMachine>(
            ref TAwaiter awaiter, ref TStateMachine stateMachine)
            where TAwaiter : INotifyCompletion
            where TStateMachine : IAsyncStateMachine
        {
            if (runner == null)
                AsyncValkarnRunner<TStateMachine>.SetStateMachine(ref stateMachine, ref runner);

            awaiter.OnCompleted(runner.MoveNextAction);
        }

        [SecuritySafeCritical]
        public void AwaitUnsafeOnCompleted<TAwaiter, TStateMachine>(
            ref TAwaiter awaiter, ref TStateMachine stateMachine)
            where TAwaiter : ICriticalNotifyCompletion
            where TStateMachine : IAsyncStateMachine
        {
            if (runner == null)
                AsyncValkarnRunner<TStateMachine>.SetStateMachine(ref stateMachine, ref runner);

            awaiter.UnsafeOnCompleted(runner.MoveNextAction);
        }

        // ── Completion ──

        public void SetResult()
        {
            // Async path: signal the runner's completion core
            if (runner != null)
            {
                runner.SetResult();
                return;
            }
            // Sync path: nothing to do — Task property returns CompletedTask
        }

        public void SetException(Exception exception)
        {
            // Async path: signal the runner's completion core
            if (runner != null)
            {
                runner.SetException(exception);
                return;
            }

            // Sync path: store exception directly.
            syncException = exception;
        }

        // ── Task property ──

        public ValkarnTask Task
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get
            {
                if (runner == null && syncException == null)
                    return default;

                return TaskSlowPath();
            }
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        ValkarnTask TaskSlowPath()
        {
            if (runner != null)
                return new ValkarnTask(runner, runner.Token);

            return ValkarnTask.FromException(syncException);
        }

        public void SetStateMachine(IAsyncStateMachine stateMachine)
        {
            // Not needed for struct state machines (always stored by value in the runner)
        }
    }
}
