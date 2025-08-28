//using Microsoft.CodeAnalysis;
//using Microsoft.CodeAnalysis.CSharp;
//using Microsoft.CodeAnalysis.CSharp.Syntax;
//using Microsoft.CodeAnalysis.Text;
//using System.Linq;
//using System.Text;

//namespace EnumSourceGenerator;

//[Generator]
//public class HostGenerator : IIncrementalGenerator
//{
//    public void Initialize(IncrementalGeneratorInitializationContext context)
//    {
//        var classDeclarations = context.SyntaxProvider
//            .CreateSyntaxProvider(
//                predicate: static (s, _) => s is ClassDeclarationSyntax,
//                transform: static (ctx, _) =>
//                {
//                    var classSyntax = (ClassDeclarationSyntax)ctx.Node;
//                    var symbol = ctx.SemanticModel.GetDeclaredSymbol(classSyntax);

//                    return symbol?.GetAttributes().Any(a =>
//                        a.AttributeClass?.Name == "SingletonAttribute") == true
//                        ? symbol
//                        : null;
//                })
//            .Where(static symbol => symbol is not null);

//        var compilationAndClasses = context.CompilationProvider.Combine(classDeclarations.Collect());

//        context.RegisterSourceOutput(compilationAndClasses, (spc, source) =>
//        {
//            var (compilation, classes) = source;
//            var sb = new StringBuilder();
//            sb.AppendLine("namespace SimpleInjection;");
//            sb.AppendLine("public static partial class Host");
//            sb.AppendLine("{");
//            sb.AppendLine("    public static Host Configure()");
//            sb.AppendLine("    {");
//            sb.AppendLine("        var host = new Host();");

//            foreach (var impl in classes.Distinct())
//            {
//                var attr = impl.GetAttributes().FirstOrDefault(a =>
//                    a.AttributeClass?.Name == "SingletonAttribute");

//                var iface = attr?.ConstructorArguments[0].Value as INamedTypeSymbol;

//                if (iface != null)
//                {
//                    sb.AppendLine($"        host._services.AddSingleton<{iface.ToDisplayString()}, {impl.ToDisplayString()}>();");
//                }
//            }

//            sb.AppendLine("        return host;");
//            sb.AppendLine("    }");
//            sb.AppendLine("}");

//            spc.AddSource("Host.Configure.g.cs", SourceText.From(sb.ToString(), Encoding.UTF8));
//        });
//    }
//}
