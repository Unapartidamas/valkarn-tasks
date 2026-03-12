// Copyright (c) Una Partida Mas. All rights reserved.
// Licensed under the MIT License. See LICENSE.md in the repository root.

using System;
using System.Threading;
using System.Threading.Tasks;

namespace UnaPartidaMas.Valkarn.Tasks
{
    public readonly partial struct ValkarnTask
    {
        /// <summary>
        /// Opens a critical section. Inside the section, lifecycle cancellation is deferred.
        /// When the section ends, if cancellation was requested during the section, it takes effect.
        /// </summary>
        public static CriticalSectionScope Critical()
            => new CriticalSectionScope();
    }

    /// <summary>
    /// RAII scope for critical sections. Uses [ThreadStatic] depth counter
    /// because ICriticalNotifyCompletion does not capture ExecutionContext
    /// (making AsyncLocal unusable).
    ///
    /// IMPORTANT: Do NOT use SwitchToThreadPool/SwitchToMainThread inside a critical
    /// section. If the async method resumes on a different thread, the depth counter
    /// on the original thread is never decremented, effectively "leaking" the scope.
    /// The guard in DisposeAsync (depth &lt;= 0) prevents corruption on the new thread
    /// but silently skips the deferred cancellation check.
    ///
    /// The lifecycle token is provided by source-generated code via
    /// <see cref="SetLifecycleToken"/> before any async method runs.
    /// </summary>
    public readonly struct CriticalSectionScope : IAsyncDisposable
    {
        [ThreadStatic] static int s_criticalDepth;
        [ThreadStatic] static CancellationToken s_lifecycleToken;

        // Saved cancellation state from when the scope was entered
        readonly CancellationToken savedToken;
        readonly int capturedThreadId;

        /// <summary>
        /// Creates a critical section scope. Prefer using ValkarnTask.Critical() for clarity.
        /// </summary>
        public CriticalSectionScope()
        {
            capturedThreadId = Thread.CurrentThread.ManagedThreadId;
            savedToken = s_lifecycleToken;
            s_criticalDepth++;
        }

        public ValueTask DisposeAsync()
        {
            // Detect cross-thread misuse: if the scope was created on thread A
            // and disposed on thread B, the [ThreadStatic] depth on A is leaked.
            // Log a warning and bail out -- do NOT touch the wrong thread's depth.
            if (Thread.CurrentThread.ManagedThreadId != capturedThreadId)
            {
                ValkarnTask.PublishUnobservedException(new InvalidOperationException(
                    "CriticalSectionScope disposed on a different thread than it was created on. " +
                    "Do not use SwitchToThreadPool/SwitchToMainThread inside a critical section."));
                return default;
            }

            if (s_criticalDepth <= 0)
                return default; // Guard against double-dispose or misuse

            s_criticalDepth--;

            // If exiting the outermost critical section and cancellation was pending,
            // return a faulted ValueTask instead of throwing synchronously.
            // This preserves the IAsyncDisposable contract where DisposeAsync
            // should not throw synchronously.
            if (s_criticalDepth == 0 && savedToken.IsCancellationRequested)
            {
                return new ValueTask(
                    System.Threading.Tasks.Task.FromException(
                        new OperationCanceledException(savedToken)));
            }

            return default;
        }

        /// <summary>True if currently inside any critical section on this thread.</summary>
        public static bool IsInCriticalSection => s_criticalDepth > 0;

        /// <summary>Current nesting depth.</summary>
        internal static int Depth => s_criticalDepth;

        /// <summary>
        /// Called by source-generated lifecycle code to register the current lifecycle token.
        /// This allows Critical() to capture and check cancellation state on scope exit.
        /// </summary>
        internal static void SetLifecycleToken(CancellationToken token)
        {
            s_lifecycleToken = token;
        }
    }
}
