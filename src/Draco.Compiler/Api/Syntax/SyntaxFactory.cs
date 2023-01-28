using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Draco.Compiler.Internal.Utilities;

namespace Draco.Compiler.Api.Syntax;

/// <summary>
/// Utilities for constructing <see cref="SyntaxNode"/>s.
/// </summary>
public static partial class SyntaxFactory
{
    // NODES ///////////////////////////////////////////////////////////////////

    public static SyntaxToken Name(string text) => MakeToken(TokenType.Identifier, text);
    public static SyntaxToken Integer(int value) => MakeToken(TokenType.LiteralInteger, value.ToString(), value);

    public static SyntaxList<TNode> SyntaxList<TNode>(IEnumerable<TNode> elements)
        where TNode : SyntaxNode => new(tree: null!, parent: null, elements.Select(n => n.Green).ToImmutableArray());
    public static SyntaxList<TNode> SyntaxList<TNode>(params TNode[] elements)
        where TNode : SyntaxNode => SyntaxList(elements.AsEnumerable());

    public static SeparatedSyntaxList<TNode> SeparatedSyntaxList<TNode>(SyntaxToken separator, IEnumerable<TNode> elements)
        where TNode : SyntaxNode => new(
            tree: null!,
            parent: null,
            elements.SelectMany(n => new[] { n.Green, separator.Green }).ToImmutableArray());
    public static SeparatedSyntaxList<TNode> SeparatedSyntaxList<TNode>(SyntaxToken separator, params TNode[] elements)
        where TNode : SyntaxNode => SeparatedSyntaxList(separator, elements.AsEnumerable());

    public static SeparatedSyntaxList<ParameterSyntax> ParameterList(IEnumerable<ParameterSyntax> parameters) =>
        SeparatedSyntaxList(Comma, parameters);
    public static SeparatedSyntaxList<ParameterSyntax> ParameterList(params ParameterSyntax[] parameters) =>
        SeparatedSyntaxList(Comma, parameters);

    public static ParameterSyntax Parameter(string name, TypeSyntax type) => Parameter(Name(name), Colon, type);

    public static CompilationUnitSyntax CompilationUnit(IEnumerable<DeclarationSyntax> decls) =>
        CompilationUnit(SyntaxList(decls), EndOfInput);
    public static CompilationUnitSyntax CompilationUnit(params DeclarationSyntax[] decls) =>
        CompilationUnit(SyntaxList(decls), EndOfInput);

    public static FunctionDeclarationSyntax FunctionDeclaration(
        string name,
        SeparatedSyntaxList<ParameterSyntax> parameters,
        TypeSyntax? returnType,
        FunctionBodySyntax body) => FunctionDeclaration(
            Func,
            Name(name),
            OpenParen,
            parameters,
            CloseParen,
            returnType is null ? null : TypeSpecifier(Colon, returnType),
            body);

    public static VariableDeclarationSyntax VariableDeclaration(
        string name,
        TypeSyntax? type = null,
        ExpressionSyntax? value = null) => VariableDeclaration(true, name, type, value);

    public static VariableDeclarationSyntax ImmutableVariableDeclaration(
        string name,
        TypeSyntax? type = null,
        ExpressionSyntax? value = null) => VariableDeclaration(false, name, type, value);

    public static VariableDeclarationSyntax VariableDeclaration(
        bool isMutable,
        string name,
        TypeSyntax? type = null,
        ExpressionSyntax? value = null) => VariableDeclaration(
        isMutable ? Var : Val,
        Name(name),
        type is null ? null : TypeSpecifier(Colon, type),
        value is null ? null : ValueSpecifier(Assign, value),
        Semicolon);

    public static LabelDeclarationSyntax LabelDeclaration(string name) => LabelDeclaration(Name(name), Colon);

    public static InlineFunctionBodySyntax InlineFunctionBody(ExpressionSyntax expr) => InlineFunctionBody(Assign, expr, Semicolon);

    public static BlockFunctionBodySyntax BlockFunctionBody(IEnumerable<StatementSyntax> stmts) => BlockFunctionBody(
        OpenBrace,
        SyntaxList(stmts),
        CloseBrace);
    public static BlockFunctionBodySyntax BlockFunctionBody(params StatementSyntax[] stmts) => BlockFunctionBody(stmts.AsEnumerable());

    public static ExpressionStatementSyntax ExpressionStatement(ExpressionSyntax expr) => ExpressionStatement(expr, null);
    public static BlockExpressionSyntax BlockExpression(
        IEnumerable<StatementSyntax> stmts,
        ExpressionSyntax? value = null) => BlockExpression(
        OpenBrace,
        SyntaxList(stmts),
        value,
        CloseBrace);
    public static BlockExpressionSyntax BlockExpression(params StatementSyntax[] stmts) => BlockExpression(stmts.AsEnumerable());

