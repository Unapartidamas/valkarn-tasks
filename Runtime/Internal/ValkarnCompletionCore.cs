// Copyright (c) Una Partida Mas. All rights reserved.
// Licensed under the MIT License. See LICENSE.md in the repository root.

using System;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using System.Threading;

namespace UnaPartidaMas.Valkarn.Tasks
{
    /// <summary>
    /// Core completion state machine for ValkarnTask.
    /// Handles the race condition between OnCompleted and TrySetResult using a sentinel pattern.
    ///
    /// Race resolution:
    ///   Pattern A: OnCompleted first → TrySetResult finds continuation → invokes it.
    ///   Pattern B: TrySetResult first → awaiter sees IsCompleted=true → calls GetResult directly.
    ///   Pattern C (race): CAS on continuation field resolves:
    ///     C.1: OnCompleted wins CAS → TrySetResult reads non-null continuation → invokes.
    ///     C.2: TrySetResult wins CAS (sets sentinel) → OnCompleted detects sentinel → invokes inline.
    ///
    /// Generational token: uint generation increments on Reset(). 32-bit generation makes wraparound
    /// effectively impossible (~4 billion reuses per slot). Slot identity is provided by the ISource reference.
    /// Deterministic error reporting: unobserved errors detected at Reset() time for pooled sources,
    /// or via ReportUnobservedIfNeeded() from Promise finalizers for non-pooled sources.
    ///
    /// Memory ordering (ARM64-safe):
    ///   completedCount uses a two-phase protocol:
    ///     0 = pending, 1 = claimed (exclusive writer), 2 = completed (result published).
    ///   CAS(0→1) grants exclusive ownership. Volatile.Write(→2) publishes result/error with release semantics.
    ///   Readers use Volatile.Read (acquire) on completedCount, ensuring they see the result/error.
    /// </summary>
    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Auto)]
    internal struct ValkarnCompletionCore<TResult>
    {
        TResult result;
        object error;               // ExceptionDispatchInfo (faulted) or OperationCanceledException (canceled)
        int generation;             // int for Interlocked.Increment; cast to uint for token comparison
        int completedCount;         // 0 = pending, 1 = claimed, 2 = completed
        bool hasUnhandledError;
        byte errorKind;             // 0 = none, 1 = faulted (EDI), 2 = canceled (OCE), 3 = canceled (EDI, shared singleton)

        Action<object> continuation;
        object continuationState;

        /// <summary>
        /// The current token for this core. Equal to the current generation counter.
        /// </summary>
        internal uint Token
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => unchecked((uint)Volatile.Read(ref generation));
        }

        // ── Token Validation ──

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        void ValidateToken(uint token)
        {
            if (token != unchecked((uint)Volatile.Read(ref generation)))
                ThrowHelper.ThrowInvalidToken();
        }

        // ── Status ──

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal ValkarnTask.Status GetStatus(uint token)
        {
            ValidateToken(token);
            return UnsafeGetStatus();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal ValkarnTask.Status UnsafeGetStatus()
        {
            // C-01 FIX: Volatile.Read acts as an acquire barrier on ARM64.
            // Paired with the Volatile.Write(completedCount, 2) in TrySet*, this
            // guarantees we see the result/error written before completion was published.
            if (Volatile.Read(ref completedCount) < 2)
                return ValkarnTask.Status.Pending;

            // Fast byte check instead of 'is' pattern matching — avoids type check overhead
            // errorKind: 0=none, 1=faulted(EDI), 2=canceled(OCE), 3=canceled(EDI, shared singleton)
            return errorKind switch
            {
                1 => ValkarnTask.Status.Faulted,
                2 or 3 => ValkarnTask.Status.Canceled,
                _ => ValkarnTask.Status.Succeeded,
            };
        }

        // ── Completion (Set) ──

        /// <summary>
        /// Attempts to transition to Succeeded state with a result value.
        /// Returns true if this was the first completion; false if already completed.
        /// </summary>
        internal bool TrySetResult(TResult result)
        {
            // Phase 1: CAS(0→1) to claim exclusive ownership. Only one thread wins.
            if (Interlocked.CompareExchange(ref completedCount, 1, 0) != 0)
                return false;

            // Phase 2: Write result. Safe — we are the sole writer after winning the CAS.
            this.result = result;

            // Phase 3: Publish completion with release semantics (Volatile.Write).
            // Any thread that reads completedCount >= 2 via Volatile.Read (acquire)
            // is guaranteed to see the result written above. This is the standard
            // store-release / load-acquire pattern, critical for ARM64 correctness.
            Volatile.Write(ref completedCount, 2);

            InvokeContinuation();
            return true;
        }

        /// <summary>
        /// Attempts to transition to Faulted state.
        /// </summary>
        internal bool TrySetException(Exception exception)
        {
            if (Interlocked.CompareExchange(ref completedCount, 1, 0) != 0)
                return false;

            // Store OCE directly for the cancellation path.
            // For all other exceptions, capture EDI eagerly so GetResult can re-throw without a second Capture.
            if (exception is OperationCanceledException)
            {
                this.error = exception;
                this.errorKind = 2; // canceled
            }
            else
            {
                this.error = ExceptionDispatchInfo.Capture(exception);
                this.errorKind = 1; // faulted
            }
            this.hasUnhandledError = true;

            Volatile.Write(ref completedCount, 2);

            InvokeContinuation();
            return true;
        }

        /// <summary>
        /// Attempts to transition to Canceled state.
        /// </summary>
        internal bool TrySetCanceled(CancellationToken cancellationToken = default)
        {
            if (Interlocked.CompareExchange(ref completedCount, 1, 0) != 0)
                return false;

            if (cancellationToken == default)
            {
                // Use the shared singleton EDI from CachedCanceledOce (non-generic static class).
                // Avoids per-generic-instantiation allocation that would occur with a static field here.
                // GetResult will use EDI.Throw() which creates a fresh copy per call (thread-safe).
                this.error = CachedCanceledOce.Edi;
                this.errorKind = 3; // canceled via EDI
            }
            else
            {
                this.error = new OperationCanceledException(cancellationToken);
                this.errorKind = 2; // canceled via OCE (unique instance, safe to throw directly)
            }
            this.hasUnhandledError = true;

            Volatile.Write(ref completedCount, 2);

            InvokeContinuation();
            return true;
        }

        // ── Continuation Registration ──

        /// <summary>
        /// Registers a continuation to be invoked when the operation completes.
        /// Resolves race with TrySetResult via sentinel CAS.
        /// </summary>
        internal void OnCompleted(Action<object> cont, object state, uint token)
        {
            ValidateToken(token);

            // C-02 FIX: Use Volatile.Write to ensure continuationState is visible to
            // InvokeContinuation on other threads BEFORE the CAS on continuation.
            // On ARM64, without this, the store to continuationState could be reordered
            // after the CAS, and InvokeContinuation could read a stale value.
            Volatile.Write(ref continuationState, state);

            // CAS: try to store our continuation
            var prev = Interlocked.CompareExchange(ref continuation, cont, null);

            if (prev == null)
            {
                // Pattern A: We won. TrySetResult will invoke our continuation later.
                return;
            }

            if (ReferenceEquals(prev, ContinuationSentinel.CompletedSentinel))
            {
                // Pattern C.2: TrySetResult already completed and placed sentinel.
                // We must invoke the continuation ourselves, inline.
                cont(state);
                return;
            }

            // Should never reach here — only one awaiter per task
            ThrowHelper.ThrowMultipleAwaiters();
        }

        // ── Get Result ──

        /// <summary>
        /// Gets the result. Throws if faulted/canceled. Marks error as handled.
        /// </summary>
        internal TResult GetResult(uint token)
        {
            ValidateToken(token);

            // C-01 FIX: Volatile.Read (acquire) ensures we see the result/error
            // written before the Volatile.Write(completedCount, 2) (release).
            if (Volatile.Read(ref completedCount) < 2)
                ThrowHelper.ThrowPendingNotAllowed();

            // Volatile.Write ensures the finalizer thread (running ReportUnobservedIfNeeded)
            // sees this write, preventing spurious "unobserved exception" reports.
            Volatile.Write(ref hasUnhandledError, false);

            // Fast byte check avoids 'is' pattern matching overhead on the hot path.
            // errorKind 2: unique OCE instance (safe to throw directly)
            // errorKind 3: shared singleton OCE via EDI (must use EDI.Throw for thread safety)
            if (errorKind == 2)
                throw (OperationCanceledException)error;

            if (errorKind == 3)
            {
                // Shared singleton OCE wrapped in EDI — Throw() creates a fresh copy per call.
                ((ExceptionDispatchInfo)error).Throw();
            }

            if (errorKind == 1)
            {
                // EDI was captured eagerly in TrySetException — re-throw without a second Capture.
                ((ExceptionDispatchInfo)error).Throw();
            }

            return result;
        }

        // ── Reset (for pool return) ──

        /// <summary>
        /// Resets the core for reuse. Reports unobserved errors.
        /// Increments generation to invalidate stale tokens.
        /// </summary>
        internal void Reset()
        {
            // Deterministic error reporting: if error was never observed, report now.
            // Skip cancellation errors unless LogUnobservedCancellations is enabled.
            if (hasUnhandledError && error != null)
            {
                bool isCancellation = errorKind == 2 || errorKind == 3;
                if (!isCancellation || SettingsHelper.GetLogUnobservedCancellations())
                {
                    var ex = error is ExceptionDispatchInfo edi
                        ? edi.SourceException
                        : (Exception)error;
                    ValkarnTask.PublishUnobservedException(ex);
                }
            }

            // Increment generation atomically so any concurrent reader with a stale token
            // gets an immediate validation failure rather than reading half-cleared state.
            // Interlocked.Increment provides a full memory barrier on all architectures.
            Interlocked.Increment(ref generation);
            result = default;
            error = null;
            errorKind = 0;
            hasUnhandledError = false;
            Volatile.Write(ref continuation, null);
            Volatile.Write(ref continuationState, null);
            // Write completedCount last with release semantics.
            // Ensures all field resets above are visible before state becomes "pending".
            Volatile.Write(ref completedCount, 0);
        }

        // ── Unobserved Error Reporting (for non-pooled sources) ──

        /// <summary>
        /// Reports the stored exception as unobserved if it was never consumed via GetResult.
        /// Called from Promise finalizers for non-pooled sources that have no Reset() lifecycle.
        /// Unlike Reset(), this does NOT clear or recycle the core state.
        /// </summary>
        internal void ReportUnobservedIfNeeded()
        {
            // Volatile.Read pairs with the Volatile.Write in GetResult to ensure
            // the finalizer thread sees the cleared flag if GetResult was called.
            if (Volatile.Read(ref hasUnhandledError) && error != null)
            {
                bool isCancellation = errorKind == 2 || errorKind == 3;
                if (!isCancellation || SettingsHelper.GetLogUnobservedCancellations())
                {
                    var ex = error is ExceptionDispatchInfo edi
                        ? edi.SourceException
                        : (Exception)error;
                    ValkarnTask.PublishUnobservedException(ex);
                }
            }
        }

        // ── Continuation Invocation ──

        /// <summary>
        /// Invokes the registered continuation, or places sentinel if none registered yet.
        /// </summary>
        void InvokeContinuation()
        {
            // CAS: try to place sentinel (indicating "we completed first")
            var cont = Interlocked.CompareExchange(ref continuation, ContinuationSentinel.CompletedSentinel, null);

            if (cont == null)
            {
                // Pattern B: No continuation registered yet. Sentinel is now in place.
                // When OnCompleted is called, it will detect the sentinel and invoke inline.
                return;
            }

            if (ReferenceEquals(cont, ContinuationSentinel.CompletedSentinel))
            {
                // Already completed — double completion attempt (should not happen with CAS on completedCount)
                return;
            }

            // Pattern A/C.1: A continuation was registered. Invoke it.
            // C-02 FIX: Volatile.Read ensures we see the continuationState written by
            // OnCompleted, even on ARM64 where the CAS on 'continuation' (a different
            // variable) does not establish happens-before for continuationState.
            var state = Volatile.Read(ref continuationState);
            cont(state);
        }
    }
}
