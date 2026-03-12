// Copyright (c) Una Partida Mas. All rights reserved.
// Licensed under the MIT License. See LICENSE.md in the repository root.

namespace UnaPartidaMas.Valkarn.Tasks
{
    /// <summary>
    /// Specifies which Unity PlayerLoop phase to use for timing operations.
    /// Values match UniTask's enum exactly for migration compatibility.
    /// Each value maps to a specific Unity PlayerLoop phase.
    /// "Last" variants run after the main phase.
    /// </summary>
    public enum PlayerLoopTiming : byte
    {
        Initialization       = 0,
        LastInitialization   = 1,
        EarlyUpdate          = 2,
        LastEarlyUpdate      = 3,
        FixedUpdate          = 4,
        LastFixedUpdate      = 5,
        PreUpdate            = 6,
        LastPreUpdate        = 7,

        /// <summary>Default timing for all operations.</summary>
        Update               = 8,

        LastUpdate           = 9,
        PreLateUpdate        = 10,
        LastPreLateUpdate    = 11,
        PostLateUpdate       = 12,
        LastPostLateUpdate   = 13,
        TimeUpdate           = 14,
        LastTimeUpdate       = 15
    }

    internal static class PlayerLoopTimingExtensions
    {
        internal const int Count = 16;
    }
}
