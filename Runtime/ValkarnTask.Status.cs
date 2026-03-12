// Copyright (c) Una Partida Mas. All rights reserved.
// Licensed under the MIT License. See LICENSE.md in the repository root.

namespace UnaPartidaMas.Valkarn.Tasks
{
    public readonly partial struct ValkarnTask
    {
        /// <summary>
        /// Represents the status of a ValkarnTask.
        /// </summary>
        public enum Status : byte
        {
            /// <summary>The task has not yet completed.</summary>
            Pending = 0,

            /// <summary>The task completed successfully.</summary>
            Succeeded = 1,

            /// <summary>The task completed with an unhandled exception.</summary>
            Faulted = 2,

            /// <summary>The task was canceled.</summary>
            Canceled = 3
        }
    }

    internal static class ValkarnTaskStatusExtensions
    {
        internal static bool IsCompleted(this ValkarnTask.Status status)
            => status != ValkarnTask.Status.Pending;
    }
}
