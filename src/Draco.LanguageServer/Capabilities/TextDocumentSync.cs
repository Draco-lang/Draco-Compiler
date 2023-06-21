using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Draco.Lsp.Model;
using Draco.Lsp.Server.TextDocument;

namespace Draco.LanguageServer;

internal sealed partial class DracoLanguageServer : ITextDocumentSync
{
    public async Task TextDocumentDidOpenAsync(DidOpenTextDocumentParams param, CancellationToken cancellationToken)
    {
        await this.PublishDiagnosticsAsync(param.TextDocument.Uri);
    }

    public Task TextDocumentDidCloseAsync(DidCloseTextDocumentParams param, CancellationToken cancellationToken) =>
        Task.CompletedTask;

    public async Task TextDocumentDidChangeAsync(DidChangeTextDocumentParams param, CancellationToken cancellationToken)
    {
        var uri = param.TextDocument.Uri;
        var change = param.ContentChanges.First();
        var sourceText = change.Text;
        await this.UpdateDocument(uri, sourceText);
    }

    private async Task PublishDiagnosticsAsync(DocumentUri uri, ImmutableArray<Compiler.Api.Diagnostics.Diagnostic>? diags = null)
    {
        var compilation = this.compilation;

        var syntaxTree = GetSyntaxTree(compilation, uri);
        if (syntaxTree is null) return;

        var semanticModel = compilation.GetSemanticModel(syntaxTree);
        diags ??= semanticModel.Diagnostics;
        var lspDiags = diags.Value.Select(Translator.ToLsp).ToList();
        await this.client.PublishDiagnosticsAsync(new()
        {
            Uri = uri,
            Diagnostics = lspDiags,
        });
    }
}
