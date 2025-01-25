using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using Draco.Compiler.Api.Diagnostics;
using Draco.Compiler.Api.Syntax;
using Draco.Compiler.Api.Syntax.Extensions;
using Draco.Compiler.Internal.Binding;
using Draco.Compiler.Internal.BoundTree;
using Draco.Compiler.Internal.Diagnostics;
using Draco.Compiler.Internal.Symbols;
using Draco.Compiler.Internal.Symbols.Source;
using Draco.Compiler.Internal.Symbols.Syntax;
using Draco.Compiler.Internal.Utilities;

namespace Draco.Compiler.Api.Semantics;

/// <summary>
/// The semantic model of a subtree.
/// </summary>
public sealed partial class SemanticModel : IBinderProvider
{
    /// <summary>
    /// The the tree that the semantic model is for.
    /// </summary>
    public SyntaxTree Tree { get; }

    /// <summary>
    /// All <see cref="Diagnostic"/>s in this model.
    /// </summary>
    public ImmutableArray<Diagnostic> Diagnostics =>
        InterlockedUtils.InitializeDefault(ref this.diagnostics, () => this.GetDiagnostics());
    private ImmutableArray<Diagnostic> diagnostics;

    internal DiagnosticBag DiagnosticBag { get; } = new ConcurrentDiagnosticBag();
    DiagnosticBag IBinderProvider.DiagnosticBag => this.DiagnosticBag;

    private readonly Compilation compilation;

    // Filled out by incremental binding
    private readonly ConcurrentDictionary<SyntaxFunctionSymbol, BoundStatement> boundFunctions = new();
    private readonly ConcurrentDictionary<SyntaxFieldSymbol, GlobalBinding> boundGlobals = new();
    private readonly ConcurrentDictionary<SyntaxNode, BoundNode> boundNodeMap = new();
    private readonly ConcurrentDictionary<SyntaxNode, Symbol> symbolMap = new();

    internal SemanticModel(Compilation compilation, SyntaxTree tree)
    {
        this.Tree = tree;
        this.compilation = compilation;
    }

    /// <summary>
    /// Retrieves all <see cref="Diagnostic"/>s on <see cref="Tree"/>.
    /// </summary>
    /// <param name="span">The span to retrieve the diagnostics in. If null, it retrieves all diagnostics
    /// regardless of the location.</param>
    /// <returns>All <see cref="Diagnostic"/>s for <see cref="Tree"/>.</returns>
    private ImmutableArray<Diagnostic> GetDiagnostics(SourceSpan? span = null)
    {
        var syntaxNodes = span is null
            ? this.Tree.Root.PreOrderTraverse()
            : this.Tree.Root.TraverseIntersectingSpan(span.Value);

        var addedImportBinders = new HashSet<ImportBinder>();

        foreach (var syntaxNode in syntaxNodes)
        {
            // Add syntax diagnostics
            var syntaxDiagnostics = this.Tree.SyntaxDiagnosticTable.Get(syntaxNode);
            this.DiagnosticBag.AddRange(syntaxDiagnostics);

            // Get the symbol this embodies
            var binder = this.GetBinder(syntaxNode);
            var containingSymbol = binder.ContainingSymbol as ISourceSymbol;

            // We want exact syntax matches to avoid duplication
            // This is a cheap way to not to attempt finding a function symbol from its body
            // and from its signature
            switch (syntaxNode)
            {
            case CompilationUnitSyntax:
            case FunctionDeclarationSyntax:
            case ScriptEntrySyntax:
            {
                containingSymbol?.Bind(this);
                break;
            }
            case VariableDeclarationSyntax varDecl when varDecl.FieldModifier is null:
            {
                // We need to search for this global
                var globalSymbol = binder.ContainingSymbol?.AllMembers
                    .OfType<SyntaxAutoPropertySymbol>()
                    .FirstOrDefault(s => s.Name == varDecl.Name.Text);
                globalSymbol?.Bind(this);
                break;
            }
            case VariableDeclarationSyntax varDecl when varDecl.FieldModifier is not null:
            {
                // We need to search for this global
                var globalSymbol = binder.ContainingSymbol?.AllMembers
                    .OfType<SyntaxFieldSymbol>()
                    .FirstOrDefault(s => s.Name == varDecl.Name.Text);
                globalSymbol?.Bind(this);
                break;
            }
            case ImportDeclarationSyntax:
            {
                // We get the binder, and if this binder wasn't added yet, we add its import errors
                var importBinder = this.GetImportBinder(syntaxNode);
                if (addedImportBinders.Add(importBinder))
                {
                    // New binder, add errors
                    // First, enforce binding
                    _ = importBinder.ImportItems;
                    this.DiagnosticBag.AddRange(importBinder.ImportDiagnostics);
                }
                break;
            }
            }
        }

        return [.. this.DiagnosticBag];
    }

