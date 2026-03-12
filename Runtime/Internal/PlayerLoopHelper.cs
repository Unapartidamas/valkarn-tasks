// Copyright (c) Una Partida Mas. All rights reserved.
// Licensed under the MIT License. See LICENSE.md in the repository root.

using System;
using System.Threading;
#if UNITY_5_3_OR_NEWER
using UnityEngine;
using UnityEngine.LowLevel;
using UnityEngine.PlayerLoop;
#endif

namespace UnaPartidaMas.Valkarn.Tasks
{
    /// <summary>
    /// Manages PlayerLoop injection for Valkarn Tasks.
    /// Injects ContinuationQueue + PlayerLoopRunner per timing (16 timings).
    ///
    /// Initialization:
    ///   [RuntimeInitializeOnLoadMethod(SubsystemRegistration)] — earliest possible point.
    ///   Captures main thread ID. Injects marker types idempotently.
    ///   Always uses GetCurrentPlayerLoop() (never GetDefaultPlayerLoop).
    /// </summary>
    internal static class PlayerLoopHelper
    {
        // ── Per-timing queues and runners ──
        static ContinuationQueue[] s_continuationQueues = new ContinuationQueue[PlayerLoopTimingExtensions.Count];
        static PlayerLoopRunner[] s_playerLoopRunners = new PlayerLoopRunner[PlayerLoopTimingExtensions.Count];

        static volatile bool s_initialized;
        static int s_frameCount;

#if UNITY_5_3_OR_NEWER
        // ── Marker types for each timing (for idempotent injection) ──
        struct ValkarnTaskInitialization { }
        struct ValkarnTaskLastInitialization { }
        struct ValkarnTaskEarlyUpdate { }
        struct ValkarnTaskLastEarlyUpdate { }
        struct ValkarnTaskFixedUpdate { }
        struct ValkarnTaskLastFixedUpdate { }
        struct ValkarnTaskPreUpdate { }
        struct ValkarnTaskLastPreUpdate { }
        struct ValkarnTaskUpdate { }
        struct ValkarnTaskLastUpdate { }
        struct ValkarnTaskPreLateUpdate { }
        struct ValkarnTaskLastPreLateUpdate { }
        struct ValkarnTaskPostLateUpdate { }
        struct ValkarnTaskLastPostLateUpdate { }
        struct ValkarnTaskTimeUpdate { }
        struct ValkarnTaskLastTimeUpdate { }

        static readonly Type[] s_markerTypes = new Type[]
        {
            typeof(ValkarnTaskInitialization),
            typeof(ValkarnTaskLastInitialization),
            typeof(ValkarnTaskEarlyUpdate),
            typeof(ValkarnTaskLastEarlyUpdate),
            typeof(ValkarnTaskFixedUpdate),
            typeof(ValkarnTaskLastFixedUpdate),
            typeof(ValkarnTaskPreUpdate),
            typeof(ValkarnTaskLastPreUpdate),
            typeof(ValkarnTaskUpdate),
            typeof(ValkarnTaskLastUpdate),
            typeof(ValkarnTaskPreLateUpdate),
            typeof(ValkarnTaskLastPreLateUpdate),
            typeof(ValkarnTaskPostLateUpdate),
            typeof(ValkarnTaskLastPostLateUpdate),
            typeof(ValkarnTaskTimeUpdate),
            typeof(ValkarnTaskLastTimeUpdate),
        };

        // Maps PlayerLoopTiming → Unity PlayerLoop parent system type
        static readonly Type[] s_parentSystemTypes = new Type[]
        {
            typeof(Initialization),          // 0  Initialization
            typeof(Initialization),          // 1  LastInitialization
            typeof(EarlyUpdate),             // 2  EarlyUpdate
            typeof(EarlyUpdate),             // 3  LastEarlyUpdate
            typeof(FixedUpdate),             // 4  FixedUpdate
            typeof(FixedUpdate),             // 5  LastFixedUpdate
            typeof(PreUpdate),               // 6  PreUpdate
            typeof(PreUpdate),               // 7  LastPreUpdate
            typeof(Update),                  // 8  Update
            typeof(Update),                  // 9  LastUpdate
            typeof(PreLateUpdate),           // 10 PreLateUpdate
            typeof(PreLateUpdate),           // 11 LastPreLateUpdate
            typeof(PostLateUpdate),          // 12 PostLateUpdate
            typeof(PostLateUpdate),          // 13 LastPostLateUpdate
            typeof(TimeUpdate),              // 14 TimeUpdate
            typeof(TimeUpdate),              // 15 LastTimeUpdate
        };

