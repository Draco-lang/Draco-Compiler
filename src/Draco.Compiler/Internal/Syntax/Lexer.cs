using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text;
using Draco.Compiler.Api.Diagnostics;
using Draco.Compiler.Api.Syntax;
using Draco.Compiler.Internal.Diagnostics;
using Draco.Compiler.Internal.Utilities;
using DiagnosticTemplate = Draco.Compiler.Api.Diagnostics.DiagnosticTemplate;

namespace Draco.Compiler.Internal.Syntax;

/// <summary>
/// Breaks up source code into a sequence of <see cref="SyntaxToken"/>s.
/// </summary>
internal sealed class Lexer
{
    /// <summary>
    /// The different kinds of modes the lexer can have.
    /// </summary>
    internal enum ModeKind
    {
        /// <summary>
        /// Regular source code lexing.
        /// </summary>
        Normal,

        /// <summary>
        /// Normal source code within line string interpolation.
        /// </summary>
        LineInterpolation,

        /// <summary>
        /// Normal source code within multi-line string interpolation.
        /// </summary>
        MultiLineInterpolation,

        /// <summary>
        /// Line string lexing.
        /// </summary>
        LineString,

        /// <summary>
        /// Multi-line string lexing.
        /// </summary>
        MultiLineString,
    }

    /// <summary>
    /// Represents a single mode on the mode stack.
    /// </summary>
    /// <param name="Kind">The kind of the mode.</param>
    /// <param name="ExtendedDelims">The number of extended delimiter characters needed for the current mode,
    /// in case it's a string lexing mode.</param>
    internal readonly record struct Mode(ModeKind Kind, int ExtendedDelims)
    {
        /// <summary>
        /// True, if the mode is some kind of interpolation mode.
        /// </summary>
        public bool IsInterpolation => this.Kind
            is ModeKind.LineInterpolation
            or ModeKind.MultiLineInterpolation;

        /// <summary>
        /// True, if this mode is some kind of string lexing mode.
        /// </summary>
        public bool IsString => this.Kind
            is ModeKind.LineString
            or ModeKind.MultiLineString;

        /// <summary>
        /// True, if this is some code lexing mode, either regular or interpolation.
        /// </summary>
        public bool IsCode => this.Kind == ModeKind.Normal || this.IsInterpolation;

        /// <summary>
        /// True, if the current mode must not hold a newline.
        /// </summary>
        public bool IsLine => this.Kind
            is ModeKind.LineString
            or ModeKind.LineInterpolation;
    }

    private static readonly Rune DefaultCharacter = new(' ');

    /// <summary>
    /// The reader the source text is read from.
    /// </summary>
    public ISourceReader SourceReader { get; }

    private Mode CurrentMode => this.modeStack.Peek();

    // Mode stack of the lexer, this is actual, changing state
    private readonly Stack<Mode> modeStack = new();

    // These are various cached builder types to save on allocations
    // The important thing is that these should not carry any significant state in the lexer in any way,
    // meaning that the behavior should be identical if we reallocated/cleared these before each token
    private readonly SyntaxToken.Builder tokenBuilder = new();
    private readonly StringBuilder valueBuilder = new();
    private readonly List<SyntaxDiagnosticInfo> diagnosticBuilder = [];

    private readonly SyntaxDiagnosticTable diagnostics;

    public Lexer(ISourceReader sourceReader, SyntaxDiagnosticTable diagnostics)
    {
        this.SourceReader = sourceReader;
        this.diagnostics = diagnostics;
        this.PushMode(ModeKind.Normal, 0);
    }

