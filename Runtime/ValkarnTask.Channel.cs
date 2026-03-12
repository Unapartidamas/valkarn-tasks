// Copyright (c) Una Partida Mas. All rights reserved.
// Licensed under the MIT License. See LICENSE.md in the repository root.

namespace UnaPartidaMas.Valkarn.Tasks
{
    public readonly partial struct ValkarnTask
    {
        /// <summary>
        /// Factory for creating channels.
        /// </summary>
        public static class Channel
        {
            /// <summary>
            /// Creates an unbounded channel with no capacity limit.
            /// Items are queued until consumed.
            /// </summary>
            /// <param name="multiConsumer">
            /// If true, multiple concurrent ReadAsync calls are supported (competing consumers).
            /// Default is false (single consumer).
            /// </param>
            public static Channel<T> CreateUnbounded<T>(bool multiConsumer = false)
                => UnboundedChannelImpl<T>.Create(multiConsumer);

            /// <summary>
            /// Creates a bounded channel with the specified capacity.
            /// Writers block when the channel is full (backpressure).
            /// </summary>
            /// <param name="capacity">Maximum number of items the channel can hold.</param>
            /// <param name="multiConsumer">
            /// If true, multiple concurrent ReadAsync calls are supported.
            /// </param>
            public static Channel<T> CreateBounded<T>(int capacity, bool multiConsumer = false)
                => BoundedChannelImpl<T>.Create(capacity, multiConsumer);
        }
    }
}
