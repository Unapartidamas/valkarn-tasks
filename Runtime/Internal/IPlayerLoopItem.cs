// Copyright (c) Una Partida Mas. All rights reserved.
// Licensed under the MIT License. See LICENSE.md in the repository root.

namespace UnaPartidaMas.Valkarn.Tasks
{
    /// <summary>
    /// Interface for recurring items executed each PlayerLoop tick.
    /// Return true to continue running; false to remove from the runner.
    /// Used by: DelayFrame, WaitUntil, WaitWhile, JobPromise, etc.
    /// </summary>
    internal interface IPlayerLoopItem
    {
        bool MoveNext();
    }
}
