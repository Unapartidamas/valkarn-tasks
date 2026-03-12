// Copyright (c) 2025 Una Partida Mas. All rights reserved.
// Licensed under the Valkarn Tasks License. See LICENSE.md in the repository root.

using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace UnaPartidaMas.Valkarn.Tasks.SourceGen.Analyzers
{
    /// <summary>
    /// Reports TT014 when [NoAutoCancel] is applied to an async ValkarnTask method
    /// in a MonoBehaviour that has no CancellationToken parameter. Without a token
    /// parameter the method has no way to observe cancellation, making [NoAutoCancel]
    /// pointless and indicating the developer forgot to add a token.
    /// </summary>
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public sealed class NoAutoCancelAnalyzer : DiagnosticAnalyzer
    {
        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics
            => ImmutableArray.Create(DiagnosticDescriptors.NoAutoCancelWithoutToken);

        public override void Initialize(AnalysisContext context)
        {
            context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
            context.EnableConcurrentExecution();
            context.RegisterSyntaxNodeAction(AnalyzeMethod, SyntaxKind.MethodDeclaration);
        }

        static void AnalyzeMethod(SyntaxNodeAnalysisContext context)
        {
            var method = (MethodDeclarationSyntax)context.Node;

            // Must be async
            if (!method.Modifiers.Any(SyntaxKind.AsyncKeyword))
                return;

            // Must return ValkarnTask or ValkarnTask<T>
            var returnType = context.SemanticModel.GetTypeInfo(method.ReturnType, context.CancellationToken).Type;
            if (!ValkarnTypeHelper.IsAnyValkarnTask(returnType))
                return;

            // Must have [NoAutoCancel] attribute
            if (!HasNoAutoCancelAttribute(method))
                return;

            // Must be inside a MonoBehaviour class
            var containingClass = method.Ancestors().OfType<ClassDeclarationSyntax>().FirstOrDefault();
            if (containingClass == null)
                return;

            var classSymbol = context.SemanticModel.GetDeclaredSymbol(containingClass, context.CancellationToken);
            if (classSymbol == null || !IsMonoBehaviour(classSymbol))
                return;

            // Check if any parameter is CancellationToken
            if (HasCancellationTokenParameter(method, context.SemanticModel))
                return;

            var className = containingClass.Identifier.Text;

            context.ReportDiagnostic(Diagnostic.Create(
                DiagnosticDescriptors.NoAutoCancelWithoutToken,
                method.Identifier.GetLocation(),
                method.Identifier.Text,
                className));
        }

        static bool HasNoAutoCancelAttribute(MethodDeclarationSyntax method)
        {
            foreach (var attrList in method.AttributeLists)
            {
                foreach (var attr in attrList.Attributes)
                {
                    var name = attr.Name.ToString();
                    if (name == "NoAutoCancel" || name == "NoAutoCancelAttribute")
                        return true;
                }
            }
            return false;
        }

        static bool IsMonoBehaviour(INamedTypeSymbol classSymbol)
        {
            var current = classSymbol.BaseType;
            while (current != null)
            {
                if (current.Name == "MonoBehaviour"
                    && current.ContainingNamespace?.ToDisplayString() == "UnityEngine")
                    return true;
                current = current.BaseType;
            }
            return false;
        }

        static bool HasCancellationTokenParameter(MethodDeclarationSyntax method, SemanticModel semanticModel)
        {
            foreach (var param in method.ParameterList.Parameters)
            {
                if (param.Type == null) continue;

                var typeInfo = semanticModel.GetTypeInfo(param.Type);
                if (typeInfo.Type == null) continue;

                if (typeInfo.Type.Name == "CancellationToken"
                    && typeInfo.Type.ContainingNamespace?.ToDisplayString() == "System.Threading")
                {
                    return true;
                }
            }
            return false;
        }
    }
}
