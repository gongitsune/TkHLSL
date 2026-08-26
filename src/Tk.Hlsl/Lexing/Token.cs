using Tk.Hlsl.Text;

namespace Tk.Hlsl.Lexing;

/// <summary>
/// A single lexical token: a <see cref="TokenKind"/> plus the span of source text it covers.
/// Token text is never materialized during lexing — callers slice the original source string via
/// <see cref="Span"/> on demand, so tokenizing does not allocate a string per token.
/// </summary>
public readonly struct Token(TokenKind kind, TextSpan span)
{
    public TokenKind Kind { get; } = kind;

    public TextSpan Span { get; } = span;

    public override string ToString() => $"{Kind}{Span}";
}
