// Copyright (c) Una Partida Mas. All rights reserved.
// Licensed under the MIT License. See LICENSE.md in the repository root.

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace UnaPartidaMas.Valkarn.Tasks
{
    /// <summary>
    /// Read side of a channel. Supports single and multi-consumer patterns.
    /// </summary>
    public abstract class ChannelReader<T>
    {
        /// <summary>
        /// Reads the next item from the channel.
        /// Waits asynchronously if no item is available.
        /// Throws <see cref="ChannelClosedException"/> if the channel is completed and drained.
        /// </summary>
        public abstract ValkarnTask<T> ReadAsync();

        /// <summary>
        /// Attempts to read an item without waiting.
        /// Returns true if an item was available.
        /// </summary>
        public abstract bool TryRead(out T item);

        /// <summary>
        /// A task that completes when the channel is fully drained after the writer calls Complete().
        /// </summary>
        public abstract ValkarnTask Completion { get; }

        /// <summary>
        /// Returns an async enumerable that reads all items until the channel is completed.
        /// </summary>
        public IAsyncEnumerable<T> ReadAllAsync(CancellationToken cancellationToken = default)
            => new ChannelAsyncEnumerable(this, cancellationToken);

        sealed class ChannelAsyncEnumerable : IAsyncEnumerable<T>
        {
            readonly ChannelReader<T> reader;
            readonly CancellationToken ct;

            internal ChannelAsyncEnumerable(ChannelReader<T> reader, CancellationToken ct)
            {
                this.reader = reader;
                this.ct = ct;
            }

            public IAsyncEnumerator<T> GetAsyncEnumerator(CancellationToken cancellationToken = default)
            {
                // Prefer the token passed to GetAsyncEnumerator, fall back to the one from ReadAllAsync
                var token = cancellationToken != default ? cancellationToken : ct;
                return new ChannelAsyncEnumerator(reader, token);
            }
        }

        sealed class ChannelAsyncEnumerator : IAsyncEnumerator<T>
        {
            readonly ChannelReader<T> reader;
            readonly CancellationToken ct;
            T current;

            internal ChannelAsyncEnumerator(ChannelReader<T> reader, CancellationToken ct)
            {
                this.reader = reader;
                this.ct = ct;
            }

            public T Current => current;

            public async ValueTask<bool> MoveNextAsync()
            {
                if (ct.IsCancellationRequested)
                    return false;

                try
                {
                    current = await reader.ReadAsync();
                    return true;
                }
                catch (ChannelClosedException)
                {
                    return false;
                }
            }

            public ValueTask DisposeAsync() => default;
        }
    }
}
