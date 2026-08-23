using TkHLSL.Text;

namespace TkHLSL.Lexing;

/// <summary>
/// A single lexical token: a <see cref="TokenKind"/> plus the span of source text it covers.
/// Token text is never materialized during lexing — callers slice the original source string via
/// <see cref="Span"/> on demand, so tokenizing does not allocate a string per token.
/// </summary>
public readonly struct Token
{
    public Token(TokenKind kind, TextSpan span)
    {
        Kind = kind;
        Span = span;
    }

    public TokenKind Kind { get; }

    public TextSpan Span { get; }

    public override string ToString() => $"{Kind}{Span}";
}
