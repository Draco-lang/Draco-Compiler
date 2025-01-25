using System;
using System.Collections.Generic;
using Draco.Compiler.Api;
using Draco.Compiler.Api.Syntax;
using Draco.Compiler.Internal.Symbols;

namespace Draco.Compiler.Internal.Binding;

// NOTE: Not sealed, as script module binder is derived from this
/// <summary>
/// Binds on a module level.
/// </summary>
internal class ModuleBinder : Binder
{
    public override SyntaxNode? DeclaringSyntax => this.symbol.DeclaringSyntax;

    public override Symbol? ContainingSymbol => this.symbol;

    public override IEnumerable<Symbol> DeclaredSymbols => this.symbol.AllMembers;

    private readonly ModuleSymbol symbol;

    public ModuleBinder(Compilation compilation, ModuleSymbol symbol)
        : base(compilation)
    {
        this.symbol = symbol;
    }

    public ModuleBinder(Binder parent, ModuleSymbol symbol)
        : base(parent)
    {
        this.symbol = symbol;
    }

    internal override void LookupLocal(LookupResult result, string name, ref LookupFlags flags, Predicate<Symbol> allowSymbol, SyntaxNode? currentReference)
    {
        foreach (var symbol in this.symbol.AllMembers)
        {
            if (symbol.Name != name) continue;
            if (!allowSymbol(symbol)) continue;
            // NOTE: These are here to disallow self-referencing
            if (symbol is FieldSymbol { IsStatic: true } && !flags.HasFlag(LookupFlags.AllowGlobals)) continue;
            if (symbol is PropertySymbol { IsStatic: true } && !flags.HasFlag(LookupFlags.AllowGlobals)) continue;
            result.Add(symbol);
        }
    }
}
