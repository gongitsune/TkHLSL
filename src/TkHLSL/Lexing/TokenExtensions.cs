using TkHLSL.Text;

namespace TkHLSL.Lexing;

/// <summary>
///     Opt-in text extraction for tokens. Kept separate from <see cref="Token" /> itself so that
///     tokenizing a source does not pay for a substring allocation per token; callers materialize
///     text only for the tokens they actually need to inspect (e.g. an identifier name).
/// </summary>
public static class TokenExtensions
{
    extension(Token token)
    {
        /// <summary>
        ///     Zero-allocation view of the token's text. Prefer this over <see cref="GetText" />
        ///     for comparisons (e.g. against a keyword) that don't need an actual <see cref="string" />.
        /// </summary>
        public ReadOnlySpan<char> GetSpan(string source)
        {
            return source.AsSpan(token.Span.Start, token.Span.Length);
        }

        public string GetText(string source)
        {
            return source.Substring(token.Span.Start, token.Span.Length);
        }

        /// <summary>Zero-allocation view of the token's text within a composite <see cref="SourceText" />.</summary>
        public ReadOnlySpan<char> GetSpan(SourceText source)
        {
            return source.Slice(token.Span);
        }

        public string GetText(SourceText source)
        {
            return source.Slice(token.Span).ToString();
        }
    }
}