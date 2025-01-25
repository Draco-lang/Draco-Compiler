using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Draco.Compiler.Api;
using Draco.Compiler.Api.Syntax;
using Draco.Compiler.Internal.Symbols;
using Draco.Compiler.Internal.Symbols.Synthetized;
using Draco.Compiler.Internal.Utilities;

namespace Draco.Compiler.Internal.Binding;

/// <summary>
/// Binds the globally imported symbols.
/// </summary>
internal sealed class GlobalImportsBinder : Binder
{
    public override IEnumerable<Symbol> DeclaredSymbols =>
        InterlockedUtils.InitializeDefault(ref this.delcaredSymbols, this.BuildDeclaredSymbols);
    private ImmutableArray<Symbol> delcaredSymbols;

    public GlobalImportsBinder(Compilation compilation)
        : base(compilation)
    {
    }

    public GlobalImportsBinder(Binder parent)
        : base(parent)
    {
    }

    internal override void LookupLocal(LookupResult result, string name, ref LookupFlags flags, Predicate<Symbol> allowSymbol, SyntaxNode? currentReference)
    {
        foreach (var symbol in this.DeclaredSymbols)
        {
            if (symbol.Name != name) continue;
            if (!allowSymbol(symbol)) continue;
            result.Add(symbol);
        }
    }

    private ImmutableArray<Symbol> BuildDeclaredSymbols()
    {
        var globalImports = this.Compilation.GlobalImports;

        var result = ImmutableArray.CreateBuilder<Symbol>();

        if (!globalImports.ModuleImports.IsDefaultOrEmpty)
        {
            foreach (var path in globalImports.ModuleImports)
            {
                var parts = path.Split('.', StringSplitOptions.TrimEntries).ToImmutableArray();
                var symbol = this.Compilation.RootModule.Lookup(parts).SingleOrDefault();
                if (symbol is not ModuleSymbol module)
                {
                    throw new InvalidOperationException($"the path '{path}' is invalid for global imports");
                }
                result.AddRange(module.AllMembers);
            }
        }

        if (!globalImports.ImportAliases.IsDefaultOrEmpty)
        {
            foreach (var (aliasName, aliasPath) in globalImports.ImportAliases)
            {
                var parts = aliasPath.Split('.', StringSplitOptions.TrimEntries).ToImmutableArray();
                var symbol = this.Compilation.RootModule.Lookup(parts).SingleOrDefault();
                if (symbol is null)
                {
                    throw new InvalidOperationException($"no symbol found to alias as '{aliasName}' for path '{aliasPath}'");
                }
                result.Add(new SynthetizedAliasSymbol(aliasName, symbol));
            }
        }

        return result.ToImmutable();
    }
}
