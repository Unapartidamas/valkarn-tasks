// Copyright (c) Una Partida Mas. All rights reserved.
// Licensed under the MIT License. See LICENSE.md in the repository root.

namespace UnaPartidaMas.Valkarn.Tasks
{
    /// <summary>
    /// Interface for objects that participate in ValkarnTaskPool's intrusive linked list.
    /// Each pooled object stores its own "next" pointer, eliminating external node allocation.
    /// </summary>
    internal interface IPoolNode<T> where T : class
    {
        ref T NextNode { get; }
    }
}
