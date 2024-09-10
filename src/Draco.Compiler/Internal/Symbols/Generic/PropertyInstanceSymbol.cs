using System.Threading;

namespace Draco.Compiler.Internal.Symbols.Generic;

/// <summary>
/// Represents a generic instantiated property.
/// The property definition itself is not generic, but the property was within a generic context.
/// </summary>
internal sealed class PropertyInstanceSymbol(
    Symbol? containingSymbol,
    PropertySymbol genericDefinition,
    GenericContext context) : PropertySymbol, IGenericInstanceSymbol
{
    public override TypeSymbol Type => LazyInitializer.EnsureInitialized(ref this.type, this.BuildType);
    private TypeSymbol? type;

    public override FunctionSymbol? Getter => InterlockedUtils.InitializeMaybeNull(ref this.getter, this.BuildGetter);
    private FunctionSymbol? getter;

    public override FunctionSymbol? Setter => InterlockedUtils.InitializeMaybeNull(ref this.setter, this.BuildSetter);
    private FunctionSymbol? setter;

    public override string Name => this.GenericDefinition.Name;

    public override bool IsIndexer => this.GenericDefinition.IsIndexer;

    public override bool IsStatic => this.GenericDefinition.IsStatic;

    public override bool IsExplicitImplementation => this.GenericDefinition.IsExplicitImplementation;

    public override Symbol? ContainingSymbol { get; } = containingSymbol;
    public override PropertySymbol GenericDefinition { get; } = genericDefinition;

    public GenericContext Context { get; } = context;

    private TypeSymbol BuildType() =>
        this.GenericDefinition.Type.GenericInstantiate(this.GenericDefinition.Type.ContainingSymbol, this.Context);

    private FunctionSymbol? BuildGetter() => this.GenericDefinition.Getter is not null
        ? new PropertyAccessorInstanceSymbol(this.ContainingSymbol, this.GenericDefinition.Getter, this.Context, this)
        : null;

    private FunctionSymbol? BuildSetter() => this.GenericDefinition.Setter is not null
        ? new PropertyAccessorInstanceSymbol(this.ContainingSymbol, this.GenericDefinition.Setter, this.Context, this)
        : null;
}
