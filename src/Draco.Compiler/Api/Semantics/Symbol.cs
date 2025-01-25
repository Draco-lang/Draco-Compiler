using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using Draco.Compiler.Api.Diagnostics;
using Draco.Compiler.Internal.Symbols;

namespace Draco.Compiler.Api.Semantics;

// Interfaces //////////////////////////////////////////////////////////////////

// TODO: Kill the "IEquatable" and expose a symbol equality comparer

/// <summary>
/// Represents a symbol in the language.
/// </summary>
public interface ISymbol : IEquatable<ISymbol>
{
    /// <summary>
    /// The name of the symbol.
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// The full name of the symbol.
    /// </summary>
    public string FullName { get; }

    /// <summary>
    /// The kind of symbol.
    /// </summary>
    public SymbolKind Kind { get; }

    /// <summary>
    /// True, if this symbol represents an error.
    /// </summary>
    public bool IsError { get; }

    /// <summary>
    /// True, if this symbol represents special symbol.
    /// </summary>
    public bool IsSpecialName { get; }

    /// <summary>
    /// The location where this symbol was defined.
    /// </summary>
    public Location? Definition { get; }

    /// <summary>
    /// Documentation attached to this symbol.
    /// </summary>
    public string Documentation { get; }

    /// <summary>
    /// All the members within this symbol.
    /// </summary>
    public IEnumerable<ISymbol> Members { get; }

    /// <summary>
    /// The static members within this symbol.
    /// </summary>
    public IEnumerable<ISymbol> StaticMembers => this.Members.Where(x => x is IMemberSymbol mem && mem.IsStatic);

    /// <summary>
    /// The instance members within this symbol.
    /// </summary>
    public IEnumerable<ISymbol> InstanceMembers => this.Members.Where(x => x is IMemberSymbol mem && !mem.IsStatic);

    /// <summary>
    /// True, if this symbol is a generic definition.
    /// </summary>
    public bool IsGenericDefinition { get; }

    /// <summary>
    /// True, if this symbol is a generic instance.
    /// </summary>
    [MemberNotNullWhen(true, nameof(GenericDefinition))]
    public bool IsGenericInstance { get; }

    /// <summary>
    /// The generic definition of this symbol, in case this is a generic instance.
    /// </summary>
    public ISymbol? GenericDefinition { get; }

    /// <summary>
    /// The generic arguments of this symbol, in case this is a generic instance.
    /// </summary>
    public IEnumerable<ITypeSymbol> GenericArguments { get; }
}

/// <summary>
/// Represents a symbol that has type.
/// </summary>
public interface ITypedSymbol
{
    /// <summary>
    /// The type of this symbol.
    /// </summary>
    public ITypeSymbol Type { get; }
}

/// <summary>
/// Represents any member symbol.
/// </summary>
public interface IMemberSymbol
{
    /// <summary>
    /// Specifying if given symbol is static.
    /// </summary>
    public bool IsStatic { get; }
}

/// <summary>
/// Represents a module symbol.
/// </summary>
public interface IModuleSymbol : ISymbol, IMemberSymbol
{
}

/// <summary>
/// Represents a variable symbol.
/// </summary>
public interface IVariableSymbol : ISymbol, ITypedSymbol
{
    /// <summary>
    /// True, if this is a mutable variable.
    /// </summary>
    public bool IsMutable { get; }
}

/// <summary>
/// Represents a field symbol.
/// </summary>
public interface IFieldSymbol : IVariableSymbol, IMemberSymbol
{
}

/// <summary>
/// Represents a property symbol.
/// </summary>
public interface IPropertySymbol : ISymbol, ITypedSymbol, IMemberSymbol
{
    public IFunctionSymbol? Getter { get; }
    public IFunctionSymbol? Setter { get; }
}

/// <summary>
/// Represents a global variable symbol.
/// </summary>
public interface IGlobalSymbol : IVariableSymbol, IMemberSymbol
{
}
/// <summary>
/// Represents a local variable symbol.
/// </summary>
public interface ILocalSymbol : IVariableSymbol
{
}

