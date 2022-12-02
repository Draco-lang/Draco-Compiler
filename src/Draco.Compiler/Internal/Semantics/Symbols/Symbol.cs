using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;
using Draco.Compiler.Api.Syntax;
using Draco.Compiler.Internal.Diagnostics;
using Draco.Compiler.Internal.Query;
using Draco.Compiler.Internal.Semantics.Symbols;
using Draco.Compiler.Internal.Semantics.Types;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using ApiSymbol = Draco.Compiler.Api.Semantics.Symbol;
using Type = Draco.Compiler.Internal.Semantics.Types.Type;

namespace Draco.Compiler.Internal.Semantics.Symbols;

/// <summary>
/// The base of all symbols.
/// </summary>
internal abstract partial class Symbol
{
    /// <summary>
    /// The name of this symbol that it can be referenced by.
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// True, if this symbol represents an error.
    /// </summary>
    public virtual bool IsError => false;

    /// <summary>
    /// The diagnostics related to this symbol.
    /// </summary>
    public virtual ImmutableArray<Diagnostic> Diagnostics => ImmutableArray<Diagnostic>.Empty;

    /// <summary>
    /// The enclosing <see cref="Scope"/> of this symbol.
    /// </summary>
    public abstract Scope? EnclosingScope { get; }

    /// <summary>
    /// The syntax node that defined this symbol.
    /// </summary>
    public abstract ParseTree? Definition { get; }

    // NOTE: Might not be the best definition of global.
    /// <summary>
    /// True, if this is a global symbol.
    /// </summary>
    public bool IsGlobal => (this.EnclosingScope?.Kind ?? ScopeKind.Global) == ScopeKind.Global;

    protected Symbol(string name)
    {
        this.Name = name;
    }

    /// <summary>
    /// Converts this <see cref="Symbol"/> to an <see cref="ApiSymbol"/>.
    /// </summary>
    /// <returns>The equivalent <see cref="ApiSymbol"/>.</returns>
    public ApiSymbol ToApiSymbol() => new(this);
}

internal abstract partial class Symbol
{
    /// <summary>
    /// Any variable symbol.
    /// </summary>
    public interface IVariable
    {
        /// <summary>
        /// True, if this is a mutable variable.
        /// </summary>
        public bool IsMutable { get; }

        /// <summary>
        /// The type of the variable.
        /// </summary>
        public Type Type { get; }
    }

    /// <summary>
    /// Any label symbol.
    /// </summary>
    public interface ILabel
    {
    }

    /// <summary>
    /// Any operator symbol.
    /// </summary>
    public interface IOperator
    {
        /// <summary>
        /// The return type of this operator.
        /// </summary>
        public Type ReturnType { get; }
    }
}

internal abstract partial class Symbol
{
    // Base class for "real" symbols found in the tree
    public abstract class InTreeBase : Symbol
    {
        public override ParseTree Definition { get; }
        public override Scope? EnclosingScope =>
            SymbolResolution.GetContainingScopeOrNull(this.db, this.Definition);

        protected readonly QueryDatabase db;

        protected InTreeBase(QueryDatabase db, string name, ParseTree definition)
            : base(name)
        {
            this.db = db;
            this.Definition = definition;
        }
    }

    // Base class for synthetized symbols
    public abstract class SynthetizedBase : Symbol
    {
        private static int varCounter = -1;

        public override Scope? EnclosingScope => null;
        public override ParseTree? Definition => null;

        public SynthetizedBase(string name)
            : base($"{name}<{Interlocked.Increment(ref varCounter)}>")
        {
        }
    }
}

internal abstract partial class Symbol
{
    /// <summary>
    /// A symbol for a reference error.
    /// </summary>
    public sealed class Error : Symbol
    {
        public override Scope? EnclosingScope => null;
        public override bool IsError => true;
        public override ParseTree? Definition => null;
        public override ImmutableArray<Diagnostic> Diagnostics { get; }

        public Error(string name, ImmutableArray<Diagnostic> diagnostics)
            : base(name)
        {
            this.Diagnostics = diagnostics;
        }
    }
}

internal abstract partial class Symbol
{
    /// <summary>
    /// A symbol for a label.
    /// </summary>
    public sealed class Label : InTreeBase, ILabel
    {
        public Label(QueryDatabase db, string name, ParseTree definition)
            : base(db, name, definition)
        {
        }
    }
}

internal abstract partial class Symbol
{
    /// <summary>
    /// A symbol for a synthetized label.
    /// </summary>
    public sealed class SynthetizedLabel : SynthetizedBase, ILabel
    {
        public SynthetizedLabel()
            : base("label")
        {
        }
    }
}

internal abstract partial class Symbol
{
    /// <summary>
    /// A symbol for a function declaration.
    /// </summary>
    public sealed class Function : InTreeBase
    {
        public ImmutableArray<Parameter> Params
        {
            get
            {
                var builder = ImmutableArray.CreateBuilder<Parameter>();
                var tree = (ParseTree.Decl.Func)this.Definition;
                foreach (var param in tree.Params.Value.Elements)
                {
                    var symbol = (Parameter?)SymbolResolution.GetDefinedSymbolOrNull(this.db, param.Value) ?? throw new InvalidOperationException();
                    builder.Add(symbol);
                }
                return builder.ToImmutable();
            }
        }

