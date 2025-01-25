using System;
using System.Collections.Generic;
using System.Threading;
using Draco.Compiler.Api.Semantics;
using Draco.Compiler.Api.Syntax;
using Draco.Compiler.Internal.Binding;
using Draco.Compiler.Internal.Documentation;
using Draco.Compiler.Internal.Documentation.Extractors;
using Draco.Compiler.Internal.Symbols.Source;
using Draco.Compiler.Internal.Symbols.Synthetized.AutoProperty;
using Draco.Compiler.Internal.Utilities;

namespace Draco.Compiler.Internal.Symbols.Syntax;

/// <summary>
/// The base for auto-properties defined based on some syntax.
/// </summary>
internal abstract class SyntaxAutoPropertySymbol : PropertySymbol, ISourceSymbol
{
    public override Symbol ContainingSymbol { get; }
    public override VariableDeclarationSyntax DeclaringSyntax { get; }

    public override string Name => this.DeclaringSyntax.Name.Text;
    public override bool IsIndexer => false;
    public override bool IsExplicitImplementation => false;
    // NOTE: In the future we probably want to check the global modifier if it's in a class
    public override bool IsStatic => this.ContainingSymbol is not TypeSymbol;

    public override Visibility Visibility =>
        GetVisibilityFromTokenKind(this.DeclaringSyntax.VisibilityModifier?.Kind);

    public override SymbolDocumentation Documentation => LazyInitializer.EnsureInitialized(ref this.documentation, this.BuildDocumentation);
    private SymbolDocumentation? documentation;

    internal override string RawDocumentation => this.DeclaringSyntax.Documentation;

    public override FunctionSymbol Getter => LazyInitializer.EnsureInitialized(ref this.getter, this.BuildGetter);
    private FunctionSymbol? getter;

    public override FunctionSymbol? Setter => this.DeclaringSyntax.Keyword.Kind == TokenKind.KeywordVal
        ? null
        : InterlockedUtils.InitializeMaybeNull(ref this.setter, this.BuildSetter);
    private FunctionSymbol? setter;

    /// <summary>
    /// The backing field of this auto-prop.
    /// </summary>
    public FieldSymbol BackingField => LazyInitializer.EnsureInitialized(ref this.backingField, this.BuildBackingField);
    private FieldSymbol? backingField;

    protected SyntaxAutoPropertySymbol(Symbol containingSymbol, VariableDeclarationSyntax syntax)
    {
        if (syntax.FieldModifier is not null) throw new ArgumentException("a property must not have the field modifier", nameof(syntax));

        this.ContainingSymbol = containingSymbol;
        this.DeclaringSyntax = syntax;
    }

    public abstract void Bind(IBinderProvider binderProvider);

    private SymbolDocumentation BuildDocumentation() =>
        MarkdownDocumentationExtractor.Extract(this);

    private FunctionSymbol BuildGetter() => new AutoPropertyGetterSymbol(this.ContainingSymbol, this);
    private FunctionSymbol? BuildSetter() => new AutoPropertySetterSymbol(this.ContainingSymbol, this);
    private FieldSymbol BuildBackingField() => new AutoPropertyBackingFieldSymbol(this.ContainingSymbol, this);

    protected internal override sealed IEnumerable<Symbol> GetAdditionalSymbols()
    {
        // For auto-properties we provide the backing field and the accessors in the same scope
        if (this.Getter is not null) yield return this.Getter;
        if (this.Setter is not null) yield return this.Setter;
        yield return this.BackingField;
    }
}