/// <summary>
/// Represents a parameter symbol.
/// </summary>
public interface IParameterSymbol : IVariableSymbol
{
}

/// <summary>
/// Represents a parameter symbol.
/// </summary>
public interface IFunctionSymbol : ISymbol, ITypedSymbol, IMemberSymbol
{
    /// <summary>
    /// The parameters this function defines.
    /// </summary>
    public ImmutableArray<IParameterSymbol> Parameters { get; }

    public ITypeSymbol ReturnType { get; }
}

/// <summary>
/// Represents a function group.
/// </summary>
public interface IFunctionGroupSymbol : ISymbol
{
    /// <summary>
    /// The functions in this group.
    /// </summary>
    public ImmutableArray<IFunctionSymbol> Functions { get; }
}

/// <summary>
/// Represents a type symbol.
/// </summary>
public interface ITypeSymbol : ISymbol, IMemberSymbol
{
    /// <summary>
    /// True, if this type is a value type.
    /// </summary>
    public bool IsValueType { get; }
}

/// <summary>
/// Represents an alias symbol.
/// </summary>
public interface IAliasSymbol : ISymbol, IMemberSymbol
{
    /// <summary>
    /// The symbol this alias substitutes.
    /// </summary>
    public ISymbol Substitution { get; }

    /// <summary>
    /// The fully resolved symbol this alias substitutes, meaning all alias chains are resolved.
    /// </summary>
    public ISymbol FullResolution { get; }
}

/// <summary>
/// Represents a type parameter symbol.
/// </summary>
public interface ITypeParameterSymbol : ITypeSymbol
{
}

/// <summary>
/// Represents a label symbol.
/// </summary>
public interface ILabelSymbol : ISymbol
{
}

// Base classes ////////////////////////////////////////////////////////////////

internal abstract class SymbolBase(Symbol symbol) : ISymbol
{
    public Symbol Symbol { get; } = symbol;

    public string Name => this.Symbol.Name;
    public string FullName => this.Symbol.FullName;
    public SymbolKind Kind => this.Symbol.Kind;
    public bool IsError => this.Symbol.IsError;
    public bool IsSpecialName => this.Symbol.IsSpecialName;
    public Location? Definition => this.Symbol.DeclaringSyntax?.Location;
    public string Documentation => this.Symbol.Documentation.ToMarkdown();
    public IEnumerable<ISymbol> Members => this.Symbol.AllMembers.Select(x => x.ToApiSymbol());
    public bool IsGenericDefinition => this.Symbol.IsGenericDefinition;
    public bool IsGenericInstance => this.Symbol.IsGenericInstance;
    public ISymbol? GenericDefinition => this.Symbol.GenericDefinition?.ToApiSymbol();
    public IEnumerable<ITypeSymbol> GenericArguments => this.Symbol.GenericArguments.Select(a => a.ToApiSymbol());

    public override bool Equals(object? obj) => this.Equals(obj as ISymbol);
    public bool Equals(ISymbol? other) => other is SymbolBase o
                                       && ReferenceEquals(this.Symbol, o.Symbol);

    public override int GetHashCode() => this.Symbol.GetHashCode();
}

internal abstract class SymbolBase<TInternalSymbol>(TInternalSymbol symbol) : SymbolBase(symbol)
    where TInternalSymbol : Symbol
{
    public new TInternalSymbol Symbol => (TInternalSymbol)base.Symbol;

    public override string ToString() => this.Symbol.ToString();
}

// Proxy classes ///////////////////////////////////////////////////////////////

internal sealed class ModuleSymbol(Internal.Symbols.ModuleSymbol module)
    : SymbolBase<Internal.Symbols.ModuleSymbol>(module), IModuleSymbol
{
    public bool IsStatic => this.Symbol.IsStatic;
}

