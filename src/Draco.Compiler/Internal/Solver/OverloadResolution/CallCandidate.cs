using System.Collections.Generic;
using System.Linq;
using Draco.Compiler.Internal.Symbols;
using Draco.Compiler.Internal.Utilities;

namespace Draco.Compiler.Internal.Solver.OverloadResolution;

/// <summary>
/// Factory methods for <see cref="CallCandidate{TData}"/>.
/// </summary>
internal static class CallCandidate
{
    public static CallCandidate<FunctionSymbol> Create(FunctionSymbol function) =>
        CallCandidate<FunctionSymbol>.Create(function);

    public static CallCandidate<object?> Create(FunctionTypeSymbol functionType) =>
        CallCandidate<object?>.Create(functionType);
}

/// <summary>
/// Represents a single candidate for overload resolution.
/// </summary>
/// <typeparam name="TData">Additional data type.</typeparam>
internal readonly struct CallCandidate<TData>
{
    public static CallCandidate<FunctionSymbol> Create(FunctionSymbol function) =>
        new(function.Parameters, function.IsVariadic, function);

    // TODO: Can a function type be variadic? This is probably something we should specify...
    public static CallCandidate<object?> Create(FunctionTypeSymbol functionType) =>
        new(functionType.Parameters, false, default);

    /// <summary>
    /// The score of the candidate.
    /// </summary>
    public CallScore Score { get; }

    /// <summary>
    /// True, if the candidate is eliminated.
    /// </summary>
    public bool IsEliminated => this.Score.HasZero;

    /// <summary>
    /// True, if the candidate is well defined.
    /// </summary>
    public bool IsWellDefined => this.Score.IsWellDefined;

    /// <summary>
    /// Additional data associated with the candidate.
    /// </summary>
    public TData Data { get; }

    private readonly IReadOnlyList<ParameterSymbol> parameters;
    private readonly bool isVariadic;

    private CallCandidate(
        IReadOnlyList<ParameterSymbol> parameters,
        bool isVariadic,
        TData data)
    {
        this.parameters = parameters;
        this.isVariadic = isVariadic;
        this.Score = new(parameters.Count);
        this.Data = data;
    }

    /// <summary>
    /// Refines the candidate by scoring the arguments.
    /// </summary>
    /// <param name="arguments">The arguments to use for the refinement.</param>
    /// <returns>True, if the score got changed in any way.</returns>
    public bool Refine(IReadOnlyList<Argument> arguments)
    {
        var changed = false;
        var scoreVector = this.Score;

        for (var i = 0; i < scoreVector.Length; ++i)
        {
            var param = this.parameters[i];
            // Handle that separately
            if (param.IsVariadic) continue;

            if (arguments.Count == i)
            {
                // Special case, this call was extended because of variadics
                if (scoreVector[i] == ArgumentScore.Undefined)
                {
                    scoreVector[i] = ArgumentScore.FullScore;
                    changed = true;
                }
                continue;
            }

            var argType = arguments[i].Type;
            var score = scoreVector[i];

            // If the argument is not null, it means we have already scored it
            if (score != ArgumentScore.Undefined) continue;

            score = ArgumentScore.ScoreArgument(param, argType);
            changed = changed || score != ArgumentScore.Undefined;
            scoreVector[i] = score;

            // If the score hit 0, terminate early, this candidate got eliminated
            if (score == 0) return changed;
        }
        // Handle variadic arguments
        if (this.isVariadic && scoreVector[^1] == ArgumentScore.Undefined)
        {
            var variadicParam = this.parameters[^1];
            var variadicArgs = arguments.Skip(this.parameters.Count - 1);
            var score = ArgumentScore.ScoreVariadicArguments(variadicParam, variadicArgs);
            changed = changed || score != ArgumentScore.Undefined;
            scoreVector[^1] = score;
        }
        return changed;
    }

    public override string ToString() => this.Data?.ToString() ?? "<unknown>";
}
