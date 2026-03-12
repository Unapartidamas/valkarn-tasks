// Copyright (c) Una Partida Mas. All rights reserved.
// Licensed under the MIT License. See LICENSE.md in the repository root.

#if UNITY_5_3_OR_NEWER
using System;
using System.Runtime.CompilerServices;
using System.Threading;
using Unity.Collections;

namespace UnaPartidaMas.Valkarn.Tasks.Bridge
{
    /// <summary>
    /// Lightweight struct wrapper that ensures a <see cref="NativeArray{T}"/> allocated with
    /// <see cref="Allocator.TempJob"/> is disposed after the async operation completes.
    /// Uses an explicit scope pattern (using/IDisposable) rather than magic auto-dispose,
    /// which risks double-dispose.
    /// <code>
    /// using var scope = TempNativeArrayScope.Create&lt;float&gt;(1024);
    /// NativeArray&lt;float&gt; array = scope.Array;
    /// // ... schedule job with array ...
    /// await handle.ToValkarnTask();
    /// // scope.Dispose() runs here -- disposes the NativeArray
    /// </code>
    /// </summary>
    /// <typeparam name="T">The unmanaged element type of the native array.</typeparam>
    public struct TempNativeArrayScope<T> : IDisposable where T : struct
    {
        NativeArray<T> array;
        int disposed;

        /// <summary>
        /// Gets the underlying <see cref="NativeArray{T}"/>.
        /// </summary>
        /// <exception cref="ObjectDisposedException">Thrown if the scope has been disposed.</exception>
        public NativeArray<T> Array
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get
            {
                if (disposed != 0)
                    throw new ObjectDisposedException(nameof(TempNativeArrayScope<T>));
                return array;
            }
        }

        /// <summary>
        /// Returns <c>true</c> if the scope has not been disposed and the underlying array is created.
        /// </summary>
        public bool IsCreated
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => disposed == 0 && array.IsCreated;
        }

        /// <summary>
        /// Allocates a new <see cref="NativeArray{T}"/> with <see cref="Allocator.TempJob"/>
        /// and wraps it in a scope for automatic disposal.
        /// </summary>
        /// <param name="length">The number of elements to allocate.</param>
        /// <returns>A new <see cref="TempNativeArrayScope{T}"/> owning the allocated array.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static TempNativeArrayScope<T> Create(int length)
        {
            return new TempNativeArrayScope<T>
            {
                array = new NativeArray<T>(length, Allocator.TempJob),
                disposed = 0
            };
        }

        /// <summary>
        /// Wraps an already-allocated <see cref="NativeArray{T}"/>, taking ownership.
        /// The array will be disposed when this scope is disposed.
        /// </summary>
        /// <param name="existing">The existing native array to take ownership of.</param>
        /// <returns>A new <see cref="TempNativeArrayScope{T}"/> owning the provided array.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static TempNativeArrayScope<T> Wrap(NativeArray<T> existing)
        {
            return new TempNativeArrayScope<T>
            {
                array = existing,
                disposed = 0
            };
        }

        /// <summary>
        /// Disposes the underlying <see cref="NativeArray{T}"/> if it is still created.
        /// Idempotent: calling <see cref="Dispose"/> multiple times is safe.
        /// Uses plain write (not Interlocked) because this scope is designed for
        /// single-thread use via <c>using var</c> on the main thread.
        /// </summary>
        public void Dispose()
        {
            if (disposed != 0)
                return;
            disposed = 1;

            if (array.IsCreated)
                array.Dispose();
        }
    }

    /// <summary>
    /// Non-generic static helper class providing convenient generic factory methods
    /// for <see cref="TempNativeArrayScope{T}"/>.
    /// </summary>
    public static class TempNativeArrayScope
    {
        /// <summary>
        /// Allocates a new <see cref="NativeArray{T}"/> with <see cref="Allocator.TempJob"/>
        /// and wraps it in a scope for automatic disposal.
        /// </summary>
        /// <typeparam name="T">The unmanaged element type of the native array.</typeparam>
        /// <param name="length">The number of elements to allocate.</param>
        /// <returns>A new <see cref="TempNativeArrayScope{T}"/> owning the allocated array.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static TempNativeArrayScope<T> Create<T>(int length) where T : struct
            => TempNativeArrayScope<T>.Create(length);

        /// <summary>
        /// Wraps an already-allocated <see cref="NativeArray{T}"/>, taking ownership.
        /// The array will be disposed when this scope is disposed.
        /// </summary>
        /// <typeparam name="T">The unmanaged element type of the native array.</typeparam>
        /// <param name="existing">The existing native array to take ownership of.</param>
        /// <returns>A new <see cref="TempNativeArrayScope{T}"/> owning the provided array.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static TempNativeArrayScope<T> Wrap<T>(NativeArray<T> existing) where T : struct
            => TempNativeArrayScope<T>.Wrap(existing);
    }
}
#endif
