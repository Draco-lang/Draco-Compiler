using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using Draco.Compiler.Api.Diagnostics;
using Draco.Compiler.Api.Syntax;
using Draco.Compiler.Internal.Binding;
using Draco.Compiler.Internal.BoundTree;
using Draco.Compiler.Internal.DracoIr;
using Draco.Compiler.Internal.Symbols;
using Draco.Compiler.Internal.Symbols.Source;
using Draco.Compiler.Internal.UntypedTree;

namespace Draco.Compiler.Api.Semantics;

/// <summary>
/// The semantic model of a subtree.
/// </summary>
public sealed class SemanticModel
{
    /// <summary>
    /// The the tree that the semantic model is for.
    /// </summary>
    public SyntaxTree Tree { get; }

    /// <summary>
    /// The semantic <see cref="Diagnostic"/>s in this model.
    /// </summary>
    public IEnumerable<Diagnostic> Diagnostics => this.GetAllDiagnostics();

    private readonly Compilation compilation;
    private readonly Dictionary<SyntaxNode, IList<BoundNode>> syntaxMap = new();

    internal SemanticModel(Compilation compilation, SyntaxTree tree)
    {
        this.Tree = tree;
        this.compilation = compilation;
    }

    /// <summary>
    /// Retrieves all semantic <see cref="Diagnostic"/>s.
    /// </summary>
    /// <returns>All <see cref="Diagnostic"/>s produced during semantic analysis.</returns>
    internal IEnumerable<Diagnostic> GetAllDiagnostics()
    {
        // TODO
        /*
        IEnumerable<Diagnostic> GetSymbolAndTypeErrors(SyntaxNode tree)
        {
            // Symbol
            foreach (var diag in SymbolResolution.GetDiagnostics(this.db, tree)) yield return diag.ToApiDiagnostic(tree);

            // Type
            foreach (var diag in TypeChecker.GetDiagnostics(this.db, tree)) yield return diag.ToApiDiagnostic(tree);

            // Children
            foreach (var diag in tree.Children.SelectMany(GetSymbolAndTypeErrors)) yield return diag;
        }

        var ast = SyntaxTreeToAst.ToAst(this.db, this.Tree.Root);

        IEnumerable<Diagnostic> GetAstErrors() => ast!.GetAllDiagnostics();
        // TODO: DataFlow
        //IEnumerable<Diagnostic> GetDataFlowErrors() => DataFlowPasses.Analyze(ast);

        return GetSymbolAndTypeErrors(this.Tree.Root)
            .Concat(GetAstErrors())
            //.Concat(GetDataFlowErrors())
            ;
        */
        return Enumerable.Empty<Diagnostic>();
    }

    // NOTE: These OrNull functions are not too pretty
    // For now public API is not that big of a concern, so they can stay
    // Instead we could just always return a nullable or an error symbol when appropriate

    /// <summary>
    /// Retrieves the <see cref="ISymbol"/> defined by <paramref name="subtree"/>.
    /// </summary>
    /// <param name="subtree">The tree that is asked for the defined <see cref="ISymbol"/>.</param>
    /// <returns>The defined <see cref="ISymbol"/> by <paramref name="subtree"/>, or null if it does not
    /// define any.</returns>
    public ISymbol? GetDefinedSymbol(SyntaxNode subtree)
    {
        // NOTE: We expect the parent to define the symbol, so we look up the parent node
        if (subtree.Parent is null) return null;
        var binder = this.compilation.GetBinder(subtree.Parent);
        var internalSymbol = (Symbol?)binder.Symbols
            .OfType<ISourceSymbol>()
            .FirstOrDefault(sym => subtree.Equals(sym.DeclarationSyntax));
        return internalSymbol?.ToApiSymbol();
    }

