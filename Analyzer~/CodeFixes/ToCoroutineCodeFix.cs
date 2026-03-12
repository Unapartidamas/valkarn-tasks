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
    /// Code fix for MIG010: Removes .ToCoroutine() call — suggests rewriting
    /// the coroutine as an async ValkarnTask method.
    /// </summary>
    [ExportCodeFixProvider(LanguageNames.CSharp), Shared]
    public sealed class ToCoroutineCodeFix : CodeFixProvider
    {
        public override ImmutableArray<string> FixableDiagnosticIds
            => ImmutableArray.Create("MIG010");

        public override FixAllProvider GetFixAllProvider()
            => WellKnownFixAllProviders.BatchFixer;

        public override async Task RegisterCodeFixesAsync(CodeFixContext context)
        {
            var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken);
            var diagnostic = context.Diagnostics[0];
            var node = root.FindNode(diagnostic.Location.SourceSpan);

            if (node is InvocationExpressionSyntax invocation
                && invocation.Expression is MemberAccessExpressionSyntax)
            {
                context.RegisterCodeFix(
                    CodeAction.Create(
                        "Remove .ToCoroutine() call",
                        ct => RemoveToCoroutineAsync(context.Document, invocation, ct),
                        "MIG010_Fix"),
                    diagnostic);
            }
        }

        static async Task<Document> RemoveToCoroutineAsync(
            Document document, InvocationExpressionSyntax invocation, CancellationToken ct)
        {
            var root = await document.GetSyntaxRootAsync(ct);

            // expr.ToCoroutine() → expr
            var memberAccess = (MemberAccessExpressionSyntax)invocation.Expression;
            var receiver = memberAccess.Expression.WithTriviaFrom(invocation);
            var newRoot = root.ReplaceNode(invocation, receiver);
            return document.WithSyntaxRoot(newRoot);
        }
    }
}
