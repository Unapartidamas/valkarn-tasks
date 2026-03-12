// Copyright (c) Una Partida Mas. All rights reserved.
// Licensed under the MIT License. See LICENSE.md in the repository root.

namespace UnaPartidaMas.Valkarn.Tasks
{
    /// <summary>
    /// A channel for passing data between producers and consumers.
    /// </summary>
    public sealed class Channel<T>
    {
        /// <summary>The read side of the channel.</summary>
        public ChannelReader<T> Reader { get; }

        /// <summary>The write side of the channel.</summary>
        public ChannelWriter<T> Writer { get; }

        internal Channel(ChannelReader<T> reader, ChannelWriter<T> writer)
        {
            Reader = reader;
            Writer = writer;
        }
    }
}
