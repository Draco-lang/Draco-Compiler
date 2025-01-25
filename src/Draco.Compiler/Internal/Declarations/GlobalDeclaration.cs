using System.Collections.Generic;
using System.Collections.Immutable;
using Draco.Compiler.Api.Syntax;

namespace Draco.Compiler.Internal.Declarations;

/// <summary>
/// A global variable declaration.
/// </summary>
internal sealed class GlobalDeclaration(VariableDeclarationSyntax syntax)
    : Declaration(syntax.Name.Text)
{
    /// <summary>
    /// The syntax of the declaration.
    /// </summary>
    public VariableDeclarationSyntax Syntax { get; } = syntax;

    public override ImmutableArray<Declaration> Children => [];

    public override IEnumerable<SyntaxNode> DeclaringSyntaxes => [this.Syntax];
}
