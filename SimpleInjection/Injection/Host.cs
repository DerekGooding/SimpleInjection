﻿namespace SimpleInjection.Injection;

/// <summary>
/// Represents a container that holds and manages service instances with different lifetimes (Singleton, Scoped, Transient).
/// It provides functionality to register services, resolve dependencies, and retrieve service instances.
/// </summary>
/// <remarks>
/// This class is responsible for initializing and managing services with different lifetimes, ensuring proper
/// initialization and dependency resolution. It automatically discovers attributed classes and registers them
/// with their appropriate lifetimes.
/// </remarks>
public sealed class Host
{
    private readonly List<ServiceDescriptor> _serviceDescriptors = [];
    private readonly Dictionary<Type, object> _singletonInstances = [];
    private readonly Dictionary<Type, Func<Scope, object>> _factories = [];
    private bool _initialized;

    private Host() { }

    /// <summary>
    /// Initializes a new <see cref="Host"/> instance by scanning the current domain for types marked
    /// with service lifetime attributes. These types are then registered as services within the host.
    /// </summary>
    /// <returns>A new <see cref="Host"/> instance populated with services based on the types found in the current domain.</returns>
    /// <remarks>
    /// This method scans all loaded assemblies in the current application domain for types decorated
    /// with service lifetime attributes (<see cref="SingletonAttribute"/>, <see cref="ScopedAttribute"/>,
    /// <see cref="TransientAttribute"/>). It then creates a new host and registers these types with appropriate lifetimes.
    /// </remarks>
    public static Host Initialize()
    {
        var host = new Host();
        host.RegisterAttributedTypes();
        host.InitializeUsingAttribute();
        return host;
    }

    /// <summary>
    /// Registers all types in the current domain that are marked with lifetime attributes.
    /// </summary>
    private void RegisterAttributedTypes()
    {
        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            foreach (var type in assembly.GetTypes())
            {
                ServiceLifetime? lifetime = null;
                if (type.GetCustomAttributes(typeof(SingletonAttribute), false).Length > 0)
                {
                    lifetime = ServiceLifetime.Singleton;
                }
                else if (type.GetCustomAttributes(typeof(ScopedAttribute), false).Length > 0)
                {
                    lifetime = ServiceLifetime.Scoped;
                }
                else if (type.GetCustomAttributes(typeof(TransientAttribute), false).Length > 0)
                {
                    lifetime = ServiceLifetime.Transient;
                }

                if (lifetime.HasValue)
                {
                    // Register the concrete type
                    _serviceDescriptors.Add(new ServiceDescriptor(type, lifetime.Value));

                    // Register all non-system interfaces implemented by this type
                    foreach (var iface in type.GetInterfaces())
                    {
                        if (iface.Namespace?.StartsWith("System") == false &&
                            !_serviceDescriptors.Any(sd => sd.ServiceType == iface))
                        {
                            _serviceDescriptors.Add(new ServiceDescriptor(iface, lifetime.Value));
                        }
                    }
                }
            }
        }
    }

    private void InitializeUsingAttribute()
    {
        if (_initialized)
            return;

        BuildFactories();
        _initialized = true;
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

    private void ProcessRemainingServices(List<ServiceDescriptor> result, List<ServiceDescriptor> remaining)
    {
        var lastCount = int.MaxValue;
        while (remaining.Count > 0 && lastCount != remaining.Count)
        {
            lastCount = remaining.Count;

            for (var i = 0; i < remaining.Count; i++)
            {
                var descriptor = remaining[i];
                var allDependenciesResolved = descriptor.Dependencies.All(dep =>
                    _serviceDescriptors.Any(sd => sd.ServiceType == dep && result.Contains(sd)));

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
        var serviceType = descriptor.ServiceType;

        _factories[serviceType] = scope =>
        {
            if (!_singletonInstances.TryGetValue(serviceType, out var instance))
            {
                var typeToCreate = serviceType;
                if (serviceType.IsInterface)
                {
                    // Find all concrete implementations for this interface
                    var implementations = _serviceDescriptors
                        .Where(sd => serviceType.IsAssignableFrom(sd.ServiceType) && !sd.ServiceType.IsInterface && !sd.ServiceType.IsAbstract)
                        .ToList();
                    if (implementations.Count == 1)
                    {
                        typeToCreate = implementations[0].ServiceType;
                    }
                    else if (implementations.Count > 1)
                    {
                        throw new InvalidOperationException($"Multiple implementations found for interface {serviceType.Name}. Please register only one or use a more specific type.");
                    }
                    else
                    {
                        throw new InvalidOperationException($"No implementation found for interface {serviceType.Name}.");
                    }
                }
                instance = CreateInstance(typeToCreate, scope);
                _singletonInstances[serviceType] = instance;
            }
            return instance;
        };
    }

    private void CreateScopedFactory(ServiceDescriptor descriptor)
    {
        var serviceType = descriptor.ServiceType;

        _factories[serviceType] = scope =>
            scope.GetOrCreateScopedInstance(serviceType, () => CreateInstance(serviceType, scope));
    }

    private void CreateTransientFactory(ServiceDescriptor descriptor)
    {
        var serviceType = descriptor.ServiceType;
        _factories[serviceType] = scope => CreateInstance(serviceType, scope);
    }

    private object CreateInstance(Type type, Scope scope)
    {
        var constructor = type.GetConstructors().FirstOrDefault()
            ?? throw new InvalidOperationException($"No public constructor found for type {type.Name}");

        var parameters = constructor.GetParameters();
        var arguments = new object[parameters.Length];

        for (var i = 0; i < parameters.Length; i++)
        {
            var paramType = parameters[i].ParameterType;
            if (!_factories.TryGetValue(paramType, out var factory))
            {
                throw new InvalidOperationException($"Cannot resolve dependency {paramType.Name} for {type.Name}");
            }

            arguments[i] = factory(scope);
        }

        return constructor.Invoke(arguments);
    }

    /// <summary>
    /// Creates a new scope for resolving scoped services.
    /// </summary>
    /// <returns>A new <see cref="Scope"/> instance.</returns>
    /// <exception cref="InvalidOperationException">Thrown if the host has not been initialized.</exception>
    public Scope CreateScope()
        => !_initialized ? throw new InvalidOperationException("Host must be initialized before creating a scope.") : new Scope(this);

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

        using var scope = CreateScope();
        return GetService<T>(scope);
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

        // Try direct factory lookup first
        if (_factories.TryGetValue(type, out var factory))
            return (T)factory(scope);

        // If not found and type is interface, try to find a concrete implementation
        if (type.IsInterface)
        {
            // Find all descriptors that implement this interface
            var implementations = _serviceDescriptors
                .Where(sd => type.IsAssignableFrom(sd.ServiceType) && !sd.ServiceType.IsInterface && !sd.ServiceType.IsAbstract)
                .ToList();

            if (implementations.Count == 1)
            {
                var implType = implementations[0].ServiceType;
                if (_factories.TryGetValue(implType, out var implFactory))
                    return (T)implFactory(scope);
            }
            else if (implementations.Count > 1)
            {
                throw new InvalidOperationException($"Multiple implementations found for interface {type.Name}. Please register only one or use a more specific type.");
            }
        }

        throw new InvalidOperationException($"Service of type {type.Name} is not registered.");
    }
}