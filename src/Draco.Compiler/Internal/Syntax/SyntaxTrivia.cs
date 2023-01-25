using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Draco.Compiler.Internal.Syntax;

/// <summary>
/// Elements of the source that are not significant for the semantics, like spaces and comments.
/// </summary>
internal sealed class SyntaxTrivia : SyntaxNode
{
    public override IEnumerable<SyntaxNode> Children => throw new NotImplementedException();

    public override Api.Syntax.SyntaxTrivia ToRedNode(SyntaxTree tree, Api.Syntax.SyntaxNode? parent) => throw new NotImplementedException();
    public override void Accept(SyntaxVisitor visitor) => throw new NotImplementedException();
    public override TResult Accept<TResult>(SyntaxVisitor<TResult> visitor) => throw new NotImplementedException();
}
