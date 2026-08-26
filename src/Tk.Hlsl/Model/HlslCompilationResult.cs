using Tk.Hlsl.Diagnostics;
using Tk.Hlsl.Text;

namespace Tk.Hlsl.Model;

/// <summary>
///     The public result of <see cref="Tk.Hlsl.HlslParser.Parse" />: every kernel found in the source,
///     each resolved to the resources it needs to be bound, plus every top-level resource declaration
///     (whether any kernel uses it — useful for unused-resource warnings) and every diagnostic
///     raised while lexing, preprocessing, parsing, and analyzing the source (see
///     docs/IMPLEMENTATION_PLAN.md §7.3).
/// </summary>
public sealed record HlslCompilationResult(
    IReadOnlyList<KernelBindingInfo> Kernels,
    IReadOnlyList<ResourceBinding> AllResources,
    IReadOnlyList<Diagnostic> Diagnostics,
    IReadOnlyList<HlslStruct> Structs = null!)
{
    /// <summary>Every <c>struct</c> declaration found in the source, in declaration order.</summary>
    public IReadOnlyList<HlslStruct> Structs { get; init; } = Structs ?? [];

    /// <summary>
    ///     The composite source every <see cref="TextSpan" /> in this result (every
    ///     <see cref="Ir.TokenRange" />-derived <c>Location</c> and every <see cref="Diagnostic.Span" />) is
    ///     an offset into — the root source with every resolved <c>#include</c> spliced in. Use
    ///     <see cref="Text.SourceText.TryGetLocation" /> to map a span back to the file it came from.
    /// </summary>
    public SourceText Source { get; init; } = SourceText.FromRoot(string.Empty);
}