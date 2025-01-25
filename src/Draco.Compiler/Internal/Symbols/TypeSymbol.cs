using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Draco.Compiler.Api.Semantics;
using Draco.Compiler.Internal.Symbols.Generic;
using Draco.Compiler.Internal.Symbols.Synthetized;
using Draco.Compiler.Internal.Utilities;

namespace Draco.Compiler.Internal.Symbols;

/// <summary>
/// Represents a type definition.
/// </summary>
internal abstract partial class TypeSymbol : Symbol, IMemberSymbol
{
    /// <summary>
    /// True, if this is a type variable, false otherwise.
    /// </summary>
    public virtual bool IsTypeVariable => false;

    /// <summary>
    /// True, if this is a ground type, meaning there are no type variables or all type variables have been substituted.
    /// </summary>
    public virtual bool IsGroundType => !this.IsGenericInstance || this.GenericArguments.All(t => t.IsGroundType);

    /// <summary>
    /// True, if this type is a value-type.
    /// </summary>
    public virtual bool IsValueType => false;

    /// <summary>
    /// True, if this type is a delegate type.
    /// </summary>
    public virtual bool IsDelegateType => false;

    /// <summary>
    /// True, if this type is an enum type.
    /// </summary>
    public virtual bool IsEnumType => false;

    /// <summary>
    /// The underlying type of this enum type, in case this is an enum type.
    /// </summary>
    public TypeSymbol? EnumUnderlyingType
    {
        get
        {
            if (!this.IsEnumType) return null;
            return this.DefinedMembers
                .OfType<FieldSymbol>()
                .FirstOrDefault(f => f.Name == CompilerConstants.EnumTagField)
                ?.Type;
        }
    }

    /// <summary>
    /// True. if this is a native .NET array type.
    /// </summary>
    public virtual bool IsArrayType => false;

    /// <summary>
    /// True, if this type is an attribute type derived from <see cref="Attribute"/>.
    /// </summary>
    public virtual bool IsAttributeType => false;

    /// <summary>
    /// True, if this type is an interface.
    /// </summary>
    public virtual bool IsInterface => false;

    /// <summary>
    /// True, if this type is abstract.
    /// </summary>
    public virtual bool IsAbstract => false;

    /// <summary>
    /// True, if this type is sealed.
    /// </summary>
    public virtual bool IsSealed => false;

    /// <summary>
    /// The substituted type of this one, in case this is a type variable.
    /// It's this instance itself, if not a type variable, or not substituted.
    /// </summary>
    public virtual TypeSymbol Substitution => this;

    /// <summary>
    /// The primary base type of this type (typically class inheritance).
    /// </summary>
    public TypeSymbol? BaseType => this.ImmediateBaseTypes.FirstOrDefault(s => !s.IsInterface);

    /// <summary>
    /// The immediate base types of this type.
    /// </summary>
    public virtual ImmutableArray<TypeSymbol> ImmediateBaseTypes => [];

    /// <summary>
    /// All types that can be considered the base type of this one, including this type itself.
    /// The types are returned in a pre-order manner, starting from this type.
    /// </summary>
    public IEnumerable<TypeSymbol> BaseTypes => InterlockedUtils.InitializeDefault(ref this.baseTypes, this.BuildBaseTypes);
    private ImmutableArray<TypeSymbol> baseTypes;

    /// <summary>
    /// The constructors defined directly in this type.
    /// </summary>
    public virtual IEnumerable<FunctionSymbol> Constructors => this.DefinedMembers
        .OfType<FunctionSymbol>()
        .Where(f => f.IsConstructor);

    /// <summary>
    /// All members within this type that are not special members.
    /// </summary>
    public IEnumerable<Symbol> NonSpecialMembers => this.AllMembers.Where(m => !m.IsSpecialName);

    public override sealed IEnumerable<Symbol> AllMembers =>
        InterlockedUtils.InitializeDefault(ref this.members, this.BuildMembers);
    private ImmutableArray<Symbol> members;

    /// <summary>
    /// All property accessors defined by this type.
    /// </summary>
    public IEnumerable<FunctionSymbol> DefinedPropertyAccessors =>
        this.DefinedMembers.OfType<PropertySymbol>().SelectMany(p => p.Accessors);

