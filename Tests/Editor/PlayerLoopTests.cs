// Copyright (c) Una Partida Mas. All rights reserved.
// Licensed under the MIT License. See LICENSE.md in the repository root.

using System;
using System.Threading;
using NUnit.Framework;
using UnaPartidaMas.Valkarn.Tasks.Testing;

namespace UnaPartidaMas.Valkarn.Tasks.Tests
{
    [TestFixture]
    public class PlayerLoopTests
    {
        TestClock clock;

        [SetUp]
        public void SetUp()
        {
            clock = ValkarnTaskTestHelper.Setup();
        }

        [TearDown]
        public void TearDown()
        {
            ValkarnTaskTestHelper.Teardown();
        }

        // ════════════════════════════════════════════════════
        //  Delay Tests
        // ════════════════════════════════════════════════════

        [Test]
        public void Delay_CompletesAfterExactDuration()
        {
            var task = ValkarnTask.Delay(3000);
            Assert.IsFalse(task.IsCompleted);

            clock.Advance(TimeSpan.FromSeconds(2));
            Assert.IsFalse(task.IsCompleted);

            clock.Advance(TimeSpan.FromSeconds(1));
            Assert.IsTrue(task.IsCompleted);
        }

        [Test]
        public void Delay_ZeroOrNegative_CompletesImmediately()
        {
            var task0 = ValkarnTask.Delay(0);
            Assert.IsTrue(task0.IsCompleted);

            var taskNeg = ValkarnTask.Delay(-100);
            Assert.IsTrue(taskNeg.IsCompleted);
        }

        [Test]
        public void Delay_TimeSpan_Works()
        {
            var task = ValkarnTask.Delay(TimeSpan.FromMilliseconds(500));
            Assert.IsFalse(task.IsCompleted);

            clock.Advance(TimeSpan.FromMilliseconds(499));
            Assert.IsFalse(task.IsCompleted);

            clock.Advance(TimeSpan.FromMilliseconds(1));
            Assert.IsTrue(task.IsCompleted);
        }

        [Test]
        public void Delay_UnscaledDeltaTime_Works()
        {
            // Set scaled time to 0 (paused), unscaled at 50ms per frame
            clock.SetDeltaTime(0f, 0.05f);

            var scaledTask = ValkarnTask.Delay(500, DelayType.DeltaTime);
            var unscaledTask = ValkarnTask.Delay(500, DelayType.UnscaledDeltaTime);

            // Advance 9 frames (450ms unscaled, 0ms scaled)
            clock.AdvanceFrames(9);
            Assert.IsFalse(scaledTask.IsCompleted, "Scaled delay should NOT complete when timeScale=0");
            Assert.IsFalse(unscaledTask.IsCompleted, "Unscaled delay should not be done yet");

            // Advance 1 more frame (500ms total unscaled)
            clock.AdvanceFrame();
            Assert.IsFalse(scaledTask.IsCompleted, "Scaled delay should still NOT complete");
            Assert.IsTrue(unscaledTask.IsCompleted, "Unscaled delay should complete at 500ms");
        }

        [Test]
        public void Delay_Realtime_Works()
        {
            var task = ValkarnTask.Delay(TimeSpan.FromSeconds(1), DelayType.Realtime);
            Assert.IsFalse(task.IsCompleted);

            clock.Advance(TimeSpan.FromMilliseconds(999));
            Assert.IsFalse(task.IsCompleted);

            clock.Advance(TimeSpan.FromMilliseconds(1));
            Assert.IsTrue(task.IsCompleted);
        }

        [Test]
        public void Delay_CancellationBeforeCompletion()
        {
            var cts = new CancellationTokenSource();
            var task = ValkarnTask.Delay(3000, ct: cts.Token);
            Assert.IsFalse(task.IsCompleted);

            clock.Advance(TimeSpan.FromSeconds(1));
            Assert.IsFalse(task.IsCompleted);

            cts.Cancel();
            clock.AdvanceFrame(); // process the cancellation check

            Assert.IsTrue(task.IsCompleted);
            Assert.AreEqual(ValkarnTask.Status.Canceled, task.GetStatus());
        }

        [Test]
        public void Delay_MultipleDelays_IndependentCompletion()
        {
            var task500 = ValkarnTask.Delay(500);
            var task1000 = ValkarnTask.Delay(1000);
            var task2000 = ValkarnTask.Delay(2000);

            clock.Advance(TimeSpan.FromMilliseconds(500));
            Assert.IsTrue(task500.IsCompleted);
            Assert.IsFalse(task1000.IsCompleted);
            Assert.IsFalse(task2000.IsCompleted);

            clock.Advance(TimeSpan.FromMilliseconds(500));
            Assert.IsTrue(task1000.IsCompleted);
            Assert.IsFalse(task2000.IsCompleted);

            clock.Advance(TimeSpan.FromSeconds(1));
            Assert.IsTrue(task2000.IsCompleted);
        }

