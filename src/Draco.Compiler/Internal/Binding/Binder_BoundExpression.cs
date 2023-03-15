using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Draco.Compiler.Api.Syntax;
using Draco.Compiler.Internal.BoundTree;
using Draco.Compiler.Internal.Symbols;
using Draco.Compiler.Internal.UntypedTree;

namespace Draco.Compiler.Internal.Binding;

internal partial class Binder
{
    /// <summary>
    /// Binds the given untyped expression to a bound expression.
    /// </summary>
    /// <param name="expression">The untyped expression to bind.</param>
    /// <param name="constraints">The constraints that has been collected during the binding process.</param>
    /// <param name="diagnostics">The diagnostics produced during the process.</param>
    /// <returns>The bound expression for <paramref name="expression"/>.</returns>
    internal virtual BoundExpression TypeExpression(UntypedExpression expression, ConstraintBag constraints, DiagnosticBag diagnostics) => expression switch
    {
        UntypedUnitExpression unit => this.TypeUnitExpression(unit, constraints, diagnostics),
        UntypedLiteralExpression literal => this.TypeLiteralExpression(literal, constraints, diagnostics),
        UntypedParameterExpression @param => this.TypeParameterExpression(param, constraints, diagnostics),
        UntypedLocalExpression local => this.TypeLocalExpression(local, constraints, diagnostics),
        UntypedFunctionExpression func => this.TypeFunctionExpression(func, constraints, diagnostics),
        UntypedReturnExpression @return => this.TypeReturnExpression(@return, constraints, diagnostics),
        UntypedBlockExpression block => this.TypeBlockExpression(block, constraints, diagnostics),
        UntypedGotoExpression @goto => this.TypeGotoExpression(@goto, constraints, diagnostics),
        UntypedIfExpression @if => this.TypeIfExpression(@if, constraints, diagnostics),
        UntypedWhileExpression @while => this.TypeWhileExpression(@while, constraints, diagnostics),
        UntypedCallExpression call => this.TypeCallExpression(call, constraints, diagnostics),
        UntypedAssignmentExpression assignment => this.TypeAssignmentExpression(assignment, constraints, diagnostics),
        UntypedUnaryExpression ury => this.TypeUnaryExpression(ury, constraints, diagnostics),
        UntypedBinaryExpression bin => this.TypeBinaryExpression(bin, constraints, diagnostics),
        UntypedRelationalExpression rel => this.TypeRelationalExpression(rel, constraints, diagnostics),
        _ => throw new ArgumentOutOfRangeException(nameof(expression)),
    };

    private BoundExpression TypeUnitExpression(UntypedUnitExpression unit, ConstraintBag constraints, DiagnosticBag diagnostics) =>
        unit.Syntax is null ? BoundUnitExpression.Default : new BoundUnitExpression(unit.Syntax);

    private BoundExpression TypeLiteralExpression(UntypedLiteralExpression literal, ConstraintBag constraints, DiagnosticBag diagnostics) =>
        new BoundLiteralExpression(literal.Syntax, literal.Value);

    private BoundExpression TypeParameterExpression(UntypedParameterExpression param, ConstraintBag constraints, DiagnosticBag diagnostics) =>
        new BoundParameterExpression(param.Syntax, param.Parameter);

    private BoundExpression TypeLocalExpression(UntypedLocalExpression local, ConstraintBag constraints, DiagnosticBag diagnostics) =>
        new BoundLocalExpression(local.Syntax, local.Local);

    private BoundExpression TypeFunctionExpression(UntypedFunctionExpression func, ConstraintBag constraints, DiagnosticBag diagnostics) =>
        new BoundFunctionExpression(func.Syntax, func.Function);

    private BoundExpression TypeReturnExpression(UntypedReturnExpression @return, ConstraintBag constraints, DiagnosticBag diagnostics)
    {
        var typedValue = this.TypeExpression(@return.Value, constraints, diagnostics);
        return new BoundReturnExpression(@return.Syntax, typedValue);
    }

    private BoundExpression TypeBlockExpression(UntypedBlockExpression block, ConstraintBag constraints, DiagnosticBag diagnostics)
    {
        var typedStatements = block.Statements
            .Select(s => this.TypeStatement(s, constraints, diagnostics))
            .ToImmutableArray();
        var typedValue = this.TypeExpression(block.Value, constraints, diagnostics);
        return new BoundBlockExpression(block.Syntax, block.Locals, typedStatements, typedValue);
    }

