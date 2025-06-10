namespace SimpleInjection.Generator;

/// <summary>
/// Represents a collection of values indexed by a key of type <typeparamref name="Tkey"/>.
/// </summary>
/// <typeparam name="Tkey">The type of the key, which must implement <see cref="INamed"/>.</typeparam>
/// <typeparam name="Tvalue">The type of the value stored in the collection.</typeparam>
public interface ISubContent<Tkey, Tvalue> where Tkey : INamed
{
    /// <summary>
    /// Gets a dictionary that maps each <typeparamref name="Tkey"/> to its corresponding <typeparamref name="Tvalue"/>.
    /// </summary>
    Dictionary<Tkey, Tvalue> ByKey { get; }

    /// <summary>
    /// Gets the value associated with the specified <typeparamref name="Tkey"/>.
    /// </summary>
    /// <param name="key">The key whose value to get.</param>
    /// <returns>The value associated with the specified key.</returns>
    Tvalue this[Tkey key] { get; }
}
