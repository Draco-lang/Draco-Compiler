using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using Draco.Compiler.Api.Semantics;
using Draco.Compiler.Api.Syntax;
using Draco.Compiler.Internal.Symbols;

namespace Draco.Compiler.Api.Services.CodeCompletion.Providers;

/// <summary>
/// Provides completions for member access.
/// </summary>
public sealed class MemberCompletionProvider : CompletionProvider
{
    public override bool IsApplicableIn(CompletionContext context)
    {
        if (!context.HasFlag(CompletionContext.Member)) return false;
        return context.HasFlag(CompletionContext.Expression)
            || context.HasFlag(CompletionContext.Type)
            || context.HasFlag(CompletionContext.Import);
    }

    public override ImmutableArray<CompletionItem> GetCompletionItems(
        SemanticModel semanticModel, int cursorIndex, SyntaxNode? nodeAtCursor, CompletionContext contexts)
    {
        if (nodeAtCursor is not SyntaxToken token) return [];

        // If it's just the dot and there's no identifier after it, we don't want to replace the dot itself
        var span = token.Kind == TokenKind.Dot ? SourceSpan.Empty(token.Span.End) : token.Span;
        var expr = token.Parent;

        // Ask for context to access from, so we can filter by visibility
        var accessContext = semanticModel.GetBindingSymbol(token);

        // Retrieve all the members referenced by node that are visible from accessContext
        var rawSymbols = GetMemberSymbols(semanticModel, expr).Where(s => IsAppropriateForContext(s, contexts));
        var symbols = SymbolCollectionBuilder.ToCollection(rawSymbols, visibleFrom: accessContext);

        // Construct
        return symbols
            .Select(s => s.ToApiSymbol())
            .Select(s => CompletionItem.Simple(span, s))
            .ToImmutableArray();
    }

    private static IEnumerable<Symbol> GetMemberSymbols(SemanticModel semanticModel, SyntaxNode? node)
    {
        if (!TryDeconstructMemberAccess(node, out var receiverSyntax)) return [];

        var referencedType = semanticModel.GetReferencedSymbolInternal(receiverSyntax);
        // NOTE: This is how we check for static access
        if (referencedType?.IsTypeOnCilLevel == true) return referencedType.StaticMembers;

        // Otherwise this is an instance access
        if (receiverSyntax is not ExpressionSyntax accessedExpr) return [];
        var receiverType = semanticModel.TypeOfInternal(accessedExpr);
        return receiverType?.InstanceMembers ?? [];
    }

    public static bool TryDeconstructMemberAccess(SyntaxNode? node, [MaybeNullWhen(false)] out SyntaxNode receiverSyntax)
    {
        switch (node)
        {
        case MemberExpressionSyntax expr:
            receiverSyntax = expr.Accessed;
            return true;
        case MemberTypeSyntax type:
            receiverSyntax = type.Accessed;
            return true;
        case MemberImportPathSyntax import:
            receiverSyntax = import.Accessed;
            return true;
        default:
            receiverSyntax = null;
            return false;
        }
    }
}
