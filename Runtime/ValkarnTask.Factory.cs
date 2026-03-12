// Copyright (c) Una Partida Mas. All rights reserved.
// Licensed under the MIT License. See LICENSE.md in the repository root.

using System;
using System.Threading;

namespace UnaPartidaMas.Valkarn.Tasks
{
    public readonly partial struct ValkarnTask
    {
        /// <summary>Creates a faulted task from an exception.</summary>
        public static ValkarnTask FromException(Exception exception)
        {
            if (exception == null) ThrowHelper.ThrowArgumentNull(nameof(exception));
            return new ValkarnTask(new ExceptionSource(exception), 0);
        }

        /// <summary>Creates a faulted task with a typed result.</summary>
        public static ValkarnTask<T> FromException<T>(Exception exception)
        {
            if (exception == null) ThrowHelper.ThrowArgumentNull(nameof(exception));
            return new ValkarnTask<T>(new ExceptionSource<T>(exception), 0);
        }

        /// <summary>Creates a canceled task.</summary>
        public static ValkarnTask FromCanceled(CancellationToken ct = default)
        {
            return new ValkarnTask(new CanceledSource(ct), 0);
        }

        /// <summary>Creates a canceled task with a typed result.</summary>
        public static ValkarnTask<T> FromCanceled<T>(CancellationToken ct = default)
        {
            return new ValkarnTask<T>(new CanceledSource<T>(ct), 0);
        }

        /// <summary>A task that never completes. Used as sentinel in WhenAny patterns.</summary>
        public static readonly ValkarnTask Never = new ValkarnTask(NeverSource.Instance, 0);
    }
}
