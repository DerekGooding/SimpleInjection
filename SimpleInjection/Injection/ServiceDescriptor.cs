namespace SimpleInjection.Injection;

/// <summary>
/// Describes a service with its implementation type and lifetime.
/// </summary>
/// <remarks>
/// This class is used by the <see cref="Host"/> to manage the registration and resolution
/// of services based on their specified lifetime.
/// </remarks>
/// <remarks>
/// Initializes a new instance of the <see cref="ServiceDescriptor"/> class.
/// </remarks>
/// <param name="serviceType">The type of the service.</param>
/// <param name="lifetime">The lifetime of the service.</param>
public class ServiceDescriptor(Type serviceType, Type implementationType, ServiceLifetime lifetime)
{
    /// <summary>
    /// Gets the type of the service (usually an interface).
    /// </summary>
    public Type ServiceType { get; } = serviceType;

    /// <summary>
    /// Gets the concrete type that implements the service.
    /// </summary>
    public Type ImplementationType { get; } = implementationType;

    /// <summary>
    /// Gets the lifetime of the service.
    /// </summary>
    public ServiceLifetime Lifetime { get; } = lifetime;

    /// <summary>
    /// Gets the list of types that this service depends on.
    /// </summary>
    public List<Type> Dependencies => [.. ImplementationType.GetConstructors()
        .SelectMany(c => c.GetParameters())
        .Select(p => p.ParameterType)];
}