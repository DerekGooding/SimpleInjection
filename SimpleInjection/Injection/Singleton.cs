namespace SimpleInjection.Injection;

internal class Singleton(Type type)
{
    internal Type Type { get; } = type;

    private object? _instance;

    public object Instance
    {
        get => _instance ?? throw new Exception("Host not properly initialized.");
        set => _instance = value;
    }

    internal List<Type> Dependencies => [.. Type.GetConstructors()
                .SelectMany(c => c.GetParameters())
                .Select(p => p.ParameterType)];

    internal void Initialize(params object[] args)
    {
        if (_instance != null)
            throw new InvalidOperationException("Instance is already initialized.");

        var constructor = Type.GetConstructors().FirstOrDefault()
            ?? throw new InvalidOperationException("No public constructors found for the type.");

        _instance = constructor.Invoke(args);
    }
}
