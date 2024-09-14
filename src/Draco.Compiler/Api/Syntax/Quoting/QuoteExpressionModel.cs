using System.Collections.Immutable;

namespace Draco.Compiler.Api.Syntax.Quoting;

/// <summary>
/// An expression generated by the quoter.
/// </summary>
internal abstract record QuoteExpression;

/// <summary>
/// A function call quote expression.
/// </summary>
/// <param name="Function">The name of the function in <see cref="SyntaxFactory"/>.</param>
/// <param name="TypeArguments">The type arguments to the function.</param>
/// <param name="Arguments">The arguments to the function.</param>
internal sealed record QuoteFunctionCall(
    string Function,
    ImmutableArray<string> TypeArguments,
    ImmutableArray<QuoteExpression> Arguments)
    : QuoteExpression
{
    public QuoteFunctionCall(string function, ImmutableArray<QuoteExpression> arguments)
        : this(function, [], arguments)
    {
    }
}

/// <summary>
/// A property access quote expression.
/// </summary>
/// <param name="Property">The name of the property in <see cref="SyntaxFactory"/>.</param>
internal sealed record QuoteProperty(string Property) : QuoteExpression;

/// <summary>
/// A list quote expression.
/// </summary>
/// <param name="Values">The values in the list.</param>
internal sealed record QuoteList(ImmutableArray<QuoteExpression> Values) : QuoteExpression;

/// <summary>
/// A null quote expression.
/// </summary>
internal sealed record QuoteNull : QuoteExpression;

/// <summary>
/// A token kind quote expression.
/// </summary>
/// <param name="Value">The token kind.</param>
internal sealed record QuoteTokenKind(TokenKind Value) : QuoteExpression;

/// <summary>
/// An integer quote expression.
/// </summary>
/// <param name="Value">The integer value.</param>
internal sealed record QuoteInteger(int Value) : QuoteExpression;

/// <summary>
/// A float quote expression.
/// </summary>
/// <param name="Value">The float value.</param>
internal sealed record QuoteFloat(float Value) : QuoteExpression;

/// <summary>
/// A boolean quote expression.
/// </summary>
/// <param name="Value">The boolean value.</param>
internal sealed record QuoteBoolean(bool Value) : QuoteExpression;

/// <summary>
/// A string quote expression.
/// </summary>
/// <param name="Value">The string value.</param>
internal sealed record QuoteString(string Value) : QuoteExpression;
