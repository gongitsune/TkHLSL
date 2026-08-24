namespace TkHLSL.Preprocessing;

/// <summary>
/// Options controlling <see cref="Preprocessor.Process"/>: which symbols count as defined for
/// <c>#if</c>/<c>#ifdef</c>/<c>#ifndef</c> resolution, and how <c>#include</c> targets are resolved.
/// </summary>
public sealed class HlslParseOptions
{
    public HlslParseOptions(IEnumerable<string>? definedSymbols = null, IIncludeResolver? includeResolver = null)
    {
        DefinedSymbols = definedSymbols is null
            ? new HashSet<string>(StringComparer.Ordinal)
            : new HashSet<string>(definedSymbols, StringComparer.Ordinal);
        IncludeResolver = includeResolver;
    }

    /// <summary>
    /// Symbols considered defined for conditional-compilation resolution. Empty by default, so a
    /// bare <c>Parse</c> call analyzes only the "no variant defined" branch of any
    /// <c>#if</c>/<c>#ifdef</c>/<c>#ifndef</c> (see docs/IMPLEMENTATION_PLAN.md §9 Phase 2).
    /// </summary>
    public ISet<string> DefinedSymbols { get; }

    public IIncludeResolver? IncludeResolver { get; }
}
