// Copyright (c) Una Partida Mas. All rights reserved.
// Licensed under the MIT License. See LICENSE.md in the repository root.

namespace UnaPartidaMas.Valkarn.Tasks
{
    /// <summary>
    /// Specifies the time source for delay operations.
    /// </summary>
    public enum DelayType : byte
    {
        /// <summary>Uses Time.deltaTime (affected by Time.timeScale). Default.</summary>
        DeltaTime = 0,

        /// <summary>Uses Time.unscaledDeltaTime (ignores Time.timeScale).</summary>
        UnscaledDeltaTime = 1,

        /// <summary>Uses Stopwatch-based real time (independent of frame rate and timescale).</summary>
        Realtime = 2
    }
}
