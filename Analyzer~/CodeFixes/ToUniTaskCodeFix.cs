// Copyright (c) 2025 Una Partida Mas. All rights reserved.
// Licensed under the Valkarn Tasks License. See LICENSE.md in the repository root.

using System.Collections.Immutable;
using System.Composition;
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
    /// Code fix for MIG013: Removes .ToUniTask() / .AsUniTask() calls.
    /// The receiver expression is kept since ValkarnTask bridges natively.
    /// </summary>
    [ExportCodeFixProvider(LanguageNames.CSharp), Shared]
    public sealed class ToUniTaskCodeFix : CodeFixProvider
    {
        public override ImmutableArray<string> FixableDiagnosticIds
            => ImmutableArray.Create("MIG013");

        public override FixAllProvider GetFixAllProvider()
            => WellKnownFixAllProviders.BatchFixer;

        public override async Task RegisterCodeFixesAsync(CodeFixContext context)
        {
            var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken);
            var diagnostic = context.Diagnostics[0];
            var span = diagnostic.Location.SourceSpan;
            var node = root.FindNode(span);

            if (node is InvocationExpressionSyntax invocation
                && invocation.Expression is MemberAccessExpressionSyntax memberAccess)
            {
                context.RegisterCodeFix(
                    CodeAction.Create(
                        "Remove conversion call",
                        ct => RemoveConversionAsync(context.Document, invocation, memberAccess, ct),
                        "MIG013_Fix"),
                    diagnostic);
            }
        }

        static async Task<Document> RemoveConversionAsync(
            Document document,
            InvocationExpressionSyntax invocation,
            MemberAccessExpressionSyntax memberAccess,
            CancellationToken ct)
        {
            var root = await document.GetSyntaxRootAsync(ct);

            // task.ToUniTask() → task
            var receiver = memberAccess.Expression.WithTriviaFrom(invocation);
            var newRoot = root.ReplaceNode(invocation, receiver);
            return document.WithSyntaxRoot(newRoot);
        }
    }
}
