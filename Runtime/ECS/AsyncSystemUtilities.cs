// Copyright (c) Una Partida Mas. All rights reserved.
// Licensed under the MIT License. See LICENSE.md in the repository root.

#if VTASKS_HAS_ENTITIES
using System;
using System.Threading;
using Unity.Entities;

namespace UnaPartidaMas.Valkarn.Tasks.ECS
{
    /// <summary>
    /// Async utility extensions for Unity ECS (Entities).
    /// </summary>
    public static class AsyncSystemUtilities
    {
        /// <summary>
        /// Returns a <see cref="CancellationToken"/> that is canceled when the
        /// <paramref name="world"/> is destroyed. Polls <see cref="World.IsCreated"/>
        /// each frame via <see cref="ValkarnTask.Yield"/>.
        /// </summary>
        /// <param name="world">The ECS world to monitor.</param>
        /// <param name="timing">The PlayerLoop timing to poll at.</param>
        /// <returns>A token that cancels when the world is destroyed.</returns>
        public static CancellationToken GetWorldCancellationToken(
            this World world,
            PlayerLoopTiming timing = PlayerLoopTiming.Update)
        {
            if (world == null)
                throw new ArgumentNullException(nameof(world));

            if (!world.IsCreated)
                return new CancellationToken(true);

            var cts = new CancellationTokenSource();
            PollWorldAlive(world, cts, timing).Forget();
            return cts.Token;
        }

        static async ValkarnTask PollWorldAlive(World world, CancellationTokenSource cts, PlayerLoopTiming timing)
        {
            try
            {
                while (world.IsCreated)
                {
                    await ValkarnTask.Yield(timing);
                }
            }
            catch (Exception)
            {
                // World may throw if accessed after disposal
            }
            finally
            {
                cts.Cancel();
                cts.Dispose();
            }
        }

        /// <summary>
        /// Checks whether an entity exists and is valid, catching
        /// <see cref="ObjectDisposedException"/> that may occur if the
        /// <see cref="EntityManager"/> has been disposed.
        /// </summary>
        /// <param name="entityManager">The entity manager to query.</param>
        /// <param name="entity">The entity to check.</param>
        /// <returns>True if the entity exists and is valid; false otherwise.</returns>
        public static bool SafeEntityExists(this EntityManager entityManager, Entity entity)
        {
            try
            {
                return entityManager.Exists(entity);
            }
            catch (ObjectDisposedException)
            {
                return false;
            }
        }
    }
}
#endif