        // ════════════════════════════════════════════════════
        //  NextFrame Tests
        // ════════════════════════════════════════════════════

        [Test]
        public void NextFrame_CompletesOnNextFrame()
        {
            var task = ValkarnTask.NextFrame();
            Assert.IsFalse(task.IsCompleted);

            clock.AdvanceFrame();
            Assert.IsTrue(task.IsCompleted);
        }

        [Test]
        public void NextFrame_Cancellation()
        {
            var cts = new CancellationTokenSource();
            var task = ValkarnTask.NextFrame(ct: cts.Token);

            cts.Cancel();
            clock.AdvanceFrame();

            Assert.IsTrue(task.IsCompleted);
            Assert.AreEqual(ValkarnTask.Status.Canceled, task.GetStatus());
        }

        // ════════════════════════════════════════════════════
        //  DelayFrame Tests
        // ════════════════════════════════════════════════════

        [Test]
        public void DelayFrame_ZeroFrames_CompletesImmediately()
        {
            var task = ValkarnTask.DelayFrame(0);
            Assert.IsTrue(task.IsCompleted);
        }

        [Test]
        public void DelayFrame_CompletesAfterExactFrames()
        {
            var task = ValkarnTask.DelayFrame(5);
            Assert.IsFalse(task.IsCompleted);

            clock.AdvanceFrames(4);
            Assert.IsFalse(task.IsCompleted);

            clock.AdvanceFrame();
            Assert.IsTrue(task.IsCompleted);
        }

        [Test]
        public void DelayFrame_Cancellation()
        {
            var cts = new CancellationTokenSource();
            var task = ValkarnTask.DelayFrame(10, ct: cts.Token);

            clock.AdvanceFrames(3);
            Assert.IsFalse(task.IsCompleted);

            cts.Cancel();
            clock.AdvanceFrame();

            Assert.IsTrue(task.IsCompleted);
            Assert.AreEqual(ValkarnTask.Status.Canceled, task.GetStatus());
        }

        // ════════════════════════════════════════════════════
        //  Yield Tests
        // ════════════════════════════════════════════════════

        [Test]
        public void Yield_SuspendsAndResumes()
        {
            bool resumed = false;

            async ValkarnTask DoWork()
            {
                await ValkarnTask.Yield();
                resumed = true;
            }

            var task = DoWork();
            Assert.IsFalse(resumed, "Should suspend at Yield");

            clock.AdvanceFrame();
            Assert.IsTrue(resumed, "Should resume after frame tick");
        }

        [Test]
        public void Yield_SpecificTiming()
        {
            bool resumed = false;

            async ValkarnTask DoWork()
            {
                await ValkarnTask.Yield(PlayerLoopTiming.FixedUpdate);
                resumed = true;
            }

            var task = DoWork();
            Assert.IsFalse(resumed);

            // Process only Update — should NOT resume FixedUpdate yield
            clock.ProcessTick(PlayerLoopTiming.Update);
            Assert.IsFalse(resumed, "Should not resume from wrong timing");

            // Process FixedUpdate — should resume
            clock.ProcessTick(PlayerLoopTiming.FixedUpdate);
            Assert.IsTrue(resumed, "Should resume from correct timing");
        }

        // ════════════════════════════════════════════════════
        //  WaitUntil / WaitWhile Tests
        // ════════════════════════════════════════════════════

        [Test]
        public void WaitUntil_CompletesWhenPredicateTrue()
        {
            bool condition = false;
            var task = ValkarnTask.WaitUntil(() => condition);
            Assert.IsFalse(task.IsCompleted);

            clock.AdvanceFrame();
            Assert.IsFalse(task.IsCompleted);

            condition = true;
            clock.AdvanceFrame();
            Assert.IsTrue(task.IsCompleted);
        }

        [Test]
        public void WaitWhile_CompletesWhenPredicateFalse()
        {
            bool condition = true;
            var task = ValkarnTask.WaitWhile(() => condition);
            Assert.IsFalse(task.IsCompleted);

            clock.AdvanceFrame();
            Assert.IsFalse(task.IsCompleted);

            condition = false;
            clock.AdvanceFrame();
            Assert.IsTrue(task.IsCompleted);
        }

