using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Draco.Compiler.Api.Syntax;
using Draco.Compiler.Internal.BoundTree;
using Draco.Compiler.Internal.UntypedTree;

namespace Draco.Compiler.Internal.Binding;

internal partial class Binder
{
    /// <summary>
    /// Binds the given syntax node to an untyped expression.
    /// </summary>
    /// <param name="lvalue">The syntax to bind.</param>
    /// <returns>The untyped expression for <paramref name="lvalue"/>.</returns>
    protected UntypedExpression BindExpression(SyntaxNode lvalue) => lvalue switch
    {
        _ => throw new ArgumentOutOfRangeException(nameof(lvalue)),
    };
}