    /// <summary>
    /// Reads the next <see cref="SyntaxToken"/> from the input.
    /// </summary>
    /// <returns>The <see cref="SyntaxToken"/> read.</returns>
    public SyntaxToken Lex()
    {
        this.tokenBuilder.Clear();
        this.valueBuilder.Clear();
        this.diagnosticBuilder.Clear();

        switch (this.CurrentMode.Kind)
        {
        case ModeKind.Normal:
        case ModeKind.LineInterpolation:
        case ModeKind.MultiLineInterpolation:
        {
            // Normal tokens can have trivia
            this.ParseLeadingTriviaList();
            this.LexNormal();
            // If we just started a line-string or ended interpolation, we are within a string, don't consume
            if (this.tokenBuilder.Kind != TokenKind.InterpolationEnd
             && this.tokenBuilder.Kind != TokenKind.LineStringStart)
            {
                this.ParseTrailingTriviaList();
            }
            break;
        }

        case ModeKind.LineString:
        case ModeKind.MultiLineString:
            this.LexString();
            // If we are starting interpolation, we can consume trailing trivia
            if (this.tokenBuilder.Kind == TokenKind.InterpolationStart
             || this.tokenBuilder.Kind == TokenKind.LineStringEnd) this.ParseTrailingTriviaList();
            break;

        default:
            throw new InvalidOperationException("unsupported lexer mode");
        }

        var token = this.tokenBuilder.Build();
        // Associate diagnostics, if needed
        if (this.diagnosticBuilder.Count > 0) this.diagnostics.AddRange(token, this.diagnosticBuilder);
        return token;
    }

