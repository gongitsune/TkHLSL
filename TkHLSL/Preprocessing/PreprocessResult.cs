using TkHLSL.Diagnostics;
using TkHLSL.Lexing;

namespace TkHLSL.Preprocessing;

/// <summary>
/// The output of <see cref="Preprocessor.Process"/>: the macro-expanded, conditional-compilation-
/// resolved token stream ready for the Phase 3 parser, plus <c>#pragma kernel</c> name candidates
/// and any preprocessing diagnostics.
/// </summary>
/// <remarks>
/// <see cref="Tokens"/> contains only code tokens and a trailing <see cref="TokenKind.EndOfFile"/>
/// — directive lines, comments, and newlines are all consumed during preprocessing and never
/// appear in the output, so the Phase 3 parser does not need to skip trivia itself.
/// </remarks>
public readonly struct PreprocessResult
{
    public PreprocessResult(IReadOnlyList<Token> tokens, IReadOnlyList<string> kernelNames, IReadOnlyList<Diagnostic> diagnostics)
    {
        Tokens = tokens;
        KernelNames = kernelNames;
        Diagnostics = diagnostics;
    }

    public IReadOnlyList<Token> Tokens { get; }

    /// <summary>
    /// Kernel names collected from <c>#pragma kernel Name</c> lines, in source order. Whether each
    /// name actually corresponds to a <c>[numthreads]</c> function is verified later, in Phase 3.
    /// </summary>
    public IReadOnlyList<string> KernelNames { get; }

    public IReadOnlyList<Diagnostic> Diagnostics { get; }
}
