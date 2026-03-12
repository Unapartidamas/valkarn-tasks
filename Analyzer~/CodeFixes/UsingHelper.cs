// Copyright (c) 2025 Una Partida Mas. All rights reserved.
// Licensed under the Valkarn Tasks License. See LICENSE.md in the repository root.

using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace UnaPartidaMas.Valkarn.Tasks.Analyzer.CodeFixes
{
    /// <summary>
    /// Shared helper for code fixes that introduce ValkarnTask-namespace types.
    /// Ensures <c>using UnaPartidaMas.Valkarn.Tasks;</c> is present in the document.
    /// </summary>
    internal static class UsingHelper
    {
        const string ValkarnNamespace = "UnaPartidaMas.Valkarn.Tasks";

        /// <summary>
        /// Adds <c>using UnaPartidaMas.Valkarn.Tasks;</c> if it is not already present.
        /// Returns the (possibly modified) document.
        /// </summary>
        internal static async Task<Document> EnsureValkarnUsingAsync(Document document, CancellationToken ct)
        {
            var root = await document.GetSyntaxRootAsync(ct);
            var compilationUnit = root as CompilationUnitSyntax;
            if (compilationUnit == null) return document;

            if (compilationUnit.Usings.Any(u => u.Name?.ToString() == ValkarnNamespace))
                return document;

            var valkarnUsing = SyntaxFactory.UsingDirective(
                    SyntaxFactory.ParseName(ValkarnNamespace))
                .NormalizeWhitespace()
                .WithTrailingTrivia(SyntaxFactory.CarriageReturnLineFeed);

            var newRoot = compilationUnit.AddUsings(valkarnUsing);
            return document.WithSyntaxRoot(newRoot);
        }
    }
}