    /// <summary>
    /// Lexes tokens that can be found in regular code.
    /// </summary>
    /// <returns>The lexed <see cref="SyntaxToken"/>.</returns>
    private void LexNormal()
    {
        void TakeBasic(TokenKind tokenKind, int length)
        {
            this.Advance(length);
            this.tokenBuilder.SetKind(tokenKind);
        }

        void TakeWithText(TokenKind tokenKind, int length)
        {
            this.tokenBuilder
                .SetKind(tokenKind)
                .SetText(this.AdvanceWithText(length));
        }

        // First check for end of source here
        if (this.SourceReader.IsEnd)
        {
            this.tokenBuilder.SetKind(TokenKind.EndOfInput);
            return;
        }

        var mode = this.CurrentMode;
        var ch = this.Peek();

        // Punctuation
        switch (ch)
        {
        case '(':
            TakeBasic(TokenKind.ParenOpen, 1);
            return;
        case ')':
            TakeBasic(TokenKind.ParenClose, 1);
            return;
        case '{':
            if (mode.IsInterpolation) this.PushMode(mode.Kind, 0);
            TakeBasic(TokenKind.CurlyOpen, 1);
            return;
        case '}':
            if (mode.IsInterpolation)
            {
                this.PopMode();
                // If we are not in interpolation anymore, this is an interpolation end token
                if (!this.CurrentMode.IsInterpolation)
                {
                    TakeBasic(TokenKind.InterpolationEnd, 1);
                    return;
                }
            }
            TakeBasic(TokenKind.CurlyClose, 1);
            return;
        case '[':
            TakeBasic(TokenKind.BracketOpen, 1);
            return;
        case ']':
            TakeBasic(TokenKind.BracketClose, 1);
            return;

        case '.':
            if (this.Peek(1) == '.' && this.Peek(2) == '.') TakeBasic(TokenKind.Ellipsis, 3);
            else TakeBasic(TokenKind.Dot, 1);
            return;
        case ',':
            TakeBasic(TokenKind.Comma, 1);
            return;
        case ':':
            TakeBasic(TokenKind.Colon, 1);
            return;
        case ';':
            TakeBasic(TokenKind.Semicolon, 1);
            return;
        case '+':
            if (this.Peek(1) == '=') TakeBasic(TokenKind.PlusAssign, 2);
            else TakeBasic(TokenKind.Plus, 1);
            return;
        case '-':
            if (this.Peek(1) == '=') TakeBasic(TokenKind.MinusAssign, 2);
            else TakeBasic(TokenKind.Minus, 1);
            return;
        case '*':
            if (this.Peek(1) == '=') TakeBasic(TokenKind.StarAssign, 2);
            else TakeBasic(TokenKind.Star, 1);
            return;
        case '/':
            if (this.Peek(1) == '=') TakeBasic(TokenKind.SlashAssign, 2);
            else TakeBasic(TokenKind.Slash, 1);
            return;
        case '<':
            if (this.Peek(1) == '=') TakeBasic(TokenKind.LessEqual, 2);
            else TakeBasic(TokenKind.LessThan, 1);
            return;
        case '>':
            if (this.Peek(1) == '=') TakeBasic(TokenKind.GreaterEqual, 2);
            else TakeBasic(TokenKind.GreaterThan, 1);
            return;
        case '=':
            if (this.Peek(1) == '=') TakeBasic(TokenKind.Equal, 2);
            else TakeBasic(TokenKind.Assign, 1);
            return;
        case '!':
            if (this.Peek(1) == '=') TakeBasic(TokenKind.NotEqual, 2);
            else TakeBasic(TokenKind.CNot, 1);
            return;
        case '%':
            TakeBasic(TokenKind.CMod, 1);
            return;
        case '|':
            if (this.Peek(1) == '|')
            {
                TakeBasic(TokenKind.COr, 2);
                return;
            }
            break;
        case '&':
            if (this.Peek(1) == '&')
            {
                TakeBasic(TokenKind.CAnd, 2);
                return;
            }
            break;
        case '@':
            TakeBasic(TokenKind.AtSign, 1);
            return;
        }

        // Numeric literals
        // NOTE: We check for numeric literals first, so we can be lazy with the identifier checking later
        // Since digits would be a valid identifier character, we can avoid separating the check for the
        // first character
        if (IsDigit(ch))
        {
            // Check for what kind of integer do we have
            var radix = this.Peek(1);
            // Hexadecimal or binary integer
            if (ch == '0' && (radix == 'x' || radix == 'b'))
            {
                this.Advance(2);
                var view = this.ParseIntLiteral(radix == 'x' ? 16 : 2, out var value);
                this.tokenBuilder
                    .SetKind(TokenKind.LiteralInteger)
                    .SetText($"0{radix}{view}")
                    .SetValue(value);
                return;
            }
            var offset = 1;
            var isFloat = false;
            while (IsDigit(this.Peek(offset))) ++offset;

            // Floating point number
            if (this.Peek(offset) == '.' && IsDigit(this.Peek(offset + 1)))
            {
                isFloat = true;
                offset += 2;
                while (IsDigit(this.Peek(offset))) ++offset;
            }

            if (char.ToLower(this.Peek(offset)) == 'e')
            {
                isFloat = true;
                ++offset;
                if (this.Peek(offset) == '+' || this.Peek(offset) == '-') ++offset;
                if (!IsDigit(this.Peek(offset)))
                {
                    this.AddError(
                        template: SyntaxErrors.UnexpectedFloatingPointLiteralEnd,
                        offset: offset,
                        width: 1);
                    this.tokenBuilder
                        .SetKind(TokenKind.LiteralFloat)
                        .SetText(this.Advance(offset).Span.ToString());
                    return;
                }
                while (IsDigit(this.Peek(offset))) ++offset;
            }

            if (isFloat)
            {
                var floatView = this.Advance(offset);
                // TODO: Parsing into an float64 might not be the best idea
                var floatValue = double.Parse(floatView.Span.ToString(), provider: CultureInfo.InvariantCulture);
                this.tokenBuilder
                    .SetKind(TokenKind.LiteralFloat)
                    .SetText(floatView.ToString())
                    .SetValue(floatValue);
                return;
            }

            // Regular integer
            var decimalView = this.ParseIntLiteral(10, out var decimalValue);
            this.tokenBuilder
                .SetKind(TokenKind.LiteralInteger)
                .SetText(decimalView)
                .SetValue(decimalValue);
            return;
        }

        // Identifier-like tokens
        if (IsIdent(ch))
        {
            var offset = 1;
            while (IsIdent(this.Peek(offset))) ++offset;
            var ident = this.Advance(offset);
            // Remap keywords
            var tokenKind = ident.Span switch
            {
                "and" => TokenKind.KeywordAnd,
                "else" => TokenKind.KeywordElse,
                "false" => TokenKind.KeywordFalse,
                "field" => TokenKind.KeywordField,
                "for" => TokenKind.KeywordFor,
                "func" => TokenKind.KeywordFunc,
                "goto" => TokenKind.KeywordGoto,
                "if" => TokenKind.KeywordIf,
                "import" => TokenKind.KeywordImport,
                "in" => TokenKind.KeywordIn,
                "internal" => TokenKind.KeywordInternal,
                "mod" => TokenKind.KeywordMod,
                "module" => TokenKind.KeywordModule,
                "not" => TokenKind.KeywordNot,
                "or" => TokenKind.KeywordOr,
                "public" => TokenKind.KeywordPublic,
                "rem" => TokenKind.KeywordRem,
                "return" => TokenKind.KeywordReturn,
                "true" => TokenKind.KeywordTrue,
                "val" => TokenKind.KeywordVal,
                "var" => TokenKind.KeywordVar,
                "while" => TokenKind.KeywordWhile,
                _ => TokenKind.Identifier,
            };
            // Return the appropriate token
            if (tokenKind == TokenKind.Identifier)
            {
                // Need to save the value
                // NOTE: We don't necessarily need to save the value yet, but if we later decide that we want
                // escaped identifiers, this can be a good way to handle it
                var identStr = ident.ToString();
                this.tokenBuilder
                    .SetKind(TokenKind.Identifier)
                    .SetText(identStr)
                    .SetValue(identStr);
            }
            else
            {
                // Keyword, we save on allocation
                this.tokenBuilder.SetKind(tokenKind);
                if (tokenKind == TokenKind.KeywordTrue) this.tokenBuilder.SetValue(true);
                if (tokenKind == TokenKind.KeywordFalse) this.tokenBuilder.SetValue(false);
            }
            return;
        }

        // Character literals
        if (ch == '\'')
        {
            var offset = 1;
            if (!this.TryPeek(offset, out var ch2))
            {
                // Unexpected end of input
                this.AddError(
                    template: SyntaxErrors.UnexpectedCharacterLiteralEnd,
                    offset: offset,
                    width: 1);
                var errText = this.AdvanceWithText(offset);
                this.tokenBuilder
                    .SetKind(TokenKind.LiteralCharacter)
                    .SetText(errText)
                    .SetValue(new Rune(' '));
                return;
            }
            var resultChar = default(Rune);
            if (ch2 == '\\')
            {
                // Escape-sequence
                ++offset;
                resultChar = this.ParseEscapeSequence(offset - 1, ref offset);
            }
            else if (!char.IsControl(ch2))
            {
                // Regular character
                ++offset;
                if (Rune.IsValid(ch2))
                {
                    resultChar = new(ch2);
                }
                else
                {
                    // UnexpectedInput
                    this.AddError(
                        SyntaxErrors.IllegalCharacterLiteral,
                        offset: offset,
                        width: 1,
                        args: (int)ch2);
                    resultChar = DefaultCharacter;
                }
            }
            else
            {
                // Error, illegal character
                this.AddError(
                    SyntaxErrors.IllegalCharacterLiteral,
                    offset: offset,
                    width: 1,
                    args: (int)ch2);
                resultChar = DefaultCharacter;
            }
            // Expect closing quote
            if (this.Peek(offset) == '\'')
            {
                ++offset;
            }
            else
            {
                // NOTE: We could have some strategy to try to look for closing quotes to try to sync the lexer
                // Maybe we could search for the next quotes as long as we are in-line?
                this.AddError(
                    SyntaxErrors.UnclosedCharacterLiteral,
                    offset: offset,
                    width: 1);
            }
            // Done
            var text = this.AdvanceWithText(offset);
            this.tokenBuilder
                .SetKind(TokenKind.LiteralCharacter)
                .SetText(text)
                .SetValue(resultChar);
            return;
        }

        // String literal starts
        {
            var extendedDelims = 0;
            while (this.Peek(extendedDelims) == '#') ++extendedDelims;
            var offset = extendedDelims;
            if (this.Peek(offset) == '"')
            {
                if (this.Peek(offset + 1) == '"' && this.Peek(offset + 2) == '"')
                {
                    // Mutli-line string opening quotes
                    this.PushMode(ModeKind.MultiLineString, extendedDelims);
                    TakeWithText(TokenKind.MultiLineStringStart, offset + 3);
                }
                else
                {
                    // Single-line string opening quote
                    this.PushMode(ModeKind.LineString, extendedDelims);
                    TakeWithText(TokenKind.LineStringStart, offset + 1);
                }
                return;
            }
        }

        // Unknown
        TakeWithText(TokenKind.Unknown, 1);
        return;
    }

