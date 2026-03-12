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
    /// Code fix for MIG011: Replaces UniTask.Create(func) with a direct async
    /// ValkarnTask invocation of the factory function.
    /// </summary>
    [ExportCodeFixProvider(LanguageNames.CSharp), Shared]
    public sealed class UniTaskCreateCodeFix : CodeFixProvider
    {
        public override ImmutableArray<string> FixableDiagnosticIds
            => ImmutableArray.Create("MIG011");

        public override FixAllProvider GetFixAllProvider()
            => WellKnownFixAllProviders.BatchFixer;

        public override async Task RegisterCodeFixesAsync(CodeFixContext context)
        {
            var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken);
            var diagnostic = context.Diagnostics[0];
            var node = root.FindNode(diagnostic.Location.SourceSpan);

            if (node is InvocationExpressionSyntax invocation)
            {
                context.RegisterCodeFix(
                    CodeAction.Create(
                        "Replace with direct async call",
                        ct => ReplaceCreateAsync(context.Document, invocation, ct),
                        "MIG011_Fix"),
                    diagnostic);
            }
        }

        static async Task<Document> ReplaceCreateAsync(
            Document document, InvocationExpressionSyntax invocation, CancellationToken ct)
        {
            var root = await document.GetSyntaxRootAsync(ct);

            // UniTask.Create(func) → func()
            // Extract the first argument (the factory function)
            if (invocation.ArgumentList.Arguments.Count > 0)
            {
                var factoryArg = invocation.ArgumentList.Arguments[0].Expression;
                var newExpr = SyntaxFactory.InvocationExpression(
                    factoryArg,
                    SyntaxFactory.ArgumentList())
                    .WithTriviaFrom(invocation);

                var newRoot = root.ReplaceNode(invocation, newExpr);
                return document.WithSyntaxRoot(newRoot);
            }

            return document;
        }
    }
}
