// Copyright (c) 2025 Una Partida Mas. All rights reserved.
// Licensed under the Valkarn Tasks License. See LICENSE.md in the repository root.

using System.Collections.Immutable;
using System.Composition;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace UnaPartidaMas.Valkarn.Tasks.Analyzer.CodeFixes
{
    /// <summary>
    /// Code fix for MIG002: Removes the cancelImmediately named argument.
    /// ValkarnTask cancels immediately by default.
    /// </summary>
    [ExportCodeFixProvider(LanguageNames.CSharp), Shared]
    public sealed class CancelImmediatelyCodeFix : CodeFixProvider
    {
        public override ImmutableArray<string> FixableDiagnosticIds
            => ImmutableArray.Create("MIG002");

        public override FixAllProvider GetFixAllProvider()
            => WellKnownFixAllProviders.BatchFixer;

        public override async Task RegisterCodeFixesAsync(CodeFixContext context)
        {
            var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken);
            var diagnostic = context.Diagnostics[0];
            var node = root.FindNode(diagnostic.Location.SourceSpan);

            if (node is ArgumentSyntax)
            {
                context.RegisterCodeFix(
                    CodeAction.Create(
                        "Remove cancelImmediately argument",
                        ct => RemoveArgumentAsync(context.Document, node, ct),
                        "MIG002_Fix"),
                    diagnostic);
            }
        }

        static async Task<Document> RemoveArgumentAsync(Document document, SyntaxNode node, CancellationToken ct)
        {
            var root = await document.GetSyntaxRootAsync(ct);
            var argument = (ArgumentSyntax)node;
            var argList = (ArgumentListSyntax)argument.Parent;

            var newArgs = argList.Arguments.Remove(argument);
            var newArgList = argList.WithArguments(newArgs);
            var newRoot = root.ReplaceNode(argList, newArgList);
            return document.WithSyntaxRoot(newRoot);
        }
    }
}