    // NOTE: We override for covariant return type
    public override TypeSymbol? GenericDefinition => null;

    public bool IsStatic => true;
    public bool IsExplicitImplementation => false;
    public override SymbolKind Kind => SymbolKind.Type;

    /// <summary>
    /// The invokable function, if this is a delegate type.
    /// </summary>
    public FunctionSymbol? InvokeMethod =>
        InterlockedUtils.InitializeMaybeNull(ref this.invokeMethod, this.BuildInvokeMethod);
    private FunctionSymbol? invokeMethod;

    /// <summary>
    /// The signature of the invokable function, if this is a delegate type.
    /// </summary>
    public FunctionTypeSymbol? InvokeSignatureType => this.InvokeMethod?.Type as FunctionTypeSymbol;

    public override bool CanBeShadowedBy(Symbol other)
    {
        if (other is not TypeSymbol) return false;
        if (this.Name != other.Name) return false;
        return this.GenericParameters.Length == other.GenericParameters.Length;
    }

    public T? GetOverriddenSymbol<T>(T @override)
        where T : Symbol, IOverridableSymbol => this.BaseTypes
        .SelectMany(x => x.DefinedMembers)
        .OfType<T>()
        .Except([@override])
        .FirstOrDefault(x => x.CanBeOverriddenBy(@override));

    private ImmutableArray<Symbol> BuildMembers()
    {
        var builder = ImmutableArray.CreateBuilder<Symbol>();
        var ignore = new List<Symbol>();

        // TODO: Maybe just include private members but filter them out during lookup?
        // NOTE: We use this workaround so we don't import interface members that
        // are implemented privately
        var relevantBases = this.IsInterface
            ? this.BaseTypes
            : this.BaseTypes.Where(b => !b.IsInterface);

        foreach (var member in relevantBases.SelectMany(x => x.DefinedMembers))
        {
            // Ignore constructors in base types
            if (member.ContainingSymbol != this && member is FunctionSymbol { IsConstructor: true }) continue;

            if (ignore.Any(member.CanBeShadowedBy)) continue;
            ignore.Add(member);
            builder.Add(member);
            if (member is not IOverridableSymbol overridable) continue;
            if (overridable.Override is not null) ignore.Add(overridable.Override);
        }
        return builder.ToImmutable();
    }

    private FunctionSymbol? BuildInvokeMethod()
    {
        if (!this.IsDelegateType) return null;

        // Look for the Invoke method
        return this.DefinedMembers
            .OfType<FunctionSymbol>()
            .FirstOrDefault(f => !f.IsStatic && f.Name == "Invoke");
    }

    private ImmutableArray<TypeSymbol> BuildBaseTypes() => GraphTraversal.DepthFirst(
        start: this,
        getNeighbors: s => s.ImmediateBaseTypes,
        comparer: SymbolEqualityComparer.AllowTypeVariables).ToImmutableArray();

    public override TypeSymbol GenericInstantiate(Symbol? containingSymbol, ImmutableArray<TypeSymbol> arguments) =>
        (TypeSymbol)base.GenericInstantiate(containingSymbol, arguments);
    public override TypeSymbol GenericInstantiate(Symbol? containingSymbol, GenericContext context)
    {
        // NOTE: Is this correct? What about nested generics?
        // Is this why .NET projects down generic args?
        if (!this.IsInGenericContext) return this;
        return new TypeInstanceSymbol(containingSymbol, this, context);
    }

    public override ITypeSymbol ToApiSymbol() => new Api.Semantics.TypeSymbol(this);

    public override void Accept(SymbolVisitor visitor) => visitor.VisitType(this);
    public override TResult Accept<TResult>(SymbolVisitor<TResult> visitor) => visitor.VisitType(this);

    public override abstract string ToString();

    protected internal override sealed IEnumerable<Symbol> GetAdditionalSymbols()
    {
        if (this.IsAbstract) yield break;
        // For non-abstract types we provide constructor functions
        foreach (var ctor in this.Constructors) yield return new ConstructorFunctionSymbol(ctor);
    }
}
