// Copyright (c) Una Partida Mas. All rights reserved.
// Licensed under the MIT License. See LICENSE.md in the repository root.

using System;

namespace UnaPartidaMas.Valkarn.Tasks
{
    public readonly partial struct ValkarnTask
    {
        /// <summary>
        /// Source interface for ValkarnTask. Implemented by runners, promises, and combinators.
        /// Similar to IValueTaskSource but with generational uint token for ABA-safe validation.
        /// </summary>
        public interface ISource
        {
            /// <summary>Gets the status of the operation identified by the token.</summary>
            ValkarnTask.Status GetStatus(uint token);

            /// <summary>Gets the result. Throws if faulted/canceled. May return to pool.</summary>
            void GetResult(uint token);

            /// <summary>
            /// Registers a continuation to be invoked when the operation completes.
            /// Only one continuation per token is supported.
            /// </summary>
            void OnCompleted(Action<object> continuation, object state, uint token);

            /// <summary>Gets status without token validation (for internal/diagnostic use).</summary>
            ValkarnTask.Status UnsafeGetStatus();
        }

        /// <summary>
        /// Generic source interface for ValkarnTask{T}. Returns a typed result.
        /// </summary>
        public interface ISource<out T> : ISource
        {
            /// <summary>Gets the typed result. Throws if faulted/canceled. May return to pool.</summary>
            new T GetResult(uint token);
        }
    }
}
