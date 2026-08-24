namespace TkHLSL.Preprocessing;

/// <summary>
/// Resolves <c>#include "path"</c> targets. TkHLSL performs no file I/O itself; hosts implement
/// this to read from disk, a virtual file system, or an in-memory bundle (e.g. Unity's
/// <c>UnityCG.cginc</c> and friends).
/// </summary>
public interface IIncludeResolver
{
    /// <summary>
    /// Attempts to resolve <paramref name="requestedPath"/> — the raw text between the quotes in
    /// the <c>#include</c> directive — to its source text.
    /// </summary>
    bool TryResolve(string requestedPath, out string? content);
}
