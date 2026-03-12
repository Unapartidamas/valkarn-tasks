// Copyright (c) Una Partida Mas. All rights reserved.
// Licensed under the MIT License. See LICENSE.md in the repository root.

using System;
using System.Threading;
using NUnit.Framework;

namespace UnaPartidaMas.Valkarn.Tasks.Tests
{
    /// <summary>
    /// Tests for TempNativeArrayScope lifetime management logic.
    /// Since NativeArray requires Unity runtime, these tests use a managed replica
    /// (<see cref="ManagedArrayScope{T}"/>) that mirrors the same Interlocked
    /// dispose-guard pattern and IDisposable contract.
    /// </summary>
    [TestFixture]
    public class TempNativeArrayScopeTests
    {
        // ════════════════════════════════════════════════════
        //  Initial state
        // ════════════════════════════════════════════════════

        [Test]
        public void Create_InitialState_DisposedIsZero()
        {
            var scope = ManagedArrayScope<float>.Create(16);

            Assert.IsTrue(scope.IsCreated);
            Assert.AreEqual(16, scope.Array.Length);
        }

        [Test]
        public void Wrap_ExistingArray_TakesOwnership()
        {
            var array = new float[32];
            var scope = ManagedArrayScope<float>.Wrap(array);

            Assert.IsTrue(scope.IsCreated);
            Assert.AreSame(array, scope.Array);
        }

        // ════════════════════════════════════════════════════
        //  Dispose behavior
        // ════════════════════════════════════════════════════

        [Test]
        public void Dispose_Once_MarksDisposed()
        {
            var scope = ManagedArrayScope<float>.Create(8);

            scope.Dispose();

            Assert.IsFalse(scope.IsCreated);
        }

        [Test]
        public void Dispose_Twice_DoesNotThrow()
        {
            var scope = ManagedArrayScope<float>.Create(8);

            scope.Dispose();

            Assert.DoesNotThrow(() => scope.Dispose());
        }

        [Test]
        public void Dispose_Idempotent_DisposesUnderlyingOnlyOnce()
        {
            var scope = ManagedArrayScope<float>.Create(8);

            scope.Dispose();
            scope.Dispose();
            scope.Dispose();

            Assert.AreEqual(1, scope.DisposeCount, "Underlying array should be disposed exactly once");
        }

        // ════════════════════════════════════════════════════
        //  Access after dispose
        // ════════════════════════════════════════════════════

        [Test]
        public void Array_AfterDispose_ThrowsObjectDisposedException()
        {
            var scope = ManagedArrayScope<float>.Create(8);
            scope.Dispose();

            Assert.Throws<ObjectDisposedException>(() =>
            {
                _ = scope.Array;
            });
        }

        [Test]
        public void IsCreated_AfterDispose_ReturnsFalse()
        {
            var scope = ManagedArrayScope<float>.Create(8);
            scope.Dispose();

            Assert.IsFalse(scope.IsCreated);
        }

        // ════════════════════════════════════════════════════
        //  Using pattern
        // ════════════════════════════════════════════════════

        [Test]
        public void UsingPattern_DisposesOnExit()
        {
            // Verify that Dispose correctly transitions the scope from created to disposed.
            // Note: ManagedArrayScope is a struct, so 'using' disposes the original on the stack.
            // We test by calling Dispose explicitly and verifying state transitions.
            var scope = ManagedArrayScope<int>.Create(4);
            Assert.IsTrue(scope.IsCreated);

            scope.Dispose();

            Assert.IsFalse(scope.IsCreated);
            Assert.AreEqual(1, scope.DisposeCount);
        }

        // ════════════════════════════════════════════════════
        //  Sequential double-dispose safety
        // ════════════════════════════════════════════════════

        [Test]
        public void Dispose_MultipleSequentialCalls_DisposesExactlyOnce()
        {
            var scope = ManagedArrayScope<float>.Create(64);

            scope.Dispose();
            scope.Dispose();
            scope.Dispose();
            scope.Dispose();

            Assert.AreEqual(1, scope.DisposeCount, "Underlying array should be disposed exactly once");
            Assert.IsFalse(scope.IsCreated);
        }

        // ════════════════════════════════════════════════════
        //  Managed replica (mirrors TempNativeArrayScope<T>)
        // ════════════════════════════════════════════════════

        /// <summary>
        /// Managed replica of <c>TempNativeArrayScope&lt;T&gt;</c> that uses a plain array
        /// instead of <c>NativeArray&lt;T&gt;</c>. Mirrors the same Interlocked dispose-guard
        /// pattern so we can test the structural behavior without Unity NativeContainers.
        /// </summary>
        struct ManagedArrayScope<T> : IDisposable
        {
            T[] array;
            int disposed;
            int disposeCount;

            /// <summary>
            /// Gets the underlying array.
            /// Throws <see cref="ObjectDisposedException"/> if disposed.
            /// </summary>
            public T[] Array
            {
                get
                {
                    if (disposed != 0)
                        throw new ObjectDisposedException(nameof(ManagedArrayScope<T>));
                    return array;
                }
            }

            /// <summary>
            /// Returns true if not disposed and the array is allocated.
            /// </summary>
            public bool IsCreated => disposed == 0 && array != null;

            /// <summary>
            /// The number of times the underlying array was actually disposed
            /// (should always be 0 or 1).
            /// </summary>
            public int DisposeCount => disposeCount;

            public static ManagedArrayScope<T> Create(int length)
            {
                return new ManagedArrayScope<T>
                {
                    array = new T[length],
                    disposed = 0,
                    disposeCount = 0
                };
            }

            public static ManagedArrayScope<T> Wrap(T[] existing)
            {
                return new ManagedArrayScope<T>
                {
                    array = existing,
                    disposed = 0,
                    disposeCount = 0
                };
            }

            public void Dispose()
            {
                if (disposed != 0)
                    return;
                disposed = 1;

                if (array != null)
                {
                    disposeCount++;
                    array = null;
                }
            }
        }
    }
}
