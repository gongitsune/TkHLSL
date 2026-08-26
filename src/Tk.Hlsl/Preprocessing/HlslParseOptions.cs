namespace Tk.Hlsl.Preprocessing;

/// <summary>
///     Options controlling <see cref="Preprocessor.Process" />: which symbols count as defined for
///     <c>#if</c>/<c>#ifdef</c>/<c>#ifndef</c> resolution, and how <c>#include</c> targets are resolved.
/// </summary>
public sealed class HlslParseOptions(
    IEnumerable<string>? definedSymbols = null,
    IIncludeResolver? includeResolver = null,
    string? sourcePath = null,
    int maxIncludeDepth = 32)
{
    /// <summary>
    ///     Symbols considered defined for conditional-compilation resolution. Empty by default, so a
    ///     bare <c>Parse</c> call analyzes only the "no variant defined" branch of any
    ///     <c>#if</c>/<c>#ifdef</c>/<c>#ifndef</c> (see docs/IMPLEMENTATION_PLAN.md §9 Phase 2).
    /// </summary>
    public ISet<string> DefinedSymbols { get; } = definedSymbols is null
        ? new HashSet<string>(StringComparer.Ordinal)
        : new HashSet<string>(definedSymbols, StringComparer.Ordinal);

    public IIncludeResolver? IncludeResolver { get; } = includeResolver;

    /// <summary>
    ///     Identity of the root source, passed to <see cref="IIncludeResolver.TryResolve" /> as
    ///     <c>includerPath</c> for any top-level <c>#include</c>, and used as the root's
    ///     <see cref="Text.SourceSegment.Path" />. <see langword="null"/> (the default) means the root has
    ///     no identity of its own — hosts resolving includes relative to it should treat that as "resolve
    ///     relative to some project-defined base".
    /// </summary>
    public string? SourcePath { get; } = sourcePath;

    /// <summary>
    ///     Maximum <c>#include</c> nesting depth before <see cref="Preprocessor.Process" /> stops recursing
    ///     and reports a diagnostic instead, guarding against runaway or maliciously deep include chains.
    ///     Clamped to at least 1.
    /// </summary>
    public int MaxIncludeDepth { get; } = maxIncludeDepth < 1 ? 1 : maxIncludeDepth;
}