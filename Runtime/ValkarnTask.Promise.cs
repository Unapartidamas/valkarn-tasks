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
        /// Manual completion source for custom async operations.
        /// Supports multiple consumers via lock-based safety.
        /// </summary>
        public class Promise
        {
            ValkarnCompletionCore<AsyncUnit> core;
            PromiseSource cachedSource;

            public Promise() { }

            ~Promise()
            {
                core.ReportUnobservedIfNeeded();
            }

            public ValkarnTask Task
            {
                get
                {
                    if (Volatile.Read(ref cachedSource) == null)
                        Interlocked.CompareExchange(ref cachedSource, new PromiseSource(this), null);
                    return new ValkarnTask(cachedSource, core.Token);
                }
            }

            public bool TrySetResult() => core.TrySetResult(AsyncUnit.Default);
            public bool TrySetException(Exception ex) => core.TrySetException(ex);
            public bool TrySetCanceled(CancellationToken ct = default) => core.TrySetCanceled(ct);

            sealed class PromiseSource : ValkarnTask.ISource
            {
                readonly Promise promise;
                internal PromiseSource(Promise p) => promise = p;

                public Status GetStatus(uint token) => promise.core.GetStatus(token);

                public void GetResult(uint token)
                {
                    try { promise.core.GetResult(token); }
                    finally { GC.SuppressFinalize(promise); }
                }

                public void OnCompleted(Action<object> continuation, object state, uint token)
                    => promise.core.OnCompleted(continuation, state, token);
                public Status UnsafeGetStatus() => promise.core.UnsafeGetStatus();
            }
        }

        /// <summary>
        /// Generic manual completion source.
        /// </summary>
        public class Promise<T>
        {
            ValkarnCompletionCore<T> core;
            PromiseSource cachedSource;

            public Promise() { }

            ~Promise()
            {
                core.ReportUnobservedIfNeeded();
            }

            public ValkarnTask<T> Task
            {
                get
                {
                    if (Volatile.Read(ref cachedSource) == null)
                        Interlocked.CompareExchange(ref cachedSource, new PromiseSource(this), null);
                    return new ValkarnTask<T>(cachedSource, core.Token);
                }
            }

            public bool TrySetResult(T result) => core.TrySetResult(result);
            public bool TrySetException(Exception ex) => core.TrySetException(ex);
            public bool TrySetCanceled(CancellationToken ct = default) => core.TrySetCanceled(ct);

            sealed class PromiseSource : ValkarnTask.ISource<T>
            {
                readonly Promise<T> promise;
                internal PromiseSource(Promise<T> p) => promise = p;

                public Status GetStatus(uint token) => promise.core.GetStatus(token);

                T ValkarnTask.ISource<T>.GetResult(uint token)
                {
                    try { return promise.core.GetResult(token); }
                    finally { GC.SuppressFinalize(promise); }
                }

                void ValkarnTask.ISource.GetResult(uint token)
                {
                    try { promise.core.GetResult(token); }
                    finally { GC.SuppressFinalize(promise); }
                }

                public void OnCompleted(Action<object> continuation, object state, uint token)
                    => promise.core.OnCompleted(continuation, state, token);
                public Status UnsafeGetStatus() => promise.core.UnsafeGetStatus();
            }
        }
    }
}
