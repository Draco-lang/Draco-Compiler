using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Draco.Compiler.Api.Syntax;
using Draco.Compiler.Internal.Query;
using Draco.Compiler.Internal.Utilities;

namespace Draco.Compiler.Internal.Semantics;

/// <summary>
/// A visitor that does type-inference on the given subtree.
/// </summary>
internal sealed class TypeInferenceVisitor : ParseTreeVisitorBase<Unit>
{
    /// <summary>
    /// Type variables are uninferred types that can be substituted to regular types or other variables.
    /// We define type-variables here, but make sure they never escape this type.
    /// </summary>
    private sealed record class TypeVar : Type
    {
        private Type? substitution;

        /// <summary>
        /// The substitution of this type variable.
        /// </summary>
        public Type Substitution
        {
            get
            {
                if (this.substitution is null) return this;
                // Pruning
                if (this.substitution is TypeVar var) this.substitution = var.Substitution;
                return this.substitution;
            }
            set
            {
                if (this.substitution is not null) throw new InvalidOperationException("tried to substitute type variable multiple times");
                this.substitution = value;
            }
        }
    }

    public ImmutableDictionary<Symbol, Type> Result => this.types
        .ToImmutableDictionary(kv => kv.Key, kv => this.RemoveSubstitutions(kv.Value));

    private readonly QueryDatabase db;
    private readonly Dictionary<Symbol, Type> types = new();

    public TypeInferenceVisitor(QueryDatabase db)
    {
        this.db = db;
    }

    /// <summary>
    /// Removes type variable substitutions.
    /// </summary>
    /// <param name="type">The <see cref="Type"/> to remove substitutions from.</param>
    /// <returns>The equivalent of <paramref name="type"/> without any variable substitutions.</returns>
    private Type RemoveSubstitutions(Type type) => type switch
    {
        Type.Builtin => type,
        _ => throw new ArgumentOutOfRangeException(nameof(type)),
    };

    public override Unit VisitVariableDecl(ParseTree.Decl.Variable node)
    {
        // The symbol we are inferring the type for
        var symbol = SymbolResolution.GetDefinedSymbolOrNull(this.db, node);
        Debug.Assert(symbol is not null);

        // The declared type after the ':' and the value type after the '='
        var declaredType = node.Type is not null
            ? TypeChecker.Evaluate(this.db, node.Type.Type)
            : null;
        var valueType = node.Initializer is not null
            ? TypeChecker.TypeOf(this.db, node.Initializer.Value)
            : null;

        // Infer the type from the two potential sources
        var inferredType = null as Type;
        if (declaredType is null && valueType is null)
        {
            // var x;
            // Just a new type variable, will need to infer from context
            inferredType = new TypeVar();
        }
        else if (declaredType is null || valueType is null)
        {
            // var x: T;
            // var x = v;
            // Whatever is non-null
            inferredType = declaredType ?? valueType;
        }
        else
        {
            // var x: T = v;
            // TODO: Need to put a constraint that valueType is subtype of declaredType
            inferredType = declaredType;
            throw new NotImplementedException();
        }

        // Store the inferred type
        Debug.Assert(inferredType is not null);
        this.types[symbol] = inferredType;

        // Inference in children
        return base.VisitVariableDecl(node);
    }
}
