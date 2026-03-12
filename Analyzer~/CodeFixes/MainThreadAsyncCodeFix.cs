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
    /// Code fix for MIG008: Replaces await Awaitable.MainThreadAsync() with
    /// await ValkarnTask.SwitchToMainThread(), or removes it entirely since
    /// ValkarnTask runs on main thread by default.
    /// </summary>
    [ExportCodeFixProvider(LanguageNames.CSharp), Shared]
    public sealed class MainThreadAsyncCodeFix : CodeFixProvider
    {
        public override ImmutableArray<string> FixableDiagnosticIds
            => ImmutableArray.Create("MIG008");

        public override FixAllProvider GetFixAllProvider()
            => WellKnownFixAllProviders.BatchFixer;

        public override async Task RegisterCodeFixesAsync(CodeFixContext context)
        {
            var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken);
            var diagnostic = context.Diagnostics[0];
            var node = root.FindNode(diagnostic.Location.SourceSpan);

            // Option 1: Replace with SwitchToMainThread
            context.RegisterCodeFix(
                CodeAction.Create(
                    "Replace with ValkarnTask.SwitchToMainThread()",
                    ct => ReplaceWithSwitchAsync(context.Document, node, ct),
                    "MIG008_Replace"),
                diagnostic);

            // Option 2: Remove the await statement entirely
            context.RegisterCodeFix(
                CodeAction.Create(
                    "Remove (ValkarnTask is main-thread by default)",
                    ct => RemoveStatementAsync(context.Document, node, ct),
                    "MIG008_Remove"),
                diagnostic);
        }

        static async Task<Document> ReplaceWithSwitchAsync(Document document, SyntaxNode node, CancellationToken ct)
        {
            var root = await document.GetSyntaxRootAsync(ct);

            var newExpr = SyntaxFactory.ParseExpression("ValkarnTask.SwitchToMainThread()")
                .WithTriviaFrom(node);
            var newRoot = root.ReplaceNode(node, newExpr);
            return await UsingHelper.EnsureValkarnUsingAsync(document.WithSyntaxRoot(newRoot), ct);
        }

        static async Task<Document> RemoveStatementAsync(Document document, SyntaxNode node, CancellationToken ct)
        {
            var root = await document.GetSyntaxRootAsync(ct);

            // Find the containing statement (await expr;)
            var statement = node.FirstAncestorOrSelf<ExpressionStatementSyntax>()
                ?? node.FirstAncestorOrSelf<StatementSyntax>();

            if (statement != null)
            {
                var newRoot = root.RemoveNode(statement, SyntaxRemoveOptions.KeepLeadingTrivia);
                return document.WithSyntaxRoot(newRoot);
            }

            return document;
        }
    }
}
