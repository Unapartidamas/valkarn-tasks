// Copyright (c) 2025 Una Partida Mas. All rights reserved.
// Licensed under the Valkarn Tasks License. See LICENSE.md in the repository root.

using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace UnaPartidaMas.Valkarn.Tasks.Analyzer.Analyzers
{
    /// <summary>
    /// Detects UniTask types in code and suggests migration to ValkarnTask.
    /// Only activates when UniTask assembly is referenced.
    /// Covers MIG001 (UniTask types), MIG007 (UniTaskVoid).
    /// </summary>
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public sealed class UniTaskTypeAnalyzer : DiagnosticAnalyzer
    {
        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics
            => ImmutableArray.Create(
                MigrationDescriptors.UniTaskType,
                MigrationDescriptors.UniTaskVoid);

        public override void Initialize(AnalysisContext context)
        {
            context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
            context.EnableConcurrentExecution();

            context.RegisterCompilationStartAction(compilationCtx =>
            {
                // Only activate if UniTask type is available in the compilation.
                // Use GetTypeByMetadataName instead of ReferencedAssemblyNames
                // because Rider resolves ProjectReferences as CompilationReferences
                // which don't appear in ReferencedAssemblyNames.
                if (compilationCtx.Compilation.GetTypeByMetadataName("Cysharp.Threading.Tasks.UniTask") == null)
                    return;

                compilationCtx.RegisterSyntaxNodeAction(AnalyzeMethodDeclaration, SyntaxKind.MethodDeclaration);
                compilationCtx.RegisterSyntaxNodeAction(AnalyzeVariableDeclaration, SyntaxKind.VariableDeclaration);
                compilationCtx.RegisterSyntaxNodeAction(AnalyzeParameter, SyntaxKind.Parameter);
                compilationCtx.RegisterSyntaxNodeAction(AnalyzePropertyDeclaration, SyntaxKind.PropertyDeclaration);
            });
        }

        static void AnalyzeMethodDeclaration(SyntaxNodeAnalysisContext context)
        {
            var method = (MethodDeclarationSyntax)context.Node;
            var typeInfo = context.SemanticModel.GetTypeInfo(method.ReturnType, context.CancellationToken);
            CheckType(context, method.ReturnType, typeInfo.Type);
        }

        static void AnalyzeVariableDeclaration(SyntaxNodeAnalysisContext context)
        {
            var decl = (VariableDeclarationSyntax)context.Node;
            var typeInfo = context.SemanticModel.GetTypeInfo(decl.Type, context.CancellationToken);
            CheckType(context, decl.Type, typeInfo.Type);
        }

        static void AnalyzeParameter(SyntaxNodeAnalysisContext context)
        {
            var param = (ParameterSyntax)context.Node;
            if (param.Type == null) return;
            var typeInfo = context.SemanticModel.GetTypeInfo(param.Type, context.CancellationToken);
            CheckType(context, param.Type, typeInfo.Type);
        }

        static void AnalyzePropertyDeclaration(SyntaxNodeAnalysisContext context)
        {
            var prop = (PropertyDeclarationSyntax)context.Node;
            var typeInfo = context.SemanticModel.GetTypeInfo(prop.Type, context.CancellationToken);
            CheckType(context, prop.Type, typeInfo.Type);
        }

        static void CheckType(SyntaxNodeAnalysisContext context, SyntaxNode typeNode, ITypeSymbol type)
        {
            if (type == null) return;
            if (!IsUniTaskNamespace(type)) return;

            if (type.Name == "UniTaskVoid")
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    MigrationDescriptors.UniTaskVoid,
                    typeNode.GetLocation()));
            }
            else if (type.Name == "UniTask")
            {
                var displayName = type is INamedTypeSymbol { IsGenericType: true } named
                    ? $"UniTask<{string.Join(", ", named.TypeArguments)}>"
                    : "UniTask";

                context.ReportDiagnostic(Diagnostic.Create(
                    MigrationDescriptors.UniTaskType,
                    typeNode.GetLocation(),
                    displayName));
            }
        }

        static bool IsUniTaskNamespace(ITypeSymbol type)
        {
            var ns = type.ContainingNamespace?.ToDisplayString();
            return ns == "Cysharp.Threading.Tasks";
        }
    }
}