    public static IfExpressionSyntax IfExpression(
        ExpressionSyntax condition,
        ExpressionSyntax then,
        ExpressionSyntax? @else = null) => IfExpression(
        If,
        OpenParen,
        condition,
        CloseParen,
        then,
        @else is null ? null : ElseClause(Else, @else));

    public static WhileExpressionSyntax WhileExpression(
        ExpressionSyntax condition,
        ExpressionSyntax body) => WhileExpression(
        While,
        OpenParen,
        condition,
        CloseParen,
        body);

    public static CallExpressionSyntax CallExpression(
        ExpressionSyntax called,
        IEnumerable<ExpressionSyntax> args) => CallExpression(
        called,
        OpenParen,
        SeparatedSyntaxList(Comma, args),
        CloseParen);
    public static CallExpressionSyntax CallExpression(
        ExpressionSyntax called,
        params ExpressionSyntax[] args) => CallExpression(called, args.AsEnumerable());

    public static ReturnExpressionSyntax ReturnExpression(ExpressionSyntax? value = null) => ReturnExpression(Return, value);
    public static GotoExpressionSyntax GotoExpression(string label) => GotoExpression(Goto, NameLabel(Name(label)));

    public static NameTypeSyntax NameType(string name) => NameType(Name(name));
    public static NameExpressionSyntax NameExpression(string name) => NameExpression(Name(name));
    public static LiteralExpressionSyntax LiteralExpression(int value) => LiteralExpression(Integer(value));
    public static LiteralExpressionSyntax LiteralExpression(bool value) => LiteralExpression(value ? True : False);
    public static StringExpressionSyntax StringExpression(string value) =>
        StringExpression(LineStringStart, SyntaxList(TextStringPart(value) as StringPartSyntax), LineStringEnd);

    public static TextStringPartSyntax TextStringPart(string value) =>
        TextStringPart(MakeToken(TokenType.StringContent, value, value));

    // TOKENS //////////////////////////////////////////////////////////////////

    public static SyntaxToken EndOfInput { get; } = MakeToken(TokenType.EndOfInput);
    public static SyntaxToken Assign { get; } = MakeToken(TokenType.Assign);
    public static SyntaxToken Comma { get; } = MakeToken(TokenType.Comma);
    public static SyntaxToken Colon { get; } = MakeToken(TokenType.Colon);
    public static SyntaxToken Semicolon { get; } = MakeToken(TokenType.Semicolon);
    public static SyntaxToken Return { get; } = MakeToken(TokenType.KeywordReturn);
    public static SyntaxToken If { get; } = MakeToken(TokenType.KeywordIf);
    public static SyntaxToken While { get; } = MakeToken(TokenType.KeywordWhile);
    public static SyntaxToken Else { get; } = MakeToken(TokenType.KeywordElse);
    public static SyntaxToken Var { get; } = MakeToken(TokenType.KeywordVar);
    public static SyntaxToken Val { get; } = MakeToken(TokenType.KeywordVal);
    public static SyntaxToken Func { get; } = MakeToken(TokenType.KeywordFunc);
    public static SyntaxToken Goto { get; } = MakeToken(TokenType.KeywordGoto);
    public static SyntaxToken True { get; } = MakeToken(TokenType.KeywordTrue);
    public static SyntaxToken False { get; } = MakeToken(TokenType.KeywordFalse);
    public static SyntaxToken OpenBrace { get; } = MakeToken(TokenType.CurlyOpen);
    public static SyntaxToken CloseBrace { get; } = MakeToken(TokenType.CurlyClose);
    public static SyntaxToken OpenParen { get; } = MakeToken(TokenType.ParenOpen);
    public static SyntaxToken CloseParen { get; } = MakeToken(TokenType.ParenClose);
    public static SyntaxToken Plus { get; } = MakeToken(TokenType.Plus);
    public static SyntaxToken LineStringStart { get; } = MakeToken(TokenType.LineStringStart, "\"");
    public static SyntaxToken LineStringEnd { get; } = MakeToken(TokenType.LineStringEnd, "\"");

    private static SyntaxToken MakeToken(TokenType tokenType) =>
        Internal.Syntax.SyntaxToken.From(tokenType).ToRedNode(null!, null);
    private static SyntaxToken MakeToken(TokenType tokenType, string text) =>
        Internal.Syntax.SyntaxToken.From(tokenType, text).ToRedNode(null!, null);
    private static SyntaxToken MakeToken(TokenType tokenType, string text, object? value) =>
        Internal.Syntax.SyntaxToken.From(tokenType, text, value).ToRedNode(null!, null);
}
