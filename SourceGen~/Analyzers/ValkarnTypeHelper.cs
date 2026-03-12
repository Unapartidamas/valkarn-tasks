// Copyright (c) 2025 Una Partida Mas. All rights reserved.
// Licensed under the Valkarn Tasks License. See LICENSE.md in the repository root.

using Microsoft.CodeAnalysis;

namespace UnaPartidaMas.Valkarn.Tasks.SourceGen.Analyzers
{
    internal static class ValkarnTypeHelper
    {
        const string Namespace = "UnaPartidaMas.Valkarn.Tasks";

        internal static bool IsValkarnTask(ITypeSymbol type)
        {
            if (type == null) return false;
            if (type is INamedTypeSymbol { IsGenericType: true }) return false;
            return type.Name == "ValkarnTask" && IsInValkarnNamespace(type);
        }

        internal static bool IsValkarnTaskT(ITypeSymbol type)
        {
            if (type is INamedTypeSymbol { IsGenericType: true } named)
                return named.Name == "ValkarnTask" && IsInValkarnNamespace(named);
            return false;
        }

        internal static bool IsAnyValkarnTask(ITypeSymbol type)
            => IsValkarnTask(type) || IsValkarnTaskT(type);

        static bool IsInValkarnNamespace(ITypeSymbol type)
            => type.ContainingNamespace?.ToDisplayString() == Namespace;
    }
}
