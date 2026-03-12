// Copyright (c) Una Partida Mas. All rights reserved.
// Licensed under the MIT License. See LICENSE.md in the repository root.

#if VTASKS_HAS_ENTITIES
using System.Threading;
using Unity.Entities;
using UnaPartidaMas.Valkarn.Tasks;
using UnaPartidaMas.Valkarn.Tasks.ECS; // For SafeEntityExists, GetWorldCancellationToken

// Your project's component types
// using MyGame.Components;

namespace Samples.ECS
{
    /// <summary>
    /// Demonstrates throttled per-entity async decisions in an ISystem.
    ///
    /// KEY PATTERN: Using AsyncThrottle to prevent unbounded task storms.
    ///
    /// Without throttling, iterating 10,000 entities and launching an async task per entity
    /// would create 10,000 concurrent tasks -- overwhelming the thread pool, starving the
    /// main thread of continuations, and causing frame hitches or OOM. AsyncThrottle caps
    /// the number of in-flight tasks to a safe limit.
    ///
    /// Safety warnings:
    ///   - ENTITY LIFETIME: Entities can be destroyed while async work is in-flight.
    ///     Always call entityManager.SafeEntityExists(entity) after every await before
    ///     writing back to the entity.
    ///   - COMPONENT LOOKUPS: ComponentLookup caches become stale across await points
    ///     because structural changes may occur while suspended. Re-acquire lookups from
    ///     SystemState or use EntityManager directly after resuming.
    ///   - THREAD SAFETY: ECS operations (EntityManager, ComponentLookup, etc.) are only
    ///     safe on the main thread. Use RunOnThreadPool only for pure computation, then
    ///     switch back before touching ECS data.
    /// </summary>
    public partial struct AISystemExample : ISystem
    {
        /// <summary>
        /// Limits how many AI decision tasks run concurrently.
        /// Tune this based on your workload:
        ///   - Too low: entities wait many frames for their turn, AI feels sluggish.
        ///   - Too high: thread pool saturation, main-thread continuation stalls.
        /// 8 is a reasonable starting point for moderate computation per entity.
        /// </summary>
        AsyncThrottle aiThrottle;

        public void OnCreate(ref SystemState state)
        {
            aiThrottle = new AsyncThrottle(maxConcurrency: 8);

            // Require AIDecision components to exist before this system runs.
            state.RequireForUpdate<AIDecisionTag>();
        }

        public void OnUpdate(ref SystemState state)
        {
            // Cache values needed by the async path. We capture these before iterating
            // because SystemState and ref-returning APIs cannot be used after yield points.
            var entityManager = state.EntityManager;
            var worldCt = state.World.GetWorldCancellationToken();

            // Iterate all entities that need AI decisions.
            // In a real project, use SystemAPI.Query or an EntityQuery.
            // This simplified loop shows the throttle pattern.
            foreach (var (aiData, entity) in
                SystemAPI.Query<RefRW<AIDecisionData>>().WithEntityAccess())
            {
                // --- Throttle gate ---
                // TryAcquire is lock-free and returns false if we are at capacity.
                // Skipped entities will be re-evaluated next frame.
                if (!aiThrottle.TryAcquire())
                    break; // Stop iterating -- all slots are occupied.

                // Capture the component value by copy. Do NOT capture the RefRW --
                // it points into a chunk that may be reallocated across await points.
                var inputSnapshot = aiData.ValueRO;

                // Launch async work. ThrottledForget automatically calls
                // aiThrottle.Release() when the task completes (success, fault, or cancel),
                // ensuring the slot is always freed.
                MakeDecisionAsync(entity, inputSnapshot, entityManager, worldCt)
                    .ThrottledForget(aiThrottle);
            }
        }

        public void OnDestroy(ref SystemState state)
        {
            // The world cancellation token cancels all in-flight tasks.
            // AsyncThrottle does not hold unmanaged resources and needs no disposal.
        }

        /// <summary>
        /// Performs an expensive AI computation on the thread pool, then writes the
        /// result back to the entity on the main thread.
        /// </summary>
        static async ValkarnTask MakeDecisionAsync(
            Entity entity,
            AIDecisionData input,
            EntityManager entityManager,
            CancellationToken ct)
        {
            // ---- Phase 1: Heavy computation on thread pool ----
            // RunOnThreadPool switches to a worker thread, runs the lambda, and
            // switches back to the main thread automatically.
            var decision = await ValkarnTask.RunOnThreadPool(() =>
            {
                // Pure C# computation only. No Unity API calls.
                // Example: pathfinding, utility scoring, behavior tree evaluation, etc.
                return ComputeAIDecision(input);
            }, cancellationToken: ct);

            // ---- Phase 2: Yield to let other systems run ----
            // Optional: yield a frame to spread main-thread write-back pressure
            // across multiple frames when many tasks complete simultaneously.
            await ValkarnTask.Yield(PlayerLoopTiming.Update);

            // ---- Phase 3: Validate entity and write back on main thread ----
            // CRITICAL: The entity may have been destroyed while we were on the thread pool
            // or during the yield. Always check before writing.
            if (!entityManager.SafeEntityExists(entity))
                return; // Entity was destroyed -- discard the result silently.

            // Check cancellation in case the world is shutting down.
            ct.ThrowIfCancellationRequested();

            // IMPORTANT: Do NOT reuse any ComponentLookup or RefRW captured before the await.
            // Structural changes may have occurred, invalidating cached chunk pointers.
            // Use EntityManager directly or re-acquire the lookup from SystemState.
            entityManager.SetComponentData(entity, new AIDecisionData
            {
                CurrentState = decision.NextState,
                Score = decision.Score,
            });
        }

        /// <summary>
        /// Pure C# AI computation. This runs on a thread pool thread and must NOT
        /// call any Unity or ECS APIs.
        /// </summary>
        static AIDecisionResult ComputeAIDecision(AIDecisionData input)
        {
            // Replace with your actual AI logic: utility scoring, GOAP planning,
            // Monte Carlo tree search, neural net inference, etc.
            return new AIDecisionResult
            {
                NextState = 1,
                Score = input.Score + 1.0f,
            };
        }

        // -----------------------------------------------------------------
        // Placeholder types -- replace with your project's actual components
        // -----------------------------------------------------------------

        /// <summary>
        /// Tag component to gate system execution via RequireForUpdate.
        /// In your project, this would be a real IComponentData singleton or tag.
        /// </summary>
        struct AIDecisionTag : IComponentData { }

        /// <summary>
        /// Per-entity AI state component.
        /// </summary>
        struct AIDecisionData : IComponentData
        {
            public int CurrentState;
            public float Score;
        }

        /// <summary>
        /// Result of the background AI computation (plain C# struct, not a component).
        /// </summary>
        struct AIDecisionResult
        {
            public int NextState;
            public float Score;
        }
    }
}
#endif