    /// <summary>
    /// Lexes a token that can be part of a string.
    /// </summary>
    /// <returns>The lexed string <see cref="SyntaxToken"/>.</returns>
    private void LexString()
    {
        // Get the largest continuous sequence without linebreaks or interpolation
        var mode = this.CurrentMode;
        var offset = 0;

    start:
        // First check for end of source here
        if (this.SourceReader.IsEnd)
        {
            this.tokenBuilder.SetKind(TokenKind.EndOfInput);
            return;
        }

        // NOTE: We are checking end of input differently here, because SourceReader.IsEnd is based on its
        // current position, but we are peeking in this input way ahead
        // End of input with nonzero offset
        if (!this.TryPeek(offset, out var ch))
        {
            // return the section we have consumed so far
            this.tokenBuilder
                .SetKind(TokenKind.StringContent)
                .SetText(this.AdvanceWithText(offset))
                .SetValue(this.valueBuilder.ToString());
            return;
        }

        // Check for closing quotes
        // For multiline strings we allow horizontal whitespace
        // This only happens for empty strings, but we still need to check if
        var stringEndOffset = offset;
        var stringEndCh = ch;
        if (mode.Kind == ModeKind.MultiLineString)
        {
            // Consume leading space
            while (IsSpace(this.Peek(stringEndOffset))) ++stringEndOffset;
            stringEndCh = this.Peek(stringEndOffset);
        }
        if (stringEndCh == '"')
        {
            var endLength = 0;
            if (mode.Kind == ModeKind.MultiLineString)
            {
                // We are expecting 2 more quotes
                if (this.Peek(stringEndOffset + 1) != '"' || this.Peek(stringEndOffset + 2) != '"') goto not_string_end;
                endLength = 3;
            }
            else
            {
                // Just a line string
                endLength = 1;
            }
            // Count the number of required closing delimiters
            for (var i = 0; i < mode.ExtendedDelims; ++i)
            {
                if (this.Peek(stringEndOffset + endLength + i) != '#') goto not_string_end;
            }
            endLength += mode.ExtendedDelims;
            // Hit the end of the string
            // NOTE: Since we are lexing the end of token as a separate character, we'll simply return what's
            // been lexed so far without the closing quotes, the next lex call will actually lex the closing
            // quotes.
            if (offset == 0)
            {
                // Nothing lexed yet, we can return the end of string token
                this.PopMode();
                // In case the string end offset doesn't match, we have leading whitespace
                if (offset != stringEndOffset) this.ParseLeadingTriviaList();
                var tokenKind = mode.Kind == ModeKind.LineString
                    ? TokenKind.LineStringEnd
                    : TokenKind.MultiLineStringEnd;
                this.tokenBuilder
                    .SetKind(tokenKind)
                    .SetText(this.AdvanceWithText(endLength));
            }
            else
            {
                // This will only be the end of string in the next iteration, we just return what we have
                // consumed so far
                this.tokenBuilder
                    .SetKind(TokenKind.StringContent)
                    .SetText(this.AdvanceWithText(offset))
                    .SetValue(this.valueBuilder.ToString());
            }
            return;
        }

    not_string_end:
        // Check for escape sequence
        if (ch == '\\')
        {
            var escapeStart = offset;

            // Count the number of required delimiters
            for (var i = 0; i < mode.ExtendedDelims; ++i)
            {
                if (this.Peek(offset + i + 1) != '#') goto not_escape_sequence;
            }

            if (escapeStart != 0)
            {
                // This is an escape, but we have content before it
                // Return that, the next call will return the escape
                this.tokenBuilder
                    .SetKind(TokenKind.StringContent)
                    .SetText(this.AdvanceWithText(offset))
                    .SetValue(this.valueBuilder.ToString());
                return;
            }

            // Interpolation
            if (this.Peek(offset + mode.ExtendedDelims + 1) == '{')
            {
                // Nothing lexed yet,we can return the start of interpolation token
                if (offset == 0)
                {
                    var newMode = mode.Kind == ModeKind.LineString
                        ? ModeKind.LineInterpolation
                        : ModeKind.MultiLineInterpolation;
                    this.PushMode(newMode, 0);
                    this.tokenBuilder
                        .SetKind(TokenKind.InterpolationStart)
                        .SetText(this.AdvanceWithText(mode.ExtendedDelims + 2));
                }
                else
                {
                    // This will only be interpolation in the next iteration, we just return what we have
                    // consumed so far
                    this.tokenBuilder
                        .SetKind(TokenKind.StringContent)
                        .SetText(this.AdvanceWithText(offset))
                        .SetValue(this.valueBuilder.ToString());
                }
                return;
            }

            // Line continuation
            if (mode.Kind == ModeKind.MultiLineString)
            {
                var offset2 = offset + mode.ExtendedDelims + 1;
                var whiteCharOffset = 0;
                while (IsSpace(this.Peek(offset2 + whiteCharOffset))) whiteCharOffset++;
                if (this.TryParseNewline(offset2 + whiteCharOffset, out var length))
                {
                    //TODO: decide what to do if there are whitespaces after line continuation
                    if (offset == 0)
                    {
                        // We can return this line-continuation
                        // The important thing is that the value is an empty string
                        this.tokenBuilder
                            .SetKind(TokenKind.StringNewline)
                            .SetText(this.AdvanceWithText(mode.ExtendedDelims + 1 + whiteCharOffset + length))
                            .SetValue(string.Empty);
                    }
                    else
                    {
                        // There is content before that we need to deal with
                        this.tokenBuilder
                            .SetKind(TokenKind.StringContent)
                            .SetText(this.AdvanceWithText(offset))
                            .SetValue(this.valueBuilder.ToString());
                    }
                    return;
                }
            }

            offset += mode.ExtendedDelims + 1;
            // Try to parse an escape
            var escaped = this.ParseEscapeSequence(escapeStart, ref offset);
            // Return as the result
            this.tokenBuilder
                .SetKind(TokenKind.EscapeSequence)
                .SetText(this.AdvanceWithText(offset))
                .SetValue(escaped);
            return;
        }

    not_escape_sequence:
        // Check for newline
        if (this.TryParseNewline(offset, out var newlineLength))
        {
            if (mode.Kind == ModeKind.LineString)
            {
                // A newline is illegal in a line string literal
                // NOTE: While we end the string mode, we DO NOT REPORT THE ERROR HERE
                // To be consistent about responsibility, matching up token pairs will be the job of the parser
                // It would be really messy to be able to report this same error for multiline strings
                // Also, this is only triggered if a newline is met, but not on the end of file
                this.PopMode();
                while (this.CurrentMode.IsLine) this.PopMode();
                this.tokenBuilder
                    .SetKind(TokenKind.StringContent)
                    .SetText(this.AdvanceWithText(offset))
                    .SetValue(this.valueBuilder.ToString());
                return;
            }
            else
            {
                // Newlines are completely valid in multiline strings
                if (offset == 0)
                {
                    // We are fine to return this newline, if it is indeed a newline
                    // If after this newline it's only whitespace and then the end quotes, we simply don't
                    // need to include this as a string newline
                    var whiteOffset = 0;
                    while (IsSpace(this.Peek(newlineLength + whiteOffset))) ++whiteOffset;
                    var endOffset = newlineLength + whiteOffset;
                    if (this.Peek(endOffset) == '"' && this.Peek(endOffset + 1) == '"' && this.Peek(endOffset + 2) == '"')
                    {
                        // The last check we need to do is to make sure we have enough delimiters
                        for (var i = 0; i < mode.ExtendedDelims; ++i)
                        {
                            if (this.Peek(endOffset + 3 + i) != '#') goto not_string_end2;
                        }
                        // This is the last newline and we have no other content to consume
                        // We build the end token with trivia
                        this.PopMode();
                        this.ParseLeadingTriviaList();
                        Debug.Assert(this.tokenBuilder.LeadingTrivia.Count is 1 or 2);
                        this.tokenBuilder
                            .SetKind(TokenKind.MultiLineStringEnd)
                            .SetText(this.AdvanceWithText(3 + mode.ExtendedDelims));
                        this.ParseTrailingTriviaList();
                        return;
                    }
                not_string_end2:
                    // Just a regular newline, more content to follow
                    var stringNewlineText = this.AdvanceWithText(newlineLength);
                    this.tokenBuilder
                        .SetKind(TokenKind.StringNewline)
                        .SetText(stringNewlineText)
                        .SetValue(stringNewlineText);
                    return;
                }
                else
                {
                    // We need to return the content fist
                    this.tokenBuilder
                        .SetKind(TokenKind.StringContent)
                        .SetText(this.AdvanceWithText(offset))
                        .SetValue(this.valueBuilder.ToString());
                    return;
                }
            }
        }

        // Just consume as a content character
        this.valueBuilder.Append(ch);
        ++offset;
        goto start;
    }

