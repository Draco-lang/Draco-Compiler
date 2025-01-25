using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Linq;
using Draco.Chr.Constraints;
using Draco.Chr.Rules;
using Draco.Compiler.Internal.Binding;
using Draco.Compiler.Internal.Diagnostics;
using Draco.Compiler.Internal.Solver.Constraints;
using Draco.Compiler.Internal.Solver.OverloadResolution;
using Draco.Compiler.Internal.Symbols;
using Draco.Compiler.Internal.Symbols.Error;
using Draco.Compiler.Internal.Symbols.Synthetized;
using static Draco.Chr.Rules.RuleFactory;
using IChrSolver = Draco.Chr.Solve.ISolver;

namespace Draco.Compiler.Internal.Solver;

internal sealed partial class ConstraintSolver
{
    private IEnumerable<Rule> ConstructRules(DiagnosticBag diagnostics) => [
        // Trivial same-type constraint, unify all
        Simplification(typeof(Same))
            .Body((ConstraintStore store, Same same) =>
            {
                for (var i = 1; i < same.Types.Length; ++i)
                {
                    if (Unify(same.Types[0], same.Types[i])) continue;
                    // Type-mismatch
                    same.ReportDiagnostic(diagnostics, builder => builder
                        .WithFormatArgs(same.Types[0].Substitution, same.Types[i].Substitution));
                    break;
                }
            })
            .Named("same"),

        // Assignable can be resolved directly, if both types are ground-types
        Simplification(typeof(Assignable))
            .Guard((Assignable assignable) => assignable.TargetType.IsGroundType
                                           && assignable.AssignedType.IsGroundType)
            .Body((ConstraintStore store, Assignable assignable) =>
            {
                var targetType = assignable.TargetType;
                var assignedType = assignable.AssignedType;
                if (SymbolEqualityComparer.Default.IsBaseOf(targetType, assignedType))
                {
                    // Ok
                    return;
                }
                // Error
                assignable.ReportDiagnostic(diagnostics, diag => diag
                    .WithFormatArgs(targetType, assignedType));
            })
            .Named("assignable"),

        // If all types are ground-types, common-type constraints are trivial
        Simplification(typeof(CommonAncestor))
            .Guard((CommonAncestor common) => common.AlternativeTypes.All(t => t.IsGroundType))
            .Body((ConstraintStore store, CommonAncestor common) =>
            {
                foreach (var type in common.AlternativeTypes)
                {
                    if (!common.AlternativeTypes.All(t => SymbolEqualityComparer.Default.IsBaseOf(type, t))) continue;
                    // Found a good common type
                    this.Assignable(common.CommonType, type, ConstraintLocator.Constraint(common));
                    return;
                }
                // No common type found
                common.ReportDiagnostic(diagnostics, builder => builder
                    .WithFormatArgs(string.Join(", ", common.AlternativeTypes)));
                // Stop cascading uninferred type
                UnifyWithError(common.CommonType);
            })
            .Named("common_ancestor"),

        // Member constraints are trivial, if the receiver is a ground-type
        Simplification(typeof(Member))
            .Guard((Member member) => !member.Receiver.Substitution.IsTypeVariable)
            .Body((ConstraintStore store, Member member) =>
            {
                var accessed = member.Receiver.Substitution;
                // Don't propagate type errors
                if (accessed.IsError)
                {
                    UnifyWithError(member.MemberType);
                    member.CompletionSource.SetResult(ErrorMemberSymbol.Instance);
                    return;
                }

                // Not a type variable, we can look into members
                var membersWithName = accessed.AllMembers
                    .Where(m => m.Name == member.MemberName)
                    .ToImmutableArray();
                if (membersWithName.Length == 0)
                {
                    // We have special member constraints that are created for operator lookup
                    // They can fail when we try to lookup operators for things like int32, which is defined globally
                    // NOTE: We might want to inject these operators into the target types in the future instead
                    if (!member.AllowFailure)
                    {
                        // No such member, error
                        member.ReportDiagnostic(diagnostics, builder => builder
                            .WithFormatArgs(member.MemberName, accessed));
                    }
                    // We still provide a single error symbol
                    UnifyWithError(member.MemberType);
                    member.CompletionSource.SetResult(ErrorMemberSymbol.Instance);
                    return;
                }
                if (membersWithName.Length == 1)
                {
                    // Check visibility
                    this.Context.CheckVisibility(member.Locator, membersWithName[0], "member", diagnostics);
                    // One member, we know what type the member type is
                    var memberType = ((ITypedSymbol)membersWithName[0]).Type;
                    // NOTE: There used to be an assignable constraint here
                    // But I believe the constraint should strictly stick to its semantics
                    // And just provide the member type as is
                    UnifyAsserted(member.MemberType, memberType);
                    member.CompletionSource.SetResult(membersWithName[0]);
                    return;
                }
                // More than one, the member constraint is fine with multiple members but we don't know the member type
                {
                    // All must be functions, otherwise we have bigger problems
                    // TODO: Can this assertion fail? Like in a faulty module decl?
                    // NOTE: Visibility will be checked by the overload constraint
                    Debug.Assert(membersWithName.All(m => m is FunctionSymbol));
                    UnifyWithError(member.MemberType);
                    var overload = new FunctionGroupSymbol(membersWithName.Cast<FunctionSymbol>().ToImmutableArray());
                    member.CompletionSource.SetResult(overload);
                }
            })
            .Named("member"),

        // Indexer constraints are trivial, if the receiver is a ground-type
        Simplification(typeof(Indexer))
            .Guard((Indexer indexer) => !indexer.Receiver.Substitution.IsTypeVariable)
            .Body((ConstraintStore store, Indexer indexer) =>
            {
                var accessed = indexer.Receiver.Substitution;
                // Don't propagate type errors
                if (accessed.IsError)
                {
                    UnifyWithError(indexer.ElementType);
                    // Best-effort shape approximation
                    var errorSymbol = indexer.IsGetter
                        ? ErrorPropertySymbol.CreateIndexerGet(indexer.Indices.Length)
                        : ErrorPropertySymbol.CreateIndexerSet(indexer.Indices.Length);
                    indexer.CompletionSource.SetResult(errorSymbol);
                    return;
                }

                // Not a type variable, we can look into members
                var indexers = accessed.AllMembers
                    .OfType<PropertySymbol>()
                    .Where(p => p.IsIndexer)
                    .Select(p => indexer.IsGetter ? p.Getter : p.Setter)
                    .OfType<FunctionSymbol>()
                    .ToImmutableArray();
                if (indexers.Length == 0)
                {
                    indexer.ReportDiagnostic(diagnostics, diag => diag
                        .WithTemplate(indexer.IsGetter
                            ? SymbolResolutionErrors.NoGettableIndexerInType
                            : SymbolResolutionErrors.NoSettableIndexerInType)
                        .WithFormatArgs(accessed));

                    UnifyWithError(indexer.ElementType);
                    // Best-effort shape approximation
                    var errorSymbol = indexer.IsGetter
                        ? ErrorPropertySymbol.CreateIndexerGet(indexer.Indices.Length)
                        : ErrorPropertySymbol.CreateIndexerSet(indexer.Indices.Length);
                    indexer.CompletionSource.SetResult(errorSymbol);
                    return;
                }

                // If there is a single indexer, we check visibility
                // This is because in this case overload resolution will skip hecking visibility
                if (indexers.Length == 1)
                {
                    this.Context.CheckVisibility(indexer.Locator, indexers[0], "indexer", diagnostics);
                }

                if (indexer.IsGetter)
                {
                    // Getter, elementType is return type
                    store.Add(new Overload(
                        locator: ConstraintLocator.Constraint(indexer),
                        functionName: "operator[]",
                        candidates: OverloadCandidateSet.Create(indexers, indexer.Indices),
                        returnType: indexer.ElementType)
                    {
                        // Important, we propagate the completion source
                        CompletionSource = indexer.CompletionSource,
                    });
                }
                else
                {
                    // Setter
                    // We allocate a type var for the return type, but we don't care about it
                    var returnType = this.AllocateTypeVariable();
                    store.Add(new Overload(
                        locator: ConstraintLocator.Constraint(indexer),
                        functionName: "operator[]",
                        candidates: OverloadCandidateSet.Create(
                            indexers,
                            // TODO: We pass in null for the value syntax...
                            indexer.Indices.Append(this.Arg(null, indexer.ElementType))),
                        returnType: returnType)
                    {
                        // Important, we propagate the completion source
                        CompletionSource = indexer.CompletionSource,
                    });
                }
            })
            .Named("indexer"),

        // A callable can be resolved directly, if the called type is not a type-variable
        Simplification(typeof(Callable))
            .Guard((Callable callable) => !callable.CalledType.Substitution.IsTypeVariable)
            .Body((ConstraintStore store, Callable callable) =>
            {
                var called = callable.CalledType.Substitution;
                if (called.IsError)
                {
                    // Don't propagate errors
                    UnifyWithError(callable.ReturnType);
                    return;
                }

                // We can now check if it's a function
                // The called thing is either a function, or is a delegate with the appropriate signature
                var functionType = called as FunctionTypeSymbol
                                ?? called.InvokeSignatureType;
                if (functionType is null)
                {
                    // Error
                    UnifyWithError(callable.ReturnType);
                    callable.ReportDiagnostic(diagnostics, diag => diag
                        .WithTemplate(TypeCheckingErrors.CallNonFunction)
                        .WithFormatArgs(called));
                    return;
                }

                // It's a function
                // The inferred return type must be assignable to the return type of the function
                this.Assignable(
                    functionType.ReturnType,
                    callable.ReturnType,
                    ConstraintLocator.Constraint(callable));

                // Check if it has the same number of args
                if (functionType.Parameters.Length != callable.Arguments.Length)
                {
                    // Error
                    callable.ReportDiagnostic(diagnostics, diag => diag
                        .WithTemplate(TypeCheckingErrors.TypeMismatch)
                        .WithFormatArgs(
                            functionType,
                            this.MakeMismatchedFunctionType(callable.Arguments, functionType.ReturnType)));
                    return;
                }

                // Start scoring args
                var candidate = CallCandidate.Create(functionType);
                candidate.Refine(callable.Arguments);

                if (candidate.IsEliminated)
                {
                    // Error
                    callable.ReportDiagnostic(diagnostics, diag => diag
                        .WithTemplate(TypeCheckingErrors.TypeMismatch)
                        .WithFormatArgs(
                            functionType,
                            this.MakeMismatchedFunctionType(callable.Arguments, functionType.ReturnType)));
                    return;
                }

                // We are done
                foreach (var (param, arg) in functionType.Parameters.Zip(callable.Arguments))
                {
                    AssignParameterToArgument(store, param.Type, arg);
                }
            })
            .Named("callable"),

        // If an overload constraint can be advanced, do that
        // NOTE: We don't save history to allow applying multiple times
        Propagation(saveHistory: false, typeof(Overload))
            .Guard((Overload overload) => !overload.Candidates.IsWellDefined && overload.Candidates.Refine())
            .Named("overload_step"),

        // If overload constraints are unambiguous, we can resolve them directly
        Simplification(typeof(Overload))
            .Guard((Overload overload) => overload.Candidates.IsWellDefined)
            .Body((ConstraintStore store, Overload overload) =>
            {
                // Call for safety
                overload.Candidates.Refine();

                var candidates = overload.Candidates.Dominators;
                if (candidates.Length == 0)
                {
                    // No such overload, error
                    FailOverload(overload);
                    // NOTE: If the arguments have an error, we don't report an error here to not cascade errors
                    if (overload.Candidates.Arguments.All(a => a.Type.Substitution.IsTypeVariable || !a.Type.Substitution.IsError))
                    {
                        overload.ReportDiagnostic(diagnostics, diag => diag
                            .WithTemplate(TypeCheckingErrors.NoMatchingOverload)
                            .WithFormatArgs(overload.FunctionName));
                    }
                    return;
                }

                if (candidates.Length > 1)
                {
                    // Ambiguity, error
                    FailOverload(overload);
                    // NOTE: If the arguments have an error, we don't report an error here to not cascade errors
                    if (overload.Candidates.Arguments.All(a => !a.Type.Substitution.IsError))
                    {
                        overload.ReportDiagnostic(diagnostics, diag => diag
                            .WithTemplate(TypeCheckingErrors.AmbiguousOverloadedCall)
                            .WithFormatArgs(overload.FunctionName, string.Join(", ", overload.Candidates)));
                    }
                    return;
                }

                // Resolved fine, choose the symbol, which might generic-instantiate it
                var chosen = this.GenericInstantiateIfNeeded(candidates.Single().Data);
                if (overload.Candidates.InitialCandidates.Length != 1)
                {
                    // We assume that if the initial candidate count was 1, we already checked visibility
                    this.Context.CheckVisibility(overload.Locator, chosen, "overload", diagnostics);
                }
                // Inference
                if (chosen.IsVariadic)
                {
                    if (!BinderFacts.TryGetVariadicElementType(chosen.Parameters[^1].Type, out var elementType))
                    {
                        // Should not happen
                        throw new InvalidOperationException();
                    }
                    var nonVariadicPairs = chosen.Parameters
                        .SkipLast(1)
                        .Zip(overload.Candidates.Arguments);
                    var variadicPairs = overload.Candidates.Arguments
                        .Skip(chosen.Parameters.Length - 1)
                        .Select(a => (ParameterType: elementType, ArgumentType: a));
                    // Non-variadic part
                    foreach (var (param, arg) in nonVariadicPairs) AssignParameterToArgument(store, param.Type, arg);
                    // Variadic part
                    foreach (var (paramType, arg) in variadicPairs) AssignParameterToArgument(store, paramType, arg);
                }
                else
                {
                    foreach (var (param, arg) in chosen.Parameters.Zip(overload.Candidates.Arguments))
                    {
                        AssignParameterToArgument(store, param.Type, arg);
                    }
                }
                // NOTE: This used to be an assignment, but again, I don't think that's in the scope of this constraint
                // In all cases, return type is simple
                UnifyAsserted(overload.ReturnType, chosen.ReturnType);
                // Resolve promise
                overload.CompletionSource.SetResult(chosen);
            })
            .Named("overload"),

        // As a last resort, we try to drive forward the solver by trying to merge assignable constraints with the same target
        // This is a common situation for things like this:
        //
        // var x = Derived();
        // x = Base();
        //
        // In this case we try to search for the common type of Derived and Base, then assign that
        Simplification(typeof(Assignable), typeof(Assignable))
            .Guard((Assignable a1, Assignable a2) =>
                SymbolEqualityComparer.AllowTypeVariables.Equals(a1.TargetType, a2.TargetType))
            .Body((ConstraintStore store, Assignable a1, Assignable a2) =>
            {
                var targetType = a1.TargetType;
                var commonType = this.AllocateTypeVariable();
                store.Add(new CommonAncestor(
                    locator: ConstraintLocator.Constraint(a2),
                    commonType: commonType,
                    alternativeTypes: [a1.AssignedType, a2.AssignedType]));
                store.Add(new Assignable(
                    locator: ConstraintLocator.Constraint(a2),
                    targetType: targetType,
                    assignedType: commonType));
            })
            .Named("merge_assignables"),

        // As a last-last effort, we assume that a singular assignment means exact matching types
        Simplification(typeof(Assignable))
            .Guard((Assignable assignable) => CanAssign(assignable.TargetType, assignable.AssignedType))
            .Body((ConstraintStore store, Assignable assignable) =>
                AssignAsserted(assignable.TargetType, assignable.AssignedType))
            .Named("sole_assignable"),

        // As a last-effort, if we see a common ancestor constraint with a single non-type-var, we
        // assume that the common type is the non-type-var
        // We also substitute all the type-vars with the common type
        Simplification(typeof(CommonAncestor))
            .Guard((CommonAncestor common) =>
            {
                if (common.AlternativeTypes.Count(t => !t.Substitution.IsTypeVariable) != 1) return false;
                if (common.AlternativeTypes.Count(t => t.Substitution.IsTypeVariable) != common.AlternativeTypes.Length - 1) return false;
                if (common.AlternativeTypes.Any(alt => !CanUnify(alt, common.CommonType))) return false;
                var nonTypeVar = common.AlternativeTypes.First(t => !t.Substitution.IsTypeVariable);
                var typeVars = common.AlternativeTypes.Where(t => t.Substitution.IsTypeVariable);
                if (!CanUnify(common.CommonType, nonTypeVar)) return false;
                return typeVars.All(t => CanUnify(t, nonTypeVar));
            })
            .Body((ConstraintStore store, CommonAncestor common) =>
            {
                var nonTypeVar = common.AlternativeTypes.First(t => !t.Substitution.IsTypeVariable);
                var typeVars = common.AlternativeTypes.Where(t => t.Substitution.IsTypeVariable);
                foreach (var typeVar in typeVars) UnifyAsserted(typeVar, nonTypeVar);
                UnifyAsserted(common.CommonType, nonTypeVar);
            })
            .Named("most_specific_common_ancestor"),

        // If the target type of common ancestor is a concrete type, we can try to unify all non-concrete types
        Simplification(typeof(CommonAncestor))
            .Guard((CommonAncestor common) => common.CommonType.Substitution.IsGroundType
                                           && common.AlternativeTypes.All(alt => CanUnify(alt, common.CommonType)))
            .Body((ConstraintStore store, CommonAncestor common) =>
            {
                var concreteType = common.CommonType.Substitution;
                foreach (var type in common.AlternativeTypes) UnifyAsserted(type, concreteType);
            })
            .Named("concrete_common_ancestor"),
    ];

