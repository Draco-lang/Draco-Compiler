using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Draco.Compiler.Api.Syntax;
using Draco.Compiler.Internal.OptimizingIr.Model;

namespace Draco.Compiler.Internal.OptimizingIr;

/// <summary>
/// Utilities for constructing instructions.
/// </summary>
internal static class InstructionFactory
{
    public static NopInstruction Nop() => new();
    public static StoreInstruction Store(IOperand target, IOperand source) => new(target, source);
    public static LoadInstruction Load(Register target, IOperand source) => new(target, source);
    public static RetInstruction Ret(IOperand value) => new(value);
    public static JumpInstruction Jump(BasicBlock target) => new(target);
    public static BranchInstruction Branch(IOperand condition, BasicBlock then, BasicBlock @else) =>
        new(condition, then, @else);
    public static CallInstruction Call(Register target, IOperand proc, IEnumerable<IOperand> args) =>
        new(target, proc, args);
    public static ArithmeticInstruction Arithmetic(Register target, ArithmeticOp op, IOperand left, IOperand right) =>
        new(target, op, left, right);
    public static ArithmeticInstruction Add(Register target, IOperand left, IOperand right) =>
        Arithmetic(target, ArithmeticOp.Add, left, right);
    public static ArithmeticInstruction Sub(Register target, IOperand left, IOperand right) =>
        Arithmetic(target, ArithmeticOp.Sub, left, right);
    public static ArithmeticInstruction Mul(Register target, IOperand left, IOperand right) =>
        Arithmetic(target, ArithmeticOp.Mul, left, right);
    public static ArithmeticInstruction Div(Register target, IOperand left, IOperand right) =>
        Arithmetic(target, ArithmeticOp.Div, left, right);
    public static ArithmeticInstruction Rem(Register target, IOperand left, IOperand right) =>
        Arithmetic(target, ArithmeticOp.Rem, left, right);
    public static ArithmeticInstruction Less(Register target, IOperand left, IOperand right) =>
        Arithmetic(target, ArithmeticOp.Less, left, right);
    public static ArithmeticInstruction Equal(Register target, IOperand left, IOperand right) =>
        Arithmetic(target, ArithmeticOp.Equal, left, right);
    public static SequencePoint SequencePoint(SyntaxNode? syntax) => new(syntax);
}