    private BoundExpression TypeGotoExpression(UntypedGotoExpression @goto, ConstraintBag constraints, DiagnosticBag diagnostics) =>
        new BoundGotoExpression(@goto.Syntax, @goto.Target);

    private BoundExpression TypeIfExpression(UntypedIfExpression @if, ConstraintBag constraints, DiagnosticBag diagnostics)
    {
        var typedCondition = this.TypeExpression(@if.Condition, constraints, diagnostics);
        var typedThen = this.TypeExpression(@if.Then, constraints, diagnostics);
        var typedElse = this.TypeExpression(@if.Else, constraints, diagnostics);
        return new BoundIfExpression(@if.Syntax, typedCondition, typedThen, typedElse);
    }

    private BoundExpression TypeWhileExpression(UntypedWhileExpression @while, ConstraintBag constraints, DiagnosticBag diagnostics)
    {
        var typedCondition = this.TypeExpression(@while.Condition, constraints, diagnostics);
        var typedThen = this.TypeExpression(@while.Then, constraints, diagnostics);
        return new BoundWhileExpression(@while.Syntax, typedCondition, typedThen, @while.ContinueLabel, @while.BreakLabel);
    }

    private BoundExpression TypeCallExpression(UntypedCallExpression call, ConstraintBag constraints, DiagnosticBag diagnostics)
    {
        var typedFunction = this.TypeExpression(call.Method, constraints, diagnostics);
        var typedArgs = call.Arguments
            .Select(arg => this.TypeExpression(arg, constraints, diagnostics))
            .ToImmutableArray();
        return new BoundCallExpression(call.Syntax, typedFunction, typedArgs);
    }

    private BoundExpression TypeAssignmentExpression(UntypedAssignmentExpression assignment, ConstraintBag constraints, DiagnosticBag diagnostics)
    {
        var typedLeft = this.TypeLvalue(assignment.Left, constraints, diagnostics);
        var typedRight = this.TypeExpression(assignment.Right, constraints, diagnostics);
        return new BoundAssignmentExpression(assignment.Syntax, typedLeft, typedRight);
    }

    private BoundExpression TypeUnaryExpression(UntypedUnaryExpression ury, ConstraintBag constraints, DiagnosticBag diagnostics)
    {
        var typedOperand = this.TypeExpression(ury.Operand, constraints, diagnostics);
        // TODO: Resolve operator from possible overload set
        var unaryOperator = (UnaryOperatorSymbol)ury.Operator;
        return new BoundUnaryExpression(ury.Syntax, unaryOperator, typedOperand);
    }

    private BoundExpression TypeBinaryExpression(UntypedBinaryExpression bin, ConstraintBag constraints, DiagnosticBag diagnostics)
    {
        var typedLeft = this.TypeExpression(bin.Left, constraints, diagnostics);
        var typedRight = this.TypeExpression(bin.Right, constraints, diagnostics);
        // TODO: Resolve operator from possible overload set
        var binaryOperator = (BinaryOperatorSymbol)bin.Operator;
        return new BoundBinaryExpression(bin.Syntax, binaryOperator, typedLeft, typedRight);
    }

    private BoundExpression TypeRelationalExpression(UntypedRelationalExpression rel, ConstraintBag constraints, DiagnosticBag diagnostics)
    {
        var first = this.TypeExpression(rel.First, constraints, diagnostics);
        var comparisons = rel.Comparisons
            .Select(cmp => this.TypeComparison(cmp, constraints, diagnostics))
            .ToImmutableArray();
        return new BoundRelationalExpression(rel.Syntax, first, comparisons);
    }

    private BoundComparison TypeComparison(UntypedComparison cmp, ConstraintBag constraints, DiagnosticBag diagnostics)
    {
        var next = this.TypeExpression(cmp.Next, constraints, diagnostics);
        // TODO: Resolve comparison operator from possible overload set
        var comparisonOperator = (ComparisonOperatorSymbol)cmp.Operator;
        return new BoundComparison(cmp.Syntax, comparisonOperator, next);
    }
}
