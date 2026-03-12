// Copyright (c) Una Partida Mas. All rights reserved.
// Licensed under the MIT License. See LICENSE.md in the repository root.

using System;

namespace UnaPartidaMas.Valkarn.Tasks
{
    /// <summary>
    /// Opt-out of automatic lifecycle cancellation for async ValkarnTask methods
    /// in MonoBehaviour or ScriptableObject. The developer takes full responsibility
    /// for cancellation management.
    /// </summary>
    [AttributeUsage(AttributeTargets.Method, Inherited = false, AllowMultiple = false)]
    public sealed class NoAutoCancelAttribute : Attribute { }
}
