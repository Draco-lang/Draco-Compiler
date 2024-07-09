using System.Collections.Immutable;

namespace Draco.Compiler.Internal.Symbols;

/// <summary>
/// Represents a function type.
/// </summary>
internal sealed class FunctionTypeSymbol(
    ImmutableArray<ParameterSymbol> parameters,
    TypeSymbol returnType) : TypeSymbol
{
    /// <summary>
    /// The parameters of the function.
    /// </summary>
    public ImmutableArray<ParameterSymbol> Parameters { get; } = parameters;

    /// <summary>
    /// The return type of the function.
    /// </summary>
    public TypeSymbol ReturnType { get; } = returnType;

    public override string ToString() =>
        $"({string.Join(", ", this.Parameters)}) -> {this.ReturnType}";
}
