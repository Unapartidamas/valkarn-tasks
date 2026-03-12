// Copyright (c) Una Partida Mas. All rights reserved.
// Licensed under the MIT License. See LICENSE.md in the repository root.

namespace UnaPartidaMas.Valkarn.Tasks
{
    /// <summary>
    /// Abstraction over Unity's Time class for testability.
    /// All time-dependent operations read from TimeProvider.Current.
    /// In production, this is UnityTimeProvider. In tests, TestClock.
    /// </summary>
    public interface ITimeProvider
    {
        /// <summary>Time.deltaTime (affected by timeScale).</summary>
        float DeltaTime { get; }

        /// <summary>Time.unscaledDeltaTime (ignores timeScale).</summary>
        float UnscaledDeltaTime { get; }

        /// <summary>High-resolution timestamp (Stopwatch ticks).</summary>
        long GetTimestamp();

        /// <summary>Current frame count.</summary>
        int FrameCount { get; }
    }
}
