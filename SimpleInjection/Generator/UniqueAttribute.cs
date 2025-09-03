namespace SimpleInjection.Generator;

/// <summary>
/// Marks a struct as unique for content generation. When applied to structs used in <see cref="IContent{T}"/> implementations,
/// the source generator will override the record struct's default equality behavior to use array index-based comparison
/// instead of comparing all properties.
/// </summary>
/// <remarks>
/// <para>
/// The <see cref="UniqueAttribute"/> should be applied to record structs that represent content items in arrays
/// where position determines identity. This is particularly useful for static content resources where:
/// </para>
/// <list type="bullet">
/// <item><description>The struct's position in the content array uniquely identifies it</description></item>
/// <item><description>Performance-critical equality comparisons are needed (O(1) instead of O(n))</description></item>
/// <item><description>The struct is used as a key in collections or lookups</description></item>
/// <item><description>The struct is used as a key in <see cref="ISubContent{TKey, TValue}"/> implementations</description></item>
/// </list>
/// <para>
/// When this attribute is present, the source generator creates:
/// </para>
/// <list type="number">
/// <item><description>A static dictionary mapping struct names to their array indices</description></item>
/// <item><description>Custom <see cref="object.Equals(object)"/> and <see cref="object.GetHashCode"/> implementations that use the array index</description></item>
/// <item><description>Optimized implementation based on whether the struct is declared as readonly</description></item>
/// </list>
/// <para>
/// <strong>Performance Benefits:</strong>
/// </para>
/// <list type="bullet">
/// <item><description>Equality comparison: O(1) integer comparison vs O(n) property comparison</description></item>
/// <item><description>Hash code generation: O(1) integer return vs O(n) property hashing</description></item>
/// <item><description>Dictionary/HashSet operations: Significantly faster lookups and insertions</description></item>
/// <item><description><strong>ISubContent optimization:</strong> When used as keys in <see cref="ISubContent{TKey, TValue}.ByKey"/> dictionaries, provides dramatically improved performance for dictionary operations due to optimized hashing and equality</description></item>
/// </list>
/// <para>
/// <strong>Readonly vs Non-Readonly Optimization:</strong>
/// </para>
/// <list type="bullet">
/// <item><description><strong>Readonly structs:</strong> Direct dictionary lookup on each access, eliminates defensive copying</description></item>
/// <item><description><strong>Non-readonly structs:</strong> Cached index lookup for repeated operations, potential defensive copying</description></item>
/// </list>
/// <para>
/// <strong>ISubContent Performance Impact:</strong>
/// </para>
/// <para>
/// When structs marked with <see cref="UniqueAttribute"/> are used as keys in <see cref="ISubContent{TKey, TValue}"/> 
/// implementations, the performance improvement is substantial:
/// </para>
/// <list type="bullet">
/// <item><description><strong>Dictionary.TryGetValue():</strong> Much faster key lookup due to optimized hash codes</description></item>
/// <item><description><strong>Dictionary.Add():</strong> Faster insertion with reduced hash collisions</description></item>
/// <item><description><strong>Dictionary enumeration:</strong> More predictable performance due to consistent hashing</description></item>
/// <item><description><strong>Memory efficiency:</strong> Reduced memory allocation from fewer hash collisions and bucket redistributions</description></item>
/// </list>
/// <para>
/// <strong>Usage Requirements:</strong>
/// </para>
/// <list type="bullet">
/// <item><description>Must be applied to record structs that implement or are used with content interfaces</description></item>
/// <item><description>The struct must have a <c>Name</c> property that matches the content item's identifier</description></item>
/// <item><description>Content items should be defined in static arrays where order is meaningful</description></item>
/// <item><description>Declaring the struct as <c>readonly</c> is optional but recommended for optimal performance</description></item>
/// <item><description>Particularly beneficial when the struct implements <see cref="INamed"/> and is used as a key type in <see cref="ISubContent{TKey, TValue}"/></description></item>
/// </list>
/// </remarks>
/// <example>
/// <code>
/// // Readonly version (recommended for best performance)
/// [Unique]
/// public readonly partial record struct Material(string Name, int Durability, Color Color) : INamed;
/// 
/// // Non-readonly version (still optimized with caching)
/// [Unique]
/// public partial record struct Material(string Name, int Durability, Color Color) : INamed;
/// 
/// public partial class Materials : IContent&lt;Material&gt;
/// {
///     public static Material[] All = [
///         new("Iron", 100, Color.Gray),
///         new("Gold", 50, Color.Yellow),
///         new("Diamond", 500, Color.White)
///     ];
/// }
/// 
/// // Usage with ISubContent for high-performance lookups:
/// public class MaterialProperties : ISubContent&lt;Material, PropertyData&gt;
/// {
///     public Dictionary&lt;Material, PropertyData&gt; ByKey { get; } = new()
///     {
///         [Materials.Iron] = new PropertyData(/* ... */),
///         [Materials.Gold] = new PropertyData(/* ... */),
///         // Fast dictionary operations due to optimized Material equality/hashing
///     };
///     
///     public PropertyData this[Material key] => ByKey[key]; // O(1) lookup
/// }
/// 
/// // Generated code enables fast index-based equality:
/// var iron1 = materials.Iron;
/// var iron2 = materials.Get(MaterialsType.Iron);
/// var areEqual = iron1.Equals(iron2); // Uses index comparison, not all properties
/// </code>
/// </example>
[AttributeUsage(AttributeTargets.Struct)]
public class UniqueAttribute : Attribute;