        [Test]
        public void WaitUntil_Cancellation()
        {
            var cts = new CancellationTokenSource();
            var task = ValkarnTask.WaitUntil(() => false, ct: cts.Token);

            clock.AdvanceFrame();
            Assert.IsFalse(task.IsCompleted);

            cts.Cancel();
            clock.AdvanceFrame();

            Assert.IsTrue(task.IsCompleted);
            Assert.AreEqual(ValkarnTask.Status.Canceled, task.GetStatus());
        }

        [Test]
        public void WaitUntil_PredicateThrows_Faults()
        {
            var task = ValkarnTask.WaitUntil(() => throw new InvalidOperationException("boom"));
            clock.AdvanceFrame();

            Assert.IsTrue(task.IsCompleted);
            Assert.AreEqual(ValkarnTask.Status.Faulted, task.GetStatus());
        }

        // ════════════════════════════════════════════════════
        //  SwitchToMainThread Tests
        // ════════════════════════════════════════════════════

        [Test]
        public void SwitchToMainThread_YieldsToNextTick()
        {
            bool afterSwitch = false;

            async ValkarnTask DoWork()
            {
                await ValkarnTask.SwitchToMainThread();
                afterSwitch = true;
            }

            var task = DoWork();
            Assert.IsFalse(afterSwitch, "Should not run inline");

            clock.AdvanceFrame();
            Assert.IsTrue(afterSwitch, "Should run after frame tick");
        }

        // ════════════════════════════════════════════════════
        //  Async Pipeline with PlayerLoop Tests
        // ════════════════════════════════════════════════════

        [Test]
        public void AsyncMethod_WithDelay_CompletesAfterAdvance()
        {
            int result = 0;

            async ValkarnTask DoWork()
            {
                await ValkarnTask.Delay(1000);
                result = 42;
            }

            var task = DoWork();
            Assert.AreEqual(0, result);
            Assert.IsFalse(task.IsCompleted);

            clock.Advance(TimeSpan.FromSeconds(1));
            // After delay completes, the continuation runs inline via ProcessAllTimings
            // But the async method's continuation needs another tick to process
            // Actually, when the delay's MoveNext sets the result, the completion core
            // invokes the awaiter's continuation synchronously. So result should be set.
            Assert.AreEqual(42, result);
            Assert.IsTrue(task.IsCompleted);
        }

        [Test]
        public void AsyncMethod_MultipleAwaits_ProgressThroughFrames()
        {
            int step = 0;

            async ValkarnTask DoWork()
            {
                step = 1;
                await ValkarnTask.NextFrame();
                step = 2;
                await ValkarnTask.DelayFrame(3);
                step = 3;
                await ValkarnTask.Delay(500);
                step = 4;
            }

            var task = DoWork();
            Assert.AreEqual(1, step); // sync up to first await

            clock.AdvanceFrame();
            Assert.AreEqual(2, step); // NextFrame completed

            clock.AdvanceFrames(3);
            Assert.AreEqual(3, step); // DelayFrame(3) completed

            clock.Advance(TimeSpan.FromMilliseconds(500));
            Assert.AreEqual(4, step); // Delay(500) completed
            Assert.IsTrue(task.IsCompleted);
        }

        [Test]
        public void AsyncMethod_WaitUntilInPipeline()
        {
            int phase = 0;
            bool ready = false;

            async ValkarnTask DoWork()
            {
                phase = 1;
                await ValkarnTask.WaitUntil(() => ready);
                phase = 2;
                await ValkarnTask.Delay(100);
                phase = 3;
            }

            var task = DoWork();
            Assert.AreEqual(1, phase);

            clock.AdvanceFrames(5);
            Assert.AreEqual(1, phase); // still waiting

            ready = true;
            clock.AdvanceFrame();
            Assert.AreEqual(2, phase); // WaitUntil completed

            clock.Advance(TimeSpan.FromMilliseconds(100));
            Assert.AreEqual(3, phase); // Delay completed
            Assert.IsTrue(task.IsCompleted);
        }

        // ════════════════════════════════════════════════════
        //  TestClock Configuration Tests
        // ════════════════════════════════════════════════════

        [Test]
        public void TestClock_AdvanceFrames_AccumulatesDeltaTime()
        {
            clock.SetDeltaTime(0.05f); // 50ms per frame
            var task = ValkarnTask.Delay(500); // 500ms

            clock.AdvanceFrames(9); // 450ms
            Assert.IsFalse(task.IsCompleted);

            clock.AdvanceFrame(); // 500ms
            Assert.IsTrue(task.IsCompleted);
        }

