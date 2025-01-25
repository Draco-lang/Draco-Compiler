using Draco.Compiler.Api.Syntax;
using Draco.Compiler.Internal.Binding;
using Draco.Compiler.Internal.Symbols.Source;
using Draco.Compiler.Internal.Symbols.Syntax;

namespace Draco.Compiler.Internal.Symbols.Script;

/// <summary>
/// A property defined inside a script.
///
/// Properties are special in script context, as global properties (global variables) can be inferred from other statements,
/// and their initialization intermixes with the rest of the script.
/// </summary>
internal sealed class ScriptAutoPropertySymbol(
    ScriptModuleSymbol containingSymbol,
    VariableDeclarationSyntax syntax) : SyntaxAutoPropertySymbol(containingSymbol, syntax), ISourceSymbol
{
    public override TypeSymbol Type => this.type ??= new TypeVariable(1);
    private TypeSymbol? type;

    public override Api.Semantics.Visibility Visibility => Api.Semantics.Visibility.Public;

    public override void Bind(IBinderProvider binderProvider) => containingSymbol.Bind(binderProvider);
}
