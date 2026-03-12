// Copyright (c) 2025 Una Partida Mas. All rights reserved.
// Licensed under the Valkarn Tasks License. See LICENSE.md in the repository root.

using System.Collections.Immutable;
using System.Composition;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace UnaPartidaMas.Valkarn.Tasks.Analyzer.CodeFixes
{
    /// <summary>
    /// Code fix for MIG006: Replaces <c>using Cysharp.Threading.Tasks;</c>
    /// with <c>using UnaPartidaMas.Valkarn.Tasks;</c>.
    /// </summary>
    [ExportCodeFixProvider(LanguageNames.CSharp), Shared]
    public sealed class PlayerLoopTimingCodeFix : CodeFixProvider
    {
        public override ImmutableArray<string> FixableDiagnosticIds
            => ImmutableArray.Create("MIG006");

        public override FixAllProvider GetFixAllProvider()
            => WellKnownFixAllProviders.BatchFixer;

        public override async Task RegisterCodeFixesAsync(CodeFixContext context)
        {
            var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken);
            var diagnostic = context.Diagnostics[0];

            context.RegisterCodeFix(
                CodeAction.Create(
                    "Change namespace to UnaPartidaMas.Valkarn.Tasks",
                    ct => ReplaceNamespaceAsync(context.Document, ct),
                    "MIG006_Fix"),
                diagnostic);
        }

        static async Task<Document> ReplaceNamespaceAsync(Document document, CancellationToken ct)
        {
            var root = await document.GetSyntaxRootAsync(ct);
            if (root == null) return document;

            var compilationUnit = root as CompilationUnitSyntax;
            if (compilationUnit == null) return document;

            // Find `using Cysharp.Threading.Tasks;`
            var uniTaskUsing = compilationUnit.Usings.FirstOrDefault(u =>
                u.Name?.ToString() == "Cysharp.Threading.Tasks");

            if (uniTaskUsing == null) return document;

            // Check if `using UnaPartidaMas.Valkarn.Tasks;` already exists
            bool hasValkarnUsing = compilationUnit.Usings.Any(u =>
                u.Name?.ToString() == "UnaPartidaMas.Valkarn.Tasks");

            SyntaxNode newRoot;
            if (hasValkarnUsing)
            {
                // Just remove the UniTask using
                newRoot = root.RemoveNode(uniTaskUsing, SyntaxRemoveOptions.KeepLeadingTrivia);
            }
            else
            {
                // Replace with Valkarn using
                var valkarnUsing = uniTaskUsing.WithName(
                    SyntaxFactory.ParseName("UnaPartidaMas.Valkarn.Tasks"))
                    .WithTriviaFrom(uniTaskUsing);
                newRoot = root.ReplaceNode(uniTaskUsing, valkarnUsing);
            }

            return document.WithSyntaxRoot(newRoot);
        }
    }
}
