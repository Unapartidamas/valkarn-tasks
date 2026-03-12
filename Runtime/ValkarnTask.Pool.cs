// Copyright (c) Una Partida Mas. All rights reserved.
// Licensed under the MIT License. See LICENSE.md in the repository root.

using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace UnaPartidaMas.Valkarn.Tasks
{
    public readonly partial struct ValkarnTask
    {
        /// <summary>
        /// Returns pool information for all active ValkarnPool instances.
        /// Each entry is (pooled object type, current size, max size).
        /// </summary>
        public static IEnumerable<(Type type, int size, int maxSize)> GetPoolInfo()
        {
            // This is a monitoring API — not performance-critical.
            // Currently provides info from all known pool types.
            // Pool types register themselves at construction time.
            // Since pools are per-generic-type and use static fields,
            // we'd need a global registry. For now, return empty.
            // Full implementation requires a PoolRegistry (added below).
            return PoolRegistry.GetAll();
        }
    }

    /// <summary>
    /// Global registry for pool monitoring. Each ValkarnPool{T} registers itself here.
    /// </summary>
    internal static class PoolRegistry
    {
        static readonly List<IPoolInfo> s_pools = new();
        static readonly object s_lock = new();

        internal static void Register(IPoolInfo pool)
        {
            lock (s_lock) { s_pools.Add(pool); }
        }

        internal static void Clear()
        {
            lock (s_lock) { s_pools.Clear(); }
        }

        internal static List<(Type type, int size, int maxSize)> GetAll()
        {
            var results = new List<(Type, int, int)>();
            lock (s_lock)
            {
                for (int i = s_pools.Count - 1; i >= 0; i--)
                {
                    var p = s_pools[i];
                    if (p.IsAlive)
                        results.Add((p.PooledType, p.Size, p.MaxSize));
                    else
                        s_pools.RemoveAt(i);
                }
            }
            return results;
        }

        /// <summary>
        /// Called by PlayerLoopHelper every TrimCheckInterval frames.
        /// Trims all registered pools.
        /// </summary>
        internal static void TrimAll(int minPoolSize)
        {
            lock (s_lock)
            {
                for (int i = s_pools.Count - 1; i >= 0; i--)
                {
                    var p = s_pools[i];
                    if (p.IsAlive)
                        p.Trim(minPoolSize);
                    else
                        s_pools.RemoveAt(i);
                }
            }
        }
    }

    internal interface IPoolInfo
    {
        Type PooledType { get; }
        int Size { get; }
        int MaxSize { get; }
        bool IsAlive { get; }
        int Trim(int minPoolSize);
    }
}
