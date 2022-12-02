using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Draco.Compiler.Api.Syntax;
using Draco.Compiler.Internal.Query;
using Draco.Compiler.Internal.Semantics.Symbols;
using Draco.Compiler.Internal.Semantics.Types;
using static Draco.Compiler.Internal.Semantics.AbstractSyntax.AstFactory;

namespace Draco.Compiler.Internal.Semantics.AbstractSyntax;

/// <summary>
/// Implements lowering (desugaring) to the <see cref="Ast"/> to simplify codegen.
/// </summary>
internal sealed class AstLowering : AstTransformerBase
{
    /// <summary>
    /// Lowers the <see cref="Ast"/> into simpler elements.
    /// </summary>
    /// <param name="db">The <see cref="QueryDatabase"/> for the computation.</param>
    /// <param name="ast">The <see cref="Ast"/> to lower.</param>
    /// <returns>The lowered equivalent of <paramref name="ast"/>.</returns>
    public static Ast Lower(QueryDatabase db, Ast ast) =>
        new AstLowering(db).Transform(ast, out _);

    private readonly QueryDatabase db;

    private AstLowering(QueryDatabase db)
    {
        this.db = db;
    }

    public override Ast.Expr TransformWhileExpr(Ast.Expr.While node, out bool changed)
    {
        // while (condition)
        // {
        //     body...
        // }
        //
        // =>
        //
        // continue_label:
        //     if (!condition) goto break_label;
        //     body...
        //     goto continue_label;
        // break_label:

        changed = true;

        var continueLabel = new Symbol.SynthetizedLabel();
        var breakLabel = new Symbol.SynthetizedLabel();
        var condition = this.TransformExpr(node.Condition, out _);
        var body = this.TransformExpr(node.Expression, out _);

        return Block(
            // continue_label:
            Stmt(Label(continueLabel)),
            // if (!condition) goto break_label;
            Stmt(If(
                condition: Unary(
                    op: Symbol.IntrinsicOperator.Not_Bool,
                    subexpr: condition),
                then: Goto(breakLabel))),
            // body...
            Stmt(body),
            // goto continue_label;
            Stmt(Goto(continueLabel)),
            // break_label:
            Stmt(Label(breakLabel)));
    }

    public override Ast.Expr TransformRelationalExpr(Ast.Expr.Relational node, out bool changed)
    {
        // expr1 < expr2 == expr3 > expr4 != ...
        //
        // =>
        //
        // {
        //     val tmp1 = expr1;
        //     val tmp2 = expr2;
        //     val tmp3 = expr3;
        //     val tmp4 = expr4;
        //     ...
        //     tmp1 < tmp2 && tmp2 == tmp3 && tmp3 > tmp4 && tmp4 != ...
        // }

        changed = true;

        // Store all expressions as temporary variables
        var tmpVariables = new List<(Ast.Expr Reference, Ast.Stmt Assignment)>();
        tmpVariables.Add(this.StoreTemporary(node.Left));
        foreach (var item in node.Comparisons) tmpVariables.Add(this.StoreTemporary(item.Right));

        // Build pairs of comparisons from symbol references
        var comparisons = new List<Ast.Expr>();
        for (var i = 0; i < node.Comparisons.Length; ++i)
        {
            var left = tmpVariables[i].Reference;
            var op = node.Comparisons[i].Operator;
            var right = tmpVariables[i + 1].Reference;
            comparisons.Add(Binary(
                left: left,
                op: op,
                right: right));
        }

        // Fold them into conjunctions
        var conjunction = comparisons
            .Aggregate((x, y) => Binary(
                left: x,
                op: null!, // TODO: && operator
                right: y));

        // Wrap up in block
        return Block(
            stmts: tmpVariables.Select(t => t.Assignment),
            value: conjunction);
    }

    // Utility to store an expression to a temporary variable
    private (Ast.Expr Reference, Ast.Stmt Assignment) StoreTemporary(Ast.Expr expr)
    {
        // Optimization: if it's already a symbol reference, leave as-is
        // Optimization: if it's a literal, don't bother copying
        if (expr is Ast.Expr.Reference or Ast.Expr.Literal)
        {
            return (expr, Ast.Stmt.NoOp.Default);
        }

        // Otherwise compute and store
        var symbol = new Symbol.SynthetizedVariable(false, TypeChecker.TypeOf(this.db, (ParseTree.Expr)expr.ParseTree!));
        var symbolRef = Reference(symbol);
        var assignment = Stmt(Var(
            varSymbol: symbol,
            value: this.TransformExpr(expr, out _)));
        return (symbolRef, assignment);
    }
}
