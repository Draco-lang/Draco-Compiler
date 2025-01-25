using System;
using System.Collections.Immutable;
using System.Threading;
using Draco.Compiler.Internal.BoundTree;
using Draco.Compiler.Internal.Symbols.Syntax;
using Draco.Compiler.Internal.Utilities;
using static Draco.Compiler.Internal.BoundTree.BoundTreeFactory;

namespace Draco.Compiler.Internal.Symbols.Synthetized.AutoProperty;

/// <summary>
/// An auto-generated setter for an auto-property.
/// </summary>
internal sealed class AutoPropertySetterSymbol(
    Symbol containingSymbol,
    SyntaxAutoPropertySymbol property) : FunctionSymbol, IPropertyAccessorSymbol
{
    public override Symbol ContainingSymbol { get; } = containingSymbol;

    public override string Name => $"{this.Property.Name}_Setter";
    public override bool IsStatic => this.Property.IsStatic;
    public override Api.Semantics.Visibility Visibility => this.Property.Visibility;
    public override bool IsSpecialName => true;

    public override ImmutableArray<ParameterSymbol> Parameters => InterlockedUtils.InitializeDefault(ref this.parameters, this.BuildParameters);
    private ImmutableArray<ParameterSymbol> parameters;

    public override TypeSymbol ReturnType => WellKnownTypes.Unit;

    public override BoundStatement Body => LazyInitializer.EnsureInitialized(ref this.body, this.BuildBody);
    private BoundStatement? body;

    PropertySymbol IPropertyAccessorSymbol.Property => this.Property;
    public SyntaxAutoPropertySymbol Property { get; } = property;

    private ImmutableArray<ParameterSymbol> BuildParameters() =>
        [new SynthetizedParameterSymbol(this, "value", this.Property.Type)];

    private BoundStatement BuildBody() => ExpressionStatement(BlockExpression(
        locals: [],
        statements: [
            ExpressionStatement(AssignmentExpression(
                compoundOperator: null,
                left: FieldLvalue(
                    receiver: this.IsStatic
                        ? null
                        : throw new NotImplementedException("TODO: classes"),
                    field: this.Property.BackingField),
                right: ParameterExpression(this.Parameters[^1]))),
            ExpressionStatement(ReturnExpression(BoundUnitExpression.Default))],
        value: BoundUnitExpression.Default));
}
