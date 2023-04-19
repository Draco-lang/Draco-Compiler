using System.Collections.Immutable;

namespace Draco.Compiler.Internal.Symbols;

/// <summary>
/// Represents a function type.
/// </summary>
internal sealed class FunctionTypeSymbol : TypeSymbol
{
    /// <summary>
    /// The parameters of the function.
    /// </summary>
    public ImmutableArray<ParameterSymbol> Parameters { get; }

    /// <summary>
    /// The return type of the function.
    /// </summary>
    public TypeSymbol ReturnType { get; }

    public override Symbol? ContainingSymbol => null;

    public FunctionTypeSymbol(ImmutableArray<ParameterSymbol> parameters, TypeSymbol returnType)
    {
        this.Parameters = parameters;
        this.ReturnType = returnType;
    }

    public override bool ContainsTypeVariable(TypeVariable variable)
    {
        for (var i = 0; i < this.Parameters.Length; ++i)
        {
            if (ReferenceEquals(this.Parameters[i].Type, variable)) return true;
        }
        return ReferenceEquals(this.ReturnType, variable);
    }

    public override string ToString() =>
        $"({string.Join(", ", this.Parameters)}) -> {this.ReturnType}";
}
