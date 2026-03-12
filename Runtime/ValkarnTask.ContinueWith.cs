// Copyright (c) Una Partida Mas. All rights reserved.
// Licensed under the MIT License. See LICENSE.md in the repository root.

using System;

namespace UnaPartidaMas.Valkarn.Tasks
{
    public static class ValkarnTaskContinueWithExtensions
    {
        /// <summary>
        /// Chains a continuation that runs after the antecedent task succeeds.
        /// If the antecedent faults or is canceled, the exception propagates without running the continuation.
        /// </summary>
        public static async ValkarnTask ContinueWith(this ValkarnTask task, Action continuation)
        {
            if (continuation == null) ThrowHelper.ThrowArgumentNull(nameof(continuation));
            await task;
            continuation();
        }

        /// <summary>
        /// Chains a continuation that runs after the antecedent task succeeds and returns a new value.
        /// If the antecedent faults or is canceled, the exception propagates without running the continuation.
        /// </summary>
        public static async ValkarnTask<TNew> ContinueWith<TNew>(this ValkarnTask task, Func<TNew> continuation)
        {
            if (continuation == null) ThrowHelper.ThrowArgumentNull(nameof(continuation));
            await task;
            return continuation();
        }

        /// <summary>
        /// Chains a continuation that receives the antecedent's result.
        /// If the antecedent faults or is canceled, the exception propagates without running the continuation.
        /// </summary>
        public static async ValkarnTask ContinueWith<T>(this ValkarnTask<T> task, Action<T> continuation)
        {
            if (continuation == null) ThrowHelper.ThrowArgumentNull(nameof(continuation));
            var result = await task;
            continuation(result);
        }

        /// <summary>
        /// Chains a continuation that receives the antecedent's result and returns a new value.
        /// If the antecedent faults or is canceled, the exception propagates without running the continuation.
        /// </summary>
        public static async ValkarnTask<TNew> ContinueWith<T, TNew>(this ValkarnTask<T> task, Func<T, TNew> continuation)
        {
            if (continuation == null) ThrowHelper.ThrowArgumentNull(nameof(continuation));
            var result = await task;
            return continuation(result);
        }
    }
}
