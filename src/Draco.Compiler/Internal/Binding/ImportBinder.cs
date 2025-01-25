using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Linq;
using Draco.Compiler.Api.Diagnostics;
using Draco.Compiler.Api.Syntax;
using Draco.Compiler.Internal.Diagnostics;
using Draco.Compiler.Internal.Symbols;
using Draco.Compiler.Internal.Symbols.Error;
using Draco.Compiler.Internal.Utilities;

namespace Draco.Compiler.Internal.Binding;

/// <summary>
/// Binds imported symbols in a scope.
/// </summary>
internal sealed class ImportBinder(Binder parent, SyntaxNode declaringSyntax) : Binder(parent)
{
    /// <summary>
    /// The diagnostics produced during import resolution.
    /// </summary>
    public DiagnosticBag ImportDiagnostics { get; } = new ConcurrentDiagnosticBag();

    /// <summary>
    /// The import items this binder brings in.
    /// </summary>
    public ImmutableArray<ImportItem> ImportItems =>
        InterlockedUtils.InitializeDefault(ref this.importItems, () => this.BindImportItems(this.ImportDiagnostics));
    private ImmutableArray<ImportItem> importItems;

    public override IEnumerable<Symbol> DeclaredSymbols => this.ImportItems.SelectMany(i => i.ImportedSymbols);

    public override SyntaxNode DeclaringSyntax { get; } = declaringSyntax;

    internal override void LookupLocal(LookupResult result, string name, ref LookupFlags flags, Predicate<Symbol> allowSymbol, SyntaxNode? currentReference)
    {
        foreach (var symbol in this.DeclaredSymbols)
        {
            if (symbol.Visibility == Api.Semantics.Visibility.Private) continue;
            if (symbol.Name != name) continue;
            if (!allowSymbol(symbol)) continue;
            result.Add(symbol);
        }
    }

    private ImmutableArray<ImportItem> BindImportItems(DiagnosticBag diagnostics)
    {
        var importSyntaxes = this.CollectImportDeclarations(diagnostics);
        var importItems = ImmutableArray.CreateBuilder<ImportItem>();
        foreach (var syntax in importSyntaxes)
        {
            var importItem = this.BindImport(syntax, diagnostics);
            importItems.Add(importItem);
        }
        return importItems.ToImmutable();
    }

    private ImportItem BindImport(ImportDeclarationSyntax syntax, DiagnosticBag diagnostics)
    {
        var parts = ImmutableArray.CreateBuilder<KeyValuePair<ImportPathSyntax, Symbol>>();

        // Fill out paths
        var importedSymbol = BindImportPath(syntax.Path);

        // From the last member find out what we import
        if (importedSymbol.IsError)
        {
            // No-op, don't cascade
            return new(syntax, parts.ToImmutable(), []);
        }
        else if (importedSymbol is not ModuleSymbol)
        {
            // Error
            diagnostics.Add(Diagnostic.Create(
                template: SymbolResolutionErrors.IllegalImport,
                location: syntax.Path.Location,
                formatArgs: syntax.Path));
            return new(syntax, parts.ToImmutable(), []);
        }
        else
        {
            return new(syntax, parts.ToImmutable(), importedSymbol.AllMembers);
        }

        Symbol BindImportPath(ImportPathSyntax syntax)
        {
            switch (syntax)
            {
            case RootImportPathSyntax root:
            {
                // This must be the first resolved element
                Debug.Assert(parts!.Count == 0);
                // Simple lookup from parent
                // NOTE: We will ask the parent to look up import paths, because the current binder is under construction
                // If we called the binding of import paths on this, we'd hit infinite recursion
                var symbol = this.Parent!.LookupValueSymbol(root.Name.Text, syntax, diagnostics);
                parts.Add(new(root, symbol));
                return symbol;
            }
            case MemberImportPathSyntax mem:
            {
                var parent = BindImportPath(mem.Accessed);
                // Don't cascade errors
                if (parent.IsError)
                {
                    parts!.Add(new(mem, ErrorMemberSymbol.Instance));
                    return parent;
                }
                // Look up in parent
                var membersWithName = parent.AllMembers
                    .Where(m => m.Name == mem.Member.Text)
                    .OfType<ModuleSymbol>()
                    .ToList();
                if (membersWithName.Count == 1)
                {
                    // It's simply this element
                    var symbol = membersWithName[0];
                    parts!.Add(new(mem, symbol));
                    return symbol;
                }
                else if (membersWithName.Count == 0)
                {
                    // Not found
                    diagnostics.Add(Diagnostic.Create(
                        template: SymbolResolutionErrors.MemberNotFound,
                        location: mem.Member.Location,
                        formatArgs: [mem.Member.Text, parent.Name]));
                    parts!.Add(new(mem, ErrorMemberSymbol.Instance));
                    return ErrorMemberSymbol.Instance;
                }
                else
                {
                    // Multiple
                    diagnostics.Add(Diagnostic.Create(
                        template: SymbolResolutionErrors.IllegalImport,
                        location: mem.Location,
                        formatArgs: mem.Member.Text));
                    // NOTE: For now this result is fine
                    parts!.Add(new(mem, ErrorMemberSymbol.Instance));
                    return ErrorMemberSymbol.Instance;
                }
            }
            default:
                throw new ArgumentOutOfRangeException(nameof(syntax));
            }
        }
    }

    /// <summary>
    /// Collects import declarations from the <see cref="DeclaringSyntax"/> and reports diagnostics
    /// for illegally placed imports.
    /// </summary>
    /// <param name="diagnostics">The bad to report diagnostics to.</param>
    /// <returns>The collected declarations.</returns>
    private ImmutableArray<ImportDeclarationSyntax> CollectImportDeclarations(DiagnosticBag diagnostics)
    {
        var importSyntaxes = ImmutableArray.CreateBuilder<ImportDeclarationSyntax>();
        var hasNonImport = false;
        foreach (var syntax in BinderFacts.EnumerateNodesInSameScope(this.DeclaringSyntax))
        {
            if (syntax is DeclarationStatementSyntax) continue;

            if (syntax is ImportDeclarationSyntax importSyntax)
            {
                importSyntaxes.Add(importSyntax);
                if (hasNonImport)
                {
                    diagnostics.Add(Diagnostic.Create(
                        template: SymbolResolutionErrors.ImportNotAtTop,
                        location: syntax.Location));
                }
            }
            else
            {
                hasNonImport = hasNonImport || syntax
                    is DeclarationSyntax
                    or StatementSyntax
                    or ExpressionSyntax;
            }
        }
        return importSyntaxes.ToImmutable();
    }
}
