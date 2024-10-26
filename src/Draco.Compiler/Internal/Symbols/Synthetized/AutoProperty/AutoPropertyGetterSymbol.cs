using Draco.Compiler.Internal.BoundTree;
using Draco.Compiler.Internal.Symbols.Source;
using System;
using System.Collections.Immutable;
using System.Threading;
using static Draco.Compiler.Internal.BoundTree.BoundTreeFactory;

namespace Draco.Compiler.Internal.Symbols.Synthetized.AutoProperty;

/// <summary>
/// An auto-generated getter for an auto-property.
/// </summary>
internal sealed class AutoPropertyGetterSymbol(
    Symbol containingSymbol,
    SourceAutoPropertySymbol property) : FunctionSymbol, IPropertyAccessorSymbol
{
    public override Symbol ContainingSymbol { get; } = containingSymbol;

    public override string Name => $"{this.Property.Name}_Getter";
    public override bool IsStatic => this.Property.IsStatic;
    public override Api.Semantics.Visibility Visibility => this.Property.Visibility;
    public override bool IsSpecialName => true;

    public override ImmutableArray<ParameterSymbol> Parameters => ImmutableArray<ParameterSymbol>.Empty;
    public override TypeSymbol ReturnType => this.Property.Type;

    public override BoundStatement Body => LazyInitializer.EnsureInitialized(ref this.body, this.BuildBody);
    private BoundStatement? body;

    PropertySymbol IPropertyAccessorSymbol.Property => this.Property;
    public SourceAutoPropertySymbol Property { get; } = property;

    private BoundStatement BuildBody() => ExpressionStatement(BlockExpression(
        locals: [],
        statements: [ExpressionStatement(ReturnExpression(FieldExpression(
            receiver: this.IsStatic
                ? null
                : throw new NotImplementedException("TODO: for classes"),
            field: this.Property.BackingField)))],
        value: BoundUnitExpression.Default));
}
