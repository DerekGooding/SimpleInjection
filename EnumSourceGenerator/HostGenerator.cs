using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
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
            var (compilation, classes) = source;
            var sb = new StringBuilder();

            sb.AppendLine("namespace SimpleInjection.Injection;");
            sb.AppendLine("public sealed class Host");
            sb.AppendLine("{");
            sb.AppendLine("    private readonly List<ServiceDescriptor> _serviceDescriptors = new();");
            sb.AppendLine("    private readonly Dictionary<Type, object> _singletonInstances = new();");
            sb.AppendLine("    private readonly Dictionary<Type, Func<Scope, object>> _factories = new();");
            sb.AppendLine("    private Scope? _rootScope;");
            sb.AppendLine("    private bool _initialized;");
            sb.AppendLine();
            sb.AppendLine("    private Host() { }");
            sb.AppendLine();
            sb.AppendLine("    public static Host Initialize()");
            sb.AppendLine("    {");
            sb.AppendLine("        var host = new Host();");

            // generate service descriptors
            foreach (var item in classes.OfType<(INamedTypeSymbol symbol, AttributeData attr)>().Distinct())
            {
                var impl = item.symbol;
                var attr = item.attr;

                var lifetime = attr.AttributeClass?.Name switch
                {
                    "SingletonAttribute" => "ServiceLifetime.Singleton",
                    "ScopedAttribute" => "ServiceLifetime.Scoped",
                    "TransientAttribute" => "ServiceLifetime.Transient",
                    _ => null
                };

                if (lifetime == null)
                    continue;

                // pick interface if explicitly provided, otherwise infer
                var iface = attr.ConstructorArguments.Length > 0
                    ? attr.ConstructorArguments[0].Value as INamedTypeSymbol
                    : impl.AllInterfaces.FirstOrDefault(i => i.Name == "I" + impl.Name)
                      ?? impl.AllInterfaces.FirstOrDefault();

                // register concrete type to itself
                sb.AppendLine(
                    $"        host._serviceDescriptors.Add(new ServiceDescriptor(typeof({impl.ToDisplayString()}), typeof({impl.ToDisplayString()}), {lifetime}));");

                // register interface -> concrete mapping (if any)
                if (iface != null)
                {
                    sb.AppendLine(
                        $"        host._serviceDescriptors.Add(new ServiceDescriptor(typeof({iface.ToDisplayString()}), typeof({impl.ToDisplayString()}), {lifetime}));");
                }
            }

            // call InitializeUsingAttribute (which builds factories)
            sb.AppendLine("        host._rootScope = new Scope(host.ResolveInternal);");
            sb.AppendLine("        host.InitializeUsingAttribute();");
            sb.AppendLine("        return host;");
            sb.AppendLine("    }");


            sb.AppendLine(_stockMethods);

            sb.AppendLine("}");

            spc.AddSource("Host.Configure.g.cs", SourceText.From(sb.ToString(), Encoding.UTF8));
        });
    }


    const string _stockMethods = """
    private void InitializeUsingAttribute()
    {
        if (_initialized)
            return;

        BuildFactories();
        _initialized = true;
    }

    private object ResolveInternal(Type type, Scope scope)
    {
        if (_factories.TryGetValue(type, out var factory))
            return factory(scope);

        throw new InvalidOperationException($"Service of type {type.Name} is not registered.");
    }
    
    private void BuildFactories()
    {
        foreach (var descriptor in SortServicesByDependencies())
        {
            CreateFactory(descriptor);
        }
    }

    private List<ServiceDescriptor> SortServicesByDependencies()
    {
        var result = new List<ServiceDescriptor>();
        var remaining = new List<ServiceDescriptor>(_serviceDescriptors);

        // Process services with no dependencies first
        ProcessServicesWithNoDependencies(result, remaining);

        // Process remaining services based on dependency order
        ProcessRemainingServices(result, remaining);

        return result;
    }

    private static void ProcessServicesWithNoDependencies(List<ServiceDescriptor> result, List<ServiceDescriptor> remaining)
    {
        for (var i = 0; i < remaining.Count; i++)
        {
            var descriptor = remaining[i];
            if (descriptor.Dependencies.Count == 0)
            {
                result.Add(descriptor);
                remaining.RemoveAt(i);
                i--;
            }
        }
    }

    private static void ProcessRemainingServices(List<ServiceDescriptor> result, List<ServiceDescriptor> remaining)
    {
        var lastCount = int.MaxValue;
        while (remaining.Count > 0 && lastCount != remaining.Count)
        {
            lastCount = remaining.Count;

            for (var i = 0; i < remaining.Count; i++)
            {
                var descriptor = remaining[i];
                var allDependenciesResolved = descriptor.Dependencies.All(dep =>
                    CanResolveDependency(dep, result));

                if (allDependenciesResolved)
                {
                    result.Add(descriptor);
                    remaining.RemoveAt(i);
                    i--;
                }
            }
        }

        if (remaining.Count > 0)
        {
            throw new InvalidOperationException("Circular dependency detected or missing service registration.");
        }
    }

    private static bool CanResolveDependency(Type dependencyType, List<ServiceDescriptor> processedDescriptors)
    {
        // Check if we have a direct match (concrete type)
        if (processedDescriptors.Any(sd => sd.ServiceType == dependencyType))
            return true;

        // If it's an interface, check if we have a concrete implementation
        return dependencyType.IsInterface &&
               processedDescriptors.Any(sd =>
               dependencyType.IsAssignableFrom(sd.ServiceType) &&
               !sd.ServiceType.IsInterface &&
               !sd.ServiceType.IsAbstract);
    }

    private void CreateFactory(ServiceDescriptor descriptor)
    {
        switch (descriptor.Lifetime)
        {
            case ServiceLifetime.Singleton:
                CreateSingletonFactory(descriptor);
                break;
            case ServiceLifetime.Scoped:
                CreateScopedFactory(descriptor);
                break;
            case ServiceLifetime.Transient:
                CreateTransientFactory(descriptor);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(descriptor), descriptor.Lifetime,
                                                      "Invalid service lifetime.");
        }
    }

    private void CreateSingletonFactory(ServiceDescriptor descriptor)
    {
        var implType = descriptor.ImplementationType ?? descriptor.ServiceType;

        _factories[implType] = scope =>
        {
            if (!_singletonInstances.TryGetValue(implType, out var instance))
            {
                instance = CreateInstance(implType, scope);
                _singletonInstances[implType] = instance;
            }
            return instance;
        };
    }

    private void CreateScopedFactory(ServiceDescriptor descriptor)
    {
        var implType = descriptor.ImplementationType ?? descriptor.ServiceType;

        _factories[implType] = scope =>
            scope.GetOrCreateScopedInstance(implType, () => CreateInstance(implType, scope));
    }

    private void CreateTransientFactory(ServiceDescriptor descriptor)
    {
        var implType = descriptor.ImplementationType ?? descriptor.ServiceType;

        _factories[implType] = scope => CreateInstance(implType, scope);
    }

    private object CreateInstance(Type type, Scope scope)
    {
        if (type.IsInterface || type.IsAbstract)
            throw new InvalidOperationException($"Cannot create an instance of {type.Name} directly. It must be mapped to a concrete type.");

        var constructor = type.GetConstructors().FirstOrDefault()
        ?? throw new InvalidOperationException($"No public constructor found for type {type.Name}");

        var parameters = constructor.GetParameters();
        var arguments = new object[parameters.Length];

        for (var i = 0; i < parameters.Length; i++)
        {
            var paramType = parameters[i].ParameterType;

            if (paramType.IsInterface)
            {
                // Find the concrete implementation for this interface
                var implementations = _serviceDescriptors
                    .Where(sd => paramType.IsAssignableFrom(sd.ServiceType) && !sd.ServiceType.IsInterface && !sd.ServiceType.IsAbstract)
                    .ToList();

                if (implementations.Count == 1)
                {
                    var implType = implementations[0].ServiceType;
                    if (_factories.TryGetValue(implType, out var implFactory))
                    {
                        arguments[i] = implFactory(scope);
                        continue;
                    }
                }
                else if (implementations.Count > 1)
                {
                    throw new InvalidOperationException($"Multiple implementations found for interface {paramType.Name} when resolving dependency for {type.Name}. Please register only one or use a more specific type.");
                }

                throw new InvalidOperationException($"No implementation found for interface {paramType.Name} when resolving dependency for {type.Name}");
            }
            else
            {
                // It's a concrete type, look it up directly
                if (_factories.TryGetValue(paramType, out var factory))
                {
                    arguments[i] = factory(scope);
                    continue;
                }

                throw new InvalidOperationException($"Cannot resolve dependency {paramType.Name} for {type.Name}");
            }
        }

        return constructor.Invoke(arguments);
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

        return new Scope((type, s) =>
        {
            // Direct factory lookup
            if (_factories.TryGetValue(type, out var factory))
                return factory(s);

            // If type is an interface or abstract, resolve via descriptor → ImplementationType
            var descriptor = _serviceDescriptors.FirstOrDefault(sd => sd.ServiceType == type);
            if (descriptor?.ImplementationType is not null && _factories.TryGetValue(descriptor.ImplementationType, out var implFactory))
            {
                return implFactory(s);
            }

            throw new InvalidOperationException($"Cannot resolve type {type.Name} from scope.");
        });
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

        return GetService<T>(_rootScope);
    }

    /// <summary>
    /// Retrieves a service of the specified type from the given scope.
    /// </summary>
    /// <typeparam name="T">The type of service to retrieve.</typeparam>
    /// <param name="scope">The scope from which to retrieve the service.</param>
    /// <returns>An instance of the specified service type.</returns>
    /// <exception cref="InvalidOperationException">Thrown if the service is not registered.</exception>
    internal T GetService<T>(Scope scope) where T : class
    {
        var type = typeof(T);

        // Fast path: direct factory lookup
        if (_factories.TryGetValue(type, out var factory))
            return (T)factory(scope);

        // Lookup by descriptor
        var descriptor = _serviceDescriptors.FirstOrDefault(sd => sd.ServiceType == type);
        if (descriptor is not null && descriptor.ImplementationType is not null)
        {
            if (_factories.TryGetValue(descriptor.ImplementationType, out var implFactory))
                return (T)implFactory(scope);
        }

        throw new InvalidOperationException($"Service of type {type.Name} is not registered.");
    }

    public void Dispose()
    {
        _rootScope.Dispose();
    }

""";

}
