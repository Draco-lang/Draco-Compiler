using System;
using System.Collections.Immutable;
using System.Linq;
using Draco.Compiler.Api.Diagnostics;
using Draco.Compiler.Api.Syntax;
using Draco.Compiler.Internal.Diagnostics;
using Draco.Compiler.Internal.Symbols;
using Draco.Compiler.Internal.Symbols.Error;

namespace Draco.Compiler.Internal.Binding;

internal partial class Binder
{
    /// <summary>
    /// Binds the given syntax node to a type symbol. If the referenced symbol is not a type,
    /// an error is reported.
    /// </summary>
    /// <param name="syntax">The type to bind.</param>
    /// <param name="diagnostics">The diagnostics produced during the process.</param>
    /// <returns>The looked up type symbol for <paramref name="syntax"/>.</returns>
    internal TypeSymbol BindTypeToTypeSymbol(TypeSyntax syntax, DiagnosticBag diagnostics)
    {
        var symbol = this.BindType(syntax, diagnostics);
        if (symbol is TypeSymbol type)
        {
            // For example referencing to Array2D without the generic arguments
            if (type.IsGenericDefinition)
            {
                diagnostics.Add(Diagnostic.Create(
                    template: TypeCheckingErrors.GenericTypeNotInstantiated,
                    location: syntax.Location,
                    formatArgs: type));
                return WellKnownTypes.ErrorType;
            }
            // Ok
            return type;
        }
        else if (symbol is ModuleSymbol)
        {
            // Module, report it
            diagnostics.Add(Diagnostic.Create(
                template: SymbolResolutionErrors.IllegalModuleType,
                location: syntax.Location,
                formatArgs: syntax));
            return WellKnownTypes.ErrorType;
        }
        else
        {
            // TODO
            throw new NotImplementedException();
        }
    }

    /// <summary>
    /// Binds the given type syntax node to a symbol.
    /// </summary>
    /// <param name="syntax">The type to bind.</param>
    /// <param name="diagnostics">The diagnostics produced during the process.</param>
    /// <returns>The looked up symbol for <paramref name="syntax"/>.</returns>
    internal virtual Symbol BindType(TypeSyntax syntax, DiagnosticBag diagnostics) => syntax switch
    {
        // NOTE: The syntax error is already reported
        UnexpectedTypeSyntax => WellKnownTypes.ErrorType,
        NameTypeSyntax name => this.BindNameType(name, diagnostics),
        MemberTypeSyntax member => this.BindMemberType(member, diagnostics),
        GenericTypeSyntax generic => this.BindGenericType(generic, diagnostics),
        _ => throw new ArgumentOutOfRangeException(nameof(syntax)),
    };

    private Symbol BindNameType(NameTypeSyntax syntax, DiagnosticBag diagnostics)
    {
        var symbol = this.LookupTypeSymbol(syntax.Name.Text, syntax, diagnostics);
        this.CheckVisibility(syntax, symbol, "symbol", diagnostics);
        return symbol;
    }

    private Symbol BindMemberType(MemberTypeSyntax syntax, DiagnosticBag diagnostics)
    {
        var left = this.BindType(syntax.Accessed, diagnostics);
        var memberName = syntax.Member.Text;
        if (left.IsError)
        {
            // Error, don't cascade
            return left;
        }
        else
        {
            // Module or type member access
            var members = left.AllMembers
                .Where(m => m.Name == memberName)
                .Where(BinderFacts.IsTypeSymbol)
                .ToImmutableArray();
            // Reuse logic from LookupResult
            var result = LookupResult.FromResultSet(members);
            var symbol = result.GetType(memberName, syntax, diagnostics);
            this.CheckVisibility(syntax, symbol, "symbol", diagnostics);
            return symbol;
        }
    }

    private Symbol BindGenericType(GenericTypeSyntax syntax, DiagnosticBag diagnostics)
    {
        var instantiated = this.BindType(syntax.Instantiated, diagnostics);
        var args = syntax.Arguments.Values
            .Select(arg => this.BindTypeToTypeSymbol(arg, diagnostics))
            .ToImmutableArray();

        if (args.Length == 0)
        {
            // This is not actually a generic instantiation, just illegal syntax, like int32<>
            // This should have been caught by the parser, so we shouldn't need to report it here
            return WellKnownTypes.ErrorType;
        }

        if (!instantiated.IsGenericDefinition)
        {
            // Not even a generic construct
            diagnostics.Add(Diagnostic.Create(
                template: TypeCheckingErrors.NotGenericConstruct,
                location: syntax.Location));
            return WellKnownTypes.ErrorType;
        }

        if (instantiated.GenericParameters.Length != args.Length)
        {
            // Wrong number of args
            diagnostics.Add(Diagnostic.Create(
                template: TypeCheckingErrors.GenericTypeParamCountMismatch,
                location: syntax.Location,
                formatArgs: [instantiated, args.Length]));
            return WellKnownTypes.ErrorType;
        }

        // Ok, instantiate
        return instantiated.GenericInstantiate(instantiated.ContainingSymbol, args);
    }
}
