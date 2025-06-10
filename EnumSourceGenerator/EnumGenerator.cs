﻿using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;

namespace EnumSourceGenerator;

[Generator]
public class EnumGenerator : IIncrementalGenerator
{
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var classDeclarations = context.SyntaxProvider
            .CreateSyntaxProvider(
                predicate: static (node, _) => node is ClassDeclarationSyntax { BaseList: not null },
                transform: static (context, _) => GetClassSymbol(context))
            .Where(static symbol => symbol is not null)
            .Select(static (symbol, _) => symbol!)
            .Collect();

        context.RegisterSourceOutput(classDeclarations, (ctx, classes) =>
        {
            foreach (var classSymbol in classes)
            {
                // Find the IContent<T> implementation
                var iContentInterface = classSymbol.AllInterfaces.FirstOrDefault(i => i.Name == "IContent");
                if (iContentInterface?.TypeArguments.FirstOrDefault() is not INamedTypeSymbol typeArgument) continue;

                // Extract enum member names from the All property of the class
                var enumMembers = ExtractEnumMembers(ctx, classSymbol).ToImmutableArray();

                var source = GenerateEnumSource(classSymbol.Name, enumMembers);
                ctx.AddSource($"{classSymbol.Name}TypeEnum.g.cs", SourceText.From(source, Encoding.UTF8));

                var helper = GeneratePartialHelper(classSymbol.Name,
                                                      classSymbol.ContainingNamespace.ToDisplayString(),
                                                      typeArgument.ToDisplayString(), enumMembers);
                ctx.AddSource($"{classSymbol.Name}Helper.g.cs", SourceText.From(helper, Encoding.UTF8));
            }
        });
    }

    private static INamedTypeSymbol? GetClassSymbol(GeneratorSyntaxContext context)
        => context.Node is ClassDeclarationSyntax classDecl
            ? context.SemanticModel.GetDeclaredSymbol(classDecl) as INamedTypeSymbol
            : null;

    private static IEnumerable<string> ExtractEnumMembers(
        SourceProductionContext ctx,
        INamedTypeSymbol classSymbol)
    {
        var allProperty = classSymbol
            .GetMembers()
            .OfType<IPropertySymbol>()
            .FirstOrDefault(p => p.Name == "All");
        if (allProperty == null)
        {
            ctx.ReportDiagnostic(Diagnostic.Create(new DiagnosticDescriptor("GEN001",
                "All Property Not Found", "The All property in class {0} was not found.",
                "Generator", DiagnosticSeverity.Warning, true), Location.None));
            yield break;
        }

        // Get the syntax reference for the property and ensure it's not null
        if (allProperty.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax()
            is not PropertyDeclarationSyntax allPropertySyntax || allPropertySyntax.Initializer == null)
        {
            ctx.ReportDiagnostic(Diagnostic.Create(new DiagnosticDescriptor("GEN002",
                "No Initializer Found", "No initializer was found for the All property in class {0}.",
                "Generator", DiagnosticSeverity.Warning, true), Location.None));
            yield break;
        }

        // Get the initializer expression value
        var initializer = allPropertySyntax.Initializer.Value;

        // Handle explicit array creation (e.g., `new Material[] { ... }`)
        if (initializer is ArrayCreationExpressionSyntax arrayCreation)
        {
            foreach (var enumName in ExtractFromArrayInitializer(ctx, arrayCreation.Initializer))
            {
                yield return enumName;
            }
        }
        // Handle implicit array creation with `{}` or `[]` (e.g., `Material[] All = [ ... ];`)
        else if (initializer is ImplicitArrayCreationExpressionSyntax implicitArrayCreation)
        {
            foreach (var enumName in ExtractFromArrayInitializer(ctx, implicitArrayCreation.Initializer))
            {
                yield return enumName;
            }
        }
        // Handle collection expressions `[]` directly
        else if (initializer is InitializerExpressionSyntax initializerExpression)
        {
            foreach (var enumName in ExtractFromArrayInitializer(ctx, initializerExpression))
            {
                yield return enumName;
            }
        }
        // Handle collection expressions `[]` directly
        else if (initializer is CollectionExpressionSyntax collection)
        {
            foreach (var enumName in ExtractFromCollection(ctx, collection))
            {
                yield return enumName;
            }
        }
        else
        {
            ctx.ReportDiagnostic(Diagnostic.Create(new DiagnosticDescriptor("GEN004",
                "Incorrect Initializer",
                "The initializer is not a recognized array creation syntax." +
                $"Of type {allPropertySyntax.Initializer.Value.GetType()}",
                "Generator", DiagnosticSeverity.Warning, true), Location.None));
        }
    }

    private static IEnumerable<string> ExtractFromCollection(
        SourceProductionContext ctx,
        CollectionExpressionSyntax collection)
    {
        if (collection.Elements.Count == 0)
        {
            ctx.ReportDiagnostic(Diagnostic.Create(new DiagnosticDescriptor("GEN008",
                "Empty Collection", "The array collection is empty.", "Generator",
                DiagnosticSeverity.Warning, true), Location.None));
            yield break;
        }

        foreach (var element in collection.Elements)
        {
            if (element is ExpressionElementSyntax objectExpression )
            {
                // Handle object initializers like `new("...")`
                if (objectExpression.Expression is ObjectCreationExpressionSyntax objectCreation)
                {
                    var firstArgument = objectCreation.ArgumentList?.Arguments.FirstOrDefault();
                    if (firstArgument?.Expression is LiteralExpressionSyntax literal)
                        yield return SanitizeEnumName(literal.Token.ValueText);
                }
                // Handle shorthand object initializers like `new("...")`
                else if (objectExpression.Expression is ImplicitObjectCreationExpressionSyntax implicitCreation)
                {
                    var firstArgument = implicitCreation.ArgumentList?.Arguments.FirstOrDefault();
                    if (firstArgument?.Expression is LiteralExpressionSyntax literal)
                        yield return SanitizeEnumName(literal.Token.ValueText);
                }
            }
        }
    }

    private static IEnumerable<string> ExtractFromArrayInitializer(
        SourceProductionContext ctx,
        InitializerExpressionSyntax? initializer)
    {
        if (initializer == null)
        {
            ctx.ReportDiagnostic(Diagnostic.Create(new DiagnosticDescriptor("GEN005",
                "Empty Initializer", "The array initializer is empty.", "Generator",
                DiagnosticSeverity.Warning, true), Location.None));
            yield break;
        }

        foreach (var expression in initializer.Expressions)
        {
            // Handle object initializers like `new("...")`
            if (expression is ObjectCreationExpressionSyntax objectCreation)
            {
                var firstArgument = objectCreation.ArgumentList?.Arguments.FirstOrDefault();
                if (firstArgument?.Expression is LiteralExpressionSyntax literal)
                    yield return SanitizeEnumName(literal.Token.ValueText);
            }
            // Handle shorthand object initializers like `new("...")`
            else if (expression is ImplicitObjectCreationExpressionSyntax implicitCreation)
            {
                var firstArgument = implicitCreation.ArgumentList?.Arguments.FirstOrDefault();
                if (firstArgument?.Expression is LiteralExpressionSyntax literal)
                    yield return SanitizeEnumName(literal.Token.ValueText);
            }
        }
    }

    private static string GenerateEnumSource(string className, ImmutableArray<string> enumMembers)
    {
        var membersSource = string.Join(",\n", enumMembers.Select(m => $"        {m}"));
        return $@"// Auto-generated code
namespace ContentEnums
{{
    public enum {className}Type
    {{
{membersSource}
    }}
}}";
    }

    private static string GeneratePartialHelper(string className, string fullNamespace, string typeArgument, ImmutableArray<string> enumMembers)
    {
        var membersSource = string.Join(";\n", enumMembers.Select((m, i) => $"        public {typeArgument} {m} => All[{i}]"));
        return $@"// Auto-generated code
using ContentEnums;

namespace {fullNamespace}
{{
    public partial class {className}
    {{
        public {typeArgument} Get({className}Type type) => All[(int)type];
        public {typeArgument} this[{className}Type type] => All[(int)type];
        public {typeArgument} GetById(int id) => All[id]; 
{membersSource};
    }}
}}";
    }

    private static string SanitizeEnumName(string name)
    {
        // Remove invalid characters and replace spaces with underscores
        StringBuilder builder = new();
        foreach (var ch in name)
        {
            if (char.IsLetterOrDigit(ch) || ch == '_')
                builder.Append(ch);
        }

        return builder.ToString();
    }
}
