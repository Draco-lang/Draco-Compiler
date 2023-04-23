using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Draco.Compiler.Api.CodeFixes;
using Draco.Lsp.Model;
using Draco.Lsp.Server.Language;

namespace Draco.LanguageServer;

internal sealed partial class DracoLanguageServer : ICodeAction
{
    public CodeActionRegistrationOptions CodeActionRegistrationOptions => new()
    {
        DocumentSelector = this.DocumentSelector,
        CodeActionKinds = new[] { CodeActionKind.QuickFix },
        ResolveProvider = false
    };

    public Task<OneOf<Command, CodeAction>[]?> CodeActionAsync(CodeActionParams param, CancellationToken cancellationToken)
    {
        var fixes = this.codeFixService.GetCodeFixes(this.syntaxTree, this.semanticModel, Translator.ToCompiler(param.Range));
        var actions = new OneOf<Command, CodeAction>[fixes.Length];

        for (int i = 0; i < fixes.Length; i++)
        {
            actions[i] = new CodeAction()
            {
                Title = fixes[i].DisplayText,
                //TODO: we might have some other fixes in future
                Kind = CodeActionKind.QuickFix,
                Edit = new WorkspaceEdit()
                {
                    Changes = new Dictionary<DocumentUri, IList<Lsp.Model.TextEdit>>()
                    {
                        { param.TextDocument.Uri, fixes[i].Edits.Select(x => Translator.ToLsp(x)).ToList() }
                    }
                }
            };
        }
        return Task.FromResult<OneOf<Command, CodeAction>[]?>(actions);
    }
}
