using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Draco.Compiler.Internal.Syntax;

/// <summary>
/// Utilities for <see cref="SeparatedSyntaxList{TNode}"/>.
/// </summary>
internal static class SeparatedSyntaxList
{
    /// <summary>
    /// Creates a builder for a <see cref="SeparatedSyntaxList{TNode}"/>.
    /// </summary>
    /// <typeparam name="TNode">The node type.</typeparam>
    /// <returns>The created builder.</returns>
    public static SeparatedSyntaxList<TNode>.Builder CreateBuilder<TNode>()
        where TNode : SyntaxNode => new();
}

internal readonly partial struct SeparatedSyntaxList<TNode>
{
    /// <summary>
    /// The builder type for a <see cref="SeparatedSyntaxList{TNode}"/>.
    /// </summary>
    public sealed class Builder
    {
        private readonly ImmutableArray<SyntaxNode>.Builder builder = ImmutableArray.CreateBuilder<SyntaxNode>();
        private bool separatorsTurn;

        /// <summary>
        /// Adds a value node to the builder.
        /// </summary>
        /// <param name="value">The value node to add.</param>
        public void Add(TNode value)
        {
            if (this.separatorsTurn) throw new InvalidOperationException("a separator was expected next");
            this.builder.Add(value);
            this.separatorsTurn = true;
        }

        /// <summary>
        /// Adds a separator to the builder.
        /// </summary>
        /// <param name="separator">The separator to add.</param>
        public void Add(SyntaxToken separator)
        {
            if (!this.separatorsTurn) throw new InvalidOperationException("a value was expected next");
            this.builder.Add(separator);
            this.separatorsTurn = false;
        }

        /// <summary>
        /// Constructs a <see cref="SeparatedSyntaxList{TNode}"/> from the builder.
        /// </summary>
        /// <returns>The constructed <see cref="SeparatedSyntaxList{TNode}"/>.</returns>
        public SeparatedSyntaxList<TNode> ToSeparatedSyntaxList() => new(this.builder.ToImmutable());
    }
}
