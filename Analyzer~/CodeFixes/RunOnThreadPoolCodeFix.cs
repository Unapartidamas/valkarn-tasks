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
    /// Code fix for MIG009: Replaces UniTask.RunOnThreadPool(func) with
    /// <c>ValkarnTask.RunOnThreadPool(func)</c>, preserving all arguments.
    /// </summary>
    [ExportCodeFixProvider(LanguageNames.CSharp), Shared]
    public sealed class RunOnThreadPoolCodeFix : CodeFixProvider
    {
        public override ImmutableArray<string> FixableDiagnosticIds
            => ImmutableArray.Create("MIG009");

        public override FixAllProvider GetFixAllProvider()
            => WellKnownFixAllProviders.BatchFixer;

        public override async Task RegisterCodeFixesAsync(CodeFixContext context)
        {
            var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken);
            var diagnostic = context.Diagnostics[0];
            var node = root.FindNode(diagnostic.Location.SourceSpan);

            if (node is InvocationExpressionSyntax)
            {
                context.RegisterCodeFix(
                    CodeAction.Create(
                        "Replace with ValkarnTask.RunOnThreadPool",
                        ct => ReplaceAsync(context.Document, node, ct),
                        "MIG009_Fix"),
                    diagnostic);
            }
        }

        static async Task<Document> ReplaceAsync(Document document, SyntaxNode node, CancellationToken ct)
        {
            var root = await document.GetSyntaxRootAsync(ct);
            var invocation = (InvocationExpressionSyntax)node;

            // Build ValkarnTask.RunOnThreadPool(...) preserving all original arguments
            var replacement = SyntaxFactory.InvocationExpression(
                    SyntaxFactory.MemberAccessExpression(
                        SyntaxKind.SimpleMemberAccessExpression,
                        SyntaxFactory.IdentifierName("ValkarnTask"),
                        SyntaxFactory.IdentifierName("RunOnThreadPool")),
                    invocation.ArgumentList)
                .WithTriviaFrom(node);

            var newRoot = root.ReplaceNode(node, replacement);
            return await UsingHelper.EnsureValkarnUsingAsync(document.WithSyntaxRoot(newRoot), ct);
        }
    }
}
