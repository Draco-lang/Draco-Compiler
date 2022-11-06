using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using System.IO;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Draco.Compiler.Api.Syntax;
using System.Reflection;

namespace Draco.LanguageServer.Handlers;
public class DracoSemanticTokensHandler : SemanticTokensHandlerBase
{
    private ParseTree? parseTree;

    private readonly DocumentSelector documentSelector = new(new DocumentFilter
    {
        Pattern = $"**/*{Constants.DracoSourceExtension}",
    });

    public override async Task<SemanticTokens?> Handle(SemanticTokensParams request, CancellationToken cancellationToken)
    {
        var result = await base.Handle(request, cancellationToken).ConfigureAwait(false);
        return result;
    }

    public override async Task<SemanticTokens?> Handle(SemanticTokensRangeParams request, CancellationToken cancellationToken)
    {
        var result = await base.Handle(request, cancellationToken).ConfigureAwait(false);
        return result;
    }

    public override async Task<SemanticTokensFullOrDelta?> Handle(SemanticTokensDeltaParams request, CancellationToken cancellationToken)
    {
        var result = await base.Handle(request, cancellationToken).ConfigureAwait(false);
        return result;
    }

    protected override async Task Tokenize(SemanticTokensBuilder builder, ITextDocumentIdentifierParams identifier, CancellationToken cancellationToken)
    {
        var content = await File.ReadAllTextAsync(DocumentUri.GetFileSystemPath(identifier)!, cancellationToken).ConfigureAwait(false);
        this.parseTree = ParseTree.Parse(content);
        var tokens = this.GetTokens(this.parseTree);
        await Task.Yield();
        foreach (var token in tokens)
        {
            if (token is not null) builder.Push(Translator.ToLsp(token.Token.Range), token.Type, token.Modifiers);
        }
    }

    protected override Task<SemanticTokensDocument> GetSemanticTokensDocument(ITextDocumentIdentifierParams @params, CancellationToken cancellationToken)
    {
        return Task.FromResult(new SemanticTokensDocument(this.RegistrationOptions.Legend));
    }

    protected override SemanticTokensRegistrationOptions CreateRegistrationOptions(SemanticTokensCapability capability, ClientCapabilities clientCapabilities)
    {
        return new SemanticTokensRegistrationOptions
        {
            DocumentSelector = this.documentSelector,
            Legend = new SemanticTokensLegend
            {
                TokenModifiers = capability.TokenModifiers,
                TokenTypes = capability.TokenTypes
            },
            Full = new SemanticTokensCapabilityRequestFull
            {
                Delta = true
            },
            Range = true
        };
    }

    private class SemanticToken
    {
        public SemanticTokenType? Type;
        public List<SemanticTokenModifier> Modifiers = new List<SemanticTokenModifier>();
        public Compiler.Api.Syntax.ParseTree Token;
        public SemanticToken(SemanticTokenType? type, List<SemanticTokenModifier> modifiers, Compiler.Api.Syntax.ParseTree token)
        {
            this.Type = type;
            this.Modifiers = modifiers;
            this.Token = token;
        }

        public SemanticToken(SemanticTokenType? type, SemanticTokenModifier modifier, Compiler.Api.Syntax.ParseTree token)
        {
            this.Type = type;
            this.Modifiers.Add(modifier);
            this.Token = token;
        }
    }

    private List<SemanticToken?> GetTokens(ParseTree tree) => tree.Tokens
        .Select(t => t.Text switch
        {
            "true" => new SemanticToken(SemanticTokenType.String, SemanticTokenModifier.Defaults.ToList(), t), //nonfinished
            _ => null,
        }).ToList();
}
