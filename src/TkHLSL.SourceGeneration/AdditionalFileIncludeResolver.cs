using TkHLSL.Preprocessing;

namespace TkHLSL.SourceGeneration;

/// <summary>
///     An <see cref="IIncludeResolver" /> backed entirely by the Roslyn <c>AdditionalFiles</c> the
///     generator was given — never touches disk. Resolves <c>#include "..."</c> the same way the
///     top-level <c>[ComputeShaderBinding]</c> path is matched: by segment-wise suffix against every
///     known additional file's normalized (<c>/</c>-separated) path, relative to the includer when one
///     is known (see docs/IMPLEMENTATION_PLAN.md §9 Phase 9.2).
/// </summary>
internal sealed class AdditionalFileIncludeResolver : IIncludeResolver
{
    private readonly IReadOnlyDictionary<string, string> _filesByNormalizedPath;

    /// <param name="filesByNormalizedPath">Every additional file's normalized path mapped to its text.</param>
    public AdditionalFileIncludeResolver(IReadOnlyDictionary<string, string> filesByNormalizedPath)
    {
        _filesByNormalizedPath = filesByNormalizedPath;
    }

    public bool TryResolve(string requestedPath, string? includerPath, out string? resolvedPath, out string? content)
    {
        var normalizedRequest = PathMatching.Normalize(requestedPath);

        // Prefer a match relative to the includer's directory first (mirrors a real filesystem's
        // #include "relative/path" resolution), then fall back to a suffix match against every
        // additional file — the same rule ComputeShaderBindingGenerator uses to resolve the
        // top-level [ComputeShaderBinding] path.
        if (includerPath is not null)
        {
            var includerDir = PathMatching.DirectoryOf(PathMatching.Normalize(includerPath));
            var joined = PathMatching.Join(includerDir, normalizedRequest);
            if (_filesByNormalizedPath.TryGetValue(joined, out var contentAtJoined))
            {
                resolvedPath = joined;
                content = contentAtJoined;
                return true;
            }
        }

        var match = PathMatching.FindBySuffix(_filesByNormalizedPath.Keys, normalizedRequest);
        if (match is { } path)
        {
            resolvedPath = path;
            content = _filesByNormalizedPath[path];
            return true;
        }

        resolvedPath = null;
        content = null;
        return false;
    }
}
