using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Draco.Compiler.Internal.Symbols;

namespace Draco.Compiler.Internal.OptimizingIr;

/// <summary>
/// A mutable <see cref="IProcedure"/> implementation.
/// </summary>
internal sealed class Procedure : IProcedure
{
    public FunctionSymbol Symbol => throw new NotImplementedException();
    public Assembly Assembly => throw new NotImplementedException();
    IAssembly IProcedure.Assembly => this.Assembly;
    public BasicBlock Entry => throw new NotImplementedException();
    IBasicBlock IProcedure.Entry => this.Entry;
    public IEnumerable<BasicBlock> BasicBlocks => throw new NotImplementedException();
    IEnumerable<IBasicBlock> IProcedure.BasicBlocks => this.BasicBlocks;
}
