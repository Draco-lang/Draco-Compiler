using System;
using System.Collections.Generic;
using System.Linq;
using Draco.Compiler.Api.Diagnostics;
using Draco.Compiler.Api.Syntax;

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
        var binder = this.compilation.GetBinder(subtree);
        // TODO: Lookup symbol
        throw new NotImplementedException();
    }

    /// <summary>
    /// Retrieves the <see cref="ISymbol"/> referenced by <paramref name="subtree"/>.
    /// </summary>
    /// <param name="subtree">The tree that is asked for the referenced <see cref="ISymbol"/>.</param>
    /// <returns>The referenced <see cref="ISymbol"/> by <paramref name="subtree"/>, or null
    /// if it does not reference any.</returns>
    public ISymbol? GetReferencedSymbol(SyntaxNode subtree)
    {
        var binder = this.compilation.GetBinder(subtree);
        // TODO: Lookup symbol
        throw new NotImplementedException();
    }
}
