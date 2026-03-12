// Copyright (c) Una Partida Mas. All rights reserved.
// Licensed under the MIT License. See LICENSE.md in the repository root.

using System;
using System.Runtime.CompilerServices;

namespace UnaPartidaMas.Valkarn.Tasks
{
    /// <summary>
    /// Static accessor for the current ITimeProvider.
    /// In production: UnityTimeProvider. In tests: TestClock.
    /// </summary>
    public static class TimeProvider
    {
#if UNITY_5_3_OR_NEWER
        static volatile ITimeProvider s_current = UnityTimeProvider.Instance;
#else
        static volatile ITimeProvider s_current;
#endif

        public static ITimeProvider Current
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => s_current ?? throw new InvalidOperationException(
                "TimeProvider.Current is not initialized. In tests, call ValkarnTaskTestHelper.Setup() first.");
            set => s_current = value ?? throw new ArgumentNullException(nameof(value));
        }

        /// <summary>
        /// Resets to the default time provider. Called on domain reload.
        /// </summary>
        internal static void ResetToDefault()
        {
#if UNITY_5_3_OR_NEWER
            s_current = UnityTimeProvider.Instance;
#else
            s_current = null;
#endif
        }
    }
}