        public Type ReturnType
        {
            get
            {
                var tree = (ParseTree.Decl.Func)this.Definition;
                return tree.ReturnType is null
                    ? Type.Unit
                    : TypeChecker.Evaluate(this.db, tree.ReturnType.Type);
            }
        }

        public Type Type => new Type.Function(
            this.Params.Select(p => p.Type).ToImmutableArray(),
            this.ReturnType);

        public Function(QueryDatabase db, string name, ParseTree definition)
            : base(db, name, definition)
        {
        }
    }
}

internal abstract partial class Symbol
{
    /// <summary>
    /// A symbol for a parameter declaration.
    /// </summary>
    public sealed class Parameter : InTreeBase, IVariable
    {
        public bool IsMutable => false;
        public Type Type => TypeChecker.TypeOf(this.db, this);

        public Parameter(QueryDatabase db, string name, ParseTree definition)
            : base(db, name, definition)
        {
        }
    }
}

internal abstract partial class Symbol
{
    /// <summary>
    /// A symbol for a variable declaration.
    /// </summary>
    public sealed class Variable : InTreeBase, IVariable
    {
        public bool IsMutable { get; }

        public Type Type => TypeChecker.TypeOf(this.db, this);

        public Variable(QueryDatabase db, string name, ParseTree definition, bool isMutable)
            : base(db, name, definition)
        {
            this.IsMutable = isMutable;
        }
    }
}

internal abstract partial class Symbol
{
    /// <summary>
    /// A symbol for a synthetized variable declarations.
    /// </summary>
    public sealed class SynthetizedVariable : SynthetizedBase, IVariable
    {
        public bool IsMutable { get; }

        public Type Type { get; }

        public SynthetizedVariable(bool mutable, Type type)
            : base("var")
        {
            this.IsMutable = mutable;
            this.Type = type;
        }
    }
}

internal abstract partial class Symbol
{
    /// <summary>
    /// A symbol for types.
    /// </summary>
    public sealed class TypeAlias : Symbol
    {
        public Type Type { get; }
        // TODO
        public override Scope? EnclosingScope => throw new NotImplementedException();
        public override ParseTree? Definition => throw new NotImplementedException();

        public TypeAlias(string name, Type type)
            : base(name)
        {
            this.Type = type;
        }
    }
}

// NOTE: Temporary
internal abstract partial class Symbol
{
    /// <summary>
    /// A symbol for intrinsics.
    /// </summary>
    public sealed class Intrinsic : Symbol
    {
        public override Scope? EnclosingScope => null;
        public override ParseTree? Definition => null;

        public Type Type { get; }

        public Intrinsic(string name, Type type)
            : base(name)
        {
            this.Type = type;
        }
    }
}
// NOTE: Temporary
internal abstract partial class Symbol
{
    /// <summary>
    /// A symbol for primitive operation.
    /// </summary>
    public sealed class IntrinsicOperator : Symbol, IOperator
    {
        public static readonly IntrinsicOperator Not_Bool = MakeUnary(TokenType.KeywordNot, Type.Bool, Type.Bool);

        public static readonly IntrinsicOperator Add_Int32 = MakeBinary(TokenType.Plus, Type.Int32, Type.Int32, Type.Int32);
        public static readonly IntrinsicOperator Sub_Int32 = MakeBinary(TokenType.Minus, Type.Int32, Type.Int32, Type.Int32);

        public static readonly IntrinsicOperator Less_Int32 = MakeRelational(TokenType.LessThan, Type.Int32, Type.Int32);

        private static IntrinsicOperator MakeUnary(TokenType op, Type operand, Type result)
        {
            var name = SymbolResolution.GetUnaryOperatorName(op);
            if (name is null) throw new ArgumentException("the token type is not an unary operator", nameof(op));
            return new(name, new Type.Function(ImmutableArray.Create(operand), result), op);
        }

        private static IntrinsicOperator MakeBinary(TokenType op, Type left, Type right, Type result)
        {
            var name = SymbolResolution.GetBinaryOperatorName(op);
            if (name is null) throw new ArgumentException("the token type is not a binary operator", nameof(op));
            return new(name, new Type.Function(ImmutableArray.Create(left, right), result), op);
        }

        private static IntrinsicOperator MakeRelational(TokenType op, Type left, Type right)
        {
            var name = SymbolResolution.GetRelationalOperatorName(op);
            if (name is null) throw new ArgumentException("the token type is not a relational operator", nameof(op));
            return new(name, new Type.Function(ImmutableArray.Create(left, right), Type.Bool), op);
        }

        public override Scope? EnclosingScope => null;
        public override ParseTree? Definition => null;

        public Type Type { get; }
        // TODO: We should have a separate enum instead for the intrinsic operator types
        // We definitely shouldn't let syntax elements leak in
        public TokenType Operator { get; }

        public Type ReturnType => ((Type.Function)this.Type).Return;

        public IntrinsicOperator(string name, Type type, TokenType op)
            : base(name)
        {
            this.Type = type;
            this.Operator = op;
        }
    }
}
