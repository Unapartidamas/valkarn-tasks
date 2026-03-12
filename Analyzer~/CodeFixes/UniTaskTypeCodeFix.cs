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
using Microsoft.CodeAnalysis.Editing;

namespace UnaPartidaMas.Valkarn.Tasks.Analyzer.CodeFixes
{
    /// <summary>
    /// Code fix for MIG001: Replaces UniTask types with ValkarnTask equivalents.
    /// Handles: UniTask → ValkarnTask, UniTask&lt;T&gt; → ValkarnTask&lt;T&gt;
    /// </summary>
    [ExportCodeFixProvider(LanguageNames.CSharp), Shared]
    public sealed class UniTaskTypeCodeFix : CodeFixProvider
    {
        public override ImmutableArray<string> FixableDiagnosticIds
            => ImmutableArray.Create("MIG001");

        public override FixAllProvider GetFixAllProvider()
            => WellKnownFixAllProviders.BatchFixer;

        public override async Task RegisterCodeFixesAsync(CodeFixContext context)
        {
            var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken);
            var diagnostic = context.Diagnostics[0];
            var span = diagnostic.Location.SourceSpan;
            var node = root.FindNode(span);

            context.RegisterCodeFix(
                CodeAction.Create(
                    "Replace with ValkarnTask",
                    ct => ReplaceTypeAsync(context.Document, node, ct),
                    "MIG001_Fix"),
                diagnostic);
        }

        static async Task<Document> ReplaceTypeAsync(Document document, SyntaxNode node, CancellationToken ct)
        {
            var root = await document.GetSyntaxRootAsync(ct);
            var semanticModel = await document.GetSemanticModelAsync(ct);

            SyntaxNode newNode;
            if (node is TypeSyntax typeSyntax)
            {
                var typeInfo = semanticModel.GetTypeInfo(typeSyntax, ct);
                if (typeInfo.Type is INamedTypeSymbol { IsGenericType: true } named)
                {
                    // UniTask<T> → ValkarnTask<T>
                    var typeArgs = string.Join(", ", named.TypeArguments.Select(t => t.ToDisplayString()));
                    newNode = SyntaxFactory.ParseTypeName($"ValkarnTask<{typeArgs}>")
                        .WithTriviaFrom(node);
                }
                else
                {
                    // UniTask → ValkarnTask
                    newNode = SyntaxFactory.ParseTypeName("ValkarnTask")
                        .WithTriviaFrom(node);
                }
            }
            else
            {
                newNode = SyntaxFactory.ParseTypeName("ValkarnTask")
                    .WithTriviaFrom(node);
            }

            var newRoot = root.ReplaceNode(node, newNode);
            return await UsingHelper.EnsureValkarnUsingAsync(document.WithSyntaxRoot(newRoot), ct);
        }
    }
}
