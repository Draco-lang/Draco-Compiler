using System.Threading.Tasks;
using System.Threading;
using System;
using PrettyPrompt;
using PrettyPrompt.Consoles;
using Draco.Compiler.Api.Scripting;

namespace Draco.Repl;

internal sealed class ReplPromptCallbacks : PromptCallbacks
{
    protected override async Task<KeyPress> TransformKeyPressAsync(string text, int caret, KeyPress keyPress, CancellationToken cancellationToken)
    {
        // Incomplete prompt, just add newline
        if (keyPress.ConsoleKeyInfo.Key == ConsoleKey.Enter
         && keyPress.ConsoleKeyInfo.Modifiers == default
         && !ReplSession.IsCompleteEntry(text))
        {
            // NOTE: We could smart-indent here like CSharpRepl does
            return new(ConsoleKey.Insert.ToKeyInfo('\0', shift: true), Environment.NewLine);
        }

        return keyPress;
    }
}
