// Copyright (c) Una Partida Mas. All rights reserved.
// Licensed under the MIT License. See LICENSE.md in the repository root.

namespace UnaPartidaMas.Valkarn.Tasks
{
    /// <summary>
    /// Write side of a channel. Supports non-blocking and async writes.
    /// </summary>
    public abstract class ChannelWriter<T>
    {
        /// <summary>
        /// Attempts to write an item without waiting.
        /// Returns false if the channel is full (bounded) or completed.
        /// </summary>
        public abstract bool TryWrite(T item);

        /// <summary>
        /// Writes an item to the channel, waiting asynchronously if necessary.
        /// For unbounded channels, completes immediately.
        /// For bounded channels, waits until space is available.
        /// Throws <see cref="ChannelClosedException"/> if the channel is completed.
        /// </summary>
        public abstract ValkarnTask WriteAsync(T item);

        /// <summary>
        /// Signals that no more items will be written.
        /// Pending readers are notified. Remaining items can still be consumed.
        /// </summary>
        public abstract void Complete();
    }
}
