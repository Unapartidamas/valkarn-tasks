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
    /// Code fix for MIG007: Replaces async UniTaskVoid with [FireAndForget] async ValkarnTask.
    /// </summary>
    [ExportCodeFixProvider(LanguageNames.CSharp), Shared]
    public sealed class UniTaskVoidCodeFix : CodeFixProvider
    {
        static readonly SyntaxAnnotation s_methodMarker = new SyntaxAnnotation("MIG007_Method");

        public override ImmutableArray<string> FixableDiagnosticIds
            => ImmutableArray.Create("MIG007");

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
                    "Replace with [FireAndForget] async ValkarnTask",
                    ct => ReplaceUniTaskVoidAsync(context.Document, node, ct),
                    "MIG007_Fix"),
                diagnostic);
        }

        static async Task<Document> ReplaceUniTaskVoidAsync(Document document, SyntaxNode node, CancellationToken ct)
        {
            var root = await document.GetSyntaxRootAsync(ct);

            // Annotate the containing method so we can find it after the tree mutation
            var method = node.Parent as MethodDeclarationSyntax;
            if (method == null)
            {
                // Just replace the type if not in a method context
                var newReturnType = SyntaxFactory.ParseTypeName("ValkarnTask").WithTriviaFrom(node);
                return await UsingHelper.EnsureValkarnUsingAsync(
                    document.WithSyntaxRoot(root.ReplaceNode(node, newReturnType)), ct);
            }

            // Annotate the method, replace return type, then find annotated method to add attribute
            var annotatedMethod = method.WithAdditionalAnnotations(s_methodMarker);
            var newRoot = root.ReplaceNode(method, annotatedMethod);

            // Find the annotated method in the new tree
            var trackedMethod = newRoot.GetAnnotatedNodes(s_methodMarker).FirstOrDefault() as MethodDeclarationSyntax;
            if (trackedMethod == null)
                return await UsingHelper.EnsureValkarnUsingAsync(document.WithSyntaxRoot(newRoot), ct);

            // Replace return type
            var valkarnTaskType = SyntaxFactory.ParseTypeName("ValkarnTask").WithTriviaFrom(trackedMethod.ReturnType);
            var updatedMethod = trackedMethod.WithReturnType(valkarnTaskType);

            // Add [FireAndForget] attribute with proper indentation
            var leadingTrivia = trackedMethod.GetLeadingTrivia();
            var attribute = SyntaxFactory.AttributeList(
                SyntaxFactory.SingletonSeparatedList(
                    SyntaxFactory.Attribute(SyntaxFactory.IdentifierName("FireAndForget"))))
                .WithLeadingTrivia(leadingTrivia)
                .WithTrailingTrivia(SyntaxFactory.ElasticCarriageReturnLineFeed);

            updatedMethod = updatedMethod.AddAttributeLists(attribute);

            // Remove the annotation
            updatedMethod = updatedMethod.WithoutAnnotations(s_methodMarker);

            newRoot = newRoot.ReplaceNode(trackedMethod, updatedMethod);
            return await UsingHelper.EnsureValkarnUsingAsync(document.WithSyntaxRoot(newRoot), ct);
        }
    }
}