    /// <summary>
    /// Retrieves the <see cref="ISymbol"/> referenced by <paramref name="subtree"/>.
    /// </summary>
    /// <param name="subtree">The tree that is asked for the referenced <see cref="ISymbol"/>.</param>
    /// <returns>The referenced <see cref="ISymbol"/> by <paramref name="subtree"/>, or null
    /// if it does not reference any.</returns>
    public ISymbol? GetReferencedSymbol(SyntaxNode subtree)
    {
        if (!BinderFacts.ReferencesSymbol(subtree)) return null;
        var binder = this.compilation.GetBinder(subtree);
        if (binder.ContainingSymbol is SourceFunctionSymbol functionSymbol)
        {
            // TODO: We should somehow get the function to use the incremental binder in this context...
            // Maybe don't expose the body at all?
            // Or should the function symbol know about semantic context?
            // Or define an accessor for body that takes an optional semantic model?
            // var boundBody = functionSymbol.Body;

            if (!this.syntaxMap.ContainsKey(subtree))
            {
                var bodyBinder = this.GetBinder(functionSymbol);
                _ = bodyBinder.BindFunctionBody(functionSymbol.DeclarationSyntax.Body);
            }

            // Now the syntax node should be in the map
            var boundNodes = this.syntaxMap[subtree];
            // TODO: We need to deal with potential multiple returns here
            if (boundNodes.Count != 1) throw new NotImplementedException();
            return boundNodes[0] switch
            {
                BoundFunctionExpression f => f.Function.ToApiSymbol(),
                _ => throw new NotImplementedException(),
            };
        }
        else
        {
            // TODO
            throw new NotImplementedException();
        }
    }

    private Binder GetBinder(Symbol symbol)
    {
        var binder = this.compilation.GetBinder(symbol);
        return new IncrementalBinder(binder, this);
    }

    /// <summary>
    /// Wraps another binder, filling out information about bound constructs.
    /// </summary>
    private sealed class IncrementalBinder : Binder
    {
        // NOTE: We only use the underlying binder for the lookup logic
        // For actual binding logic, we rely on the base class implementation
        // Otherwise, we escape memo context
        /// <summary>
        /// The binder being wrapped by this one.
        /// </summary>
        public Binder UnderlyingBinder { get; }

        private readonly SemanticModel semanticModel;

        public IncrementalBinder(Binder underlyingBinder, SemanticModel semanticModel)
            : base(underlyingBinder.Compilation, underlyingBinder.Parent)
        {
            this.UnderlyingBinder = underlyingBinder;
            this.semanticModel = semanticModel;
        }

        protected override Binder GetBinder(SyntaxNode node)
        {
            var binder = base.GetBinder(node);
            return binder is IncrementalBinder
                ? binder
                : new IncrementalBinder(binder, this.semanticModel);
        }

        public override void LookupValueSymbol(LookupResult result, string name, SyntaxNode? reference) =>
            this.UnderlyingBinder.LookupValueSymbol(result, name, reference);

        public override void LookupTypeSymbol(LookupResult result, string name, SyntaxNode? reference) =>
            this.UnderlyingBinder.LookupTypeSymbol(result, name, reference);

        internal override BoundStatement TypeStatement(UntypedStatement statement, ConstraintBag constraints, DiagnosticBag diagnostics) =>
            this.TypeNode(statement, () => base.TypeStatement(statement, constraints, diagnostics));

        internal override BoundExpression TypeExpression(UntypedExpression expression, ConstraintBag constraints, DiagnosticBag diagnostics) =>
            this.TypeNode(expression, () => base.TypeExpression(expression, constraints, diagnostics));

        internal override BoundLvalue TypeLvalue(UntypedLvalue lvalue, ConstraintBag constraints, DiagnosticBag diagnostics) =>
            this.TypeNode(lvalue, () => base.TypeLvalue(lvalue, constraints, diagnostics));

        // TODO: There's nothing incremental in this,
        // but current usage doesn't require it either
        private TBoundNode TypeNode<TUntypedNode, TBoundNode>(TUntypedNode node, Func<TBoundNode> binder)
            where TUntypedNode : UntypedNode
            where TBoundNode : BoundNode
        {
            if (node.Syntax is null) return binder();
            if (!this.semanticModel.syntaxMap.TryGetValue(node.Syntax, out var nodeList))
            {
                nodeList = new List<BoundNode>();
                this.semanticModel.syntaxMap.Add(node.Syntax, nodeList);
            }
            var boundNode = binder();
            nodeList.Add(boundNode);
            return boundNode;
        }
    }
}
