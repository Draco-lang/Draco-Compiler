using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Draco.Compiler.Internal.Symbols.Error;

/// <summary>
/// Represents an illegal, in-source type reference.
/// </summary>
internal sealed class ErrorTypeSymbol : TypeSymbol
{
    public override bool IsError => true;
    public override Types.Type Type => Types.Intrinsics.Error;
    public override Symbol? ContainingSymbol => throw new NotImplementedException();

    public override string Name { get; }

    public ErrorTypeSymbol(string name)
    {
        this.Name = name;
    }

    public override Api.Semantics.ISymbol ToApiSymbol() => new Api.Semantics.TypeSymbol(this);
}