internal sealed class FieldSymbol(Internal.Symbols.FieldSymbol field)
    : SymbolBase<Internal.Symbols.FieldSymbol>(field), IFieldSymbol
{
    public bool IsMutable => this.Symbol.IsMutable;
    public bool IsStatic => this.Symbol.IsStatic;
    public ITypeSymbol Type => this.Symbol.Type.ToApiSymbol();
}

internal sealed class PropertySymbol(Internal.Symbols.PropertySymbol property)
    : SymbolBase<Internal.Symbols.PropertySymbol>(property), IPropertySymbol
{
    public bool IsStatic => this.Symbol.IsStatic;
    public ITypeSymbol Type => this.Symbol.Type.ToApiSymbol();

    public IFunctionSymbol? Getter => this.Symbol.Getter?.ToApiSymbol();
    public IFunctionSymbol? Setter => this.Symbol.Setter?.ToApiSymbol();
}

internal sealed class LocalSymbol(Internal.Symbols.LocalSymbol local)
    : SymbolBase<Internal.Symbols.LocalSymbol>(local), ILocalSymbol
{
    public bool IsMutable => this.Symbol.IsMutable;
    public ITypeSymbol Type => this.Symbol.Type.ToApiSymbol();
}

internal sealed class ParameterSymbol(Internal.Symbols.ParameterSymbol parameter)
    : SymbolBase<Internal.Symbols.ParameterSymbol>(parameter), IParameterSymbol
{
    public bool IsMutable => this.Symbol.IsMutable;
    public ITypeSymbol Type => this.Symbol.Type.ToApiSymbol();
}

internal sealed class FunctionSymbol(Internal.Symbols.FunctionSymbol function)
    : SymbolBase<Internal.Symbols.FunctionSymbol>(function), IFunctionSymbol
{
    public ITypeSymbol Type => this.Symbol.Type.ToApiSymbol();
    public ITypeSymbol ReturnType => this.Symbol.ReturnType.ToApiSymbol();
    public bool IsStatic => this.Symbol.IsStatic;

    public ImmutableArray<IParameterSymbol> Parameters => this.Symbol.Parameters
        .Select(s => s.ToApiSymbol())
        .ToImmutableArray();
}

internal sealed class FunctionGroupSymbol(Internal.Symbols.Synthetized.FunctionGroupSymbol group)
    : SymbolBase<Internal.Symbols.Synthetized.FunctionGroupSymbol>(group), IFunctionGroupSymbol
{
    public ImmutableArray<IFunctionSymbol> Functions => this.Symbol.Functions
        .Select(s => s.ToApiSymbol())
        .ToImmutableArray();
}

internal sealed class LabelSymbol(Internal.Symbols.LabelSymbol label)
    : SymbolBase<Internal.Symbols.LabelSymbol>(label), ILabelSymbol
{
}

internal sealed class TypeSymbol(Internal.Symbols.TypeSymbol type)
    : SymbolBase<Internal.Symbols.TypeSymbol>(type), ITypeSymbol
{
    public bool IsStatic => this.Symbol.IsStatic;
    public bool IsValueType => this.Symbol.IsValueType;
}

internal sealed class AliasSymbol(Internal.Symbols.AliasSymbol type)
    : SymbolBase<Internal.Symbols.AliasSymbol>(type), IAliasSymbol
{
    public bool IsStatic => this.Symbol.IsStatic;

    public ISymbol Substitution => this.Symbol.Substitution.ToApiSymbol();
    public ISymbol FullResolution => this.Symbol.FullResolution.ToApiSymbol();
}

internal sealed class TypeParameterSymbol(Internal.Symbols.TypeParameterSymbol type)
    : SymbolBase<Internal.Symbols.TypeParameterSymbol>(type), ITypeParameterSymbol
{
    public bool IsStatic => this.Symbol.IsStatic;
    public bool IsValueType => this.Symbol.IsValueType;
}

// NOTE: Mostly for generic error sentinel values
internal sealed class AnySymbol(Symbol type)
    : SymbolBase<Symbol>(type)
{
}
