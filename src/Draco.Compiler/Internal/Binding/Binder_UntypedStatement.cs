using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Draco.Compiler.Api.Syntax;
using Draco.Compiler.Internal.BoundTree;
using Draco.Compiler.Internal.Symbols;
using Draco.Compiler.Internal.Symbols.Source;
using Draco.Compiler.Internal.UntypedTree;

namespace Draco.Compiler.Internal.Binding;

internal partial class Binder
{
    /// <summary>
    /// Binds the given syntax node to an untyped statement.
    /// </summary>
    /// <param name="syntax">The syntax to bind.</param>
    /// <param name="constraints">The constraints that has been collected during the binding process.</param>
    /// <param name="diagnostics">The diagnostics produced during the process.</param>
    /// <returns>The untyped statement for <paramref name="syntax"/>.</returns>
    protected UntypedStatement BindStatement(SyntaxNode syntax, ConstraintBag constraints, DiagnosticBag diagnostics) => syntax switch
    {
        DeclarationStatementSyntax decl => this.BindStatement(decl.Declaration, constraints, diagnostics),
        BlockFunctionBodySyntax body => this.BindBlockFunctionBody(body, constraints, diagnostics),
        InlineFunctionBodySyntax body => this.BindInlineFunctionBody(body, constraints, diagnostics),
        LabelDeclarationSyntax label => this.BindLabelStatement(label, constraints, diagnostics),
        VariableDeclarationSyntax decl => this.BindVariableDeclaration(decl, constraints, diagnostics),
        _ => throw new ArgumentOutOfRangeException(nameof(syntax)),
    };

    private UntypedStatement BindBlockFunctionBody(BlockFunctionBodySyntax syntax, ConstraintBag constraints, DiagnosticBag diagnostics)
    {
        var binder = this.Compilation.GetBinder(syntax);
        var statements = syntax.Statements
            .Select(s => binder.BindStatement(s, constraints, diagnostics))
            .ToImmutableArray();
        return new UntypedExpressionStatement(
            syntax,
            new UntypedBlockExpression(syntax, statements, UntypedUnitExpression.Default));
    }

    private UntypedStatement BindInlineFunctionBody(InlineFunctionBodySyntax syntax, ConstraintBag constraints, DiagnosticBag diagnostics)
    {
        var binder = this.Compilation.GetBinder(syntax);
        var value = binder.BindExpression(syntax.Value, constraints, diagnostics);
        return new UntypedExpressionStatement(syntax, new UntypedReturnExpression(syntax, value));
    }

    private UntypedStatement BindLabelStatement(LabelDeclarationSyntax syntax, ConstraintBag constraints, DiagnosticBag diagnostics) =>
        throw new NotImplementedException();

    private UntypedStatement BindVariableDeclaration(VariableDeclarationSyntax syntax, ConstraintBag constraints, DiagnosticBag diagnostics)
    {
        var localSymbol = (Symbol?)((LocalBinder)this).LocalDeclarations
            .Select(decl => decl.Symbol)
            .OfType<ISourceSymbol>()
            .FirstOrDefault(sym => sym.DeclarationSyntax == syntax);
        Debug.Assert(localSymbol is not null);

        // TODO
        throw new NotImplementedException();
    }
}
