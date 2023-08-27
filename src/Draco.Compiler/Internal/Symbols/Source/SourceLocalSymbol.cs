using Draco.Compiler.Api.Syntax;
using Draco.Compiler.Internal.Binding;
using Draco.Compiler.Internal.Documentation;
using Draco.Compiler.Internal.Documentation.Extractors;

namespace Draco.Compiler.Internal.Symbols.Source;

/// <summary>
/// An in-source local declaration.
/// </summary>
internal sealed class SourceLocalSymbol : LocalSymbol, ISourceSymbol
{
    public override TypeSymbol Type { get; }

    public override Symbol ContainingSymbol => this.untypedSymbol.ContainingSymbol;
    public override string Name => this.untypedSymbol.Name;

    public override SyntaxNode DeclaringSyntax => this.untypedSymbol.DeclaringSyntax;

    public override bool IsMutable => this.untypedSymbol.IsMutable;

    public override SymbolDocumentation Documentation => InterlockedUtils.InitializeNull(ref this.documentation, () => new MarkdownDocumentationExtractor(this.DeclaringSyntax.Documentation, this).Extract());
    private SymbolDocumentation? documentation;

    private readonly UntypedLocalSymbol untypedSymbol;

    public SourceLocalSymbol(UntypedLocalSymbol untypedSymbol, TypeSymbol type)
    {
        this.untypedSymbol = untypedSymbol;
        this.Type = type;
    }

    public void Bind(IBinderProvider binderProvider) { }
}