    /// <summary>
    /// Retrieves all <see cref="ISymbol"/>s accesible from given <paramref name="node"/>.
    /// </summary>
    /// <param name="node">The <see cref="SyntaxNode"/> from which to start looking for declared symbols.</param>
    /// <returns>All the <see cref="ISymbol"/>s accesible from the <paramref name="node"/>.</returns>
    public ImmutableArray<ISymbol> GetAllAccessibleSymbols(SyntaxNode? node)
    {
        var startBinder = this.compilation.GetBinder(node ?? this.Tree.Root);
        var result = new SymbolCollectionBuilder() { VisibleFrom = startBinder.ContainingSymbol };
        foreach (var binder in startBinder.AncestorChain)
        {
            result.AddRange(binder.DeclaredSymbols);
        }
        return result
            .EnumerateResult()
            .Select(s => s.ToApiSymbol())
            .ToImmutableArray();
    }

    /// <summary>
    /// Retrieves the <see cref="ISymbol"/> declared by <paramref name="syntax"/>.
    /// </summary>
    /// <param name="syntax">The tree that is asked for the defined <see cref="ISymbol"/>.</param>
    /// <returns>The defined <see cref="ISymbol"/> by <paramref name="syntax"/>, or null if it does not
    /// declared any.</returns>
    public ISymbol? GetDeclaredSymbol(SyntaxNode syntax) => this
        .GetDeclaredSymbolInternal(syntax)
        ?.ToApiSymbol();

    internal Symbol? GetDeclaredSymbolInternal(SyntaxNode syntax)
    {
        if (this.symbolMap.TryGetValue(syntax, out var existing)) return existing;

        // Get enclosing context
        var binder = this.GetBinder(syntax);
        var containingSymbol = binder.ContainingSymbol;

        switch (containingSymbol)
        {
        case SyntaxFunctionSymbol func:
        {
            // This is just the function itself
            if (func.DeclaringSyntax == syntax) return containingSymbol;

            // Could be a generic parameter
            if (syntax is GenericParameterSyntax genericParam)
            {
                var paramSymbol = containingSymbol.GenericParameters
                    .FirstOrDefault(p => p.DeclaringSyntax == syntax);
                return paramSymbol;
            }

            // Bind the function contents
            func.Bind(this);

            // Look up inside the binder
            var symbol = binder.DeclaredSymbols
                .SingleOrDefault(sym => sym.DeclaringSyntax == syntax);
            return symbol;
        }
        case SourceModuleSymbol module:
        {
            // The module itself
            if (module.DeclaringSyntaxes.Contains(syntax)) return containingSymbol;

            // Just search for the corresponding syntax
            var symbol = module.AllMembers
                .SingleOrDefault(sym => sym.DeclaringSyntax == syntax);
            return symbol;
        }
        default:
            return null;
        }
    }

    /// <summary>
    /// Retrieves the <see cref="ISymbol"/> referenced by <paramref name="syntax"/>.
    /// </summary>
    /// <param name="syntax">The tree that is asked for the referenced <see cref="ISymbol"/>.</param>
    /// <returns>The referenced <see cref="ISymbol"/> by <paramref name="syntax"/>, or null
    /// if it does not reference any.</returns>
    public ISymbol? GetReferencedSymbol(SyntaxNode syntax) => this
        .GetReferencedSymbolInternal(syntax)
        ?.ToApiSymbol();

