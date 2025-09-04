namespace SimpleInjection.Injection;

/// <summary>
/// Represents a scope for dependency resolution within the dependency injection system.
/// </summary>
/// <remarks>
/// Initializes a new scope with a resolver function.
/// </remarks>
/// <param name="resolver">
/// A function that takes a service type and the current scope, and returns an instance.
/// </param>
public class Scope(Func<Type, Scope, object> resolver) : IDisposable
{
    private readonly Func<Type, Scope, object> _resolver = resolver ?? throw new ArgumentNullException(nameof(resolver));
    private readonly Dictionary<Type, object> _scopedInstances = [];
    private bool _disposed;

    /// <summary>
    /// Resolves a service of the specified type from the scope.
    /// </summary>
    public T Get<T>() where T : class => _disposed ? throw new ObjectDisposedException(nameof(Scope)) : (T)_resolver(typeof(T), this);

    /// <summary>
    /// Gets or creates a scoped instance of the specified type.
    /// </summary>
    public object GetOrCreateScopedInstance(Type type, Func<object> factory)
    {
        if (!_scopedInstances.TryGetValue(type, out var instance))
        {
            instance = factory();
            _scopedInstances.Add(type, instance);
        }
        return instance;
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            foreach (var instance in _scopedInstances.Values)
                (instance as IDisposable)?.Dispose();

            _scopedInstances.Clear();
            _disposed = true;
        }
        GC.SuppressFinalize(this);
    }
}
