// Copyright (c) Una Partida Mas. All rights reserved.
// Licensed under the MIT License. See LICENSE.md in the repository root.

using System;

namespace UnaPartidaMas.Valkarn.Tasks
{
    /// <summary>
    /// Executes recurring IPlayerLoopItem instances each PlayerLoop tick.
    /// Array-based with in-place compaction preserving insertion order.
    /// Items added during Run are processed next tick.
    /// </summary>
    internal sealed class PlayerLoopRunner
    {
        const int InitialCapacity = 16;

        IPlayerLoopItem[] items;
        int count;
        readonly object gate = new();

        internal PlayerLoopRunner()
        {
            items = new IPlayerLoopItem[InitialCapacity];
        }

        internal void Add(IPlayerLoopItem item)
        {
            lock (gate)
            {
                if (count == items.Length)
                    Array.Resize(ref items, items.Length * 2);

                items[count++] = item;
            }
        }

        internal void Run()
        {
            // Snapshot current count AND array reference — items added during Run
            // may trigger Array.Resize on a different thread, so we must iterate
            // a stable reference to the current array.
            int snapshot;
            IPlayerLoopItem[] localItems;
            lock (gate) { snapshot = count; localItems = items; }

            // In-place compaction: write index tracks where the next live item goes.
            // This preserves insertion order (stable).
            int writeIndex = 0;
            for (int i = 0; i < snapshot; i++)
            {
                var item = localItems[i];
                bool keepRunning;
                try
                {
                    keepRunning = item.MoveNext();
                }
                catch (Exception ex)
                {
                    keepRunning = false;
                    ValkarnTask.PublishUnobservedException(ex);
                }

                if (keepRunning)
                {
                    if (writeIndex != i)
                        localItems[writeIndex] = item;
                    writeIndex++;
                }
            }

            // Merge compacted results back under lock
            lock (gate)
            {
                int removed = snapshot - writeIndex;
                if (removed > 0)
                {
                    // If Array.Resize occurred during Run(), localItems is the old array.
                    // Compacted items at [0..writeIndex) exist only in localItems —
                    // copy them to the current (resized) array.
                    if (items != localItems)
                        Array.Copy(localItems, 0, items, 0, writeIndex);

                    // Move items added during Run down to fill the gap
                    int newItemsAdded = count - snapshot;
                    if (newItemsAdded > 0)
                        Array.Copy(items, snapshot, items, writeIndex, newItemsAdded);

                    int oldCount = count;
                    count = writeIndex + newItemsAdded;

                    // Clear stale references in tail to avoid GC rooting
                    for (int j = count; j < oldCount; j++)
                        items[j] = null;
                }
            }
        }
    }
}
