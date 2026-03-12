// Copyright (c) Una Partida Mas. All rights reserved.
// Licensed under the MIT License. See LICENSE.md in the repository root.

using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace UnaPartidaMas.Valkarn.Tasks
{
    /// <summary>
    /// Zero-allocation, struct-based async return type with a result value.
    /// source == null means synchronously completed with result stored inline (zero-alloc fast path).
    /// </summary>
    [AsyncMethodBuilder(typeof(CompilerServices.AsyncValkarnMethodBuilder<>))]
    [StructLayout(LayoutKind.Auto)]
    public readonly struct ValkarnTask<T>
    {
        internal readonly ValkarnTask.ISource<T> source;
        internal readonly T result;
        internal readonly uint token;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal ValkarnTask(ValkarnTask.ISource<T> source, T result, uint token)
        {
            this.source = source;
            this.result = result;
            this.token = token;
        }

        /// <summary>
        /// Sync-completed constructor — source and token default to null/0.
        /// Uses a single parameter to help the JIT keep source/token in registers
        /// instead of materializing a full struct on the stack.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal ValkarnTask(T result)
        {
            this.source = null;
            this.result = result;
            this.token = 0;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal ValkarnTask(ValkarnTask.ISource<T> source, uint token)
        {
            this.source = source;
            this.result = default;
            this.token = token;
        }

        /// <summary>Gets the current status.</summary>
        public ValkarnTask.Status GetStatus()
        {
            if (source == null) return ValkarnTask.Status.Succeeded;
            return source.GetStatus(token);
        }

        /// <summary>True if this task has completed.</summary>
        public bool IsCompleted
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => source == null || source.GetStatus(token).IsCompleted();
        }

        /// <summary>Gets the awaiter.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Awaiter GetAwaiter() => new Awaiter(in this);

        /// <summary>
        /// Implicit conversion to non-generic ValkarnTask (discards result type).
        /// Useful for WhenAll/WhenAny with mixed types.
        /// </summary>
        public ValkarnTask AsNonGeneric()
            => new ValkarnTask(source, token);

        // ── Awaiter ──

        public readonly struct Awaiter : ICriticalNotifyCompletion
        {
            readonly ValkarnTask<T> task;

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            internal Awaiter(in ValkarnTask<T> task) => this.task = task;

            public bool IsCompleted
            {
                [MethodImpl(MethodImplOptions.AggressiveInlining)]
                get
                {
                    var s = task.source;
                    return s == null || s.GetStatus(task.token).IsCompleted();
                }
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public T GetResult()
            {
                var s = task.source;
                if (s == null)
                    return task.result;
                return s.GetResult(task.token);
            }

            public void OnCompleted(Action continuation)
            {
                var s = task.source;
                if (s == null)
                {
                    continuation();
                    return;
                }
                s.OnCompleted(InvokeActionContinuation, continuation, task.token);
            }

            public void UnsafeOnCompleted(Action continuation)
            {
                var s = task.source;
                if (s == null)
                {
                    continuation();
                    return;
                }
                s.OnCompleted(InvokeActionContinuation, continuation, task.token);
            }

            static readonly Action<object> InvokeActionContinuation = static state =>
            {
                ((Action)state)();
            };
        }
    }
}
