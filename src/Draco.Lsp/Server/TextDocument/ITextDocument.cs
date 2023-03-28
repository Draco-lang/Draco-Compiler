using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Draco.Lsp.Attributes;
using Draco.Lsp.Model;

namespace Draco.Lsp.Server.TextDocument;

public interface ITextDocument : ITextDocumentDidOpen, ITextDocumentDidClose, ITextDocumentDidChange
{
    [Capability(nameof(ServerCapabilities.TextDocumentSync))]
    public TextDocumentSyncOptions? Capability => null;

    public IList<DocumentFilter>? DocumentSelector { get; }
    public TextDocumentSyncKind SyncKind { get; }

    TextDocumentRegistrationOptions ITextDocumentDidOpen.DidOpenRegistrationOptions => new()
    {
        DocumentSelector = this.DocumentSelector,
    };
    TextDocumentRegistrationOptions ITextDocumentDidClose.DidCloseRegistrationOptions => new()
    {
        DocumentSelector = this.DocumentSelector,
    };
    TextDocumentChangeRegistrationOptions ITextDocumentDidChange.DidChangeRegistrationOptions => new()
    {
        DocumentSelector = this.DocumentSelector,
        SyncKind = this.SyncKind,
    };
}
