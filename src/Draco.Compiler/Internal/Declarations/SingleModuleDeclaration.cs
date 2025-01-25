using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Draco.Compiler.Api.Syntax;
using Draco.Compiler.Internal.Utilities;

namespace Draco.Compiler.Internal.Declarations;

/// <summary>
/// Represents a portion of a module that was read up from a single file.
/// </summary>
internal sealed class SingleModuleDeclaration(string name, SplitPath path, ContainerSyntax syntax)
    : Declaration(name)
{
    /// <summary>
    /// The syntax node of this module portion.
    /// </summary>
    public ContainerSyntax Syntax { get; } = syntax;

    /// <summary>
    /// The path of this module, including the root module.
    /// </summary>
    public SplitPath Path { get; } = path;

    public override ImmutableArray<Declaration> Children =>
        InterlockedUtils.InitializeDefault(ref this.children, this.BuildChildren);
    private ImmutableArray<Declaration> children;

    public override IEnumerable<SyntaxNode> DeclaringSyntaxes => [this.Syntax];

    private ImmutableArray<Declaration> BuildChildren() =>
        this.Syntax.Declarations.Select(this.BuildChild).OfType<Declaration>().ToImmutableArray();

    private Declaration? BuildChild(SyntaxNode node) => node switch
    {
        // NOTE: We ignore import declarations in the declaration tree, unlike Roslyn
        // We handle import declarations during constructing the binders
        // Since we allow for imports in local scopes too, this is the most sensible choice
        ImportDeclarationSyntax => null,
        VariableDeclarationSyntax var => new GlobalDeclaration(var),
        FunctionDeclarationSyntax func => new FunctionDeclaration(func),
        ModuleDeclarationSyntax module => new SingleModuleDeclaration(module.Name.Text, this.Path.Append(module.Name.Text), module),
        UnexpectedDeclarationSyntax => null,
        _ => throw new ArgumentOutOfRangeException(nameof(node)),
    };
}
