using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Draco.Compiler.Api.Diagnostics;
using Draco.Compiler.Api.Syntax;
using Draco.Compiler.Internal.BoundTree;
using Draco.Compiler.Internal.Diagnostics;
using Draco.Compiler.Internal.Solver;
using Draco.Compiler.Internal.Symbols;
using Draco.Compiler.Internal.Symbols.Script;
using Draco.Compiler.Internal.Symbols.Source;
using static Draco.Compiler.Internal.BoundTree.BoundTreeFactory;

namespace Draco.Compiler.Internal.Binding;

internal partial class Binder
{
    protected readonly record struct AttributeBinding(
        AttributeSyntax Syntax,
        AttributeInstance Instance,
        AttributeUsageAttribute? Usage);

    public virtual BoundStatement BindFunction(SourceFunctionSymbol function, DiagnosticBag diagnostics)
    {
        var functionName = function.DeclaringSyntax.Name.Text;
        var constraints = new ConstraintSolver(this, $"function {functionName}");
        var statementTask = this.BindStatement(function.DeclaringSyntax.Body, constraints, diagnostics);
        constraints.Solve(diagnostics);
        return statementTask.Result;
    }

    public virtual GlobalBinding BindGlobalField(SourceFieldSymbol global, DiagnosticBag diagnostics) =>
        this.BindGlobal(global, global.DeclaringSyntax, diagnostics);

    public virtual GlobalBinding BindGlobalProperty(SourceAutoPropertySymbol global, DiagnosticBag diagnostics) =>
        this.BindGlobal(global, global.DeclaringSyntax, diagnostics);

