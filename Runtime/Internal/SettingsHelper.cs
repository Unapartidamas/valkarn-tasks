// Copyright (c) Una Partida Mas. All rights reserved.
// Licensed under the MIT License. See LICENSE.md in the repository root.

using System.Runtime.CompilerServices;

namespace UnaPartidaMas.Valkarn.Tasks
{
    /// <summary>
    /// Internal helper to read ValkarnTaskSettings values without #if at every call site.
    /// For Unity builds, reads from the ScriptableObject asset with fallback defaults.
    /// For non-Unity builds, reads from the static ValkarnTaskSettings properties.
    /// </summary>
    internal static class SettingsHelper
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static int GetTrimHysteresisCount()
        {
#if UNITY_5_3_OR_NEWER
            return ValkarnTaskSettings.Instance?.TrimHysteresisCount ?? 2;
#else
            return ValkarnTaskSettings.TrimHysteresisCount;
#endif
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static float GetTrimReleaseRatio()
        {
#if UNITY_5_3_OR_NEWER
            return ValkarnTaskSettings.Instance?.TrimReleaseRatio ?? 0.25f;
#else
            return ValkarnTaskSettings.TrimReleaseRatio;
#endif
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static bool GetEnableAutoCancel()
        {
#if UNITY_5_3_OR_NEWER
            return ValkarnTaskSettings.Instance?.EnableAutoCancel ?? true;
#else
            return ValkarnTaskSettings.EnableAutoCancel;
#endif
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static bool GetLogUnobservedCancellations()
        {
#if UNITY_5_3_OR_NEWER
            return ValkarnTaskSettings.Instance?.LogUnobservedCancellations ?? false;
#else
            return ValkarnTaskSettings.LogUnobservedCancellations;
#endif
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static int GetMaxExceptionLogsPerFrame()
        {
#if UNITY_5_3_OR_NEWER
            return ValkarnTaskSettings.Instance?.MaxExceptionLogsPerFrame ?? 10;
#else
            return ValkarnTaskSettings.MaxExceptionLogsPerFrame;
#endif
        }
    }
}
