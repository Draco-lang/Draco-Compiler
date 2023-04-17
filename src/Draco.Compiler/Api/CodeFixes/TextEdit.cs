using Draco.Compiler.Api.Syntax;

namespace Draco.Compiler.Api.CodeFixes;

/// <summary>
/// Represents an edit in source text.
/// </summary>
/// <param name="Text">The text that should be placed into the source document.</param>
/// <param name="Range">The range of the thext that will be replaced by <paramref name="Text"/>.</param>
public record class TextEdit(string Text, SyntaxRange Range);