    /// <summary>
    /// Parses an escape sequence for strings and character literals.
    /// Reports an error, if the escape sequence is illegal.
    /// </summary>
    /// <param name="escapeStart">The position where the escape character occurred.</param>
    /// <param name="offset">A reference to an offset that points after the backslash and optional extended
    /// delimiter characters. If parsing the escape succeeded, the result is written back here.</param>
    /// <returns>True, if an escape was successfully parsed.</returns>
    private Rune ParseEscapeSequence(int escapeStart, ref int offset)
    {
        if (!this.TryPeek(offset, out var esc))
        {
            // Unexpected end of input
            this.AddError(
                template: SyntaxErrors.UnexpectedEscapeSequenceEnd,
                offset: offset,
                width: 1);
            // We return a space as a default
            return DefaultCharacter;
        }
        // Valid in any string
        if (esc == 'u' && this.Peek(offset + 1) == '{')
        {
            offset += 2;
            // Parse hex unicode value
            var unicodeValue = 0;
            var length = 0;
            while (TryParseHexDigit(this.Peek(offset), out var digit))
            {
                unicodeValue = unicodeValue * 16 + digit;
                ++length;
                ++offset;
            }
            // Expect closing brace
            if (this.Peek(offset) == '}')
            {
                ++offset;
                if (length > 0)
                {
                    if (!Rune.IsValid(unicodeValue))
                    {
                        this.AddError(
                            SyntaxErrors.IllegalUnicodeCodepoint,
                            offset: escapeStart,
                            width: offset - escapeStart,
                            unicodeValue);
                        return DefaultCharacter;
                    }
                    return new(unicodeValue);
                }
                else
                {
                    this.AddError(
                        SyntaxErrors.ZeroLengthUnicodeCodepoint,
                        offset: escapeStart,
                        width: offset - escapeStart);
                    // We just return an empty character
                    return DefaultCharacter;
                }
            }
            else
            {
                this.AddError(
                    SyntaxErrors.UnclosedUnicodeCodepoint,
                    offset: offset,
                    width: 1);
                // We just return an empty character
                return DefaultCharacter;
            }
        }
        // Any single-character escape, find the escaped equivalent
        char? escaped = esc switch
        {
            '0' => '\0',
            'a' => '\a',
            'b' => '\b',
            'f' => '\f',
            'n' => '\n',
            'r' => '\r',
            't' => '\t',
            'v' => '\v',
            '\'' => '\'',
            '\"' => '\"',
            '\\' => '\\',
            _ => null,
        };
        if (escaped is not null)
        {
            ++offset;
            return new(escaped.Value);
        }
        else
        {
            this.AddError(
                SyntaxErrors.IllegalEscapeCharacter,
                offset: escapeStart,
                width: 2,
                args: esc);
            ++offset;
            return DefaultCharacter;
        }
    }

