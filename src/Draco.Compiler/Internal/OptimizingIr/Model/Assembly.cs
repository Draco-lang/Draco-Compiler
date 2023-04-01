using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Draco.Compiler.Internal.Symbols;
using Draco.Compiler.Internal.Symbols.Synthetized;
using Draco.Compiler.Internal.Types;

namespace Draco.Compiler.Internal.OptimizingIr.Model;

/// <summary>
/// A mutable <see cref="IAssembly"/> implementation.
/// </summary>
internal sealed class Assembly : IAssembly
{
    private static readonly string doubleNewline = $"{System.Environment.NewLine}{System.Environment.NewLine}";

    public ModuleSymbol Symbol { get; }
    public string Name { get; set; } = "output";
    public IReadOnlyDictionary<GlobalSymbol, Global> Globals => this.globals;
    public Procedure GlobalInitializer { get; }
    IProcedure IAssembly.GlobalInitializer => this.GlobalInitializer;
    public IReadOnlyDictionary<FunctionSymbol, IProcedure> Procedures => this.procedures;

    private readonly Dictionary<GlobalSymbol, Global> globals = new();
    private readonly Dictionary<FunctionSymbol, IProcedure> procedures = new();

    public Assembly(ModuleSymbol symbol)
    {
        this.Symbol = symbol;
        this.GlobalInitializer = this.DefineProcedure(new SynthetizedFunctionSymbol(
            name: "<global initializer>",
            paramTypes: Enumerable.Empty<Type>(),
            returnType: IntrinsicTypes.Unit));
    }

    public Global DefineGlobal(GlobalSymbol globalSymbol)
    {
        if (!this.globals.TryGetValue(globalSymbol, out var result))
        {
            result = new Global(globalSymbol);
            this.globals.Add(globalSymbol, result);
        }
        return result;
    }

    public Procedure DefineProcedure(FunctionSymbol functionSymbol)
    {
        if (!this.procedures.TryGetValue(functionSymbol, out var result))
        {
            result = new Procedure(this, functionSymbol);
            this.procedures.Add(functionSymbol, result);
        }
        return (Procedure)result;
    }

    public override string ToString()
    {
        var result = new StringBuilder();
        result.AppendJoin(System.Environment.NewLine, this.globals.Values);
        if (this.globals.Count > 0 && this.procedures.Count > 1) result.Append(doubleNewline);
        result.AppendJoin(doubleNewline, this.procedures.Values);
        return result.ToString();
    }
}
