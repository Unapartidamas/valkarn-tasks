// Copyright (c) Una Partida Mas. All rights reserved.
// Licensed under the MIT License. See LICENSE.md in the repository root.

using System;
using System.Runtime.CompilerServices;
using System.Threading;

namespace UnaPartidaMas.Valkarn.Tasks
{
    public readonly partial struct ValkarnTask
    {
        /// <summary>
        /// Switches execution to the ThreadPool. Continuation runs on a thread pool thread.
        /// Zero allocation (struct awaitable).
        /// </summary>
        public static SwitchToThreadPoolAwaitable SwitchToThreadPool()
            => new SwitchToThreadPoolAwaitable();

        /// <summary>
        /// Switches execution to the main thread at the specified PlayerLoop timing.
        /// Even if already on main thread, yields to next tick (avoids re-entrancy).
        /// </summary>
        public static SwitchToMainThreadAwaitable SwitchToMainThread(
            PlayerLoopTiming timing = PlayerLoopTiming.Update)
            => new SwitchToMainThreadAwaitable(timing);
    }

    // ── SwitchToThreadPool ──

    public readonly struct SwitchToThreadPoolAwaitable
    {
        public Awaiter GetAwaiter() => default;

        public readonly struct Awaiter : ICriticalNotifyCompletion
        {
            // Never complete synchronously — always switch
            public bool IsCompleted => false;

            public void GetResult() { }

            public void OnCompleted(Action continuation)
            {
#if UNITY_WEBGL && !UNITY_EDITOR
                throw new PlatformNotSupportedException(
                    "SwitchToThreadPool is not supported on WebGL. WebGL is single-threaded.");
#else
                ThreadPool.UnsafeQueueUserWorkItem(static state => ((Action)state)(), continuation);
#endif
            }

            public void UnsafeOnCompleted(Action continuation)
            {
#if UNITY_WEBGL && !UNITY_EDITOR
                throw new PlatformNotSupportedException(
                    "SwitchToThreadPool is not supported on WebGL. WebGL is single-threaded.");
#else
                ThreadPool.UnsafeQueueUserWorkItem(static state => ((Action)state)(), continuation);
#endif
            }
        }
    }

    // ── SwitchToMainThread ──

    public readonly struct SwitchToMainThreadAwaitable
    {
        readonly PlayerLoopTiming timing;

        internal SwitchToMainThreadAwaitable(PlayerLoopTiming timing) => this.timing = timing;

        public Awaiter GetAwaiter() => new Awaiter(timing);

        public readonly struct Awaiter : ICriticalNotifyCompletion
        {
            readonly PlayerLoopTiming timing;

            internal Awaiter(PlayerLoopTiming timing) => this.timing = timing;

            // Never complete synchronously — always yield to next tick
            // This prevents re-entrancy when already on main thread
            public bool IsCompleted => false;

            public void GetResult() { }

            public void OnCompleted(Action continuation)
            {
                PlayerLoopHelper.AddContinuation(timing, InvokeAction, continuation);
            }

            public void UnsafeOnCompleted(Action continuation)
            {
                PlayerLoopHelper.AddContinuation(timing, InvokeAction, continuation);
            }

            static readonly Action<object> InvokeAction = static state => ((Action)state)();
        }
    }
}
