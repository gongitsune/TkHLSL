using TkHLSL.Diagnostics;
using TkHLSL.Text;

namespace TkHLSL.Lexing;

/// <summary>
/// Converts HLSL source text into a flat token stream in a single forward pass.
/// </summary>
/// <remarks>
/// Allocation policy: tokens carry only a <see cref="TokenKind"/> and a <see cref="TextSpan"/>
/// (two ints) — no substring is materialized per token, so the scan itself allocates nothing
/// beyond the backing storage for the returned token/diagnostic lists. The scan runs over a
/// <see cref="ReadOnlySpan{T}"/> view of the source and uses its vectorized
/// <see cref="MemoryExtensions.IndexOf(ReadOnlySpan{char}, char)"/>/<c>IndexOfAny</c> overloads to
/// jump straight to the next delimiter when scanning comments and string literals, instead of
/// testing one character at a time.
/// </remarks>
public static class Lexer
{
    public static LexResult Tokenize(string source)
    {
        if (source is null)
        {
            throw new ArgumentNullException(nameof(source));
        }

        ReadOnlySpan<char> span = source.AsSpan();
        var tokens = new List<Token>(Math.Max(16, span.Length / 3));
        List<Diagnostic>? diagnostics = null;
        int length = span.Length;
        int pos = 0;

        while (pos < length)
        {
            char c = span[pos];
            switch (c)
            {
                case ' ':
                case '\t':
                case '\r':
                    pos++;
                    continue;
                case '\n':
                    tokens.Add(new Token(TokenKind.NewLine, new TextSpan(pos, 1)));
                    pos++;
                    continue;
                case '\\':
                    if (TryConsumeLineContinuation(span, ref pos))
                    {
                        continue;
                    }
                    tokens.Add(new Token(TokenKind.Error, new TextSpan(pos, 1)));
                    AddDiagnostic(ref diagnostics, $"予期しない文字 '{c}' です。", pos, 1);
                    pos++;
                    continue;
                case '#':
                    tokens.Add(Single(TokenKind.Hash, pos));
                    pos++;
                    continue;
                case '"':
                    tokens.Add(ScanString(span, ref pos, ref diagnostics));
                    continue;
                case '/':
                    tokens.Add(ScanSlashOrComment(span, ref pos, ref diagnostics));
                    continue;
                case '(':
                    tokens.Add(Single(TokenKind.OpenParen, pos));
                    pos++;
                    continue;
                case ')':
                    tokens.Add(Single(TokenKind.CloseParen, pos));
                    pos++;
                    continue;
                case '{':
                    tokens.Add(Single(TokenKind.OpenBrace, pos));
                    pos++;
                    continue;
                case '}':
                    tokens.Add(Single(TokenKind.CloseBrace, pos));
                    pos++;
                    continue;
                case '[':
                    tokens.Add(Single(TokenKind.OpenBracket, pos));
                    pos++;
                    continue;
                case ']':
                    tokens.Add(Single(TokenKind.CloseBracket, pos));
                    pos++;
                    continue;
                case ',':
                    tokens.Add(Single(TokenKind.Comma, pos));
                    pos++;
                    continue;
                case ';':
                    tokens.Add(Single(TokenKind.Semicolon, pos));
                    pos++;
                    continue;
                case ':':
                    tokens.Add(Single(TokenKind.Colon, pos));
                    pos++;
                    continue;
                case '?':
                    tokens.Add(Single(TokenKind.Question, pos));
                    pos++;
                    continue;
                case '~':
                    tokens.Add(Single(TokenKind.Tilde, pos));
                    pos++;
                    continue;
                case '.':
                    if (IsDigit(PeekAt(span, pos, 1)))
                    {
                        tokens.Add(ScanNumber(span, ref pos));
                    }
                    else
                    {
                        tokens.Add(Single(TokenKind.Dot, pos));
                        pos++;
                    }
                    continue;
                case '+':
                    tokens.Add(ScanCompound(span, ref pos, '+', TokenKind.Increment, TokenKind.PlusAssign, TokenKind.Plus));
                    continue;
                case '-':
                    tokens.Add(ScanCompound(span, ref pos, '-', TokenKind.Decrement, TokenKind.MinusAssign, TokenKind.Minus));
                    continue;
                case '&':
                    tokens.Add(ScanCompound(span, ref pos, '&', TokenKind.AmpAmp, TokenKind.AmpAssign, TokenKind.Amp));
                    continue;
                case '|':
                    tokens.Add(ScanCompound(span, ref pos, '|', TokenKind.PipePipe, TokenKind.PipeAssign, TokenKind.Pipe));
                    continue;
                case '*':
                    tokens.Add(ScanMaybeAssign(span, ref pos, TokenKind.StarAssign, TokenKind.Star));
                    continue;
                case '%':
                    tokens.Add(ScanMaybeAssign(span, ref pos, TokenKind.PercentAssign, TokenKind.Percent));
                    continue;
                case '=':
                    tokens.Add(ScanMaybeAssign(span, ref pos, TokenKind.Equal, TokenKind.Assign));
                    continue;
                case '!':
                    tokens.Add(ScanMaybeAssign(span, ref pos, TokenKind.NotEqual, TokenKind.Bang));
                    continue;
                case '^':
                    tokens.Add(ScanMaybeAssign(span, ref pos, TokenKind.CaretAssign, TokenKind.Caret));
                    continue;
                case '<':
                    tokens.Add(ScanShift(span, ref pos, '<', TokenKind.LessEqual, TokenKind.LeftShift, TokenKind.LeftShiftAssign, TokenKind.Less));
                    continue;
                case '>':
                    tokens.Add(ScanShift(span, ref pos, '>', TokenKind.GreaterEqual, TokenKind.RightShift, TokenKind.RightShiftAssign, TokenKind.Greater));
                    continue;
                default:
                    if (IsIdentifierStart(c))
                    {
                        tokens.Add(ScanIdentifier(span, ref pos));
                    }
                    else if (IsDigit(c))
                    {
                        tokens.Add(ScanNumber(span, ref pos));
                    }
                    else
                    {
                        tokens.Add(new Token(TokenKind.Error, new TextSpan(pos, 1)));
                        AddDiagnostic(ref diagnostics, $"予期しない文字 '{c}' です。", pos, 1);
                        pos++;
                    }
                    continue;
            }
        }

        tokens.Add(new Token(TokenKind.EndOfFile, new TextSpan(length, 0)));

        IReadOnlyList<Diagnostic> diagnosticList = diagnostics is null
            ? Array.Empty<Diagnostic>()
            : diagnostics;
        return new LexResult(tokens, diagnosticList);
    }

