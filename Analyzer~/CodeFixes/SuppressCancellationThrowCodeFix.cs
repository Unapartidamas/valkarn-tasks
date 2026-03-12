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
    /// Code fix for MIG003: Replaces .SuppressCancellationThrow() with .AsResult().
    /// </summary>
    [ExportCodeFixProvider(LanguageNames.CSharp), Shared]
    public sealed class SuppressCancellationThrowCodeFix : CodeFixProvider
    {
        public override ImmutableArray<string> FixableDiagnosticIds
            => ImmutableArray.Create("MIG003");

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
                        "Replace with .AsResult()",
                        ct => ReplaceWithAsResultAsync(context.Document, invocation, memberAccess, ct),
                        "MIG003_Fix"),
                    diagnostic);
            }
        }

        static async Task<Document> ReplaceWithAsResultAsync(
            Document document,
            InvocationExpressionSyntax invocation,
            MemberAccessExpressionSyntax memberAccess,
            CancellationToken ct)
        {
            var root = await document.GetSyntaxRootAsync(ct);

            // task.SuppressCancellationThrow() → task.AsResult()
            var newMemberAccess = memberAccess.WithName(
                SyntaxFactory.IdentifierName("AsResult"));
            var newInvocation = invocation.WithExpression(newMemberAccess);

            var newRoot = root.ReplaceNode(invocation, newInvocation);
            return await UsingHelper.EnsureValkarnUsingAsync(document.WithSyntaxRoot(newRoot), ct);
        }
    }
}
