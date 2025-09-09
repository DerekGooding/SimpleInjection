using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace EnumSourceGenerator;

[Generator]
public class HostGenerator : IIncrementalGenerator
{
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var classSymbols = context.SyntaxProvider
            .CreateSyntaxProvider<(INamedTypeSymbol, AttributeData)?>(
                predicate: static (node, _) => node is ClassDeclarationSyntax,
                transform: static (ctx, _) =>
                {
                    var classDecl = (ClassDeclarationSyntax)ctx.Node;
                    if (ctx.SemanticModel.GetDeclaredSymbol(classDecl) is not INamedTypeSymbol symbol) return null;

                    var attr = symbol.GetAttributes()
                        .FirstOrDefault(n => n.AttributeClass?.Name is "SingletonAttribute" or "ScopedAttribute" or "TransientAttribute");

                    return attr is null ? null : (symbol, attr);
                })
            .Where(s => s is not null)!;

        

        var compilationAndClasses = context.CompilationProvider
            .Combine(classSymbols.Collect());

        context.RegisterSourceOutput(compilationAndClasses, (spc, source) =>
        {
            if( source.Right.IsEmpty) return;

            var projectName = source.Left.AssemblyName ?? "GeneratedProject";

            var (compilation, classes) = source;
            var services = classes.OfType<(INamedTypeSymbol symbol, AttributeData attr)>().Distinct().ToList();

            var sb = new StringBuilder();

            sb.AppendLine("#nullable enable");
            sb.AppendLine("using System;");
            sb.AppendLine("using System.Collections.Generic;");
            sb.AppendLine("using System.Reflection;");
            sb.AppendLine();
            sb.AppendLine($"namespace {projectName}.Genenerated;");
            sb.AppendLine("public sealed class Host : IDisposable");
            sb.AppendLine("{");
            sb.AppendLine("    private readonly Dictionary<Type, object> _singletonInstances = new();");
            sb.AppendLine("    private readonly Dictionary<Type, Func<Scope, object>> _factories = new();");
            sb.AppendLine("    private Func<Type, Scope, object> _resolver = null!;");
            sb.AppendLine("    private Scope? _rootScope;");
            sb.AppendLine("    private bool _initialized;");
            sb.AppendLine();
            sb.AppendLine("    private Host() { }");
            sb.AppendLine();
            sb.AppendLine("    public static Host Initialize()");
            sb.AppendLine("    {");
            sb.AppendLine("        var host = new Host();");
            sb.AppendLine("        host._resolver = host.ResolveInternal;");
            sb.AppendLine("        host._rootScope = new Scope(host._resolver);");
            sb.AppendLine();

            // Build dependency graph and sort services
            var serviceInfos = BuildServiceInfos(services, compilation);
            

            // Generate baked factories in dependency order
            foreach (var serviceInfo in TopologicalSort(serviceInfos))
            {
                GenerateFactory(sb, serviceInfo, compilation);
            }

            sb.AppendLine("        host._initialized = true;");
            sb.AppendLine("        return host;");
            sb.AppendLine("    }");
            sb.AppendLine();

            // Generate minimal runtime methods
            GenerateRuntimeMethods(sb);

            sb.AppendLine("}");

            spc.AddSource($"{projectName}.Host.Configure.g.cs", SourceText.From(sb.ToString(), Encoding.UTF8));
        });
    }

    private static List<ServiceInfo> BuildServiceInfos(List<(INamedTypeSymbol symbol, AttributeData attr)> services, Compilation compilation)
    {
        var serviceInfos = new List<ServiceInfo>();

        foreach (var (symbol, attr) in services)
        {
            var lifetime = attr.AttributeClass?.Name switch
            {
                "SingletonAttribute" => ServiceLifetime.Singleton,
                "ScopedAttribute" => ServiceLifetime.Scoped,
                "TransientAttribute" => ServiceLifetime.Transient,
                _ => ServiceLifetime.Transient
            };

            // Get interface if explicitly provided, otherwise infer
            var iface = attr.ConstructorArguments.Length > 0
                ? attr.ConstructorArguments[0].Value as INamedTypeSymbol
                : symbol.AllInterfaces.FirstOrDefault(i => i.Name == "I" + symbol.Name)
                  ?? symbol.AllInterfaces.FirstOrDefault();

            // Get constructor dependencies
            var constructor = symbol.Constructors.FirstOrDefault(c => c.DeclaredAccessibility == Accessibility.Public);
            var dependencies = constructor?.Parameters.Select(p => p.Type).ToList() ?? new List<ITypeSymbol>();

            var serviceInfo = new ServiceInfo
            {
                ImplementationType = symbol,
                InterfaceType = iface,
                Lifetime = lifetime,
                Dependencies = dependencies,
                Constructor = constructor
            };

            serviceInfos.Add(serviceInfo);
        }

        return serviceInfos;
    }

    private static List<ServiceInfo> TopologicalSort(List<ServiceInfo> services)
    {
        var result = new List<ServiceInfo>();
        var remaining = new List<ServiceInfo>(services);
        var inProgress = new HashSet<INamedTypeSymbol>(SymbolEqualityComparer.Default);

        while (remaining.Count > 0)
        {
            var beforeCount = remaining.Count;

            for (int i = remaining.Count - 1; i >= 0; i--)
            {
                var service = remaining[i];

                if (CanResolve(service, result, services, inProgress))
                {
                    result.Add(service);
                    remaining.RemoveAt(i);
                    inProgress.Remove(service.ImplementationType);
                }
            }

            if (remaining.Count == beforeCount)
            {
                // No progress made, likely circular dependency
                throw new InvalidOperationException($"Circular dependency detected involving: {string.Join(", ", remaining.Select(s => s.ImplementationType.Name))}");
            }
        }

        return result;
    }

    private static bool CanResolve(ServiceInfo service, List<ServiceInfo> resolved, List<ServiceInfo> allServices, HashSet<INamedTypeSymbol> inProgress)
    {
        if (inProgress.Contains(service.ImplementationType))
            return false; // Circular dependency

        inProgress.Add(service.ImplementationType);

        foreach (var dependency in service.Dependencies)
        {
            if (!IsResolved(dependency, resolved, allServices))
            {
                inProgress.Remove(service.ImplementationType);
                return false;
            }
        }

        return true;
    }

    private static bool IsResolved(ITypeSymbol dependencyType, List<ServiceInfo> resolved, List<ServiceInfo> allServices)
    {
        // Check if we have this type resolved already
        if (resolved.Any(r => SymbolEqualityComparer.Default.Equals(r.ImplementationType, dependencyType)))
            return true;

        // If it's an interface, check if we have a concrete implementation resolved
        if (dependencyType.TypeKind == TypeKind.Interface)
        {
            return resolved.Any(r =>
                r.InterfaceType != null &&
                SymbolEqualityComparer.Default.Equals(r.InterfaceType, dependencyType));
        }

        return false;
    }

    private static void GenerateFactory(StringBuilder sb, ServiceInfo serviceInfo, Compilation compilation)
    {
        var implType = serviceInfo.ImplementationType;
        var implTypeName = implType.ToDisplayString();

        switch (serviceInfo.Lifetime)
        {
            case ServiceLifetime.Singleton:
                GenerateSingletonFactory(sb, serviceInfo);
                break;
            case ServiceLifetime.Scoped:
                GenerateScopedFactory(sb, serviceInfo);
                break;
            case ServiceLifetime.Transient:
                GenerateTransientFactory(sb, serviceInfo);
                break;
        }

        // Also register interface mapping if present
        if (serviceInfo.InterfaceType != null)
        {
            var ifaceTypeName = serviceInfo.InterfaceType.ToDisplayString();
            sb.AppendLine($"        host._factories[typeof({ifaceTypeName})] = host._factories[typeof({implTypeName})];");
        }
    }

    private static void GenerateSingletonFactory(StringBuilder sb, ServiceInfo serviceInfo)
    {
        var implTypeName = serviceInfo.ImplementationType.ToDisplayString();

        sb.AppendLine($"        host._factories[typeof({implTypeName})] = scope =>");
        sb.AppendLine("        {");
        sb.AppendLine($"            if (!host._singletonInstances.TryGetValue(typeof({implTypeName}), out var singletonInstance))");
        sb.AppendLine("            {");
        GenerateConstructorCall(sb, serviceInfo, "                ");
        sb.AppendLine($"                host._singletonInstances[typeof({implTypeName})] = constructedInstance;");
        sb.AppendLine("                singletonInstance = constructedInstance;");
        sb.AppendLine("            }");
        sb.AppendLine($"            return ({implTypeName})singletonInstance;");
        sb.AppendLine("        };");
    }

    private static void GenerateScopedFactory(StringBuilder sb, ServiceInfo serviceInfo)
    {
        var implTypeName = serviceInfo.ImplementationType.ToDisplayString();

        sb.AppendLine($"        host._factories[typeof({implTypeName})] = scope =>");
        sb.AppendLine($"            scope.GetOrCreateScopedInstance(typeof({implTypeName}), () =>");
        sb.AppendLine("            {");
        GenerateConstructorCall(sb, serviceInfo, "                ");
        sb.AppendLine($"                return constructedInstance;");
        sb.AppendLine("            });");
    }

    private static void GenerateTransientFactory(StringBuilder sb, ServiceInfo serviceInfo)
    {
        var implTypeName = serviceInfo.ImplementationType.ToDisplayString();

        sb.AppendLine($"        host._factories[typeof({implTypeName})] = scope =>");
        sb.AppendLine("        {");
        GenerateConstructorCall(sb, serviceInfo, "            ");
        sb.AppendLine($"            return constructedInstance;");
        sb.AppendLine("        };");
    }

    private static void GenerateConstructorCall(StringBuilder sb, ServiceInfo serviceInfo, string indent)
    {
        if (serviceInfo.Constructor == null || serviceInfo.Constructor.Parameters.Length == 0)
        {
            sb.AppendLine($"{indent}var constructedInstance = new {serviceInfo.ImplementationType.ToDisplayString()}();");
            return;
        }

        var parameters = serviceInfo.Constructor.Parameters;

        // Generate parameter resolution using the host's resolver function
        for (int i = 0; i < parameters.Length; i++)
        {
            var param = parameters[i];
            var paramType = param.Type.ToDisplayString();
            sb.AppendLine($"{indent}var param{i} = ({paramType})host._resolver(typeof({paramType}), scope);");
        }

        // Generate constructor call
        var paramNames = string.Join(", ", Enumerable.Range(0, parameters.Length).Select(i => $"param{i}"));
        sb.AppendLine($"{indent}var constructedInstance = new {serviceInfo.ImplementationType.ToDisplayString()}({paramNames});");
    }

    private static void GenerateRuntimeMethods(StringBuilder sb)
    {
        sb.AppendLine("""
    private object ResolveInternal(Type type, Scope scope)
    {
        if (_factories.TryGetValue(type, out var factory))
            return factory(scope);

        throw new InvalidOperationException($"Service of type {type.Name} is not registered.");
    }

    /// <summary>
    /// Creates a new scope for resolving scoped services.
    /// </summary>
    /// <returns>A new <see cref="Scope"/> instance.</returns>
    /// <exception cref="InvalidOperationException">Thrown if the host has not been initialized.</exception>
    public Scope CreateScope()
    {
        if (!_initialized)
            throw new InvalidOperationException("Host must be initialized before creating a scope.");

        return new Scope(_resolver);
    }

    /// <summary>
    /// Retrieves a singleton service of the specified type.
    /// </summary>
    /// <typeparam name="T">The type of service to retrieve.</typeparam>
    /// <returns>The singleton instance of the specified service type.</returns>
    /// <exception cref="InvalidOperationException">Thrown if the host has not been initialized or the service is not registered.</exception>
    public T Get<T>() where T : class
    {
        if (!_initialized)
            throw new InvalidOperationException("Host must be initialized.");

        return (T)ResolveInternal(typeof(T), _rootScope!);
    }

    public void Dispose()
    {
        _rootScope?.Dispose();
    }
""");
    }

    private class ServiceInfo
    {
        public INamedTypeSymbol ImplementationType { get; set; } = null!;
        public INamedTypeSymbol? InterfaceType { get; set; }
        public ServiceLifetime Lifetime { get; set; }
        public List<ITypeSymbol> Dependencies { get; set; } = new();
        public IMethodSymbol? Constructor { get; set; }
    }

    private enum ServiceLifetime
    {
        Transient,
        Scoped,
        Singleton
    }
}