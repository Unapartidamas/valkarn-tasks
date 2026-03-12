// Copyright (c) Una Partida Mas. All rights reserved.
// Licensed under the MIT License. See LICENSE.md in the repository root.

#if UNITY_2023_1_OR_NEWER
using System;
using System.Runtime.CompilerServices;
using UnityEngine;

namespace UnaPartidaMas.Valkarn.Tasks.Bridge
{
    /// <summary>
    /// Extension methods for converting Unity Awaitable to ValkarnTask-compatible awaiters.
    /// Use <c>await myAwaitable.AsValkarnTask()</c> to bridge into the ValkarnTask pipeline
    /// with ICriticalNotifyCompletion (skipping ExecutionContext capture).
    ///
    /// Note: C# overload resolution always prefers instance GetAwaiter() over extension methods,
    /// so explicit conversion via AsValkarnTask() is required. Direct <c>await myAwaitable</c>
    /// inside async ValkarnTask methods works but uses the native awaiter, which only
    /// implements INotifyCompletion (the builder handles this correctly).
    /// </summary>
    public static class AwaitableBridge
    {
        /// <summary>
        /// Converts a Unity Awaitable to a ValkarnTask-compatible awaiter
        /// that implements ICriticalNotifyCompletion (no ExecutionContext capture).
        /// </summary>
        public static AwaitableValkarnTaskAwaiter AsValkarnTask(this Awaitable awaitable)
            => new AwaitableValkarnTaskAwaiter(awaitable);

        /// <summary>
        /// Converts a Unity Awaitable&lt;T&gt; to a ValkarnTask-compatible awaiter
        /// that implements ICriticalNotifyCompletion (no ExecutionContext capture).
        /// </summary>
        public static AwaitableValkarnTaskAwaiter<T> AsValkarnTask<T>(this Awaitable<T> awaitable)
            => new AwaitableValkarnTaskAwaiter<T>(awaitable);
    }

    /// <summary>
    /// Custom awaiter for Unity Awaitable that implements ICriticalNotifyCompletion.
    /// Wraps the native Awaitable awaiter.
    /// </summary>
    public readonly struct AwaitableValkarnTaskAwaiter : ICriticalNotifyCompletion
    {
        readonly Awaitable.Awaiter inner;

        public AwaitableValkarnTaskAwaiter(Awaitable awaitable)
        {
            inner = awaitable.GetAwaiter();
        }

        public bool IsCompleted => inner.IsCompleted;

        public void GetResult() => inner.GetResult();

        public void OnCompleted(Action continuation)
            => inner.OnCompleted(continuation);

        public void UnsafeOnCompleted(Action continuation)
            => inner.OnCompleted(continuation);
    }

    /// <summary>
    /// Generic custom awaiter for Unity Awaitable&lt;T&gt;.
    /// </summary>
    public readonly struct AwaitableValkarnTaskAwaiter<T> : ICriticalNotifyCompletion
    {
        readonly Awaitable<T>.Awaiter inner;

        public AwaitableValkarnTaskAwaiter(Awaitable<T> awaitable)
        {
            inner = awaitable.GetAwaiter();
        }

        public bool IsCompleted => inner.IsCompleted;

        public T GetResult() => inner.GetResult();

        public void OnCompleted(Action continuation)
            => inner.OnCompleted(continuation);

        public void UnsafeOnCompleted(Action continuation)
            => inner.OnCompleted(continuation);
    }
}
#endif
