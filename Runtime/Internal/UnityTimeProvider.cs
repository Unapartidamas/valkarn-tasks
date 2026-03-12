// Copyright (c) Una Partida Mas. All rights reserved.
// Licensed under the MIT License. See LICENSE.md in the repository root.

using System.Diagnostics;
using UnityEngine;

namespace UnaPartidaMas.Valkarn.Tasks
{
    /// <summary>
    /// Production time provider that delegates to Unity's Time class and Stopwatch.
    /// </summary>
    internal sealed class UnityTimeProvider : ITimeProvider
    {
        internal static readonly UnityTimeProvider Instance = new();

        public float DeltaTime => Time.deltaTime;
        public float UnscaledDeltaTime => Time.unscaledDeltaTime;
        public long GetTimestamp() => Stopwatch.GetTimestamp();
        public int FrameCount => Time.frameCount;
    }
}