    /// <summary>
    /// Parses leading trivia into <see cref="leadingTriviaList"/>. Leading trivia is essentially all trivia
    /// tokens until the next significant token.
    /// </summary>
    private void ParseLeadingTriviaList()
    {
        while (this.TryParseTrivia(out var trivia)) this.tokenBuilder.LeadingTrivia.Add(trivia);
    }

    /// <summary>
    /// Parses trailing trivia into <see cref="trailingTriviaList"/>. Trailing trivia is essentially all trivia
    /// tokens until the next end of line, which is also included in the list.
    /// </summary>
    private void ParseTrailingTriviaList()
    {
        while (this.TryParseTrivia(out var trivia))
        {
            this.tokenBuilder.TrailingTrivia.Add(trivia);
            if (trivia.Kind == TriviaKind.Newline) break;
        }
    }

    /// <summary>
    /// Tries to parse a single trivia token.
    /// </summary>
    /// <param name="result">The parsed trivia token is written here.</param>
    /// <returns>True, if a trivia token was parsed.</returns>
    private bool TryParseTrivia([MaybeNullWhen(false)] out SyntaxTrivia result)
    {
        var ch = this.Peek();
        // Newline
        if (this.TryParseNewline(0, out var newlineLength))
        {
            if (this.CurrentMode.Kind == ModeKind.LineInterpolation)
            {
                // Illegal newline, we have to stop any nested interpolation and string lexing
                this.modeStack.Pop();
                while (this.CurrentMode.Kind != ModeKind.Normal) this.modeStack.Pop();
            }
            result = SyntaxTrivia.From(TriviaKind.Newline, this.AdvanceWithText(newlineLength));
            return true;
        }
        // Any horizontal whitespace
        if (IsSpace(ch))
        {
            // We merge it into one chunk to not produce so many individual tokens
            var offset = 1;
            while (IsSpace(this.Peek(offset))) ++offset;
            result = SyntaxTrivia.From(TriviaKind.Whitespace, this.AdvanceWithText(offset));
            return true;
        }
        // Comment
        if (ch == '/' && this.Peek(1) == '/')
        {
            // Line Comment
            var offset = 2;
            var commentKind = TriviaKind.LineComment;
            // Documentation Comment
            if (this.Peek(2) == '/')
            {
                offset = 3;
                commentKind = TriviaKind.DocumentationComment;
            }
            // NOTE: We use a little trick here, we specify a newline character as the default for Peek,
            // which means that this will terminate, even if the comment was on the last line of the file
            // without a line break
            while (!IsNewline(this.Peek(offset, @default: '\n'))) ++offset;
            result = SyntaxTrivia.From(commentKind, this.AdvanceWithText(offset));
            return true;
        }
        // Not trivia
        result = null;
        return false;
    }