    internal Symbol? GetReferencedSymbolInternal(SyntaxNode syntax)
    {
        // If it's a token, we assume it's wrapped in a more sensible syntax node
        if (syntax is SyntaxToken token && token.Parent is not null)
        {
            return this.GetReferencedSymbolInternal(token.Parent);
        }

        if (syntax is ImportPathSyntax)
        {
            // Imports are special, we need to search in the binder
            var importBinder = this.GetImportBinder(syntax);
            var importItems = importBinder.ImportItems;
            var importSyntax = GetImportSyntax(syntax);
            // Search for the import item
            var importItem = importItems.SingleOrDefault(i => i.Syntax == importSyntax);
            // Not found in item
            if (importItem is null) return null;
            // Search for path element
            // NOTE: Yes, this could be simplified to a SingleOrDefault to a predicate,
            // but the default for a KeyValuePair is not nullable, so a null-warn would be swallowed
            // Decided to write it this way, in case the code gets shuffled around later
            var pathSymbol = importItem.Path
                .Where(i => i.Key == syntax)
                .Select(i => i.Value)
                .SingleOrDefault();
            // Not found in path
            if (pathSymbol is null) return null;
            return pathSymbol;
        }

        if (this.symbolMap.TryGetValue(syntax, out var existing)) return existing;

        // Get enclosing context
        var binder = this.GetBinder(syntax);
        var containingSymbol = binder.ContainingSymbol;

        // Source containers need to get bound
        if (containingSymbol is ISourceSymbol sourceSymbol)
        {
            sourceSymbol.Bind(this);
            foreach (var nested in containingSymbol.AllMembers.OfType<ISourceSymbol>()) nested.Bind(this);
        }

        // Attempt to retrieve
        this.symbolMap.TryGetValue(syntax, out var symbol);

        if (symbol is null)
        {
            // Apply some fallback strategies

            // Resolve calls to a function
            if (syntax is NameExpressionSyntax name)
            {
                // If it's a method of a call, we want the method referenced by the call instead
                var called = syntax;

                if (name.Parent is GenericExpressionSyntax generic && generic.Instantiated.Equals(called))
                {
                    called = generic;
                }

                if (called.Parent is CallExpressionSyntax call && call.Function.Equals(called))
                {
                    // This is a call, we want the function
                    return this.GetReferencedSymbolInternal(call);
                }
            }
        }

        return symbol;
    }

    private ImportBinder GetImportBinder(SyntaxNode syntax) => this.compilation
        .GetBinder(syntax)
        .AncestorChain
        .OfType<ImportBinder>()
        .First();

    /// <summary>
    /// Retrieves the type of the expression represented by <paramref name="syntax"/>.
    /// </summary>
    /// <param name="syntax">The expression that the type will be checked of.</param>
    /// <returns>The <see cref="ITypeSymbol"/> that <paramref name="syntax"/> will evaluate to,
    /// or null if it does not evaluate to a value with type.</returns>
    public ITypeSymbol? TypeOf(ExpressionSyntax syntax) => this.TypeOfInternal(syntax)?.ToApiSymbol();

    /// <summary>
    /// Retrieves the type of the expression represented by <paramref name="syntax"/>.
    /// </summary>
    /// <param name="syntax">The expression that the type will be checked of.</param>
    /// <returns>The <see cref="TypeSymbol"/> that <paramref name="syntax"/> will evaluate to,
    /// or null if it does not evaluate to a value with type.</returns>
    internal Internal.Symbols.TypeSymbol? TypeOfInternal(ExpressionSyntax syntax)
    {
        if (this.TryGetBoundNode(syntax, out var existing))
        {
            return (existing as BoundExpression)?.Type;
        }

        // NOTE: Very similar logic to GetReferencedSymbol, maybe factor out?
        // Get enclosing context
        var binder = this.GetBinder(syntax);
        var containingSymbol = binder.ContainingSymbol;

        // Source containers need to get bound
        if (containingSymbol is ISourceSymbol sourceSymbol) sourceSymbol.Bind(this);

        // Attempt to retrieve
        this.TryGetBoundNode(syntax, out var node);
        return (node as BoundExpression)?.Type;
    }

