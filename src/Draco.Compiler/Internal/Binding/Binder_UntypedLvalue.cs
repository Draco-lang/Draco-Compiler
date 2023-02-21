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
    /// Binds the given syntax node to an untyped lvalue.
    /// </summary>
    /// <param name="syntax">The lvalue to bind.</param>
    /// <returns>The untyped lvalue for <paramref name="syntax"/>.</returns>
    protected UntypedLvalue BindLvalue(SyntaxNode syntax) => syntax switch
    {
        _ => throw new ArgumentOutOfRangeException(nameof(syntax)),
    };
}