    /// <summary>
    /// Attempts to parse a newline.
    /// </summary>
    /// <param name="offset">The offset to start parsing from.</param>
    /// <param name="length">The length of the newline in characters.</param>
    /// <returns>True, if a newline was parsed, false otherwise.</returns>
    private bool TryParseNewline(int offset, out int length)
    {
        var ch = this.SourceReader.Peek(offset);
        if (ch == '\r')
        {
            // OS-X 9 or Windows-style newline
            if (this.SourceReader.Peek(offset + 1) == '\n')
            {
                // Windows-style
                length = 2;
                return true;
            }
            // OS-X 9 style
            length = 1;
            return true;
        }
        if (ch == '\n')
        {
            // UNIX-style newline
            length = 1;
            return true;
        }
        // Not a newline
        length = 0;
        return false;
    }

    // Errors
    private void AddError(DiagnosticTemplate template, int offset, int width, params object?[] args)
    {
        var info = DiagnosticInfo.Create(template, args);
        var diag = new SyntaxDiagnosticInfo(info, Offset: offset, Width: width);
        this.diagnosticBuilder.Add(diag);
    }

    // Mode stack
    private void PushMode(ModeKind kind, int extendedDelims) => this.modeStack.Push(new(kind, extendedDelims));
    private void PopMode()
    {
        // We don't allow knocking off the lowest level mode
        if (this.modeStack.Count == 1) return;
        this.modeStack.Pop();
    }

