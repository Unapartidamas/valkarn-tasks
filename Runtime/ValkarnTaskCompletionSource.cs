// Copyright (c) Una Partida Mas. All rights reserved.
// Licensed under the MIT License. See LICENSE.md in the repository root.

using System;

namespace UnaPartidaMas.Valkarn.Tasks
{
    /// <summary>
    /// Manual completion source for ValkarnTask.
    /// Wraps ValkarnTask.Promise for a familiar TaskCompletionSource-like API.
    /// </summary>
    public class ValkarnTaskCompletionSource<T>
    {
        private readonly ValkarnTask.Promise<T> _promise = new ValkarnTask.Promise<T>();

        public ValkarnTask<T> Task => _promise.Task;
        public bool TrySetResult(T result) => _promise.TrySetResult(result);
        public bool TrySetException(Exception ex)
        {
            if (ex == null) throw new ArgumentNullException(nameof(ex));
            return _promise.TrySetException(ex);
        }
        public bool TrySetCanceled() => _promise.TrySetCanceled();
    }
}
