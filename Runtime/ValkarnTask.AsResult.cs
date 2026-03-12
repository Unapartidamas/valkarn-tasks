// Copyright (c) Una Partida Mas. All rights reserved.
// Licensed under the MIT License. See LICENSE.md in the repository root.

using System;

namespace UnaPartidaMas.Valkarn.Tasks
{
    /// <summary>
    /// Extension methods for converting ValkarnTask to Result{T} without try/catch.
    /// Replacement for UniTask's SuppressCancellationThrow().
    /// </summary>
    public static class ValkarnTaskAsResultExtensions
    {
        /// <summary>
        /// Wraps a ValkarnTask{T} into a Result{T}, catching exceptions and cancellation.
        /// </summary>
        public static ValkarnTask<Result<T>> AsResult<T>(this ValkarnTask<T> task)
        {
            // Sync fast path: source == null means sync-completed with inline result
            if (task.source == null)
                return ValkarnTask.FromResult(Result<T>.Success(task.result));

            return AsResultAsync(task);
        }

        static async ValkarnTask<Result<T>> AsResultAsync<T>(ValkarnTask<T> task)
        {
            try
            {
                var value = await task;
                return Result<T>.Success(value);
            }
            catch (OperationCanceledException)
            {
                return Result<T>.Canceled();
            }
            catch (Exception ex)
            {
                return Result<T>.Faulted(ex);
            }
        }

        /// <summary>
        /// Wraps a non-generic ValkarnTask into a Result.
        /// </summary>
        public static ValkarnTask<Result> AsResult(this ValkarnTask task)
        {
            // Sync fast path: source == null means sync-completed
            if (task.source == null)
                return ValkarnTask.FromResult(Result.SuccessResult);

            return AsResultAsync(task);
        }

        static async ValkarnTask<Result> AsResultAsync(ValkarnTask task)
        {
            try
            {
                await task;
                return Result.SuccessResult;
            }
            catch (OperationCanceledException)
            {
                return Result.Canceled();
            }
            catch (Exception ex)
            {
                return Result.Faulted(ex);
            }
        }
    }
}