    private static char PeekAt(ReadOnlySpan<char> source, int pos, int offset)
    {
        int i = pos + offset;
        return i < source.Length ? source[i] : '\0';
    }

    private static Token Single(TokenKind kind, int pos) => new(kind, new TextSpan(pos, 1));

    private static bool TryConsumeLineContinuation(ReadOnlySpan<char> source, ref int pos)
    {
        char next = PeekAt(source, pos, 1);
        if (next == '\n')
        {
            pos += 2;
            return true;
        }

        if (next == '\r' && PeekAt(source, pos, 2) == '\n')
        {
            pos += 3;
            return true;
        }

        return false;
    }

    /// <summary>
    /// Handles operators of the shape "cc" (doubled), "c=" (compound assign), or "c" (plain) —
    /// covers <c>+ ++ +=</c>, <c>- -- -=</c>, <c>&amp; &amp;&amp; &amp;=</c>, <c>| || |=</c>.
    /// </summary>
    private static Token ScanCompound(ReadOnlySpan<char> source, ref int pos, char self, TokenKind doubled, TokenKind assign, TokenKind plain)
    {
        char next = PeekAt(source, pos, 1);
        if (next == self)
        {
            var token = new Token(doubled, new TextSpan(pos, 2));
            pos += 2;
            return token;
        }

        if (next == '=')
        {
            var token = new Token(assign, new TextSpan(pos, 2));
            pos += 2;
            return token;
        }

        var single = Single(plain, pos);
        pos += 1;
        return single;
    }

    /// <summary>Handles operators of the shape "c=" or "c" — covers <c>* % = ! ^</c>.</summary>
    private static Token ScanMaybeAssign(ReadOnlySpan<char> source, ref int pos, TokenKind assignKind, TokenKind plainKind)
    {
        if (PeekAt(source, pos, 1) == '=')
        {
            var token = new Token(assignKind, new TextSpan(pos, 2));
            pos += 2;
            return token;
        }

        var single = Single(plainKind, pos);
        pos += 1;
        return single;
    }

    /// <summary>Handles shift-capable operators — covers <c>&lt; &lt;= &lt;&lt; &lt;&lt;=</c> and <c>&gt; &gt;= &gt;&gt; &gt;&gt;=</c>.</summary>
    private static Token ScanShift(ReadOnlySpan<char> source, ref int pos, char self, TokenKind equalKind, TokenKind repeatKind, TokenKind repeatAssignKind, TokenKind plainKind)
    {
        char next = PeekAt(source, pos, 1);
        if (next == '=')
        {
            var token = new Token(equalKind, new TextSpan(pos, 2));
            pos += 2;
            return token;
        }

        if (next == self)
        {
            if (PeekAt(source, pos, 2) == '=')
            {
                var assignToken = new Token(repeatAssignKind, new TextSpan(pos, 3));
                pos += 3;
                return assignToken;
            }

            var repeatToken = new Token(repeatKind, new TextSpan(pos, 2));
            pos += 2;
            return repeatToken;
        }

        var single = Single(plainKind, pos);
        pos += 1;
        return single;
    }

