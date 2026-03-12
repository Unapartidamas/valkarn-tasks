// Copyright (c) Una Partida Mas. All rights reserved.
// Licensed under the MIT License. See LICENSE.md in the repository root.

#if VTASKS_HAS_ENTITIES
using System.Threading;
using Unity.Entities;
using UnaPartidaMas.Valkarn.Tasks;
using UnaPartidaMas.Valkarn.Tasks.ECS;

// Your project's component types
// using MyGame.Components;

namespace Samples.ECS
{
    /// <summary>
    /// Demonstrates async world initialization from an ISystem.
    ///
    /// Pattern:
    ///   1. OnCreate fires a one-shot async method via Forget().
    ///   2. The async method obtains a CancellationToken tied to the World lifetime
    ///      so that work is automatically canceled if the World is destroyed.
    ///   3. Heavy I/O runs on the thread pool via RunOnThreadPool, which automatically
    ///      switches back to the main thread when the delegate completes.
    ///   4. After returning to the main thread, ECS structural changes are safe.
    ///
    /// Safety notes:
    ///   - Never access EntityManager or perform structural changes from a thread pool thread.
    ///   - Always pass the world cancellation token through to async calls so cleanup is automatic.
    ///   - Forget() ensures exceptions are routed to ValkarnTask.PublishUnobservedException
    ///     rather than being silently swallowed.
    /// </summary>
    public partial struct AsyncLoadSystem : ISystem
    {
        public void OnCreate(ref SystemState state)
        {
            // Obtain a cancellation token that fires when this World is destroyed.
            // This prevents async work from outliving the world and accessing stale data.
            var worldCt = state.World.GetWorldCancellationToken();

            // Fire the async initialization. Forget() converts any faulted result into
            // an unobserved exception report rather than silently dropping it.
            InitializeAsync(state.WorldUnmanaged, worldCt).Forget();
        }

        public void OnUpdate(ref SystemState state)
        {
            // Normal per-frame logic runs here after initialization completes.
        }

        public void OnDestroy(ref SystemState state)
        {
            // The world cancellation token will cancel any in-flight async work automatically.
        }

        /// <summary>
        /// One-shot async initialization that loads data from disk on a background thread,
        /// then applies it to the ECS world on the main thread.
        /// </summary>
        static async ValkarnTask InitializeAsync(WorldUnmanaged world, CancellationToken ct)
        {
            // ---- Phase 1: Load data on a background thread ----
            // RunOnThreadPool moves execution to the thread pool, runs the delegate,
            // and automatically switches back to the main thread when done.
            // The returned value is available on the main thread.
            var configData = await ValkarnTask.RunOnThreadPool(() => LoadFromDisk(), cancellationToken: ct);

            // ---- Phase 2: Apply data on the main thread ----
            // We are now back on the main thread. It is safe to perform ECS operations.
            // Check cancellation in case the world was destroyed while we were loading.
            ct.ThrowIfCancellationRequested();

            ApplyConfiguration(world, configData);
        }

        /// <summary>
        /// Simulates loading configuration from disk. This runs on a thread pool thread
        /// and must NOT access any Unity or ECS APIs.
        /// </summary>
        static ConfigData LoadFromDisk()
        {
            // Replace with actual file I/O, JSON parsing, database queries, etc.
            // This is a pure C# method with no Unity API calls.
            //
            // Example:
            //   var json = System.IO.File.ReadAllText("config.json");
            //   return JsonUtility.FromJson<ConfigData>(json); // NOT safe here -- JsonUtility is main-thread only
            //   return System.Text.Json.JsonSerializer.Deserialize<ConfigData>(json); // OK -- pure C#

            return new ConfigData { MaxEnemies = 100, SpawnRadius = 50f };
        }

        /// <summary>
        /// Applies loaded data to the ECS world. Must run on the main thread.
        /// </summary>
        static void ApplyConfiguration(WorldUnmanaged world, ConfigData data)
        {
            // Perform structural changes, create entities, set singleton components, etc.
            // Example:
            //   var em = world.EntityManager;
            //   var entity = em.CreateEntity();
            //   em.AddComponentData(entity, new GameConfig { MaxEnemies = data.MaxEnemies });

            UnityEngine.Debug.Log($"[AsyncLoadSystem] Configuration applied: MaxEnemies={data.MaxEnemies}");
        }

        /// <summary>
        /// Plain C# data container for configuration loaded from disk.
        /// This is NOT an ECS component -- it is a temporary transfer object.
        /// </summary>
        struct ConfigData
        {
            public int MaxEnemies;
            public float SpawnRadius;
        }
    }
}
#endif
