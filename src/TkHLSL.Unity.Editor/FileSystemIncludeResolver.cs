using TkHLSL.Preprocessing;

namespace TkHLSL.Unity.Editor;

/// <summary>
///     An <see cref="IIncludeResolver" /> backed by a real filesystem — used by
///     <see cref="ShaderManifestBuilder" /> to resolve a <c>.compute</c>'s <c>#include</c> closure at
///     Unity Editor import time. Resolution order: relative to the includer's directory (mirrors a
///     real compiler's <c>#include "relative/path"</c>), then relative to the project root, then by
///     bare filename against a project-wide index — the same fallback shape
///     <c>AdditionalFileIncludeResolver</c> gives the generator, since a shader commonly
///     <c>#include</c>s a file that lives in neither the includer's own folder nor the project root
///     (e.g. a shared <c>ShaderLibrary/</c>). See docs/IMPLEMENTATION_PLAN.md, "csc.rsp を廃止し..."
///     plan §3.
/// </summary>
public sealed class FileSystemIncludeResolver : IIncludeResolver
{
    private readonly Func<string, bool> _fileExists;
    private readonly Func<string, string?> _readFile;
    private readonly IReadOnlyDictionary<string, string> _filenameIndex;
    private readonly HashSet<string> _visited = new(StringComparer.Ordinal);

    /// <param name="fileExists">Whether a project-root-relative path exists.</param>
    /// <param name="readFile">Reads a project-root-relative path's contents, or <see langword="null" /> if it cannot be read.</param>
    /// <param name="filenameIndex">Every candidate file's bare filename mapped to one project-root-relative path — the last-resort fallback when neither includer-relative nor root-relative resolution finds a match.</param>
    public FileSystemIncludeResolver(Func<string, bool> fileExists, Func<string, string?> readFile,
        IReadOnlyDictionary<string, string> filenameIndex)
    {
        _fileExists = fileExists;
        _readFile = readFile;
        _filenameIndex = filenameIndex;
    }

    /// <summary>Every file actually resolved so far, in first-seen order — becomes a manifest's <c>input</c> list once the root is added by the caller.</summary>
    public IReadOnlyCollection<string> Resolved => _visited;

    public bool TryResolve(string requestedPath, string? includerPath, out string? resolvedPath, out string? content)
    {
        var normalizedRequest = Normalize(requestedPath);

        if (includerPath is not null)
        {
            var includerDir = DirectoryOf(Normalize(includerPath));
            var joined = Join(includerDir, normalizedRequest);
            if (TryReadExisting(joined, out content))
            {
                resolvedPath = joined;
                _visited.Add(joined);
                return true;
            }
        }

        if (TryReadExisting(normalizedRequest, out content))
        {
            resolvedPath = normalizedRequest;
            _visited.Add(normalizedRequest);
            return true;
        }

        var bareName = normalizedRequest.Substring(normalizedRequest.LastIndexOf('/') + 1);
        if (_filenameIndex.TryGetValue(bareName, out var indexed) && TryReadExisting(indexed, out content))
        {
            resolvedPath = indexed;
            _visited.Add(indexed);
            return true;
        }

        resolvedPath = null;
        content = null;
        return false;
    }

    private bool TryReadExisting(string path, out string? content)
    {
        if (_fileExists(path))
        {
            content = _readFile(path);
            return content is not null;
        }

        content = null;
        return false;
    }

    internal static string Normalize(string path)
    {
        return path.Replace('\\', '/');
    }

    private static string DirectoryOf(string normalizedPath)
    {
        var slash = normalizedPath.LastIndexOf('/');
        return slash < 0 ? string.Empty : normalizedPath.Substring(0, slash);
    }

    private static string Join(string baseDir, string relativePath)
    {
        var segments = new List<string>();
        if (baseDir.Length > 0) segments.AddRange(baseDir.Split('/'));

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
