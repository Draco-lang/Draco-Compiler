using System.Collections.Generic;
using Draco.Compiler.Api.Diagnostics;
using Draco.Compiler.Api.Syntax;
using Draco.Compiler.Internal.Binding.Tasks;
using Draco.Compiler.Internal.BoundTree;
using Draco.Compiler.Internal.Diagnostics;
using Draco.Compiler.Internal.Solver;
using Draco.Compiler.Internal.Symbols;
using Draco.Compiler.Internal.Symbols.Script;
using Draco.Compiler.Internal.Symbols.Source;

namespace Draco.Compiler.Internal.Binding;

internal partial class Binder
{
    public virtual BoundStatement BindFunction(SourceFunctionSymbol function, DiagnosticBag diagnostics)
    {
        var functionName = function.DeclaringSyntax.Name.Text;
        var constraints = new ConstraintSolver(function.DeclaringSyntax, $"function {functionName}");
        var statementTask = this.BindStatement(function.DeclaringSyntax.Body, constraints, diagnostics);
        constraints.Solve(diagnostics);
        return statementTask.Result;
    }

    public virtual GlobalBinding BindGlobal(SourceGlobalSymbol global, DiagnosticBag diagnostics)
    {
        var globalName = global.DeclaringSyntax.Name.Text;
        var constraints = new ConstraintSolver(global.DeclaringSyntax, $"global {globalName}");

        var typeSyntax = global.DeclaringSyntax.Type;
        var valueSyntax = global.DeclaringSyntax.Value;

        // Bind type and value
        var type = typeSyntax is null ? null : this.BindTypeToTypeSymbol(typeSyntax.Type, diagnostics);
        var valueTask = valueSyntax is null
            ? null
            : this.BindExpression(valueSyntax.Value, constraints, diagnostics);

        // Infer declared type
        var declaredType = type ?? constraints.AllocateTypeVariable(track: false);

        // Add assignability constraint, if needed
        if (valueTask is not null)
        {
            constraints.Assignable(
                declaredType,
                valueTask.GetResultType(valueSyntax, constraints, diagnostics),
                global.DeclaringSyntax.Value!.Value);
        }

        // Solve
        constraints.Solve(diagnostics);

        // Type out the expression, if needed
        var boundValue = valueTask?.Result;

        // Unwrap the type
        declaredType = declaredType.Substitution;

        if (declaredType.IsTypeVariable)
        {
            // We could not infer the type
            diagnostics.Add(Diagnostic.Create(
                template: TypeCheckingErrors.CouldNotInferType,
                location: global.DeclaringSyntax.Location,
                formatArgs: global.Name));
            // We use an error type
            declaredType = WellKnownTypes.ErrorType;
        }

        // Done
        return new(declaredType, boundValue);
    }

    public virtual ScriptBinding BindScript(ScriptModuleSymbol module, DiagnosticBag diagnostics)
    {
        // Binding scripts is a little different, since they share the inference context,
        // meaning that a global can be inferred from a much later context

        var solver = new ConstraintSolver(module.DeclaringSyntax, "script");

        var evalFuncStatements = new List<BindingTask<BoundStatement>>();

        // Go through all statements and bind them
        foreach (var stmt in module.DeclaringSyntax.Statements)
        {
            if (stmt is DeclarationStatementSyntax declStmt)
            {
                var decl = declStmt.Declaration;
                // Imports are skipped
                if (decl is ImportDeclarationSyntax) continue;
                // Globals mean an assignment into the eval function
                if (decl is VariableDeclarationSyntax varDecl)
                {
                    // TODO
                    continue;
                }
                // Functions are just bound in this context
                if (decl is FunctionDeclarationSyntax funcDecl)
                {
                    // TODO
                    continue;
                }
            }
            else
            {
                // Regular statement, that goes into the eval function
                var evalFuncStmt = this.BindStatement(stmt, solver, diagnostics);
                evalFuncStatements.Add(evalFuncStmt);
            }
        }
    }
}
