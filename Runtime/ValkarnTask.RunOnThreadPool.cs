// Copyright (c) Una Partida Mas. All rights reserved.
// Licensed under the MIT License. See LICENSE.md in the repository root.

using System;
using System.Threading;

namespace UnaPartidaMas.Valkarn.Tasks
{
    public readonly partial struct ValkarnTask
    {
        /// <summary>
        /// Executes <paramref name="action"/> on the thread pool, then switches back to
        /// the main thread at the specified <paramref name="timing"/>.
        /// If the action throws, the exception propagates without returning to main thread.
        /// </summary>
        public static async ValkarnTask RunOnThreadPool(
            Action action,
            PlayerLoopTiming timing = PlayerLoopTiming.Update,
            CancellationToken cancellationToken = default)
        {
            if (action == null) ThrowHelper.ThrowArgumentNull(nameof(action));
            cancellationToken.ThrowIfCancellationRequested();
            await SwitchToThreadPool();
            cancellationToken.ThrowIfCancellationRequested();
            action();
            await SwitchToMainThread(timing);
        }

        /// <summary>
        /// Executes <paramref name="func"/> on the thread pool and returns its result,
        /// then switches back to the main thread at the specified <paramref name="timing"/>.
        /// If the func throws, the exception propagates without returning to main thread.
        /// </summary>
        public static async ValkarnTask<T> RunOnThreadPool<T>(
            Func<T> func,
            PlayerLoopTiming timing = PlayerLoopTiming.Update,
            CancellationToken cancellationToken = default)
        {
            if (func == null) ThrowHelper.ThrowArgumentNull(nameof(func));
            cancellationToken.ThrowIfCancellationRequested();
            await SwitchToThreadPool();
            cancellationToken.ThrowIfCancellationRequested();
            var result = func();
            await SwitchToMainThread(timing);
            return result;
        }

        /// <summary>
        /// Executes an async <paramref name="func"/> on the thread pool, then switches back
        /// to the main thread at the specified <paramref name="timing"/>.
        /// If the func throws, the exception propagates without returning to main thread.
        /// </summary>
        public static async ValkarnTask RunOnThreadPool(
            Func<ValkarnTask> func,
            PlayerLoopTiming timing = PlayerLoopTiming.Update,
            CancellationToken cancellationToken = default)
        {
            if (func == null) ThrowHelper.ThrowArgumentNull(nameof(func));
            cancellationToken.ThrowIfCancellationRequested();
            await SwitchToThreadPool();
            cancellationToken.ThrowIfCancellationRequested();
            await func();
            await SwitchToMainThread(timing);
        }

        /// <summary>
        /// Executes an async <paramref name="func"/> on the thread pool and returns its result,
        /// then switches back to the main thread at the specified <paramref name="timing"/>.
        /// If the func throws, the exception propagates without returning to main thread.
        /// </summary>
        public static async ValkarnTask<T> RunOnThreadPool<T>(
            Func<ValkarnTask<T>> func,
            PlayerLoopTiming timing = PlayerLoopTiming.Update,
            CancellationToken cancellationToken = default)
        {
            if (func == null) ThrowHelper.ThrowArgumentNull(nameof(func));
            cancellationToken.ThrowIfCancellationRequested();
            await SwitchToThreadPool();
            cancellationToken.ThrowIfCancellationRequested();
            var result = await func();
            await SwitchToMainThread(timing);
            return result;
        }
    }
}
