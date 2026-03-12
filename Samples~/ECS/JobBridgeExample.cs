// Copyright (c) Una Partida Mas. All rights reserved.
// Licensed under the MIT License. See LICENSE.md in the repository root.

#if UNITY_5_3_OR_NEWER && VTASKS_HAS_ENTITIES
using System;
using System.Threading;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using UnaPartidaMas.Valkarn.Tasks;
using UnaPartidaMas.Valkarn.Tasks.Bridge;
using UnaPartidaMas.Valkarn.Tasks.ECS;

// Your project's component types
// using MyGame.Components;

namespace Samples.ECS
{
    /// <summary>
    /// Demonstrates bridging the Unity Job System into ValkarnTask from an ISystem.
    ///
    /// Pattern:
    ///   1. Query entities and copy data into a temporary NativeArray.
    ///   2. Schedule a parallel Burst-compiled job to process the data.
    ///   3. Await the job handle via <c>handle.ToValkarnTask()</c>, which polls completion
    ///      each frame without blocking the main thread.
    ///   4. Read results back on the main thread and apply them to entities.
    ///
    /// This pattern is useful when you want to:
    ///   - Chain job results into further async work (e.g., network I/O after processing).
    ///   - Integrate job-based computation into a larger async pipeline.
    ///   - Avoid manual JobHandle.Complete() calls that block the main thread.
    ///
    /// Safety notes:
    ///   - NativeArrays allocated with Allocator.TempJob must be disposed after the job completes.
    ///     This example uses a try/finally block to guarantee disposal.
    ///   - ToValkarnTask() polls each frame; it does NOT block the calling thread.
    ///   - The job handle is always completed (even on cancellation) to prevent job system leaks.
    /// </summary>
    public partial struct JobBridgeExample : ISystem
    {
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<HealthData>();
        }

        public void OnUpdate(ref SystemState state)
        {
            var worldCt = state.World.GetWorldCancellationToken();

            // ---- Extract all data synchronously inside OnUpdate ----
            // SystemAPI is source-generated and only works inside partial ISystem methods.
            // Async methods cannot have ref/in/out parameters (CS1988), so we must query
            // and copy entity data here, then pass plain values to the async method.
            var query = SystemAPI.QueryBuilder().WithAll<HealthData>().Build();
            var entityCount = query.CalculateEntityCount();

            if (entityCount == 0)
                return;

            var entities = query.ToEntityArray(Allocator.TempJob);
            var healthArray = query.ToComponentDataArray<HealthData>(Allocator.TempJob);
            var results = new NativeArray<float>(entityCount, Allocator.TempJob);

            // Fire-and-forget the async pipeline. Forget() routes exceptions to
            // ValkarnTask.PublishUnobservedException.
            // The async method owns disposal of the NativeArrays via its finally block.
            ProcessHealthAsync(state.EntityManager, entities, healthArray, results, worldCt).Forget();

            // Disable this system after the first run to prevent re-launching every frame.
            // In a real project, you might use a flag or enable/disable based on game state.
            state.Enabled = false;
        }

        public void OnDestroy(ref SystemState state) { }

        /// <summary>
        /// Processes entity health data in a parallel job, awaits completion
        /// via ValkarnTask, and applies the results.
        /// </summary>
        /// <remarks>
        /// Async methods cannot have <c>ref</c>, <c>in</c>, or <c>out</c> parameters (CS1988).
        /// All ECS data must be extracted synchronously in the calling ISystem method and
        /// passed as regular (by-value) parameters. This method takes ownership of the
        /// NativeArrays and disposes them in its finally block.
        /// </remarks>
        static async ValkarnTask ProcessHealthAsync(
            EntityManager entityManager,
            NativeArray<Entity> entities,
            NativeArray<HealthData> healthArray,
            NativeArray<float> results,
            CancellationToken ct)
        {
            try
            {
                // ---- Phase 1: Schedule a parallel Burst job ----
                var job = new HealthProcessingJob
                {
                    HealthInputs = healthArray,
                    ProcessedOutputs = results,
                };

                // Schedule with a batch size appropriate for your workload.
                var handle = job.Schedule(entities.Length, batchSize: 64);

                // ---- Phase 2: Await the job without blocking the main thread ----
                // ToValkarnTask() polls JobHandle.IsCompleted each frame and calls
                // JobHandle.Complete() when ready. This frees the main thread to
                // continue rendering and processing input while the job runs.
                await handle.ToValkarnTask(cancellationToken: ct);

                // ---- Phase 3: Apply results on the main thread ----
                // We are back on the main thread. The job is complete and results are readable.
                ct.ThrowIfCancellationRequested();

                for (int i = 0; i < entities.Length; i++)
                {
                    var entity = entities[i];

                    // Entities may have been destroyed while the job was running.
                    if (!entityManager.SafeEntityExists(entity))
                        continue;

                    entityManager.SetComponentData(entity, new HealthData
                    {
                        CurrentHealth = results[i],
                    });
                }
            }
            finally
            {
                // ---- Cleanup: Always dispose NativeArrays ----
                // This runs whether the task succeeded, faulted, or was canceled.
                if (entities.IsCreated) entities.Dispose();
                if (healthArray.IsCreated) healthArray.Dispose();
                if (results.IsCreated) results.Dispose();
            }
        }

        // -----------------------------------------------------------------
        // Job definition
        // -----------------------------------------------------------------

        /// <summary>
        /// Burst-compiled parallel job that processes health data.
        /// Replace with your actual computation.
        /// </summary>
        [BurstCompile]
        struct HealthProcessingJob : IJobParallelFor
        {
            [ReadOnly] public NativeArray<HealthData> HealthInputs;
            [WriteOnly] public NativeArray<float> ProcessedOutputs;

            public void Execute(int index)
            {
                // Example: apply regeneration, damage-over-time, clamping, etc.
                var health = HealthInputs[index];
                var newHealth = health.CurrentHealth + health.RegenRate;

                // Clamp to max
                if (newHealth > health.MaxHealth)
                    newHealth = health.MaxHealth;

                ProcessedOutputs[index] = newHealth;
            }
        }

        // -----------------------------------------------------------------
        // Placeholder component -- replace with your project's actual type
        // -----------------------------------------------------------------

        /// <summary>
        /// Per-entity health component. In your project, this would be defined
        /// in your game's component assembly.
        /// </summary>
        struct HealthData : IComponentData
        {
            public float CurrentHealth;
            public float MaxHealth;
            public float RegenRate;
        }
    }
}
#endif
