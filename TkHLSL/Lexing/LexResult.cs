using TkHLSL.Diagnostics;

namespace TkHLSL.Lexing;

/// <summary>
/// The output of <see cref="Lexer.Tokenize"/>: the token stream plus any lexing diagnostics.
/// </summary>
public readonly struct LexResult
{
    public LexResult(IReadOnlyList<Token> tokens, IReadOnlyList<Diagnostic> diagnostics)
    {
        Tokens = tokens;
        Diagnostics = diagnostics;
    }

    public IReadOnlyList<Token> Tokens { get; }

    public IReadOnlyList<Diagnostic> Diagnostics { get; }
}