    private static Token ScanSlashOrComment(ReadOnlySpan<char> source, ref int pos, ref List<Diagnostic>? diagnostics)
    {
        char next = PeekAt(source, pos, 1);
        if (next == '/')
        {
            int start = pos;
            int contentStart = pos + 2;
            int relativeNewLine = source.Slice(contentStart).IndexOf('\n');
            pos = relativeNewLine < 0 ? source.Length : contentStart + relativeNewLine;
            return new Token(TokenKind.LineComment, new TextSpan(start, pos - start));
        }

        if (next == '*')
        {
            return ScanBlockComment(source, ref pos, ref diagnostics);
        }

        return ScanMaybeAssign(source, ref pos, TokenKind.SlashAssign, TokenKind.Slash);
    }

    private static Token ScanBlockComment(ReadOnlySpan<char> source, ref int pos, ref List<Diagnostic>? diagnostics)
    {
        int start = pos;
        int contentStart = pos + 2; // skip "/*"
        int relativeEnd = source.Slice(contentStart).IndexOf("*/".AsSpan());
        if (relativeEnd >= 0)
        {
            pos = contentStart + relativeEnd + 2;
            return new Token(TokenKind.BlockComment, new TextSpan(start, pos - start));
        }

        pos = source.Length;
        AddDiagnostic(ref diagnostics, "未終端のブロックコメントです。", start, pos - start);
        return new Token(TokenKind.Error, new TextSpan(start, pos - start));
    }

    private static Token ScanString(ReadOnlySpan<char> source, ref int pos, ref List<Diagnostic>? diagnostics)
    {
        int start = pos;
        int length = source.Length;
        pos++; // skip opening quote

        while (pos < length)
        {
            int relative = source.Slice(pos).IndexOfAny('"', '\\', '\n');
            if (relative < 0)
            {
                pos = length;
                break;
            }

            pos += relative;
            char found = source[pos];
            if (found == '"')
            {
                pos++;
                return new Token(TokenKind.StringLiteral, new TextSpan(start, pos - start));
            }

            if (found == '\n')
            {
                break;
            }

            // found == '\\': skip the escaped character (if any) and keep scanning.
            pos += pos + 1 < length ? 2 : 1;
        }

        AddDiagnostic(ref diagnostics, "未終端の文字列リテラルです。", start, pos - start);
        return new Token(TokenKind.Error, new TextSpan(start, pos - start));
    }

    private static Token ScanIdentifier(ReadOnlySpan<char> source, ref int pos)
    {
        int start = pos;
        int length = source.Length;
        pos++;
        while (pos < length && IsIdentifierPart(source[pos]))
        {
            pos++;
        }

        return new Token(TokenKind.Identifier, new TextSpan(start, pos - start));
    }

    private static Token ScanNumber(ReadOnlySpan<char> source, ref int pos)
    {
        int start = pos;
        int length = source.Length;
        bool isFloat = false;

        if (source[pos] == '0' && (PeekAt(source, pos, 1) is 'x' or 'X'))
        {
            pos += 2;
            while (pos < length && IsHexDigit(source[pos]))
            {
                pos++;
            }

            if (pos < length && (source[pos] is 'u' or 'U'))
            {
                pos++;
            }

            return new Token(TokenKind.IntLiteral, new TextSpan(start, pos - start));
        }

        while (pos < length && IsDigit(source[pos]))
        {
            pos++;
        }

        if (pos < length && source[pos] == '.')
        {
            isFloat = true;
            pos++;
            while (pos < length && IsDigit(source[pos]))
            {
                pos++;
            }
        }

        if (pos < length && (source[pos] is 'e' or 'E'))
        {
            int expDigitsStart = pos + 1;
            if (expDigitsStart < length && (source[expDigitsStart] is '+' or '-'))
            {
                expDigitsStart++;
            }

            if (expDigitsStart < length && IsDigit(source[expDigitsStart]))
            {
                isFloat = true;
                pos = expDigitsStart;
                while (pos < length && IsDigit(source[pos]))
                {
                    pos++;
                }
            }
        }

        if (pos < length && (source[pos] is 'f' or 'F' or 'h' or 'H'))
        {
            isFloat = true;
            pos++;
        }
        else if (!isFloat && pos < length && (source[pos] is 'u' or 'U'))
        {
            pos++;
        }

        return new Token(isFloat ? TokenKind.FloatLiteral : TokenKind.IntLiteral, new TextSpan(start, pos - start));
    }

    private static bool IsIdentifierStart(char c) => c == '_' || (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z');

    private static bool IsIdentifierPart(char c) => IsIdentifierStart(c) || IsDigit(c);

    private static bool IsDigit(char c) => c >= '0' && c <= '9';

    private static bool IsHexDigit(char c) => IsDigit(c) || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F');

    private static void AddDiagnostic(ref List<Diagnostic>? diagnostics, string message, int start, int length)
    {
        diagnostics ??= new List<Diagnostic>();
        diagnostics.Add(new Diagnostic(DiagnosticSeverity.Error, message, new TextSpan(start, length)));
    }
}
