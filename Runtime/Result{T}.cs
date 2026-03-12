// Copyright (c) Una Partida Mas. All rights reserved.
// Licensed under the MIT License. See LICENSE.md in the repository root.

using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace UnaPartidaMas.Valkarn.Tasks
{
    /// <summary>
    /// Represents the outcome of a ValkarnTask: success with value, fault with exception, or canceled.
    /// Used by combinators (WhenAll) to report per-task results without throwing.
    /// Also usable as a general-purpose Result pattern via Success/Failure factory methods.
    /// </summary>
    [StructLayout(LayoutKind.Auto)]
    public readonly struct Result<T> : IEquatable<Result<T>>
    {
        readonly T value;
        readonly Exception error;
        readonly ValkarnTask.Status status;

        Result(T value, Exception error, ValkarnTask.Status status)
        {
            this.value = value;
            this.error = error;
            this.status = status;
        }

        /// <summary>True if the task completed successfully. Prefer IsSuccess.</summary>
        [Obsolete("Use IsSuccess instead for consistency with IsFailure/IsFaulted/IsCanceled.")]
        public bool Succeeded
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => status == ValkarnTask.Status.Succeeded;
        }

        /// <summary>True if the task completed successfully.</summary>
        public bool IsSuccess
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => status == ValkarnTask.Status.Succeeded;
        }

        /// <summary>True if failed or canceled.</summary>
        public bool IsFailure
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => status != ValkarnTask.Status.Succeeded;
        }

        /// <summary>True if the task failed with an exception.</summary>
        public bool IsFaulted
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => status == ValkarnTask.Status.Faulted;
        }

        /// <summary>True if the task was canceled.</summary>
        public bool IsCanceled
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => status == ValkarnTask.Status.Canceled;
        }

        /// <summary>The task status.</summary>
        public ValkarnTask.Status Status => status;

        /// <summary>The result value. Throws if not succeeded.</summary>
        public T Value
        {
            get
            {
                if (status != ValkarnTask.Status.Succeeded)
                    ThrowHelper.ThrowResultNotSucceeded(status, error);
                return value;
            }
        }

        /// <summary>The exception if faulted; null otherwise.</summary>
        public Exception Error => error;

        /// <summary>Implicit conversion to bool — true if succeeded.</summary>
        public static implicit operator bool(Result<T> r) => r.IsSuccess;

        public static Result<T> Success(T value)
            => new Result<T>(value, null, ValkarnTask.Status.Succeeded);

        public static Result<T> Failure(string error)
            => new Result<T>(default, new InvalidOperationException(error), ValkarnTask.Status.Faulted);

        public static Result<T> Faulted(Exception error)
            => new Result<T>(default, error, ValkarnTask.Status.Faulted);

        public static Result<T> Canceled(OperationCanceledException oce = null)
            => new Result<T>(default, oce, ValkarnTask.Status.Canceled);

        public bool Equals(Result<T> other)
            => status == other.status
            && EqualityComparer<T>.Default.Equals(value, other.value)
            && Equals(error, other.error);

        public override bool Equals(object obj) => obj is Result<T> other && Equals(other);

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = (int)status * 397;
                hash ^= EqualityComparer<T>.Default.GetHashCode(value);
                if (error != null) hash ^= error.GetHashCode();
                return hash;
            }
        }

        public static bool operator ==(Result<T> left, Result<T> right) => left.Equals(right);
        public static bool operator !=(Result<T> left, Result<T> right) => !left.Equals(right);

        public override string ToString()
        {
            return status switch
            {
                ValkarnTask.Status.Succeeded => $"Success({value})",
                ValkarnTask.Status.Faulted => $"Faulted({error?.GetType().Name}: {error?.Message})",
                ValkarnTask.Status.Canceled => "Canceled",
                _ => "Pending"
            };
        }
    }

    /// <summary>
    /// Non-generic Result for void-returning tasks.
    /// </summary>
    [StructLayout(LayoutKind.Auto)]
    public readonly struct Result : IEquatable<Result>
    {
        readonly Exception error;
        readonly ValkarnTask.Status status;

        Result(Exception error, ValkarnTask.Status status)
        {
            this.error = error;
            this.status = status;
        }

        /// <summary>True if the task completed successfully. Prefer IsSuccess.</summary>
        [Obsolete("Use IsSuccess instead for consistency with IsFailure/IsFaulted/IsCanceled.")]
        public bool Succeeded
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => status == ValkarnTask.Status.Succeeded;
        }

        /// <summary>True if the task completed successfully.</summary>
        public bool IsSuccess
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => status == ValkarnTask.Status.Succeeded;
        }

        /// <summary>True if failed or canceled.</summary>
        public bool IsFailure
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => status != ValkarnTask.Status.Succeeded;
        }

        /// <summary>True if the task failed with an exception.</summary>
        public bool IsFaulted
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => status == ValkarnTask.Status.Faulted;
        }

        /// <summary>True if the task was canceled.</summary>
        public bool IsCanceled
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => status == ValkarnTask.Status.Canceled;
        }

        /// <summary>The task status.</summary>
        public ValkarnTask.Status Status => status;

        /// <summary>The exception if faulted; null otherwise.</summary>
        public Exception Error => error;

        public static implicit operator bool(Result r) => r.IsSuccess;

        public static Result Success()
            => new Result(null, ValkarnTask.Status.Succeeded);

        public static Result Failure(string error)
            => new Result(new InvalidOperationException(error), ValkarnTask.Status.Faulted);

        public static Result Faulted(Exception error)
            => new Result(error, ValkarnTask.Status.Faulted);

        public static Result Canceled(OperationCanceledException oce = null)
            => new Result(oce, ValkarnTask.Status.Canceled);

        internal static readonly Result SuccessResult = new Result(null, ValkarnTask.Status.Succeeded);

        public bool Equals(Result other)
            => status == other.status && Equals(error, other.error);

        public override bool Equals(object obj) => obj is Result other && Equals(other);

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = (int)status * 397;
                if (error != null) hash ^= error.GetHashCode();
                return hash;
            }
        }

        public static bool operator ==(Result left, Result right) => left.Equals(right);
        public static bool operator !=(Result left, Result right) => !left.Equals(right);

        public override string ToString()
        {
            return status switch
            {
                ValkarnTask.Status.Succeeded => "Success",
                ValkarnTask.Status.Faulted => $"Faulted({error?.GetType().Name}: {error?.Message})",
                ValkarnTask.Status.Canceled => "Canceled",
                _ => "Pending"
            };
        }
    }
}
