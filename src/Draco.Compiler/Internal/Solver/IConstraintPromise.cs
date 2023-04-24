using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Draco.Compiler.Api.Diagnostics;
using Draco.Compiler.Internal.Diagnostics;

namespace Draco.Compiler.Internal.Solver;

/// <summary>
/// Represents the promise for a solver constraint. The promise can resolve, which means that the corresponding
/// constraint was solved successfully, or it can fail, which corresponds to the fact that the constraint can
/// not be solved.
/// </summary>
internal interface IConstraintPromise
{
    /// <summary>
    /// True, if this promise is resolved, either ba succeeding or failing.
    /// </summary>
    public bool IsResolved { get; }

    /// <summary>
    /// Configures the diagnostic messages for the constraint of this promise in case it fails.
    /// </summary>
    /// <param name="configure">The configuration function.</param>
    /// <returns>The promise instance.</returns>
    public IConstraintPromise ConfigureDiagnostics(Action<Diagnostic.Builder> configure);
}

/// <summary>
/// An <see cref="IConstraintPromise"/> with a known result type <typeparamref name="TResult"/>.
/// </summary>
/// <typeparam name="TResult">The result type of the promise.</typeparam>
internal interface IConstraintPromise<TResult> : IConstraintPromise
{
    /// <summary>
    /// The result of the promise.
    /// </summary>
    public TResult Result { get; }

    /// <summary>
    /// Resolves this promise with the given result.
    /// </summary>
    /// <param name="result">The result value to resolve with.</param>
    public void Resolve(TResult result);

    /// <summary>
    /// Fails this constraint, reporting the error.
    /// </summary>
    /// <param name="result">The result for the failure.</param>
    /// <param name="diagnostics">The diagnostics to report to, if needed.</param>
    public void Fail(TResult result, DiagnosticBag? diagnostics);

    /// <summary>
    /// <see cref="IConstraintPromise.ConfigureDiagnostics(Action{Diagnostic.Builder})"/>.
    /// </summary>
    public new IConstraintPromise<TResult> ConfigureDiagnostics(Action<Diagnostic.Builder> configure);
}
