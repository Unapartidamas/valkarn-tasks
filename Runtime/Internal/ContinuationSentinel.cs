// Copyright (c) Una Partida Mas. All rights reserved.
// Licensed under the MIT License. See LICENSE.md in the repository root.

using System;

namespace UnaPartidaMas.Valkarn.Tasks
{
    /// <summary>
    /// Sentinel delegates used for CAS-based race resolution in ValkarnTaskCompletionCore.
    /// When TrySetResult wins the race against OnCompleted, the sentinel is placed
    /// in the continuation field. OnCompleted then detects the sentinel and invokes inline.
    /// </summary>
    internal static class ContinuationSentinel
    {
        /// <summary>
        /// Placed by TrySetResult to indicate "result is already set, invoke continuation inline".
        /// </summary>
        internal static readonly Action<object> CompletedSentinel = static _ => { };

    }
}
