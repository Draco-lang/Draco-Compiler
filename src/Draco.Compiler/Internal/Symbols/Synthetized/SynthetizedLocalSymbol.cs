using System;
using Draco.Compiler.Api.Semantics;

namespace Draco.Compiler.Internal.Symbols.Synthetized;

/// <summary>
/// A local generated by the compiler.
/// </summary>
internal sealed class SynthetizedLocalSymbol : LocalSymbol
{
    public override Types.Type Type => throw new NotImplementedException();

    public override bool IsMutable => throw new NotImplementedException();

    public override Symbol? ContainingSymbol => throw new NotImplementedException();

    public override ISymbol ToApiSymbol() => throw new System.NotImplementedException();
}
