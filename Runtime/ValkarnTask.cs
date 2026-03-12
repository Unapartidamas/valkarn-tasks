// Copyright (c) Una Partida Mas. All rights reserved.
// Licensed under the MIT License. See LICENSE.md in the repository root.

using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading;

namespace UnaPartidaMas.Valkarn.Tasks
{
    /// <summary>
    /// Zero-allocation, struct-based async return type for Unity.
    /// source == null means synchronously completed (zero-alloc fast path).
    /// </summary>
    [AsyncMethodBuilder(typeof(CompilerServices.AsyncValkarnMethodBuilder))]
    [StructLayout(LayoutKind.Auto)]
    public readonly partial struct ValkarnTask
    {
        internal readonly ISource source;
        internal readonly uint token;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ValkarnTask(ISource source, uint token)
        {
            this.source = source;
            this.token = token;
        }

        /// <summary>
        /// A task that is already completed. Zero allocation.
        /// </summary>
        public static readonly ValkarnTask CompletedTask = default;

        /// <summary>
        /// Creates a completed ValkarnTask{T} with the specified result. Zero allocation.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static ValkarnTask<T> FromResult<T>(T value)
            => new ValkarnTask<T>(value);

        /// <summary>
        /// Gets the current status of this task.
        /// </summary>
        public Status GetStatus()
        {
            if (source == null) return Status.Succeeded;
            return source.GetStatus(token);
        }

        /// <summary>
        /// True if this task has completed (succeeded, faulted, or canceled).
        /// </summary>
        public bool IsCompleted
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => source == null || source.GetStatus(token).IsCompleted();
        }

        /// <summary>
        /// Gets the awaiter for this task. Used by the compiler for async/await.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Awaiter GetAwaiter() => new Awaiter(in this);

        // ── Unobserved Exception Handling ──

        static Action<Exception> s_unobservedException;

        /// <summary>
        /// Event raised when a faulted task's exception is not observed.
        /// Called deterministically at pool return time (not via finalizer).
        /// Thread-safe: handlers may be added/removed from any thread.
        /// </summary>
        public static event Action<Exception> UnobservedException
        {
            add
            {
                Action<Exception> current, updated;
                do
                {
                    current = s_unobservedException;
                    updated = current + value;
                } while (Interlocked.CompareExchange(ref s_unobservedException, updated, current) != current);
            }
            remove
            {
                Action<Exception> current, updated;
                do
                {
                    current = s_unobservedException;
                    updated = current - value;
                } while (Interlocked.CompareExchange(ref s_unobservedException, updated, current) != current);
            }
        }

        internal static void PublishUnobservedException(Exception ex)
        {
            var handler = s_unobservedException;
            if (handler != null)
            {
                try { handler(ex); }
                catch (Exception handlerEx)
                {
#if UNITY_5_3_OR_NEWER
                    UnityEngine.Debug.LogException(handlerEx);
#else
                    System.Diagnostics.Debug.WriteLine(
                        $"[ValkarnTask] UnobservedException handler threw: {handlerEx}");
#endif
                }
            }
        }

        // ── Pool Configuration ──
        // Delegates to ValkarnTaskSettings. In Unity builds, reads from ScriptableObject asset
        // (with fallback defaults if no asset exists). In non-Unity builds, reads/writes
        // the static properties on ValkarnTaskSettings directly.

#if UNITY_5_3_OR_NEWER
        // Per-session overrides. Null means "use ValkarnTaskSettings asset value (or default)".
        // Reset on domain reload via ResetStatics().
        static int? s_defaultMaxPoolSizeOverride;
        static int? s_trimCheckIntervalOverride;
        static int? s_minPoolSizeOverride;

        /// <summary>Default maximum pool size per type. Default: 256.</summary>
        public static int DefaultMaxPoolSize
        {
            get => s_defaultMaxPoolSizeOverride ?? ValkarnTaskSettings.Instance?.DefaultMaxPoolSize ?? 256;
            set => s_defaultMaxPoolSizeOverride = Math.Max(1, value);
        }

        /// <summary>Frames between pool trim checks. Default: 300 (~5s at 60fps).</summary>
        public static int TrimCheckInterval
        {
            get => s_trimCheckIntervalOverride ?? ValkarnTaskSettings.Instance?.TrimCheckInterval ?? 300;
            set => s_trimCheckIntervalOverride = Math.Max(1, value);
        }

        /// <summary>Minimum objects kept in pool after trimming. Default: 8.</summary>
        public static int MinPoolSize
        {
            get => s_minPoolSizeOverride ?? ValkarnTaskSettings.Instance?.MinPoolSize ?? 8;
            set => s_minPoolSizeOverride = Math.Max(0, value);
        }
#else
        /// <summary>Default maximum pool size per type. Default: 256.</summary>
        public static int DefaultMaxPoolSize
        {
            get => ValkarnTaskSettings.DefaultMaxPoolSize;
            set => ValkarnTaskSettings.DefaultMaxPoolSize = Math.Max(1, value);
        }

        /// <summary>Frames between pool trim checks. Default: 300 (~5s at 60fps).</summary>
        public static int TrimCheckInterval
        {
            get => ValkarnTaskSettings.TrimCheckInterval;
            set => ValkarnTaskSettings.TrimCheckInterval = Math.Max(1, value);
        }

        /// <summary>Minimum objects kept in pool after trimming. Default: 8.</summary>
        public static int MinPoolSize
        {
            get => ValkarnTaskSettings.MinPoolSize;
            set => ValkarnTaskSettings.MinPoolSize = Math.Max(0, value);
        }
#endif

        /// <summary>
        /// Resets all static state. Called on domain reload and by test infrastructure.
        /// </summary>
        internal static void ResetStatics()
        {
            s_unobservedException = null;
#if UNITY_5_3_OR_NEWER
            s_defaultMaxPoolSizeOverride = null;
            s_trimCheckIntervalOverride = null;
            s_minPoolSizeOverride = null;
            ValkarnTaskSettings.ResetInstance();
#endif
        }

        // ── Awaiter ──

        /// <summary>
        /// Struct awaiter for ValkarnTask. Implements ICriticalNotifyCompletion
        /// to avoid ExecutionContext/SynchronizationContext capture.
        /// </summary>
        public readonly struct Awaiter : ICriticalNotifyCompletion
        {
            readonly ValkarnTask task;

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            internal Awaiter(in ValkarnTask task) => this.task = task;

            /// <summary>True if the task is already completed (sync fast path).</summary>
            public bool IsCompleted
            {
                [MethodImpl(MethodImplOptions.AggressiveInlining)]
                get
                {
                    var s = task.source;
                    return s == null || s.GetStatus(task.token).IsCompleted();
                }
            }

            /// <summary>Gets the result. Throws if faulted/canceled.</summary>
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public void GetResult()
            {
                var s = task.source;
                if (s != null)
                    s.GetResult(task.token);
            }

            /// <summary>
            /// Registers the continuation. Called by the compiler's state machine.
            /// Uses OnCompleted(Action{object}, object) to avoid closure allocation.
            /// </summary>
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

            /// <summary>
            /// Critical version — never captures ExecutionContext.
            /// This is the path used by AsyncValkarnMethodBuilder.
            /// </summary>
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
