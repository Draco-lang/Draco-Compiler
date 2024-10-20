using System.Collections.Generic;
using System.Linq;
using Draco.Compiler.Internal.BoundTree;

namespace Draco.Compiler.Internal.FlowAnalysis.Domains;

/// <summary>
/// The possible return states of a method.
/// </summary>
internal enum ReturnState
{
    /// <summary>
    /// The method does not return on all paths.
    /// </summary>
    DoesNotReturn,

    /// <summary>
    /// The method returns on all paths.
    /// </summary>
    Returns,
}

/// <summary>
/// The domain for analyzing whether a method returns on all paths.
/// </summary>
internal sealed class ReturnsOnAllPathsDomain : FlowDomain<ReturnState>
{
    public override FlowDirection Direction => FlowDirection.Forward;
    public override ReturnState Top => ReturnState.DoesNotReturn;

    public override void Join(ref ReturnState target, IEnumerable<ReturnState> sources) =>
        target = sources.Contains(ReturnState.DoesNotReturn) ? ReturnState.DoesNotReturn : ReturnState.Returns;

    public override bool Transfer(ref ReturnState state, BoundNode node) => node switch
    {
        BoundReturnExpression ret => this.TransferReturn(ref state, ret),
        _ => false,
    };

    private bool TransferReturn(ref ReturnState state, BoundReturnExpression ret)
    {
        if (state == ReturnState.Returns) return false;

        state = ReturnState.Returns;
        return true;
    }
}
