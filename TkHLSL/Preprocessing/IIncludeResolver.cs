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
    /// the <c>#include</c> directive — to its source text, given the path of the file that contains
    /// the directive (<see langword="null"/> when the directive is in the root source), so the host
    /// can resolve <paramref name="requestedPath"/> relative to it.
    /// </summary>
    /// <param name="requestedPath">The raw, unresolved path between the quotes.</param>
    /// <param name="includerPath">
    /// The resolved path of the file containing the <c>#include</c> directive, or <see langword="null"/>
    /// for the root source.
    /// </param>
    /// <param name="resolvedPath">
    /// On success, a canonical identity for the resolved file (e.g. an absolute path), used to detect
    /// <c>#pragma once</c> re-inclusion and include cycles, and passed back as <paramref name="includerPath"/>
    /// for any <c>#include</c> nested inside it. Hosts should normalize this consistently so the same file
    /// reached via different relative paths compares equal.
    /// </param>
    /// <param name="content">The resolved file's source text, on success.</param>
    bool TryResolve(string requestedPath, string? includerPath, out string? resolvedPath, out string? content);
}
