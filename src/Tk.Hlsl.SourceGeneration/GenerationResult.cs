namespace Tk.Hlsl.SourceGeneration;

/// <summary>
///     One <c>[ComputeShaderBinding]</c> target's fully-computed generation output: the hint name and
///     generated source (or a <see langword="null" /> source when generation was skipped — no matching
///     file, an ambiguous match, a non-partial type, or an HLSL parse error), plus every diagnostic to
///     report. Computed once, in the incremental pipeline's cached <c>Select</c> step
///     (see docs/IMPLEMENTATION_PLAN.md §9 Phase 9.2) — <see cref="Tk.Hlsl.SourceGeneration.Generator.ComputeShaderBindingGenerator" />'s
///     <c>RegisterSourceOutput</c> callback only replays this record's contents, it does no computation
///     of its own.
/// </summary>
internal sealed record GenerationResult(
    string HintName,
    string? Source,
    EquatableArray<EmitDiagnosticInfo> Diagnostics);

/// <summary>
///     One diagnostic to report, in a form that survives incremental-generator caching: a descriptor id
///     (e.g. <c>"TKH0001"</c>) looked up against
///     <see cref="Tk.Hlsl.SourceGeneration.Diagnostics.TkHlslDiagnostics.ById" /> plus plain-value message
///     arguments and location, rather than a <see cref="Microsoft.CodeAnalysis.Diagnostic" /> itself
///     (which does not compare structurally).
/// </summary>
internal readonly record struct EmitDiagnosticInfo(
    string DescriptorId,
    EquatableArray<string> MessageArgs,
    string FilePath,
    LinePositionSpanInfo Span);
