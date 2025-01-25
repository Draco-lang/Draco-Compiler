using System.Collections.Immutable;
using System.Linq;
using System.Reflection;

namespace Draco.Compiler.Internal.Symbols;

/// <summary>
/// An attribute instance attached to a symbol.
/// </summary>
internal sealed class AttributeInstance(
    FunctionSymbol constructor,
    ImmutableArray<ConstantValue> fixedArguments,
    ImmutableDictionary<string, ConstantValue> namedArguments)
{
    /// <summary>
    /// The type of the attribute.
    /// </summary>
    public TypeSymbol Type => (TypeSymbol)this.Constructor.ContainingSymbol!;

    /// <summary>
    /// The attribute constructor.
    /// </summary>
    public FunctionSymbol Constructor { get; } = constructor;

    /// <summary>
    /// The fixed arguments of the attribute.
    /// </summary>
    public ImmutableArray<ConstantValue> FixedArguments { get; } = fixedArguments;

    /// <summary>
    /// The named arguments of the attribute.
    /// </summary>
    public ImmutableDictionary<string, ConstantValue> NamedArguments { get; } = namedArguments;

    private object? builtAttribute = null;

    /// <summary>
    /// Translates this attribute instance to an attribute of type <typeparamref name="T"/>.
    /// </summary>
    /// <typeparam name="T">The type of attribute to translate to.</typeparam>
    /// <returns>The attribute instance as an attribute of type <typeparamref name="T"/>.</returns>
    public T ToAttribute<T>()
        where T : System.Attribute
    {
        this.builtAttribute ??= this.BuildAttribute<T>();
        return (T)this.builtAttribute;
    }

    private T BuildAttribute<T>()
        where T : System.Attribute
    {
        // Search for the constructor
        var ctorInfo = typeof(T).GetConstructors().FirstOrDefault(this.SignatureMatches);
        if (ctorInfo is null) throw new System.ArgumentException("no matching constructor found");

        // Translate potential enum value arguments
        var args = this.FixedArguments
            .Zip(ctorInfo.GetParameters())
            .Select(pair => pair.Second.ParameterType.IsEnum
                ? System.Enum.ToObject(pair.Second.ParameterType, pair.First.Value!)
                : pair.First.Value)
            .ToArray();

        // Instantiate
        var instance = (T)ctorInfo.Invoke(args);

        // Fill in properties with named arguments
        foreach (var (argName, argValue) in this.NamedArguments)
        {
            var propInfo = typeof(T).GetProperty(argName);
            if (propInfo is null) throw new System.ArgumentException("no matching property found");

            propInfo.SetValue(instance, argValue.Value);
        }

        return instance;
    }

    private bool SignatureMatches(ConstructorInfo ctorInfo)
    {
        var ctorParams = ctorInfo.GetParameters();
        if (this.Constructor.Parameters.Length != ctorParams.Length) return false;

        // Compare arguments sequentially
        for (var i = 0; i < this.Constructor.Parameters.Length; i++)
        {
            var internalCtorParam = this.Constructor.Parameters[i];
            var reflCtorParam = ctorParams[i];

            if (internalCtorParam.Name != reflCtorParam.Name) return false;
            if (!this.TypesMatch(reflCtorParam.ParameterType, internalCtorParam.Type)) return false;
        }

        return true;
    }

    private bool TypesMatch(System.Type reflType, TypeSymbol internalType)
    {
        if (reflType.IsEnum && internalType.IsEnumType)
        {
            if (reflType.FullName != internalType.FullName) return false;
            reflType = System.Enum.GetUnderlyingType(reflType);
            internalType = internalType.EnumUnderlyingType!;
        }

        var wellKnownTypes = this.Constructor.DeclaringCompilation?.WellKnownTypes;
        if (wellKnownTypes is null) throw new System.NotImplementedException();

        var translatedReflType = wellKnownTypes.TranslatePrimitive(reflType);
        if (translatedReflType is null) throw new System.NotImplementedException();

        return SymbolEqualityComparer.Default.Equals(translatedReflType, internalType);
    }
}
