using Tk.Hlsl.Diagnostics;
using Tk.Hlsl.Lexing;
using Tk.Hlsl.Text;

namespace Tk.Hlsl.Preprocessing;

/// <summary>
///     The output of <see cref="Preprocessor.Process" />: the macro-expanded, conditional-compilation-
///     resolved, include-expanded token stream ready for the Phase 3 parser, the composite
///     <see cref="SourceText" /> every <see cref="Tokens" /> span is an offset into, plus <c>#pragma kernel</c>
///     name candidates and any preprocessing diagnostics.
/// </summary>
/// <remarks>
///     <see cref="Tokens" /> contains only code tokens and a trailing <see cref="TokenKind.EndOfFile" />
///     — directive lines, comments, and newlines are all consumed during preprocessing and never
///     appear in the output, so the Phase 3 parser does not need to skip trivia itself.
/// </remarks>
public readonly struct PreprocessResult(
    IReadOnlyList<Token> tokens,
    IReadOnlyList<string> kernelNames,
    IReadOnlyList<Diagnostic> diagnostics,
    SourceText? source = null)
{
    public IReadOnlyList<Token> Tokens { get; } = tokens;

    /// <summary>
    ///     Kernel names collected from <c>#pragma kernel Name</c> lines, in source order. Whether each
    ///     name actually corresponds to a <c>[numthreads]</c> function is verified later, in Phase 3.
    /// </summary>
    public IReadOnlyList<string> KernelNames { get; } = kernelNames;

    public IReadOnlyList<Diagnostic> Diagnostics { get; } = diagnostics;

    /// <summary>
    ///     The composite source every span in <see cref="Tokens" /> and <see cref="Diagnostics" /> is an
    ///     offset into — the root source with every resolved <c>#include</c> spliced in. When the source had
    ///     no includes, <see cref="Text.SourceText.Text" /> is the original root string itself.
    /// </summary>
    public SourceText Source { get; } = source ?? SourceText.FromRoot(string.Empty);
}
