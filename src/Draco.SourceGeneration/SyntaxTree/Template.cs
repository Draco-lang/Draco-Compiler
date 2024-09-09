using static Draco.SourceGeneration.TemplateUtils;

namespace Draco.SourceGeneration.SyntaxTree;

internal static class Template
{
    public static string GenerateGreenTree(Tree tree) => FormatCSharp($$"""
using System.Collections.Generic;

namespace Draco.Compiler.Internal.Syntax;

#nullable enable

{{ForEach(tree.Nodes, node => GreenNodeClass(tree, node))}}

/// <summary>
/// Visitor base class for <see cref="{{tree.Root.Name}}"/>.
/// </summary>
internal abstract partial class SyntaxVisitor
{
    {{Visitors(tree.Nodes, "void", null)}}
}

/// <summary>
/// Visitor base class for <see cref="{{tree.Root.Name}}"/>.
/// </summary>
/// <typeparam name="TResult">
/// The return type of the visitor methods.
/// </typeparam>
internal abstract partial class SyntaxVisitor<TResult>
{
    {{Visitors(tree.Nodes, "TResult", "default!")}}
}

/// <summary>
/// A base class for rewriting <see cref="{{tree.Root.Name}}"/>.
/// </summary>
internal abstract partial class SyntaxRewriter : SyntaxVisitor<{{tree.Root.Name}}>
{
    {{Rewriters(tree)}}
}

#nullable restore
""");

    public static string GenerateRedTree(Tree tree) => FormatCSharp($$"""
// TODO: Implement
""");

    private static string GreenNodeClass(Tree tree, Node node) => FormatCSharp($$"""
    /// <summary>
    /// {{node.Documentation}}
    /// </summary>
    internal {{ClassHeader(node)}}
    {
        {{ForEach(node.Fields, field => $"{FieldPrefix(field)} {{ get; }}")}}

        {{Children(tree, node)}}

        {{ProtectedPublic(node)}} {{node.Name}}(
            {{ForEach(node.Fields, ", ", field => When(!field.Abstract, $"{field.Type} {CamelCase(field.Name)}"))}})
        {
            {{ForEach(node.Fields, field => When(!field.Abstract && field.IsToken, $$"""
                if (
                    {{When(field.IsNullable, $"{CamelCase(field.Name)} is not null &&")}}
                    {{CamelCase(field.Name)}} is not {{ForEach(field.TokenKinds, " and not ", kind => $"Api.Syntax.TokenKind.{kind}")}})
                {
                    throw new System.ArgumentException(
                        nameof({{CamelCase(field.Name)}}),
                        "the token must be of kind {{ForEach(field.TokenKinds, " or ", k => k)}}");
                }
            """))}}

            {{ForEach(node.Fields, field => $"this.{field.Name} = {CamelCase(field.Name)};")}}
        }

        {{When(node.IsAbstract,
            whenTrue: $"""
                public abstract {When(node.Base is not null, "override")} Api.Syntax.{node.Name} ToRedNode(
                    Api.Syntax.SyntaxTree tree, Api.Syntax.{tree.Root.Name}? parent, int fullPosition);
                """,
            whenFalse: $"""
                public override Api.Syntax.{node.Name} ToRedNode(
                    Api.Syntax.SyntaxTree tree, Api.Syntax.{tree.Root.Name}? parent, int fullPosition) =>
                    new Api.Syntax.{node.Name}(tree, parent, fullPosition, this);
                """)}}

        {{When(node.IsAbstract && node.Base is null,
            whenTrue: """
                public abstract void Accept(SyntaxVisitor visitor);
                public abstract TResult Accept<TResult>(SyntaxVisitor<TResult> visitor);
                """,
            whenFalse: $"""
                public override void Accept(SyntaxVisitor visitor) =>
                    visitor.{VisitorName(node)}(this);
                public override TResult Accept<TResult>(SyntaxVisitor<TResult> visitor) =>
                    visitor.{VisitorName(node)}(this);
                """)}}

        // TODO: Update functions
    }
    """);

    private static string Children(Tree tree, Node node) => When(!node.IsAbstract, $$"""
    public override IEnumerable<{{tree.Root.Name}}> Children
    {
        get
        {
            {{ForEach(node.Fields, field => $"""
                {When(field.IsNullable, $"if (this.{field.Name} is not null)")}
                yield return this.{field.Name};
            """)}}
            yield break;
        }
    }
    """);

    private static string VisitorName(Node node) => $"Visit{RemoveSuffix(node.Name, "Syntax")}";

    private static string ClassHeader(Node node) =>
        $"{AbstractSealed(node)} partial class {node.Name} {Base(node)}";

    private static string FieldPrefix(Field field) => $$"""
        {{NotNull(field.Documentation, doc => $"""
        /// <summary>
        /// {doc}
        /// </summary>
        """)}}
        public {{When(field.Abstract, "abstract")}} {{When(field.Override, "override")}} {{field.Type}} {{field.Name}}
        """;

    private static string AbstractSealed(Node node) => node.IsAbstract ? "abstract" : "sealed";
    private static string ProtectedPublic(Node node) => node.IsAbstract ? "protected" : "public";
    private static string Base(Node node) => NotNull(node.Base, b => $": {b.Name}");
}
