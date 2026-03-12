// Copyright (c) Una Partida Mas. All rights reserved.
// Licensed under the MIT License. See LICENSE.md in the repository root.

using System;
using NUnit.Framework;
using Unity.Entities;
using UnaPartidaMas.Valkarn.Tasks.ECS;
using UnaPartidaMas.Valkarn.Tasks.Testing;

namespace UnaPartidaMas.Valkarn.Tasks.Tests.ECS
{
    /// <summary>
    /// Integration tests for <see cref="AsyncSystemUtilities"/>
    /// exercising real Unity ECS <see cref="World"/> and <see cref="EntityManager"/>.
    /// </summary>
    [TestFixture]
    public class AsyncSystemUtilitiesTests
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
        //  Null world → ArgumentNullException
        // ════════════════════════════════════════════════════

        [Test]
        public void GetWorldCancellationToken_NullWorld_ThrowsArgumentNullException()
        {
            Assert.Throws<ArgumentNullException>(() =>
                AsyncSystemUtilities.GetWorldCancellationToken(null));
        }

        // ════════════════════════════════════════════════════
        //  Already-destroyed world → pre-canceled token
        // ════════════════════════════════════════════════════

        [Test]
        public void GetWorldCancellationToken_DestroyedWorld_ReturnsPreCanceledToken()
        {
            var world = new World("TestDestroyedWorld");
            world.Dispose();

            var token = world.GetWorldCancellationToken();

            Assert.IsTrue(token.IsCancellationRequested);
        }

        // ════════════════════════════════════════════════════
        //  Live world → token not canceled
        // ════════════════════════════════════════════════════

        [Test]
        public void GetWorldCancellationToken_LiveWorld_TokenNotCanceled()
        {
            var world = new World("TestLiveWorld");
            try
            {
                var token = world.GetWorldCancellationToken();

                Assert.IsFalse(token.IsCancellationRequested);
            }
            finally
            {
                world.Dispose();
            }
        }

        // ════════════════════════════════════════════════════
        //  Destroy world + advance frame → token cancels
        // ════════════════════════════════════════════════════

        [Test]
        public void GetWorldCancellationToken_DestroyWorld_AdvanceFrame_TokenCancels()
        {
            var world = new World("TestCancelWorld");
            var token = world.GetWorldCancellationToken();

            Assert.IsFalse(token.IsCancellationRequested);

            world.Dispose();

            // Advance frames to allow the polling task to detect the destruction
            for (int i = 0; i < 10 && !token.IsCancellationRequested; i++)
                clock.AdvanceFrame();

            Assert.IsTrue(token.IsCancellationRequested);
        }

        // ════════════════════════════════════════════════════
        //  SafeEntityExists: valid entity → true
        // ════════════════════════════════════════════════════

        [Test]
        public void SafeEntityExists_ValidEntity_ReturnsTrue()
        {
            var world = new World("TestEntityWorld");
            try
            {
                var entity = world.EntityManager.CreateEntity();

                Assert.IsTrue(world.EntityManager.SafeEntityExists(entity));
            }
            finally
            {
                world.Dispose();
            }
        }

        // ════════════════════════════════════════════════════
        //  SafeEntityExists: destroyed entity → false
        // ════════════════════════════════════════════════════

        [Test]
        public void SafeEntityExists_DestroyedEntity_ReturnsFalse()
        {
            var world = new World("TestDestroyedEntityWorld");
            try
            {
                var entity = world.EntityManager.CreateEntity();
                world.EntityManager.DestroyEntity(entity);

                Assert.IsFalse(world.EntityManager.SafeEntityExists(entity));
            }
            finally
            {
                world.Dispose();
            }
        }
    }
}
