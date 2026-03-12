// Copyright (c) Una Partida Mas. All rights reserved.
// Licensed under the MIT License. See LICENSE.md in the repository root.

using System;

namespace UnaPartidaMas.Valkarn.Tasks
{
    /// <summary>
    /// Marks an async ValkarnTask method as intentionally fire-and-forget.
    /// Suppresses VTASKS-TASK002/VTASKS-TASK013 warnings for callers that don't await.
    /// The source generator wraps the method body to catch and publish unobserved exceptions.
    /// </summary>
    [AttributeUsage(AttributeTargets.Method, Inherited = false, AllowMultiple = false)]
    public sealed class FireAndForgetAttribute : Attribute { }
}
