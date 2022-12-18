using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Draco.Compiler.Internal.DracoIr;

/// <summary>
/// A unit of compilation resulting in a single binary.
/// </summary>
/// <param name="Procs">The procedures in this <see cref="Assembly"/>.</param>
internal sealed record class Assembly(
    ImmutableDictionary<string, Proc> Procs);

/// <summary>
/// A single procedure defined.
/// </summary>
/// <param name="Name">The name of the procedure.</param>
/// <param name="Params">The procedure parameters.</param>
/// <param name="ReturnType">The return type of the procedure.</param>
/// <param name="BasicBlocks">The basic blocks in this procedure.</param>
internal sealed record class Proc(
    string Name,
    ImmutableArray<Value.Param> Params,
    Type ReturnType,
    ImmutableArray<BasicBlock> BasicBlocks);

/// <summary>
/// A single continuous block of instructions that can only be jumped into at the very beginning and only has
/// branching at the very end.
/// </summary>
/// <param name="Instructions">The instructions within this block.</param>
internal sealed record class BasicBlock(
    ImmutableArray<Instr> Instructions);

/// <summary>
/// The base class for all instructions.
/// </summary>
internal abstract partial record class Instr
{
    /// <summary>
    /// True, if this is some kind of jump instruction.
    /// </summary>
    public virtual bool IsJump => false;
}

internal abstract partial record class Instr
{
    /// <summary>
    /// Adds together the two integer operands.
    /// </summary>
    /// <param name="Target">The target register to store in.</param>
    /// <param name="Left">The left operand.</param>
    /// <param name="Right">The right operand.</param>
    public sealed record class AddInt(Value.Reg Target, Value Left, Value Right) : Instr;
}

/// <summary>
/// The base of all IR types.
/// </summary>
internal abstract record class Type
{
    public static readonly Builtin Void = new(typeof(void));
    public static readonly Builtin Int32 = new(typeof(int));
    public static readonly Builtin Bool = new(typeof(bool));

    /// <summary>
    /// A builtin type.
    /// </summary>
    /// <param name="Type">The built-in <see cref="System.Type"/>.</param>
    public sealed record class Builtin(System.Type Type) : Type;
}

/// <summary>
/// The base of all IR values that can be used as operands.
/// </summary>
internal abstract record class Value
{
    /// <summary>
    /// Represents a procedure parameter.
    /// </summary>
    /// <param name="Type">The type of the parameter.</param>
    public sealed record class Param(Type Type) : Value;

    /// <summary>
    /// A single virtual register that can be only assigned once.
    /// </summary>
    /// <param name="Type">The type of the value the register can store.</param>
    public sealed record class Reg(Type Type) : Value;
}
