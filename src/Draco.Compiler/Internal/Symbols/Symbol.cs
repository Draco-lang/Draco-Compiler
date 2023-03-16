using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Draco.Compiler.Api;
using Draco.Compiler.Internal.Utilities;

namespace Draco.Compiler.Internal.Symbols;

/// <summary>
/// Base for all symbols within the language.
/// </summary>
internal abstract partial class Symbol
{
    /// <summary>
    /// The <see cref="Compilation"/> that defines this symbol.
    /// </summary>
    public virtual Compilation? DeclaringCompilation => this.ContainingSymbol?.DeclaringCompilation;

    /// <summary>
    /// The symbol directly containing this one.
    /// </summary>
    public abstract Symbol? ContainingSymbol { get; }

    /// <summary>
    /// True, if this symbol represents some error.
    /// </summary>
    public virtual bool IsError => false;

    /// <summary>
    /// The name of this symbol.
    /// </summary>
    public virtual string Name => string.Empty;

    /// <summary>
    /// All the members within this symbol.
    /// </summary>
    public virtual IEnumerable<Symbol> Members => Enumerable.Empty<Symbol>();

    /// <summary>
    /// Documentation attached to this symbol.
    /// </summary>
    public virtual string Documentation => string.Empty;

    /// <summary>
    /// Converts this symbol into an API symbol.
    /// </summary>
    /// <returns>The equivalent API symbol.</returns>
    public abstract Api.Semantics.ISymbol ToApiSymbol();

    /// <summary>
    /// Converts the symbol-tree to a DOT graph for debugging purposes.
    /// </summary>
    /// <returns>The DOT graph of the symbol-tree.</returns>
    public string ToDot()
    {
        var builder = new DotGraphBuilder<Symbol>(isDirected: true);
        builder.WithName("SymbolTree");

        void Recurse(Symbol symbol)
        {
            builder!
                .AddVertex(symbol)
                .WithLabel($"{symbol.GetType().Name}\n{symbol.Name}");
            foreach (var m in symbol.Members)
            {
                builder.AddEdge(symbol, m);
                Recurse(m);
            }
        }

        Recurse(this);

        return builder.ToDot();
    }

    public override string ToString() => this is ITypedSymbol typed
        ? $"{this.Name}: {typed.Type}"
        : this.Name;
}
