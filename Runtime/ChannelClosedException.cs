// Copyright (c) Una Partida Mas. All rights reserved.
// Licensed under the MIT License. See LICENSE.md in the repository root.

using System;

namespace UnaPartidaMas.Valkarn.Tasks
{
    /// <summary>
    /// Thrown when attempting to read from a channel that has been completed and fully drained.
    /// </summary>
    public sealed class ChannelClosedException : InvalidOperationException
    {
        public ChannelClosedException()
            : base("The channel has been closed.") { }

        public ChannelClosedException(string message)
            : base(message) { }

        public ChannelClosedException(Exception innerException)
            : base("The channel has been closed.", innerException) { }
    }
}
