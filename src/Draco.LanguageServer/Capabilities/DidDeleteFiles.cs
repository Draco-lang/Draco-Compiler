using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Draco.Compiler.Api.Syntax;
using Draco.Lsp.Model;
using Draco.Lsp.Server.Workspace;

namespace Draco.LanguageServer;

internal partial class DracoLanguageServer : IDidDeleteFiles
{
    public FileOperationRegistrationOptions DidDeleteFileRegistrationOptions => new()
    {
        Filters = new FileOperationFilter[]
        {
            new()
            {
                Pattern = new()
                {
                    Glob = this.DocumentSelector[0].Pattern!
                }
            }
        }
    };

    public async Task DidDeleteFilesAsync(DeleteFilesParams param, CancellationToken cancellationToken)
    {
        foreach (var file in param.Files)
        {
            await this.DeleteDocument(DocumentUri.From(file.Uri));
        }
    }

    private async Task DeleteDocument(DocumentUri documentUri)
    {
        await this.PublishDiagnosticsAsync(documentUri, ImmutableArray<Compiler.Api.Diagnostics.Diagnostic>.Empty);
        var uri = documentUri.ToUri();
        var oldTree = this.compilation.SyntaxTrees
            .First(tree => tree.SourceText.Path == uri);
        this.compilation = this.compilation.UpdateSyntaxTree(oldTree, null);
        if (this.syntaxTree == oldTree)
        {
            this.syntaxTree = SyntaxTree.Create(SyntaxFactory.CompilationUnit());
            this.semanticModel = this.compilation.GetSemanticModel(this.syntaxTree);
        }
        else
        {
            this.semanticModel = this.compilation.GetSemanticModel(this.syntaxTree);
            await this.PublishDiagnosticsAsync(DocumentUri.From(this.syntaxTree.SourceText.Path!));
        }
    }
}