    private bool TryGetBoundNode(SyntaxNode syntax, [MaybeNullWhen(false)] out BoundNode node) =>
        this.boundNodeMap.TryGetValue(syntax, out node);

    /// <summary>
    /// Retrieves the function overloads referenced by <paramref name="syntax"/>.
    /// </summary>
    /// <param name="syntax">The tree that is asked for the referenced overloads.</param>
    /// <returns>The referenced overloads by <paramref name="syntax"/>, or empty array
    /// if it does not reference any.</returns>
    public ImmutableArray<IFunctionSymbol> GetReferencedOverloads(ExpressionSyntax syntax) => this
        .GetReferencedOverloadsInternal(syntax)
        .Select(s => s.ToApiSymbol())
        .ToImmutableArray();

    /// <summary>
    /// Retrieves the function overloads referenced by <paramref name="syntax"/>.
    /// </summary>
    /// <param name="syntax">The tree that is asked for the referenced overloads.</param>
    /// <returns>The referenced overloads by <paramref name="syntax"/>, or empty array
    /// if it does not reference any.</returns>
    internal ImmutableArray<Internal.Symbols.FunctionSymbol> GetReferencedOverloadsInternal(ExpressionSyntax syntax)
    {
        var startBinder = this.compilation.GetBinder(syntax);
        var result = new SymbolCollectionBuilder() { VisibleFrom = startBinder.ContainingSymbol };

        // NOTE: Duplication with MemberCompletionProvider
        if (syntax is MemberExpressionSyntax member)
        {
            var receiverSymbol = this.GetReferencedSymbolInternal(member.Accessed);
            // NOTE: This is how we check for static access
            if (receiverSymbol?.IsTypeOnCilLevel == true)
            {
                result.AddRange(receiverSymbol.StaticMembers.Where(m => m.Name == member.Member.Text));
            }
            else
            {
                // Assume instance access
                var receiverType = this.TypeOfInternal(member.Accessed);
                if (receiverType is not null)
                {
                    result.AddRange(receiverType.InstanceMembers.Where(m => m.Name == member.Member.Text));
                }
            }
        }
        else
        {
            // We look up syntax based on the symbol in context
            foreach (var binder in startBinder.AncestorChain)
            {
                var symbols = binder.DeclaredSymbols
                    .Where(x => x is Internal.Symbols.FunctionSymbol
                             && x.Name == syntax.ToString());
                result.AddRange(symbols);
            }
        }

        if (result.Count == 0) return [];
        if (result.Count == 1)
        {
            // Just a single method added
            var function = result
                .EnumerateResult()
                .Cast<Internal.Symbols.FunctionSymbol>()
                .Single();
            return [function];
        }
        else
        {
            // It's a set of overloads grouped under a single name
            var group = result
                .EnumerateResult()
                .Cast<Internal.Symbols.Synthetized.FunctionGroupSymbol>()
                .Single();
            return group.Functions;
        }
    }

    /// <summary>
    /// Retrieves the symbol that <paramref name="syntax"/> is bound inside of.
    /// </summary>
    /// <param name="syntax">The syntax node to get the binding symbol of.</param>
    /// <returns>The symbol that <paramref name="syntax"/> is bound inside of.</returns>
    internal Symbol? GetBindingSymbol(SyntaxNode syntax) => this.GetBinder(syntax).ContainingSymbol;

    private IncrementalBinder GetBinder(SyntaxNode syntax)
    {
        var binder = this.compilation.GetBinder(syntax);
        return new(binder, this);
    }

    private IncrementalBinder GetBinder(Symbol symbol)
    {
        var binder = this.compilation.GetBinder(symbol);
        return new(binder, this);
    }

    Binder IBinderProvider.GetBinder(SyntaxNode syntax) => this.GetBinder(syntax);
    Binder IBinderProvider.GetBinder(Symbol symbol) => this.GetBinder(symbol);

    private static ImportDeclarationSyntax GetImportSyntax(SyntaxNode syntax)
    {
        while (true)
        {
            if (syntax is ImportDeclarationSyntax decl) return decl;
            syntax = syntax.Parent!;
        }
    }
}
