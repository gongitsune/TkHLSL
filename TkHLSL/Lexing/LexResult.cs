using TkHLSL.Diagnostics;

namespace TkHLSL.Lexing;

/// <summary>
/// The output of <see cref="Lexer.Tokenize"/>: the token stream plus any lexing diagnostics.
/// </summary>
public readonly struct LexResult(IReadOnlyList<Token> tokens, IReadOnlyList<Diagnostic> diagnostics)
{
    public IReadOnlyList<Token> Tokens { get; } = tokens;

    public IReadOnlyList<Diagnostic> Diagnostics { get; } = diagnostics;
}
