// Copyright (c) Una Partida Mas. All rights reserved.
// Licensed under the MIT License. See LICENSE.md in the repository root.

using System.Runtime.InteropServices;

namespace UnaPartidaMas.Valkarn.Tasks
{
    /// <summary>
    /// Unit type for void-returning async methods.
    /// Used as TResult in ValkarnTaskCompletionCore{AsyncUnit} for non-generic ValkarnTask.
    /// </summary>
    [StructLayout(LayoutKind.Sequential, Size = 1)]
    internal readonly struct AsyncUnit
    {
        internal static readonly AsyncUnit Default = default;
    }
}
