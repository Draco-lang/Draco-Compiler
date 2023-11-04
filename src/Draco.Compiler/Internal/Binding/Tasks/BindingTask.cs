using System.Collections.Generic;
using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using Draco.Compiler.Internal.Solver;
using Draco.Compiler.Internal.Solver.Tasks;
using Draco.Compiler.Internal.Symbols;

namespace Draco.Compiler.Internal.Binding.Tasks;

internal static class BindingTask
{
    public static BindingTask<T> FromResult<T>(ConstraintSolver solver, T result)
    {
        var task = new BindingTask<T>();
        task.Awaiter.Solver = solver;
        task.Awaiter.SetResult(result, null);
        return task;
    }

    public static async SolverTask<ImmutableArray<T>> WhenAll<T>(IEnumerable<BindingTask<T>> tasks)
    {
        var result = ImmutableArray.CreateBuilder<T>();
        foreach (var task in tasks) result.Add(await task);
        return result.ToImmutable();
    }
}

[AsyncMethodBuilder(typeof(BindingTaskMethodBuilder<>))]
internal struct BindingTask<T>
{
    internal BindingTaskAwaiter<T> Awaiter;
    internal readonly ConstraintSolver Solver => this.Awaiter.Solver;
    public readonly bool IsCompleted => this.Awaiter.IsCompleted;
    public readonly T Result => this.Awaiter.GetResult();
    public readonly TypeSymbol? ResultType => this.Awaiter.ResultType;
    public readonly TypeSymbol ResultTypeRequired => this.Awaiter.ResultType
                                                  ?? throw new System.InvalidOperationException();
    public readonly BindingTaskAwaiter<T> GetAwaiter() => this.Awaiter;
}
