// Copyright (c) Una Partida Mas. All rights reserved.
// Licensed under the MIT License. See LICENSE.md in the repository root.

using System;
using System.Threading;

namespace UnaPartidaMas.Valkarn.Tasks.Testing
{
    /// <summary>
    /// Setup/teardown utilities for testing ValkarnTask code without Unity runtime.
    /// Call Setup() in [SetUp] and Teardown() in [TearDown].
    ///
    /// Usage:
    ///   TestClock clock;
    ///   [SetUp] public void SetUp() => clock = ValkarnTaskTestHelper.Setup();
    ///   [TearDown] public void TearDown() => ValkarnTaskTestHelper.Teardown();
    /// </summary>
    public static class ValkarnTaskTestHelper
    {
        /// <summary>
        /// Initializes the Valkarn Tasks runtime for testing:
        /// - Sets main thread ID
        /// - Initializes PlayerLoop queues and runners
        /// - Installs a TestClock as the active TimeProvider
        /// Returns the TestClock for advancing time in tests.
        /// </summary>
        public static TestClock Setup(float defaultDeltaTime = 1f / 60f)
        {
            var clock = new TestClock(defaultDeltaTime);
            TimeProvider.Current = clock;
            PlayerLoopHelper.InitializeForTest();
            return clock;
        }

        /// <summary>
        /// Cleans up the Valkarn Tasks runtime after testing.
        /// Resets TimeProvider and shuts down PlayerLoop infrastructure.
        /// </summary>
        public static void Teardown()
        {
            // Reset to a dummy time provider so tests don't leak state.
            // We use a fresh TestClock as a safe no-op provider.
            TimeProvider.Current = new TestClock();
            PlayerLoopHelper.ShutdownForTest();
        }
    }
}
