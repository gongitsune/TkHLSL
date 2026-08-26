using TkHLSL.Preprocessing;
using TkHLSL.SourceGeneration.Manifest;

namespace TkHLSL.Unity.Editor;

/// <summary>
///     Parses one root <c>.compute</c> (resolving its <c>#include</c> closure against the project
///     filesystem) and serializes the result as a <c>tkhlsl-manifest</c> string via
///     <see cref="ShaderManifest.Write" /> — the piece <c>TkHLSLManifestPostprocessor</c> calls per
///     changed shader. Pure — takes its file access as delegates so it can be unit-tested without a
///     real Unity project on disk. See docs/IMPLEMENTATION_PLAN.md, "csc.rsp を廃止し..." plan §3.
/// </summary>
public static class ShaderManifestBuilder
{
    /// <param name="rootPath">The shader's project-root-relative path (e.g. <c>Assets/Shaders/Blur.compute</c>), used as the manifest's <c>root</c> and matched against a <c>[ComputeShaderBinding]</c> path the same way a raw AdditionalFile path is.</param>
    /// <param name="readFile">Reads a project-root-relative path's contents.</param>
    /// <param name="fileExists">Whether a project-root-relative path exists.</param>
    /// <param name="filenameIndex">Every candidate include file's bare filename mapped to one project-root-relative path (see <see cref="FileSystemIncludeResolver" />).</param>
    /// <param name="defines">The preprocessor symbols to analyze with — empty unless a future revision lets Unity-side code discover a shader's <c>multi_compile</c>/<c>shader_feature</c> variants (see plan §5).</param>
    public static string Build(string rootPath, Func<string, string?> readFile, Func<string, bool> fileExists,
        IReadOnlyDictionary<string, string> filenameIndex, IReadOnlyList<string>? defines = null)
    {
        var rootText = readFile(rootPath) ?? string.Empty;
        var resolver = new FileSystemIncludeResolver(fileExists, readFile, filenameIndex);
        var options = new HlslParseOptions(defines, resolver, rootPath);
        var result = HlslParser.Parse(rootText, options);

        var inputs = new List<string>(resolver.Resolved.Count + 1) { rootPath };
        foreach (var path in resolver.Resolved)
            if (path != rootPath)
                inputs.Add(path);

        var filesByPath = new Dictionary<string, string>(StringComparer.Ordinal) { [rootPath] = rootText };
        foreach (var path in resolver.Resolved)
            if (!filesByPath.ContainsKey(path))
                filesByPath[path] = readFile(path) ?? string.Empty;

        return ShaderManifest.Write(rootPath, defines ?? Array.Empty<string>(), inputs, result, span =>
        {
            if (!result.Source.TryGetLocation(span.Start, out var segment, out var offsetInFile))
                return new ManifestLocation(rootPath, 0, 0, 0, 0);
            var path = segment.Path.Length == 0 ? rootPath : segment.Path;
            var text = filesByPath.TryGetValue(path, out var t) ? t : string.Empty;
            var lineSpan = GetLinePositionSpan(text, offsetInFile, span.Length);
            return new ManifestLocation(path, lineSpan.StartLine, lineSpan.StartChar, lineSpan.EndLine,
                lineSpan.EndChar);
        });
    }

    /// <summary>
    ///     Reads just the <c>root</c> and <c>input</c> records out of an already-written manifest —
    ///     used by <c>TkHLSLManifestPostprocessor</c> (a plain Unity-compiled script, with no access
    ///     to <see cref="ShaderManifest" />'s <see langword="internal" /> reader) to find which
    ///     existing manifests a changed <c>.hlsl</c>/<c>.cginc</c> affects, and to detect an orphaned
    ///     manifest whose root <c>.compute</c> was deleted.
    /// </summary>
    public static bool TryReadRootAndInputs(string manifestText, out string root, out IReadOnlyList<string> inputs)
    {
        if (ShaderManifest.TryRead(manifestText, out var data) && data is not null)
        {
            root = data.Root;
            inputs = data.Inputs;
            return true;
        }

        root = string.Empty;
        inputs = Array.Empty<string>();
        return false;
    }

    /// <summary>
    ///     Converts a plain character offset into a 0-based (line, column) pair — a self-contained copy
    ///     of <c>TkHLSL.SourceGeneration.LineMap</c>'s logic (that type is internal to, and tied to,
    ///     the Roslyn-facing project; duplicating this ~10-line scan here keeps this assembly free of
    ///     any <c>Microsoft.CodeAnalysis</c> dependency).
    /// </summary>
    private static (int StartLine, int StartChar, int EndLine, int EndChar) GetLinePositionSpan(string text,
        int start, int length)
    {
        var (startLine, startChar) = OffsetToPosition(text, start);
        var (endLine, endChar) = OffsetToPosition(text, Math.Min(start + length, text.Length));
        return (startLine, startChar, endLine, endChar);
    }

    private static (int Line, int Char) OffsetToPosition(string text, int offset)
    {
        var limit = Math.Min(Math.Max(offset, 0), text.Length);
        var line = 0;
        var lineStart = 0;
        for (var i = 0; i < limit; i++)
            if (text[i] == '\n')
            {
                line++;
                lineStart = i + 1;
            }

        return (line, limit - lineStart);
    }
}