    /// <summary>
    /// Fails the overload constraint by setting the return type to an error type and resolving the promise.
    /// </summary>
    /// <param name="overload">The overload constraint to fail.</param>
    private static void FailOverload(Overload overload)
    {
        UnifyWithError(overload.ReturnType);
        var errorSymbol = new ErrorFunctionSymbol(overload.Candidates.Arguments.Length);
        overload.CompletionSource.SetResult(errorSymbol);
    }

    /// <summary>
    /// Attempts to fail all remaining constraints in the store.
    /// </summary>
    /// <param name="solver">The CHR solver to use for solving the constraints.</param>
    private void FailRemainingRules(IChrSolver solver)
    {
        var previousStoreSize = this.constraintStore.Count;
        while (true)
        {
            // We unify type variables with the error type
            foreach (var typeVar in this.typeVariables)
            {
                var unwrapped = typeVar.Substitution;
                if (unwrapped is TypeVariable unwrappedTv) UnifyAsserted(unwrappedTv, WellKnownTypes.UninferredType);
            }

            var constraintsToRemove = new List<IConstraint>();

            // We can also solve all overload constraints by failing them instantly
            var overloadConstraints = this.constraintStore
                .ConstraintsOfType(typeof(Overload))
                .ToList();
            foreach (var constraint in overloadConstraints)
            {
                var overload = (Overload)constraint.Value;
                FailOverload(overload);
                constraintsToRemove.Add(constraint);
            }

            this.constraintStore.RemoveRange(constraintsToRemove);

            // Assume this solves everything
            solver.Solve(this.constraintStore);

            // Check for exit condition
            if (previousStoreSize == this.constraintStore.Count) break;
            previousStoreSize = this.constraintStore.Count;
        }

        if (this.constraintStore.Count > 0)
        {
            throw new InvalidOperationException("fallback operation could not solve all constraints");
        }
    }
}
