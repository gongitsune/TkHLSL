namespace Tk.Hlsl.SourceGeneration;

/// <summary>
///     The one path-matching rule used everywhere this generator needs to line a requested path (an
///     attribute's <c>path</c> argument, or an <c>#include "..."</c> target) up against an
///     <c>AdditionalText</c>'s real path: normalize separators to <c>/</c>, then match by segment-wise
///     suffix so a short, project-relative path (<c>"Shaders/Blur.compute"</c>) matches a longer,
///     absolute one (<c>C:\Proj\Assets\Shaders\Blur.compute</c>) without false positives on a partial
///     path segment (<c>"ur.compute"</c> must not match) — see docs/IMPLEMENTATION_PLAN.md §9 Phase 9.2.
/// </summary>
internal static class PathMatching
{
    public static string Normalize(string path)
    {
        return path.Replace('\\', '/');
    }

    /// <summary>
    ///     Every <paramref name="candidates" /> whose path ends with <paramref name="requestedPath" /> at
    ///     a <c>/</c> boundary (or equals it exactly), in the order given.
    /// </summary>
    public static IReadOnlyList<string> FindAllBySuffix(IEnumerable<string> candidates, string requestedPath)
    {
        var normalizedRequest = Normalize(requestedPath);
        var matches = new List<string>();
        foreach (var candidate in candidates)
            if (IsSuffixMatch(candidate, normalizedRequest))
                matches.Add(candidate);

        return matches;
    }

    /// <summary>The single match, or <see langword="null" /> if zero or more than one candidate matches.</summary>
    public static string? FindBySuffix(IEnumerable<string> candidates, string requestedPath)
    {
        var matches = FindAllBySuffix(candidates, requestedPath);
        return matches.Count == 1 ? matches[0] : null;
    }

    private static bool IsSuffixMatch(string candidate, string normalizedRequest)
    {
        if (candidate.Length == normalizedRequest.Length)
            return string.Equals(candidate, normalizedRequest, StringComparison.Ordinal);

        if (candidate.Length < normalizedRequest.Length) return false;

        return candidate.EndsWith(normalizedRequest, StringComparison.Ordinal) &&
               candidate[candidate.Length - normalizedRequest.Length - 1] == '/';
    }

    public static string DirectoryOf(string normalizedPath)
    {
        var slash = normalizedPath.LastIndexOf('/');
        return slash < 0 ? string.Empty : normalizedPath.Substring(0, slash);
    }

    /// <summary>Joins <paramref name="baseDir" /> and <paramref name="relativePath" />, resolving <c>./</c> and <c>../</c> segments.</summary>
    public static string Join(string baseDir, string relativePath)
    {
        var segments = new List<string>(baseDir.Length == 0 ? Array.Empty<string>() : baseDir.Split('/'));
        foreach (var segment in relativePath.Split('/'))
        {
            if (segment.Length == 0 || segment == ".") continue;

            if (segment == "..")
            {
                if (segments.Count > 0) segments.RemoveAt(segments.Count - 1);
                continue;
            }

            segments.Add(segment);
        }

        return string.Join("/", segments);
    }
}
