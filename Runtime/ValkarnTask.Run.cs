// Copyright (c) Una Partida Mas. All rights reserved.
// Licensed under the MIT License. See LICENSE.md in the repository root.

namespace UnaPartidaMas.Valkarn.Tasks
{
    public readonly partial struct ValkarnTask
    {
        // ── Run (convenience aliases for RunOnThreadPool) ──

        public static ValkarnTask Run(System.Action action, PlayerLoopTiming timing = PlayerLoopTiming.Update, System.Threading.CancellationToken cancellationToken = default)
            => RunOnThreadPool(action, timing, cancellationToken);

        public static ValkarnTask<T> Run<T>(System.Func<T> func, PlayerLoopTiming timing = PlayerLoopTiming.Update, System.Threading.CancellationToken cancellationToken = default)
            => RunOnThreadPool(func, timing, cancellationToken);

        public static ValkarnTask Run(System.Func<ValkarnTask> func, PlayerLoopTiming timing = PlayerLoopTiming.Update, System.Threading.CancellationToken cancellationToken = default)
            => RunOnThreadPool(func, timing, cancellationToken);

        public static ValkarnTask<T> Run<T>(System.Func<ValkarnTask<T>> func, PlayerLoopTiming timing = PlayerLoopTiming.Update, System.Threading.CancellationToken cancellationToken = default)
            => RunOnThreadPool<T>(func, timing, cancellationToken);

        // ── Post (dispatch to main thread) ──

        public static async ValkarnTask Post(System.Action action, PlayerLoopTiming timing = PlayerLoopTiming.Update)
        {
            if (action == null) ThrowHelper.ThrowArgumentNull(nameof(action));
            await Yield(timing);
            action();
        }

        // ── Create (factory from delegate) ──

        public static async ValkarnTask Create(System.Func<ValkarnTask> factory)
        {
            if (factory == null) ThrowHelper.ThrowArgumentNull(nameof(factory));
            await factory();
        }

        public static async ValkarnTask<T> Create<T>(System.Func<ValkarnTask<T>> factory)
        {
            if (factory == null) ThrowHelper.ThrowArgumentNull(nameof(factory));
            return await factory();
        }
    }
}
