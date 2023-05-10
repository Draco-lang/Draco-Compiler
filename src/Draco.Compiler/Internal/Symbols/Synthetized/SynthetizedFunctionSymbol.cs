using Draco.Compiler.Internal.BoundTree;

namespace Draco.Compiler.Internal.Symbols.Synthetized;

/// <summary>
/// A function synthetized by the compiler.
/// </summary>
internal abstract class SynthetizedFunctionSymbol : FunctionSymbol
{
    public override Symbol? ContainingSymbol => null;
    public override Api.Semantics.Visibility Visibility => Api.Semantics.Visibility.Internal;

    public override string Name { get; }

    /// <summary>
    /// The body of this synthetized function.
    /// </summary>
    public abstract BoundStatement Body { get; }

    protected SynthetizedFunctionSymbol(string name)
    {
        this.Name = name;
    }
}
