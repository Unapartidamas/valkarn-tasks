// Copyright (c) Una Partida Mas. All rights reserved.
// Licensed under the MIT License. See LICENSE.md in the repository root.

using System;
using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace UnaPartidaMas.Valkarn.Tasks
{
    internal static class ThrowHelper
    {
        [MethodImpl(MethodImplOptions.NoInlining)]
        internal static void ThrowInvalidToken()
            => throw new InvalidOperationException(
                "ValkarnTask token mismatch — the task has already been consumed or the pool slot was recycled.");

        [MethodImpl(MethodImplOptions.NoInlining)]
        internal static void ThrowTaskAlreadyCompleted()
            => throw new InvalidOperationException(
                "ValkarnTask has already been completed. A task cannot be completed more than once.");

        [MethodImpl(MethodImplOptions.NoInlining)]
        internal static void ThrowMultipleAwaiters()
            => throw new InvalidOperationException(
                "Multiple awaiters are not supported. A ValkarnTask can only be awaited once.");

        [MethodImpl(MethodImplOptions.NoInlining)]
        internal static void ThrowResultNotSucceeded(ValkarnTask.Status status, Exception error)
        {
            if (status == ValkarnTask.Status.Canceled)
            {
                if (error is OperationCanceledException oce)
                    throw oce;
                throw new OperationCanceledException();
            }

            if (status == ValkarnTask.Status.Faulted)
            {
                if (error != null)
                    System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(error).Throw();
                throw new InvalidOperationException("Task is in faulted state but no exception was captured.");
            }

            throw new InvalidOperationException(
                $"Cannot get result: task status is {status}.");
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        internal static void ThrowArgumentNull(string paramName)
            => throw new ArgumentNullException(paramName);

        [MethodImpl(MethodImplOptions.NoInlining)]
        internal static void ThrowPendingNotAllowed()
            => throw new InvalidOperationException(
                "GetResult called while task is still pending.");
    }
}
