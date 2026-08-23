namespace TkHLSL.Lexing;

/// <summary>
/// Opt-in text extraction for tokens. Kept separate from <see cref="Token"/> itself so that
/// tokenizing a source does not pay for a substring allocation per token; callers materialize
/// text only for the tokens they actually need to inspect (e.g. an identifier name).
/// </summary>
public static class TokenExtensions
{
    /// <summary>Zero-allocation view of the token's text. Prefer this over <see cref="GetText"/>
    /// for comparisons (e.g. against a keyword) that don't need an actual <see cref="string"/>.</summary>
    public static ReadOnlySpan<char> GetSpan(this Token token, string source) =>
        source.AsSpan(token.Span.Start, token.Span.Length);

    public static string GetText(this Token token, string source) =>
        source.Substring(token.Span.Start, token.Span.Length);
}