        [Test]
        public void TestClock_FrameCountIncrements()
        {
            Assert.AreEqual(0, clock.FrameCount);

            clock.AdvanceFrame();
            Assert.AreEqual(1, clock.FrameCount);

            clock.AdvanceFrames(5);
            Assert.AreEqual(6, clock.FrameCount);

            clock.Advance(TimeSpan.FromSeconds(1));
            Assert.AreEqual(7, clock.FrameCount);
        }
    }

    // ════════════════════════════════════════════════════════════════
    //  PlayerLoopRunner — Stale-array race (items added during Run)
    // ════════════════════════════════════════════════════════════════

    [TestFixture]
    public class PlayerLoopRunnerTests
    {
        [SetUp]
        public void SetUp()
        {
            TestHelper.EnsureInitialized();
        }

        /// <summary>
        /// Simulates the stale-array race: when MoveNext() adds new items to the runner
        /// during Run(), Array.Resize may create a new array. The compacted items in
        /// the old array (localItems) must be copied to the new array. Without the fix,
        /// compacted items would be lost.
        /// </summary>
        [Test]
        public void Run_ItemAddedDuringMoveNext_SurvivesCompaction()
        {
            var runner = new PlayerLoopRunner();

            // Fill the runner to its initial capacity (16).
            // 14 items that survive (MoveNext returns true) + 1 that dies (returns false)
            // + 1 "adder" item that adds a new item during MoveNext (triggering Array.Resize)
            // and then dies (returns false).
            // After Run: 14 survivors + 1 newly-added = 15 items.

            var survivorItems = new CountingItem[14];
            for (int i = 0; i < 14; i++)
            {
                survivorItems[i] = new CountingItem();
                runner.Add(survivorItems[i]);
            }

            // This item returns false (dies) — creates a gap for compaction
            runner.Add(new OneTimeItem());

            // This item adds a new item during MoveNext (causing Array.Resize since
            // the array is full at 16), then returns false itself.
            var lateItem = new CountingItem();
            runner.Add(new AdderItem(runner, lateItem));

            // Run — compacts dead items, must preserve surviving + newly-added items
            runner.Run();

            // Verify: the 14 survivors should have been called
            for (int i = 0; i < 14; i++)
                Assert.AreEqual(1, survivorItems[i].MoveNextCount, $"Survivor {i} should have been called");

            // Run again to verify the late-added item survived the race
            runner.Run();

            // The late item should have been called in the second Run
            Assert.AreEqual(1, lateItem.MoveNextCount,
                "Item added during MoveNext (triggering Array.Resize) should survive compaction");

            // All 14 original survivors should now have been called twice
            for (int i = 0; i < 14; i++)
                Assert.AreEqual(2, survivorItems[i].MoveNextCount, $"Survivor {i} should have run twice");
        }

        [Test]
        public void Run_CompactionWithNoResize_Works()
        {
            var runner = new PlayerLoopRunner();

            // Add 4 items: 2 survive, 2 die
            var s1 = new CountingItem();
            var s2 = new CountingItem();
            runner.Add(s1);
            runner.Add(new OneTimeItem());
            runner.Add(s2);
            runner.Add(new OneTimeItem());

            runner.Run();

            Assert.AreEqual(1, s1.MoveNextCount);
            Assert.AreEqual(1, s2.MoveNextCount);

            // Run again — only 2 items should remain
            runner.Run();

            Assert.AreEqual(2, s1.MoveNextCount);
            Assert.AreEqual(2, s2.MoveNextCount);
        }

        // ── Helper items for PlayerLoopRunner tests ──

        /// <summary>Always returns true. Tracks how many times MoveNext was called.</summary>
        sealed class CountingItem : IPlayerLoopItem
        {
            public int MoveNextCount;
            public bool MoveNext() { MoveNextCount++; return true; }
        }

        /// <summary>Returns false on first MoveNext (dies immediately).</summary>
        sealed class OneTimeItem : IPlayerLoopItem
        {
            public bool MoveNext() => false;
        }

        /// <summary>
        /// On first MoveNext, adds a new item to the runner (potentially triggering
        /// Array.Resize), then returns false (dies). This simulates the stale-array race.
        /// </summary>
        sealed class AdderItem : IPlayerLoopItem
        {
            readonly PlayerLoopRunner runner;
            readonly IPlayerLoopItem itemToAdd;
            public AdderItem(PlayerLoopRunner runner, IPlayerLoopItem item)
            {
                this.runner = runner;
                this.itemToAdd = item;
            }
            public bool MoveNext()
            {
                runner.Add(itemToAdd);
                return false;
            }
        }
    }
}
