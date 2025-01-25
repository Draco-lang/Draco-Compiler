using System.Collections.Generic;
using System.Collections.Immutable;
using System.Text;
using System.Threading;
using Draco.Compiler.Api.Semantics;
using Draco.Compiler.Api.Syntax;
using Draco.Compiler.Internal.BoundTree;
using Draco.Compiler.Internal.OptimizingIr.Codegen;
using Draco.Compiler.Internal.OptimizingIr.Model;
using Draco.Compiler.Internal.Symbols.Generic;

namespace Draco.Compiler.Internal.Symbols;

/// <summary>
/// Represents a free-function.
/// </summary>
internal abstract partial class FunctionSymbol : Symbol, ITypedSymbol, IMemberSymbol, IOverridableSymbol
{
    /// <summary>
    /// A delegate to generate IR code.
    /// </summary>
    /// <param name="codegen">The code generator.</param>
    /// <param name="targetType">The target type of the resulting value.</param>
    /// <param name="operands">The compiled operand references.</param>
    /// <returns>The operand that holds the result of the operation.</returns>
    public delegate IOperand CodegenDelegate(
        LocalCodegen codegen,
        TypeSymbol targetType,
        ImmutableArray<IOperand> operands);

    /// <summary>
    /// Retrieves the name for the unary operator that is referenced by a given token.
    /// </summary>
    /// <param name="token">The <see cref="TokenKind"/> that references the unary operator.</param>
    /// <returns>The name of the symbol to look up the unary operator.</returns>
    public static string GetUnaryOperatorName(TokenKind token) => token switch
    {
        TokenKind.Plus => "op_UnaryPlus",
        TokenKind.Minus => "op_UnaryNegation",
        TokenKind.KeywordNot or TokenKind.CNot => "op_LogicalNot",
        _ => throw new System.ArgumentOutOfRangeException(nameof(token)),
    };

    /// <summary>
    /// Retrieves the name for the binary operator that is referenced by a given token.
    /// </summary>
    /// <param name="token">The <see cref="TokenKind"/> that references the binary operator.</param>
    /// <returns>The name of the symbol to look up the binary operator.</returns>
    public static string GetBinaryOperatorName(TokenKind token) => token switch
    {
        TokenKind.Plus => "op_Addition",
        TokenKind.Minus => "op_Subtraction",
        TokenKind.Star => "op_Multiply",
        TokenKind.Slash => "op_Division",
        // NOTE: This is actually remainder
        TokenKind.KeywordRem => "op_Modulus",
        // TODO: Consider for interop
        TokenKind.KeywordMod or TokenKind.CMod => "op_DracoModulo",
        _ => throw new System.ArgumentOutOfRangeException(nameof(token)),
    };

    /// <summary>
    /// Retrieves the name for the comparison operator that is referenced by a given token.
    /// </summary>
    /// <param name="token">The <see cref="TokenKind"/> that references the comparison operator.</param>
    /// <returns>The name of the symbol to look up the comparison operator.</returns>
    public static string GetComparisonOperatorName(TokenKind token) => token switch
    {
        TokenKind.Equal => "op_Equality",
        TokenKind.NotEqual => "op_Inequality",
        TokenKind.GreaterThan => "op_GreaterThan",
        TokenKind.LessThan => "op_LessThan",
        TokenKind.GreaterEqual => "op_GreaterThanOrEqual",
        TokenKind.LessEqual => "op_LessThanOrEqual",
        _ => throw new System.ArgumentOutOfRangeException(nameof(token)),
    };

    /// <summary>
    /// The parameters of this function.
    /// </summary>
    public abstract ImmutableArray<ParameterSymbol> Parameters { get; }

    /// <summary>
    /// The return type of this function.
    /// </summary>
    public abstract TypeSymbol ReturnType { get; }

    public virtual bool IsStatic => true;
    public virtual bool IsExplicitImplementation => false;

    /// <summary>
    /// If true, this is a virtual function.
    /// </summary>
    public virtual bool IsVirtual => false;

