// Copyright (c) Una Partida Mas. All rights reserved.
// Licensed under the MIT License. See LICENSE.md in the repository root.

using System;

namespace UnaPartidaMas.Valkarn.Tasks
{
    /// <summary>
    /// Singleton source that never completes. Used by ValkarnTask.Never.
    /// WARNING: Awaiting this source leaks the async state machine and all its captured
    /// variables because OnCompleted silently drops the continuation. Users should prefer
    /// ValkarnTask.Delay with a CancellationToken for cancellable "wait forever" scenarios.
    /// </summary>
    internal sealed class NeverSource : ValkarnTask.ISource
    {
        internal static readonly NeverSource Instance = new();

        public ValkarnTask.Status GetStatus(uint token) => ValkarnTask.Status.Pending;
        public void GetResult(uint token) => ThrowHelper.ThrowPendingNotAllowed();
        public void OnCompleted(Action<object> continuation, object state, uint token) { /* never fires */ }
        public ValkarnTask.Status UnsafeGetStatus() => ValkarnTask.Status.Pending;
    }
}
