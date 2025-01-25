using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Draco.Compiler.Api;
using Draco.Compiler.Api.Semantics;
using Draco.Compiler.Api.Syntax;
using Draco.Compiler.Internal.Documentation;
using Draco.Compiler.Internal.Symbols.Generic;
using Draco.Compiler.Internal.Symbols.Metadata;
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
    public virtual Symbol? ContainingSymbol => null;

    /// <summary>
    /// The ancestor chain of this symbol, starting with this one.
    /// </summary>
    public IEnumerable<Symbol> AncestorChain
    {
        get
        {
            for (var result = this; result is not null; result = result.ContainingSymbol) yield return result;
        }
    }

    /// <summary>
    /// Indicates, if this symbol translated to metadata would result in a .NET type.
    /// 
    /// In the .NET world, static classes are types too, but in Draco they are modules instead.
    /// </summary>
    public bool IsTypeOnCilLevel => this is TypeSymbol or ModuleSymbol;

    /// <summary>
    /// The root of this hierarchy.
    /// </summary>
    public Symbol? RootSymbol => this.AncestorChain.LastOrDefault();

    /// <summary>
    /// The root module of this hierarchy.
    /// </summary>
    public ModuleSymbol? RootModule => this.RootSymbol as ModuleSymbol;

    /// <summary>
    /// True, if this symbol represents some error.
    /// </summary>
    public virtual bool IsError => false;

    /// <summary>
    /// True, if this symbol represents special symbol.
    /// </summary>
    public virtual bool IsSpecialName => false;

    /// <summary>
    /// True, if this is a generic definition.
    /// </summary>
    public bool IsGenericDefinition => this.GenericParameters.Length > 0;

    /// <summary>
    /// True, if this is a generic instantiated symbol.
    /// </summary>
    public bool IsGenericInstance => this.GenericArguments.Length > 0;

    /// <summary>
    /// True, if this symbol is in a generic context.
    /// </summary>
    public bool IsInGenericContext =>
           this.IsGenericDefinition
        || (this.ContainingSymbol?.IsInGenericContext ?? false);

    /// <summary>
    /// The metadata name of this symbol which is used to reference it in CIL metadata.
    /// </summary>
    public virtual string MetadataName => this.Name;

    /// <summary>
    /// The name logical of this symbol that can be used to reference it in code.
    /// </summary>
    public virtual string Name => string.Empty;

    /// <summary>
    /// The fully qualified name of this symbol, generally used for debugging or error messages.
    /// </summary>
    public virtual string FullName
    {
        get
        {
            var parentFullName = this.ContainingSymbol?.FullName;
            var thisFullName = string.IsNullOrWhiteSpace(parentFullName)
                ? this.Name
                : $"{parentFullName}.{this.Name}";
            return $"{thisFullName}{this.GenericsToString()}";
        }
    }

    /// <summary>
    /// The fully qualified metadata name of this symbol that can be used to reference it in CIL metadata.
    /// </summary>
    public virtual string MetadataFullName
    {
        get
        {
            var parentFullName = this.ContainingSymbol is not MetadataAssemblySymbol
                ? this.ContainingSymbol?.MetadataFullName
                : null;

            return string.IsNullOrWhiteSpace(parentFullName)
                ? this.MetadataName
                : $"{parentFullName}.{this.MetadataName}";
        }
    }

    /// <summary>
    /// All the members within this symbol, including inherited ones.
    /// </summary>
    public virtual IEnumerable<Symbol> AllMembers => this.DefinedMembers;

    /// <summary>
    /// The members defined directly in this symbol.
    /// </summary>
    public virtual IEnumerable<Symbol> DefinedMembers => [];

    /// <summary>
    /// The static members within this symbol.
    /// </summary>
    public IEnumerable<Symbol> StaticMembers => this.AllMembers.Where(x => x is IMemberSymbol mem && mem.IsStatic);

    /// <summary>
    /// The instance members within this symbol.
    /// </summary>
    public IEnumerable<Symbol> InstanceMembers => this.AllMembers.Where(x => x is IMemberSymbol mem && !mem.IsStatic);

    /// <summary>
    /// The structured documentation attached to this symbol.
    /// </summary>
    public virtual SymbolDocumentation Documentation => SymbolDocumentation.Empty;

    /// <summary>
    /// The documentation of symbol as raw xml or markdown;
    /// </summary>
    internal virtual string RawDocumentation => string.Empty;

    /// <summary>
    /// The visibility of this symbol.
    /// </summary>
    public virtual Visibility Visibility => Visibility.Internal;

    /// <summary>
    /// The syntax declaring this symbol.
    /// </summary>
    public virtual SyntaxNode? DeclaringSyntax => null;

    /// <summary>
    /// The generic definition of this symbol, in case this is a generic instance.
    /// </summary>
    public virtual Symbol? GenericDefinition => null;

    /// <summary>
    /// The generic parameters of this symbol.
    /// </summary>
    public virtual ImmutableArray<TypeParameterSymbol> GenericParameters => [];

    /// <summary>
    /// The generic arguments that this symbol was instantiated with.
    /// </summary>
    public virtual ImmutableArray<TypeSymbol> GenericArguments => [];

    /// <summary>
    /// The attributes attached to this symbol.
    /// </summary>
    public virtual ImmutableArray<AttributeInstance> Attributes => [];

    /// <summary>
    /// The kind of this symbol.
    /// </summary>
    public abstract SymbolKind Kind { get; }

    public abstract void Accept(SymbolVisitor visitor);
    public abstract TResult Accept<TResult>(SymbolVisitor<TResult> visitor);

    /// <summary>
    /// Checks if this symbol can be shadowed by <paramref name="other"/> symbol.
    /// </summary>
    /// <param name="other">The other symbol.</param>
    /// <returns>True, if this symbol can be shadowed by <paramref name="other"/> symbol,
    /// otherwise false.</returns>
    public virtual bool CanBeShadowedBy(Symbol other) => this.Name == other.Name;

    /// <summary>
    /// Instantiates this generic symbol with the given substitutions.
    /// </summary>
    /// <param name="containingSymbol">The symbol that should be considered the containing symbol.</param>
    /// <param name="arguments">The generic arguments.</param>
    /// <returns>The instantiated symbol.</returns>
    public virtual Symbol GenericInstantiate(Symbol? containingSymbol, ImmutableArray<TypeSymbol> arguments)
    {
        if (this.GenericParameters.Length != arguments.Length)
        {
            throw new ArgumentException(
                $"the number of generic parameters ({this.GenericParameters.Length}) does not match the passed in number of arguments ({arguments.Length})",
                nameof(arguments));
        }

        var substitutions = this.GenericParameters
            .Zip(arguments)
            .ToImmutableDictionary(pair => pair.First, pair => pair.Second);
        var context = new GenericContext(substitutions);
        return this.GenericInstantiate(containingSymbol, context);
    }

    /// <summary>
    /// Instantiates this generic symbol with the given substitutions.
    /// </summary>
    /// <param name="containingSymbol">The symbol that should be considered the containing symbol.</param>
    /// <param name="context">The generic context.</param>
    /// <returns>This symbol with all type parameters replaced according to <paramref name="context"/>.</returns>
    public virtual Symbol GenericInstantiate(Symbol? containingSymbol, GenericContext context) =>
        throw new NotSupportedException();

    /// <summary>
    /// Converts this symbol into an API symbol.
    /// </summary>
    /// <returns>The equivalent API symbol.</returns>
    public abstract ISymbol ToApiSymbol();

    /// <summary>
    /// Checks, if this symbol is visible from another symbol.
    /// </summary>
    /// <param name="from">The symbol from which visibility (access) is checked.</param>
    /// <returns>True, if this symbol is visible from <paramref name="from"/> symbol.</returns>
    public bool IsVisibleFrom(Symbol? from)
    {
        var to = this;

        // Unwrap generics
        if (from?.IsGenericInstance == true) from = from.GenericDefinition!;
        if (to.IsGenericInstance) to = to.GenericDefinition!;

        if (ReferenceEquals(from, to)) return true;

        if (from is null) return true;

        if (to.Visibility == Visibility.Private)
        {
            // This is a private symbol, only visible from the same or nested module
            if (to.ContainingSymbol is null) return false;
            return from.AncestorChain.Contains(to.ContainingSymbol, SymbolEqualityComparer.Default);
        }

        if (to.Visibility == Visibility.Internal)
        {
            // They HAVE TO be from the same assembly
            // For that, we can check if the root module is the same
            if (!SymbolEqualityComparer.Default.Equals(from.RootSymbol, to.RootSymbol)) return false;

            // But other than that, the containing symbol has to be accessible
            return to.ContainingSymbol?.IsVisibleFrom(from) ?? true;
        }

        if (to.Visibility == Visibility.Public)
        {
            // The containing symbol has to be accessible
            return to.ContainingSymbol?.IsVisibleFrom(from) ?? true;
        }

        throw new InvalidOperationException("unknown visibility");
    }

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
                .WithLabel($"{symbol.GetType().Name}\n{symbol}");
            foreach (var m in symbol.AllMembers)
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

    /// <summary>
    /// Turns the generic list of this symbol into a string representation.
    /// </summary>
    /// <returns>The generic parameters or arguments between angle brackets, or an empty string,
    /// if this symbol is not a generic definition or instantiation.</returns>
    private protected string GenericsToString()
    {
        if (this.IsGenericDefinition) return $"<{string.Join(", ", this.GenericParameters)}>";
        if (this.IsGenericInstance) return $"<{string.Join(", ", this.GenericArguments)}>";
        return string.Empty;
    }

    /// <summary>
    /// Translates the given token kind into a visibility modifier. On null, private is assumed.
    /// </summary>
    /// <param name="kind">The token kind to translate.</param>
    /// <returns>The visibility modifier that <paramref name="kind"/> denotes.</returns>
    private protected static Visibility GetVisibilityFromTokenKind(TokenKind? kind) => kind switch
    {
        null => Visibility.Private,
        TokenKind.KeywordInternal => Visibility.Internal,
        TokenKind.KeywordPublic => Visibility.Public,
        _ => throw new InvalidOperationException($"illegal visibility modifier token {kind}"),
    };

    /// <summary>
    /// Retrieves additional symbols for this symbol that should live in the same scope as this symbol itself.
    /// This returns the constructor functions for types for example.
    /// </summary>
    /// <returns>The additional symbols for this symbol.</returns>
    protected internal virtual IEnumerable<Symbol> GetAdditionalSymbols() => [];
}
