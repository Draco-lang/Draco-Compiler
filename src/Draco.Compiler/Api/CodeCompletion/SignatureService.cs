using System.Collections.Immutable;
using System.Linq;
using Draco.Compiler.Api.Semantics;
using Draco.Compiler.Api.Syntax;

namespace Draco.Compiler.Api.CodeCompletion;

public static class SignatureService
{
    public static SignatureItem? GetSignature(SyntaxTree tree, SemanticModel semanticModel, SyntaxPosition cursor)
    {
        // Check if this is a call expression
        var call = tree.Root.TraverseSubtreesAtCursorPosition(cursor).LastOrDefault(x => x is CallExpressionSyntax) as CallExpressionSyntax;
        if (call is null) return null;

        // Get all overloads
        var symbols = semanticModel.GetReferencedOverloads(call.Function).Select(x => (IFunctionSymbol)x).OrderBy(x => x.Parameters.Length).ToImmutableArray();
        if (symbols.Length == 0) return null;
        // Figure out which param should be active
        var paramCount = call.ArgumentList.Values.Count();
        var separatorCount = call.ArgumentList.Separators.Count();
        var activeParam = separatorCount == paramCount - 1 ? paramCount - 1 : paramCount;

        // Select the best overload to show as default in the signature
        var currentOverload = symbols.FirstOrDefault(x => x.Parameters.Length == paramCount && (separatorCount == paramCount - 1 || paramCount == 0));
        if (currentOverload is null) currentOverload = symbols.FirstOrDefault(x => x.Parameters.Length > paramCount);
        if (currentOverload is null) currentOverload = symbols.First();
        IParameterSymbol? currentParameter = null;
        if (currentOverload.Parameters.Length != 0) currentParameter = currentOverload.Parameters[activeParam];
        // Return all the overloads
        return new SignatureItem(symbols, currentOverload, currentParameter);
    }
}
