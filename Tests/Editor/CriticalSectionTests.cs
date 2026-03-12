// Copyright (c) Una Partida Mas. All rights reserved.
// Licensed under the MIT License. See LICENSE.md in the repository root.

using System.Threading.Tasks;
using NUnit.Framework;

namespace UnaPartidaMas.Valkarn.Tasks.Tests
{
    [TestFixture]
    public class CriticalSectionTests
    {
        [SetUp]
        public void SetUp()
        {
            TestHelper.EnsureInitialized();
        }

        [TearDown]
        public void TearDown()
        {
            // Reset depth to 0 by disposing dummy scopes
            // Each DisposeAsync decrements by 1
            while (CriticalSectionScope.Depth > 0)
            {
                // Create a default scope (doesn't increment) and dispose (decrements)
                default(CriticalSectionScope).DisposeAsync().GetAwaiter().GetResult();
            }
        }

        [Test]
        public void IsActive_InitiallyFalse()
        {
            Assert.IsFalse(CriticalSectionScope.IsInCriticalSection);
        }

        [Test]
        public void Critical_SetsIsActive()
        {
            var scope = ValkarnTask.Critical();
            Assert.IsTrue(CriticalSectionScope.IsInCriticalSection);
            Assert.AreEqual(1, CriticalSectionScope.Depth);

            scope.DisposeAsync().GetAwaiter().GetResult();
            Assert.IsFalse(CriticalSectionScope.IsInCriticalSection);
            Assert.AreEqual(0, CriticalSectionScope.Depth);
        }

        [Test]
        public void Critical_Nesting_IncreasesDepth()
        {
            var scope1 = ValkarnTask.Critical();
            Assert.AreEqual(1, CriticalSectionScope.Depth);

            var scope2 = ValkarnTask.Critical();
            Assert.AreEqual(2, CriticalSectionScope.Depth);
            Assert.IsTrue(CriticalSectionScope.IsInCriticalSection);

            scope2.DisposeAsync().GetAwaiter().GetResult();
            Assert.AreEqual(1, CriticalSectionScope.Depth);
            Assert.IsTrue(CriticalSectionScope.IsInCriticalSection);

            scope1.DisposeAsync().GetAwaiter().GetResult();
            Assert.AreEqual(0, CriticalSectionScope.Depth);
            Assert.IsFalse(CriticalSectionScope.IsInCriticalSection);
        }

        [Test]
        public void DisposeAsync_ReturnsDefaultValueTask()
        {
            var scope = ValkarnTask.Critical();
            var vt = scope.DisposeAsync();
            Assert.IsTrue(vt.IsCompleted);
        }
    }
}