        // ── Unity Initialization ──

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void Init()
        {
            // Capture main thread ID — used by all pool/queue operations
            var mainThreadId = Thread.CurrentThread.ManagedThreadId;
            ValkarnPoolShared.SetMainThreadId(mainThreadId);
            MainThreadId = mainThreadId;

            // Initialize queues and runners
            for (int i = 0; i < PlayerLoopTimingExtensions.Count; i++)
            {
                s_continuationQueues[i] = new ContinuationQueue();
                s_playerLoopRunners[i] = new PlayerLoopRunner();
            }

            // Inject into PlayerLoop (idempotent)
            var playerLoop = PlayerLoop.GetCurrentPlayerLoop();
            if (!HasValkarnTaskSystems(ref playerLoop))
            {
                InjectValkarnTaskSystems(ref playerLoop);
                PlayerLoop.SetPlayerLoop(playerLoop);
            }

            s_initialized = true;

#if UNITY_EDITOR
            UnityEditor.EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
            UnityEditor.EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
#endif
        }

#if UNITY_EDITOR
        static void OnPlayModeStateChanged(UnityEditor.PlayModeStateChange state)
        {
            if (state == UnityEditor.PlayModeStateChange.EnteredEditMode
                || state == UnityEditor.PlayModeStateChange.ExitingPlayMode)
            {
                // Reset state on domain reload / play mode exit
                s_initialized = false;
                s_frameCount = 0;
                for (int i = 0; i < PlayerLoopTimingExtensions.Count; i++)
                {
                    s_continuationQueues[i] = null;
                    s_playerLoopRunners[i] = null;
                }

                // Reset static state in other subsystems
                ValkarnTask.ResetStatics();
                TimeProvider.ResetToDefault();
                PoolRegistry.Clear();
                ContinuationQueue.ContinuationNode.ResetPool();
            }
        }
#endif

        // ── Idempotency Check ──

        static bool HasValkarnTaskSystems(ref PlayerLoopSystem playerLoop)
        {
            if (playerLoop.subSystemList == null) return false;

            for (int i = 0; i < playerLoop.subSystemList.Length; i++)
            {
                if (playerLoop.subSystemList[i].type == typeof(ValkarnTaskUpdate))
                    return true;

                var subList = playerLoop.subSystemList[i].subSystemList;
                if (subList != null)
                {
                    for (int j = 0; j < subList.Length; j++)
                    {
                        if (subList[j].type == typeof(ValkarnTaskUpdate))
                            return true;
                    }
                }
            }
            return false;
        }

        // ── Injection ──

        static void InjectValkarnTaskSystems(ref PlayerLoopSystem playerLoop)
        {
            for (int timing = 0; timing < PlayerLoopTimingExtensions.Count; timing++)
            {
                var parentType = s_parentSystemTypes[timing];
                var markerType = s_markerTypes[timing];
                bool isLast = (timing & 1) == 1; // odd indices are "Last" variants

                int queueIndex = timing;
                var newSystem = new PlayerLoopSystem
                {
                    type = markerType,
                    updateDelegate = CreateUpdateDelegate(queueIndex)
                };

                InsertIntoParent(ref playerLoop, parentType, newSystem, isLast);
            }
        }

        static PlayerLoopSystem.UpdateFunction CreateUpdateDelegate(int timingIndex)
        {
            return () =>
            {
                if (!s_initialized) return;

                // Run continuations (one-shot)
                s_continuationQueues[timingIndex]?.Run();

                // Run recurring items
                s_playerLoopRunners[timingIndex]?.Run();

                // Frame count tracking + pool trimming (once per Update timing)
                if (timingIndex == (int)PlayerLoopTiming.Update)
                {
                    s_frameCount++;
                    if (s_frameCount % ValkarnTask.TrimCheckInterval == 0)
                        PoolRegistry.TrimAll(ValkarnTask.MinPoolSize);
                }
            };
        }

