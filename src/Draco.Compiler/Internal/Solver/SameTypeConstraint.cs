using Draco.Compiler.Internal.Symbols;

namespace Draco.Compiler.Internal.Solver;

/// <summary>
/// Represents a constraint that enforces two types to be the same.
/// </summary>
internal sealed class SameTypeConstraint : Constraint
{
    /// <summary>
    /// The first type that has to be the same as <see cref="Second"/>.
    /// </summary>
    public TypeSymbol First { get; }

    /// <summary>
    /// The second type that has to be the same as <see cref="First"/>.
    /// </summary>
    public TypeSymbol Second { get; }

    /// <summary>
    /// The promise of this constraint.
    /// </summary>
    public ConstraintPromise<TypeSymbol> Promise { get; }

    public SameTypeConstraint(TypeSymbol first, TypeSymbol second)
    {
        this.First = first;
        this.Second = second;
        this.Promise = ConstraintPromise.FromResult(this, first);
    }
}