    private GlobalBinding BindGlobal(Symbol symbol, VariableDeclarationSyntax syntax, DiagnosticBag diagnostics)
    {
        var globalName = syntax.Name.Text;
        var constraints = new ConstraintSolver(this, $"global {globalName}");

        var typeSyntax = syntax.Type?.Type;
        var valueSyntax = syntax.Value?.Value;

        // Bind type and value
        var type = typeSyntax is null ? null : this.BindTypeToTypeSymbol(typeSyntax, diagnostics);
        var valueTask = valueSyntax is null
            ? null
            : this.BindExpression(valueSyntax, constraints, diagnostics);

        // Infer declared type
        var declaredType = type ?? constraints.AllocateTypeVariable(track: false);

        // Add assignability constraint, if needed
        if (valueTask is not null)
        {
            constraints.Assignable(
                declaredType,
                valueTask.GetResultType(valueSyntax, constraints, diagnostics),
                valueSyntax!);
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
                location: syntax.Location,
                formatArgs: symbol.Name));
            // We use an error type
            declaredType = WellKnownTypes.ErrorType;
        }

        // Done
        return new(declaredType, boundValue);
    }

    // TODO: Does this need to be virtual?
    public virtual ScriptBinding BindScript(ScriptModuleSymbol module, DiagnosticBag diagnostics)
    {
        // Binding scripts is a little different, since they share the inference context,
        // meaning that a global can be inferred from a much later context

        var solver = new ConstraintSolver(this, "script");

        var globalBindings = ImmutableDictionary.CreateBuilder<VariableDeclarationSyntax, GlobalBinding>();
        var functionBodies = ImmutableDictionary.CreateBuilder<FunctionDeclarationSyntax, BoundStatement>();
        var evalFuncStatements = ImmutableArray.CreateBuilder<BoundStatement>();

        // NOTE: Since the API is asynchronous where task results are only available after
        // the solver has been asked to solve, we store "continuations" here and run them at the end
        // Quite hacky, but will work for now
        var fillerTasks = new List<Action>();

        // Go through all statements and bind them
        foreach (var stmt in module.DeclaringSyntax.Statements)
        {
            if (stmt is DeclarationStatementSyntax declStmt)
            {
                var decl = declStmt.Declaration;
                // Imports are skipped
                if (decl is ImportDeclarationSyntax) continue;
                // Global fields and props mean an assignment into the eval function
                if (decl is VariableDeclarationSyntax { FieldModifier: not null } fieldDecl)
                {
                    // Retrieve the symbol
                    var symbol = module.AllMembers
                        .OfType<ScriptFieldSymbol>()
                        .First(g => g.DeclaringSyntax == fieldDecl);

                    BindGlobalField(symbol);

                    continue;
                }
                if (decl is VariableDeclarationSyntax { FieldModifier: null } propDecl)
                {
                    // Retrieve the symbol
                    var symbol = module.AllMembers
                        .OfType<ScriptAutoPropertySymbol>()
                        .First(g => g.DeclaringSyntax == propDecl);

                    BindGlobalProperty(symbol);

                    continue;
                }
                // Functions are just bound in this context
                if (decl is FunctionDeclarationSyntax funcDecl)
                {
                    // Retrieve the symbol
                    var symbol = module.AllMembers
                        .OfType<ScriptFunctionSymbol>()
                        .First(f => f.DeclaringSyntax == funcDecl);

                    BindFunction(symbol);

                    continue;
                }
            }
            else
            {
                // Regular statement, that goes into the eval function
                var evalFuncStmt = this.BindStatement(stmt, solver, diagnostics);
                fillerTasks.Add(() => evalFuncStatements.Add(evalFuncStmt.Result));
            }
        }

        // Infer evaluation type
        var evalType = WellKnownTypes.Unit;
        if (module.DeclaringSyntax.Value is not null)
        {
            // Bind the expression
            var resultValue = this.BindExpression(module.DeclaringSyntax.Value, solver, diagnostics);
            evalType = resultValue.GetResultType(module.DeclaringSyntax.Value, solver, diagnostics);
            // Add return statement
            fillerTasks.Add(() => evalFuncStatements.Add(ExpressionStatement(ReturnExpression(resultValue.Result))));
        }
        else
        {
            // Add a default return statement
            fillerTasks.Add(() => evalFuncStatements.Add(ExpressionStatement(ReturnExpression(BoundUnitExpression.Default))));
        }

        // Run the solver
        solver.Solve(diagnostics);

        // Now we can run the fillers
        foreach (var filler in fillerTasks) filler();

        // And finally have all the results
        return new ScriptBinding(
            GlobalBindings: globalBindings.ToImmutable(),
            FunctionBodies: functionBodies.ToImmutable(),
            EvalBody: ExpressionStatement(BlockExpression(
                locals: [],
                statements: evalFuncStatements.ToImmutableArray(),
                value: BoundUnitExpression.Default)),
            EvalType: evalType);

        void BindGlobalField(ScriptFieldSymbol symbol) => BindGlobal(symbol.DeclaringSyntax, symbol);
        void BindGlobalProperty(ScriptAutoPropertySymbol symbol) => BindGlobal(symbol.DeclaringSyntax, symbol.BackingField);

        void BindGlobal(VariableDeclarationSyntax syntax, FieldSymbol targetField)
        {
            var typeSyntax = syntax.Type?.Type;
            var valueSyntax = syntax.Value?.Value;

            var type = typeSyntax is null ? null : this.BindTypeToTypeSymbol(typeSyntax, diagnostics);
            var valueTask = valueSyntax is null ? null : this.BindExpression(valueSyntax, solver, diagnostics);

            // Infer declared type
            var declaredType = type ?? solver.AllocateTypeVariable();

            // Unify with the type declared on the symbol
            ConstraintSolver.UnifyAsserted(declaredType, targetField.Type);

            // Add assignability constraint, if needed
            if (valueTask is not null)
            {
                solver.Assignable(
                    declaredType,
                    valueTask.GetResultType(valueSyntax, solver, diagnostics),
                    valueSyntax!);
            }

            fillerTasks.Add(() =>
            {
                var assignedValue = valueTask?.Result;
                if (assignedValue is not null)
                {
                    // Add the assignment to the eval function
                    evalFuncStatements.Add(ExpressionStatement(AssignmentExpression(
                        left: FieldLvalue(receiver: null, field: targetField),
                        right: assignedValue)));
                }

                globalBindings.Add(syntax, new(declaredType, assignedValue));
            });
        }

        void BindFunction(ScriptFunctionSymbol symbol)
        {
            var binder = this.GetBinder(symbol.DeclaringSyntax);
            var statementTask = binder.BindStatement(symbol.DeclaringSyntax.Body, solver, diagnostics);
            fillerTasks.Add(() => functionBodies.Add(symbol.DeclaringSyntax, statementTask.Result));
        }
    }

    public virtual ImmutableArray<AttributeInstance> BindAttributeList(
        Symbol target, SyntaxList<AttributeSyntax> syntaxes, DiagnosticBag diagnostics)
    {
        var builder = ImmutableArray.CreateBuilder<AttributeInstance>();
        foreach (var syntax in syntaxes)
        {
            var binding = this.BindAttribute(target, syntax, diagnostics);

            if (binding.Usage?.AllowMultiple == false)
            {
                // Check for previous attributes of the same type
                var previous = builder
                    .FirstOrDefault(attr => SymbolEqualityComparer.Default.Equals(attr.Type, binding.Instance.Type));

                if (previous is not null)
                {
                    // Report error
                    diagnostics.Add(Diagnostic.Create(
                        template: TypeCheckingErrors.AttributeAlreadyApplied,
                        location: syntax.Location,
                        formatArgs: [binding.Instance.Type]));
                }
            }

            builder.Add(binding.Instance);
        }
        return builder.ToImmutable();
    }

    // TODO: Does this need to be virtual?
    // NOTE: Probably yes to stash reference between syntax and symbol in incremental binding
    protected virtual AttributeBinding BindAttribute(Symbol target, AttributeSyntax syntax, DiagnosticBag diagnostics)
    {
        var attributeType = this.BindTypeToTypeSymbol(syntax.Type, diagnostics);
        if (!attributeType.IsAttributeType)
        {
            diagnostics.Add(Diagnostic.Create(
                template: TypeCheckingErrors.NotAnAttribute,
                location: syntax.Location,
                formatArgs: [attributeType]));
        }

        var fixedArguments = ImmutableArray.CreateBuilder<ConstantValue>();
        // TODO: Implement named arguments
        var namedArguments = ImmutableDictionary.CreateBuilder<string, ConstantValue>();

        // We need to resolve the proper overload for the constructor
        var solver = new ConstraintSolver(this, "attribute");

        var attribCtors = attributeType.Constructors;
        var argTasks = syntax.Arguments?.ArgumentList.Values
            .Select(arg => this.BindExpression(arg, solver, diagnostics))
            .ToList() ?? [];

        var ctorTask = solver.Overload(
            name: attributeType.Name,
            functions: attribCtors.ToImmutableArray(),
            args: argTasks
                .Zip(syntax.Arguments?.ArgumentList.Values ?? [])
                .Select(pair => solver.Arg(pair.Second, pair.First, diagnostics))
                .ToImmutableArray(),
            returnType: out _,
            syntax: syntax);

        solver.Solve(diagnostics);

        // Turn the bound args into constant values
        var constantArgs = argTasks
            .Select(arg => this.Compilation.ConstantEvaluator.Evaluate(arg.Result, diagnostics));
        fixedArguments.AddRange(constantArgs);

        this.BindSyntaxToSymbol(syntax, ctorTask.Result);

        var instance = new AttributeInstance(ctorTask.Result, fixedArguments.ToImmutable(), namedArguments.ToImmutable());
        var usage = this.CheckForValidAttributeUsage(target, syntax, attributeType, diagnostics);

        return new(syntax, instance, usage);
    }

    private AttributeUsageAttribute? CheckForValidAttributeUsage(
        Symbol target, AttributeSyntax syntax, TypeSymbol attributeType, DiagnosticBag diagnostics)
    {
        var (targetFlag, targetName) = target switch
        {
            FunctionSymbol _ => (AttributeTargets.Method, "function"),
            FieldSymbol { IsStatic: true } _ => (AttributeTargets.Field, "global"),
            ParameterSymbol _ => (AttributeTargets.Parameter, "parameter"),
            TypeSymbol t when t.IsValueType => (AttributeTargets.Struct, "value-type"),
            TypeSymbol => (AttributeTargets.Class, "reference-type"),
            _ => throw new ArgumentOutOfRangeException(nameof(target)),
        };

        // Check for usage, if not present, we allow
        var attributeUsage = attributeType.GetAttribute<AttributeUsageAttribute>(this.WellKnownTypes.SystemAttributeUsageAttribute);
        if (attributeUsage is null) return null;

        if (!attributeUsage.ValidOn.HasFlag(targetFlag))
        {
            diagnostics.Add(Diagnostic.Create(
                template: TypeCheckingErrors.CanNotApplyAttribute,
                location: syntax.Location,
                formatArgs: [attributeType, targetName]));
        }

        return attributeUsage;
    }
}