        static void InsertIntoParent(ref PlayerLoopSystem playerLoop, Type parentType,
            PlayerLoopSystem newSystem, bool insertLast)
        {
            if (playerLoop.subSystemList == null) return;

            for (int i = 0; i < playerLoop.subSystemList.Length; i++)
            {
                if (playerLoop.subSystemList[i].type == parentType)
                {
                    ref var parent = ref playerLoop.subSystemList[i];
                    var existing = parent.subSystemList ?? Array.Empty<PlayerLoopSystem>();
                    var newSubs = new PlayerLoopSystem[existing.Length + 1];

                    if (insertLast)
                    {
                        Array.Copy(existing, newSubs, existing.Length);
                        newSubs[existing.Length] = newSystem;
                    }
                    else
                    {
                        newSubs[0] = newSystem;
                        Array.Copy(existing, 0, newSubs, 1, existing.Length);
                    }

                    parent.subSystemList = newSubs;
                    return;
                }
            }
        }
#endif // UNITY_5_3_OR_NEWER

        // ── Test Support (available in all builds) ──

        /// <summary>
        /// Initializes queues and runners without Unity PlayerLoop injection.
        /// Used by test infrastructure.
        /// </summary>
        internal static void InitializeForTest()
        {
            var mainThreadId = Thread.CurrentThread.ManagedThreadId;
            ValkarnPoolShared.SetMainThreadId(mainThreadId);
            MainThreadId = mainThreadId;

            for (int i = 0; i < PlayerLoopTimingExtensions.Count; i++)
            {
                s_continuationQueues[i] = new ContinuationQueue();
                s_playerLoopRunners[i] = new PlayerLoopRunner();
            }

            s_frameCount = 0;
            s_initialized = true;
        }

        /// <summary>
        /// Shuts down test infrastructure. Clears all queues and runners.
        /// </summary>
        internal static void ShutdownForTest()
        {
            s_initialized = false;
            for (int i = 0; i < PlayerLoopTimingExtensions.Count; i++)
            {
                s_continuationQueues[i] = null;
                s_playerLoopRunners[i] = null;
            }
        }

        /// <summary>
        /// Processes a single timing tick — runs continuations and recurring items.
        /// Used by TestClock to simulate PlayerLoop ticks.
        /// </summary>
        internal static void ProcessTick(PlayerLoopTiming timing)
        {
            if (!s_initialized) return;

            var index = (int)timing;
            s_continuationQueues[index]?.Run();
            s_playerLoopRunners[index]?.Run();
        }

        /// <summary>
        /// Processes all 16 timing ticks in order (one full frame).
        /// Used by TestClock.AdvanceFrame().
        /// </summary>
        internal static void ProcessAllTimings()
        {
            if (!s_initialized) return;

            for (int i = 0; i < PlayerLoopTimingExtensions.Count; i++)
            {
                s_continuationQueues[i]?.Run();
                s_playerLoopRunners[i]?.Run();
            }

            // Increment frame count so pool trimming triggers during tests
            s_frameCount++;
            if (s_frameCount % ValkarnTask.TrimCheckInterval == 0)
                PoolRegistry.TrimAll(ValkarnTask.MinPoolSize);
        }

        // ── Public API ──

        /// <summary>
        /// Enqueues a one-shot continuation for the specified timing.
        /// </summary>
        internal static void AddContinuation(PlayerLoopTiming timing, Action<object> action, object state)
        {
            var queue = s_continuationQueues[(int)timing];
            if (queue == null)
                throw new InvalidOperationException(
                    "PlayerLoopHelper is not initialized. Ensure Init() or InitializeForTest() has been called.");
            if (ValkarnPoolShared.IsMainThread())
                queue.Enqueue(action, state);
            else
                queue.EnqueueFromThread(action, state);
        }

        /// <summary>
        /// Adds a recurring item to the specified timing's runner.
        /// </summary>
        internal static void AddAction(PlayerLoopTiming timing, IPlayerLoopItem item)
        {
            var runner = s_playerLoopRunners[(int)timing];
            if (runner == null)
                throw new InvalidOperationException(
                    "PlayerLoopHelper is not initialized. Ensure Init() or InitializeForTest() has been called.");
            runner.Add(item);
        }

        internal static int MainThreadId { get; private set; }
    }
}
