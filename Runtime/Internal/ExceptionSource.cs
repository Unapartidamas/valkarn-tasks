// Copyright (c) Una Partida Mas. All rights reserved.
// Licensed under the MIT License. See LICENSE.md in the repository root.

using System;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;

namespace UnaPartidaMas.Valkarn.Tasks
{
    /// <summary>
    /// Lightweight, non-pooled source that immediately reports a faulted/canceled state.
    /// No ValkarnTaskCompletionCore, no pool, no Volatile ops — minimal overhead for the
    /// synchronous exception path (FromException, builder SetException).
    /// Token is always 0 (no validation needed — source is never reused).
    /// </summary>
    internal sealed class ExceptionSource : ValkarnTask.ISource
    {
        readonly ExceptionDispatchInfo edi;
        readonly byte errorKind;   // 1 = faulted, 2 = canceled (OCE)

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal ExceptionSource(Exception exception)
        {
            // Always capture via EDI to preserve original stack trace for both paths
            this.edi = ExceptionDispatchInfo.Capture(exception);
            this.errorKind = exception is OperationCanceledException ? (byte)2 : (byte)1;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ValkarnTask.Status GetStatus(uint token)
            => errorKind == 2 ? ValkarnTask.Status.Canceled : ValkarnTask.Status.Faulted;

        public void GetResult(uint token)
        {
            edi.Throw();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void OnCompleted(Action<object> continuation, object state, uint token)
        {
            // Already completed — invoke inline immediately
            continuation(state);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ValkarnTask.Status UnsafeGetStatus()
            => errorKind == 2 ? ValkarnTask.Status.Canceled : ValkarnTask.Status.Faulted;
    }

    internal sealed class ExceptionSource<T> : ValkarnTask.ISource<T>
    {
        readonly ExceptionDispatchInfo edi;
        readonly byte errorKind;   // 1 = faulted, 2 = canceled (OCE)

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal ExceptionSource(Exception exception)
        {
            this.edi = ExceptionDispatchInfo.Capture(exception);
            this.errorKind = exception is OperationCanceledException ? (byte)2 : (byte)1;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ValkarnTask.Status GetStatus(uint token)
            => errorKind == 2 ? ValkarnTask.Status.Canceled : ValkarnTask.Status.Faulted;

        T ValkarnTask.ISource<T>.GetResult(uint token)
        {
            edi.Throw();
            return default; // unreachable
        }

        void ValkarnTask.ISource.GetResult(uint token)
        {
            edi.Throw();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void OnCompleted(Action<object> continuation, object state, uint token)
        {
            // Already completed — invoke inline immediately
            continuation(state);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ValkarnTask.Status UnsafeGetStatus()
            => errorKind == 2 ? ValkarnTask.Status.Canceled : ValkarnTask.Status.Faulted;
    }
}
