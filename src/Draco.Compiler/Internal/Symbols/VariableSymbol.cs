using Draco.Compiler.Api.Syntax;

namespace Draco.Compiler.Internal.Symbols;

/// <summary>
/// Any variable-like symbol.
/// </summary>
internal abstract partial class VariableSymbol : Symbol, ITypedSymbol
{
    /// <summary>
    /// The type of the local.
    /// </summary>
    public abstract TypeSymbol Type { get; }

    /// <summary>
    /// True, if this local is mutable.
    /// </summary>
    public abstract bool IsMutable { get; }

    public override Api.Semantics.Visibility Visibility
    {
        get
        {
            var syntax = this.DeclaringSyntax as FunctionDeclarationSyntax;
            if (syntax is null) return Api.Semantics.Visibility.Internal; // Default
            return syntax.VisibilityModifier?.Kind switch
            {
                null => Api.Semantics.Visibility.Private,
                TokenKind.KeywordInternal => Api.Semantics.Visibility.Internal,
                TokenKind.KeywordPublic => Api.Semantics.Visibility.Public,
                _ => throw new System.ArgumentOutOfRangeException(nameof(syntax.VisibilityModifier.Kind)),
            };
        }
    }
}
