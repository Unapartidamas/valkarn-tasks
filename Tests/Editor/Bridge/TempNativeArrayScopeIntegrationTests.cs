// Copyright (c) Una Partida Mas. All rights reserved.
// Licensed under the MIT License. See LICENSE.md in the repository root.

using System;
using NUnit.Framework;
using Unity.Collections;
using UnaPartidaMas.Valkarn.Tasks.Bridge;

namespace UnaPartidaMas.Valkarn.Tasks.Tests.Bridge
{
    /// <summary>
    /// Integration tests for <see cref="TempNativeArrayScope{T}"/>
    /// exercising real Unity <see cref="NativeArray{T}"/>.
    /// </summary>
    [TestFixture]
    public class TempNativeArrayScopeIntegrationTests
    {
        // ════════════════════════════════════════════════════
        //  Create — initial state
        // ════════════════════════════════════════════════════

        [Test]
        public void Create_IsCreatedTrue_LengthMatches()
        {
            var scope = TempNativeArrayScope<float>.Create(32);
            try
            {
                Assert.IsTrue(scope.IsCreated);
                Assert.AreEqual(32, scope.Array.Length);
            }
            finally
            {
                scope.Dispose();
            }
        }

        // ════════════════════════════════════════════════════
        //  Wrap — takes ownership of existing array
        // ════════════════════════════════════════════════════

        [Test]
        public void Wrap_TakesOwnership()
        {
            var array = new NativeArray<int>(16, Allocator.TempJob);
            var scope = TempNativeArrayScope<int>.Wrap(array);
            try
            {
                Assert.IsTrue(scope.IsCreated);
                Assert.AreEqual(16, scope.Array.Length);
            }
            finally
            {
                scope.Dispose();
            }
        }

        // ════════════════════════════════════════════════════
        //  Dispose — marks as not created
        // ════════════════════════════════════════════════════

        [Test]
        public void Dispose_IsCreatedFalse_NativeArrayDisposed()
        {
            var scope = TempNativeArrayScope<float>.Create(8);
            scope.Dispose();

            Assert.IsFalse(scope.IsCreated);
        }

        // ════════════════════════════════════════════════════
        //  Double dispose — no crash
        // ════════════════════════════════════════════════════

        [Test]
        public void DoubleDispose_DoesNotThrow()
        {
            var scope = TempNativeArrayScope<float>.Create(8);
            scope.Dispose();

            Assert.DoesNotThrow(() => scope.Dispose());
        }

        // ════════════════════════════════════════════════════
        //  Array after dispose → ObjectDisposedException
        // ════════════════════════════════════════════════════

        [Test]
        public void Array_AfterDispose_ThrowsObjectDisposedException()
        {
            var scope = TempNativeArrayScope<float>.Create(8);
            scope.Dispose();

            Assert.Throws<ObjectDisposedException>(() =>
            {
                _ = scope.Array;
            });
        }

        // ════════════════════════════════════════════════════
        //  Read/write data through scope
        // ════════════════════════════════════════════════════

        [Test]
        public void ReadWriteData_ThroughScope()
        {
            var scope = TempNativeArrayScope<int>.Create(4);
            try
            {
                var array = scope.Array;
                array[0] = 10;
                array[1] = 20;
                array[2] = 30;
                array[3] = 40;

                Assert.AreEqual(10, scope.Array[0]);
                Assert.AreEqual(20, scope.Array[1]);
                Assert.AreEqual(30, scope.Array[2]);
                Assert.AreEqual(40, scope.Array[3]);
            }
            finally
            {
                scope.Dispose();
            }
        }

        // ════════════════════════════════════════════════════
        //  Non-generic helper Create<T>()
        // ════════════════════════════════════════════════════

        [Test]
        public void NonGenericHelper_Create_Works()
        {
            var scope = TempNativeArrayScope.Create<float>(16);
            try
            {
                Assert.IsTrue(scope.IsCreated);
                Assert.AreEqual(16, scope.Array.Length);
            }
            finally
            {
                scope.Dispose();
            }
        }
    }
}
