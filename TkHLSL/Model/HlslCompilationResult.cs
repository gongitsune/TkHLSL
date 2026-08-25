using TkHLSL.Diagnostics;

namespace TkHLSL.Model;

/// <summary>
///     The public result of <see cref="TkHLSL.HlslParser.Parse" />: every kernel found in the source,
///     each resolved to the resources it needs to be bound, plus every top-level resource declaration
///     (whether any kernel uses it — useful for unused-resource warnings) and every diagnostic
///     raised while lexing, preprocessing, parsing, and analyzing the source (see
///     docs/IMPLEMENTATION_PLAN.md §7.3).
/// </summary>
public sealed record HlslCompilationResult(
    IReadOnlyList<KernelBindingInfo> Kernels,
    IReadOnlyList<ResourceBinding> AllResources,
    IReadOnlyList<Diagnostic> Diagnostics);