// Copyright (c) Una Partida Mas. All rights reserved.
// Licensed under the MIT License. See LICENSE.md in the repository root.

using System;
using System.Diagnostics;
using System.Threading;

namespace UnaPartidaMas.Valkarn.Tasks.Testing
{
    /// <summary>
    /// Deterministic time provider for unit testing.
    /// Controls time progression and processes PlayerLoop ticks manually.
    ///
    /// Usage:
    ///   var clock = ValkarnTaskTestHelper.Setup();
    ///   var task = ValkarnTask.Delay(3000);
    ///   clock.Advance(TimeSpan.FromSeconds(2));
    ///   Assert.IsFalse(task.IsCompleted);
    ///   clock.Advance(TimeSpan.FromSeconds(1));
    ///   Assert.IsTrue(task.IsCompleted);
    /// </summary>
    public sealed class TestClock : ITimeProvider
    {
        float _deltaTime;
        float _unscaledDeltaTime;
        long _timestamp;
        int _frameCount;

        public TestClock(float defaultDeltaTime = 1f / 60f)
        {
            _deltaTime = defaultDeltaTime;
            _unscaledDeltaTime = defaultDeltaTime;
            _timestamp = 0;
            _frameCount = 0;
        }

        // ── ITimeProvider ──

        public float DeltaTime => _deltaTime;
        public float UnscaledDeltaTime => _unscaledDeltaTime;
        public long GetTimestamp() => _timestamp;
        public int FrameCount => _frameCount;

        // ── Configuration ──

        /// <summary>
        /// Sets both DeltaTime and UnscaledDeltaTime for subsequent frames.
        /// </summary>
        public void SetDeltaTime(float deltaTime)
        {
            _deltaTime = deltaTime;
            _unscaledDeltaTime = deltaTime;
        }

        /// <summary>
        /// Sets DeltaTime and UnscaledDeltaTime independently (simulates timeScale).
        /// </summary>
        public void SetDeltaTime(float deltaTime, float unscaledDeltaTime)
        {
            _deltaTime = deltaTime;
            _unscaledDeltaTime = unscaledDeltaTime;
        }

        // ── Time Advancement ──

        /// <summary>
        /// Advances time by the specified duration in a single frame.
        /// The entire duration is applied as DeltaTime for that frame,
        /// then all PlayerLoop timings are processed.
        /// </summary>
        public void Advance(TimeSpan duration)
        {
            var seconds = (float)duration.TotalSeconds;
            var savedDt = _deltaTime;
            var savedUdt = _unscaledDeltaTime;

            _deltaTime = seconds;
            _unscaledDeltaTime = seconds;
            _timestamp += (long)(duration.TotalSeconds * Stopwatch.Frequency);
            _frameCount++;

            PlayerLoopHelper.ProcessAllTimings();

            _deltaTime = savedDt;
            _unscaledDeltaTime = savedUdt;
        }

        /// <summary>
        /// Advances one frame using the current DeltaTime.
        /// Increments FrameCount and processes all PlayerLoop timings.
        /// </summary>
        public void AdvanceFrame()
        {
            _timestamp += (long)(_deltaTime * Stopwatch.Frequency);
            _frameCount++;
            // Brief sleep to let worker threads (e.g. Unity Job System)
            // finish before processing PlayerLoop items that poll them.
            // In real Unity frames there is always a natural gap; tests
            // run back-to-back with no gap, causing race conditions.
            Thread.Sleep(1);
            PlayerLoopHelper.ProcessAllTimings();
        }

        /// <summary>
        /// Advances N frames using the current DeltaTime.
        /// </summary>
        public void AdvanceFrames(int count)
        {
            for (int i = 0; i < count; i++)
                AdvanceFrame();
        }

        /// <summary>
        /// Processes a single PlayerLoop timing tick without advancing time or frame count.
        /// Useful for testing specific timing phases.
        /// </summary>
        public void ProcessTick(PlayerLoopTiming timing)
        {
            PlayerLoopHelper.ProcessTick(timing);
        }
    }
}
