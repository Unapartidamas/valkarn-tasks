// Copyright (c) Una Partida Mas. All rights reserved.
// Licensed under the MIT License. See LICENSE.md in the repository root.

using System;
using System.Runtime.CompilerServices;
using System.Threading;

namespace UnaPartidaMas.Valkarn.Tasks
{
    /// <summary>
    /// Lightweight, non-pooled source that immediately reports a canceled state.
    /// No ValkarnTaskCompletionCore, no pool, no Volatile ops — minimal overhead.
    /// Token is always 0 (no validation needed — source is never reused).
    /// </summary>
    internal sealed class CanceledSource : ValkarnTask.ISource
    {
        readonly object errorObj;  // OperationCanceledException or ExceptionDispatchInfo
        readonly bool isEdi;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal CanceledSource(CancellationToken ct)
        {
            if (ct == default)
            {
                // Use EDI for the shared singleton to avoid data race on _stackTrace
                this.errorObj = CachedCanceledOce.Edi;
                this.isEdi = true;
            }
            else
            {
                this.errorObj = new OperationCanceledException(ct);
                this.isEdi = false;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ValkarnTask.Status GetStatus(uint token) => ValkarnTask.Status.Canceled;

        public void GetResult(uint token)
        {
            if (isEdi)
                ((System.Runtime.ExceptionServices.ExceptionDispatchInfo)errorObj).Throw();
            else
                throw (OperationCanceledException)errorObj;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void OnCompleted(Action<object> continuation, object state, uint token)
            => continuation(state);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ValkarnTask.Status UnsafeGetStatus() => ValkarnTask.Status.Canceled;
    }

    internal sealed class CanceledSource<T> : ValkarnTask.ISource<T>
    {
        readonly object errorObj;
        readonly bool isEdi;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal CanceledSource(CancellationToken ct)
        {
            if (ct == default)
            {
                this.errorObj = CachedCanceledOce.Edi;
                this.isEdi = true;
            }
            else
            {
                this.errorObj = new OperationCanceledException(ct);
                this.isEdi = false;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ValkarnTask.Status GetStatus(uint token) => ValkarnTask.Status.Canceled;

        T ValkarnTask.ISource<T>.GetResult(uint token)
        {
            if (isEdi) ((System.Runtime.ExceptionServices.ExceptionDispatchInfo)errorObj).Throw();
            else throw (OperationCanceledException)errorObj;
            return default; // unreachable
        }
        void ValkarnTask.ISource.GetResult(uint token)
        {
            if (isEdi) ((System.Runtime.ExceptionServices.ExceptionDispatchInfo)errorObj).Throw();
            else throw (OperationCanceledException)errorObj;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void OnCompleted(Action<object> continuation, object state, uint token)
            => continuation(state);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ValkarnTask.Status UnsafeGetStatus() => ValkarnTask.Status.Canceled;
    }

    /// <summary>
    /// Cached default OCE wrapped in EDI for thread-safe re-throwing.
    /// EDI.Throw() creates a fresh exception copy each time, avoiding
    /// the data race on _stackTrace that occurs when throwing a shared OCE directly.
    /// </summary>
    internal static class CachedCanceledOce
    {
        internal static readonly System.Runtime.ExceptionServices.ExceptionDispatchInfo Edi =
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(new OperationCanceledException());
    }
}
