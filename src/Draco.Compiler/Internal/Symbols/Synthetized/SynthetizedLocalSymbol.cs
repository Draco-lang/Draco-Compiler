using Draco.Compiler.Api.Semantics;
using Draco.Compiler.Internal.Types;

namespace Draco.Compiler.Internal.Symbols.Synthetized;

/// <summary>
/// A local generated by the compiler.
/// </summary>
internal sealed class SynthetizedLocalSymbol : LocalSymbol
{
    public override Type Type { get; }
    public override bool IsMutable { get; }
    public override Symbol? ContainingSymbol => null;

    public SynthetizedLocalSymbol(Type type, bool isMutable)
    {
        this.Type = type;
        this.IsMutable = isMutable;
    }

    public override ISymbol ToApiSymbol() => new Api.Semantics.LocalSymbol(this);
}