    // Propagating functions to the source reader to decouple the API a bit, in case it changes
    // later for performance reasons
    private char Peek(int offset = 0, char @default = '\0') =>
        this.SourceReader.Peek(offset: offset, @default: @default);
    private bool TryPeek(int offset, out char result) =>
        this.SourceReader.TryPeek(offset, out result);
    private ReadOnlyMemory<char> Advance(int amount) => this.SourceReader.Advance(amount);
    private string AdvanceWithText(int amount) => this.Advance(amount).ToString();

    // Character categorization
    private static bool IsIdent(char ch) => char.IsLetterOrDigit(ch) || ch == '_';
    private static bool IsSpace(char ch) => char.IsWhiteSpace(ch) && !IsNewline(ch);
    private static bool IsNewline(char ch) => ch == '\r' || ch == '\n';
    private static bool IsDigit(char ch) => ch >= '0' && ch <= '9';
    private static bool TryParseHexDigit(char ch, out int value)
    {
        if (ch >= '0' && ch <= '9')
        {
            value = ch - '0';
            return true;
        }
        if (ch >= 'a' && ch <= 'f')
        {
            value = ch - 'a' + 10;
            return true;
        }
        if (ch >= 'A' && ch <= 'F')
        {
            value = ch - 'A' + 10;
            return true;
        }
        value = 0;
        return false;
    }

    private string ParseIntLiteral(int radix, out int value)
    {
        var offset = 0;
        value = 0;
        while (TryParseHexDigit(this.Peek(offset), out var digit))
        {
            value = value * radix + digit;
            offset++;
        }
        return this.AdvanceWithText(offset);
    }
}