    /// <summary>
    /// True, if this is a variadic function.
    /// </summary>
    public bool IsVariadic => this.Parameters.Length > 0 && this.Parameters[^1].IsVariadic;

    /// <summary>
    /// True, if this is a constructor.
    /// </summary>
    public virtual bool IsConstructor => false;

    /// <summary>
    /// True, if this is a nested function.
    /// </summary>
    public bool IsNested => this.ContainingSymbol is FunctionSymbol;

    public override bool IsSpecialName => this.IsConstructor;

    // TODO: Apart from exposing the metadata name here, we need to take care of nested names for local methods for example
    // The name should include the parent functions too, like func foo() { func bar() { ... } }
    // the inner method should be called foo.bar in metadata

    // NOTE: It seems like the backtick is only for types
    // TODO: In that case, maybe move this logic out from Symbol and make MetadataName abstract or default to this instead?
    public override string MetadataName => this.Name;

    // NOTE: We override for covariant return type
    public override FunctionSymbol? GenericDefinition => null;
    public override IEnumerable<Symbol> AllMembers => this.Parameters;
    public override SymbolKind Kind => SymbolKind.Function;

    public TypeSymbol Type => LazyInitializer.EnsureInitialized(ref this.type, this.BuildType);
    private TypeSymbol? type;

    public virtual Symbol? Override => null;

    /// <summary>
    /// The bound body of this function, if it has one.
    /// This is the case for in-source and certain synthesized functions.
    /// When null, the function will not be emitted.
    /// </summary>
    public virtual BoundStatement? Body => null;

    /// <summary>
    /// The code generator for this function, if it has one.
    /// This is the case for certain synthesized functions.
    /// </summary>
    public virtual CodegenDelegate? Codegen => null;

    /// <summary>
    /// Retrieves the nested name of this function, which prepends the containing function names.
    /// </summary>
    public string NestedName => this.ContainingSymbol switch
    {
        FunctionSymbol f => $"{f.NestedName}.{this.Name}",
        _ => this.Name,
    };

    public override string ToString()
    {
        var result = new StringBuilder();
        result.Append(this.Name);
        result.Append(this.GenericsToString());
        result.Append('(');
        result.AppendJoin(", ", this.Parameters);
        result.Append($"): {this.ReturnType}");
        return result.ToString();
    }

    public override bool CanBeShadowedBy(Symbol other)
    {
        if (other is not FunctionSymbol function) return false;
        if (this.Name != function.Name) return false;
        if (this.GenericParameters.Length != function.GenericParameters.Length) return false;
        if (this.Parameters.Length != function.Parameters.Length) return false;
        for (var i = 0; i < this.Parameters.Length; i++)
        {
            if (!SymbolEqualityComparer.AllowTypeVariables.Equals(this.Parameters[i].Type, function.Parameters[i].Type)) return false;
            if (this.Parameters[i].IsVariadic != function.Parameters[i].IsVariadic) return false;
        }
        return true;
    }

    public bool CanBeOverriddenBy(IOverridableSymbol other)
    {
        if (other is not FunctionSymbol function) return false;
        if (!this.CanBeShadowedBy(function)) return false;
        return SymbolEqualityComparer.AllowTypeVariables.IsBaseOf(this.ReturnType, function.ReturnType);
    }

    public override FunctionSymbol GenericInstantiate(Symbol? containingSymbol, ImmutableArray<TypeSymbol> arguments) =>
        (FunctionSymbol)base.GenericInstantiate(containingSymbol, arguments);
    public override FunctionSymbol GenericInstantiate(Symbol? containingSymbol, GenericContext context) =>
        new FunctionInstanceSymbol(containingSymbol, this, context);

    public override IFunctionSymbol ToApiSymbol() => new Api.Semantics.FunctionSymbol(this);

    public override void Accept(SymbolVisitor visitor) => visitor.VisitFunction(this);
    public override TResult Accept<TResult>(SymbolVisitor<TResult> visitor) => visitor.VisitFunction(this);

    private TypeSymbol BuildType() => new FunctionTypeSymbol(this.Parameters, this.ReturnType);
}
