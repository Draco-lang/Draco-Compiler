using Draco.Compiler.Api.Diagnostics;

namespace Draco.Compiler.Internal.Solver;

/// <summary>
/// Utility base-class for constraints.
/// </summary>
/// <typeparam name="TResult">The result type.</typeparam>
internal abstract class Constraint<TResult> : IConstraint<TResult>
{
    public IConstraintPromise<TResult> Promise { get; }
    IConstraintPromise IConstraint.Promise => this.Promise;
    public Diagnostic.Builder Diagnostic { get; }
    public ConstraintLocator Locator { get; }

    protected Constraint(Diagnostic.Builder diagnostic, ConstraintLocator locator)
    {
        this.Promise = ConstraintPromise.Create(this);
        this.Diagnostic = diagnostic;
        this.Locator = locator;
    }

    protected Constraint(ConstraintLocator locator)
        : this(new(), locator)
    {
    }

    public override abstract string ToString();
}
