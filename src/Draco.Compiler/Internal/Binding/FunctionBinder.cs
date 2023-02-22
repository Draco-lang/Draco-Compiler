using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Draco.Compiler.Api.Syntax;
using Draco.Compiler.Internal.Symbols;

namespace Draco.Compiler.Internal.Binding;

/// <summary>
/// Binds on a function level, including its parameters.
/// </summary>
internal sealed class FunctionBinder : Binder
{
    public override Symbol? ContainingSymbol => this.symbol;

    private readonly FunctionSymbol symbol;

    public FunctionBinder(Binder parent, FunctionSymbol symbol)
        : base(parent)
    {
        this.symbol = symbol;
    }

    public override void LookupValueSymbol(LookupResult result, string name, SyntaxNode? reference) =>
        throw new NotImplementedException();

    public override void LookupTypeSymbol(LookupResult result, string name, SyntaxNode? reference) =>
        throw new NotImplementedException();
}
