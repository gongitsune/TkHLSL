using TkHLSL.SourceGeneration.Manifest;
using TkHLSL.Text;

namespace TkHLSL.SourceGeneration.Emit;

/// <summary>
///     Resolves an HLSL <see cref="TextSpan" /> (from an <see cref="TkHLSL.Model.HlslCompilationResult" />
///     element or a <see cref="TkHLSL.Diagnostics.Diagnostic" />) to the file and line/column it
///     points at, so a reported <see cref="Microsoft.CodeAnalysis.Location" /> lands on the actual
///     <c>.compute</c>/<c>.cginc</c> source. Two implementations back this: one walking a real
///     <see cref="SourceText" /> (the raw-AdditionalFile pipeline, which still has HLSL source text to
///     scan), one looking up a pre-resolved table (the <c>*.additionalfile</c> manifest pipeline, which
///     never sees HLSL source at all) — see docs/IMPLEMENTATION_PLAN.md, "csc.rsp を廃止し..." plan §2.
/// </summary>
internal interface IEmitLocationResolver
{
    (string Path, LinePositionSpanInfo Span) Resolve(TextSpan span);
}

/// <summary>Resolves against a composite <see cref="SourceText" /> via <see cref="LineMap" /> — the pre-existing behavior for a shader parsed directly from raw AdditionalFiles.</summary>
internal sealed class SourceTextLocationResolver : IEmitLocationResolver
{
    private readonly SourceText _source;
    private readonly IReadOnlyDictionary<string, string> _filesByPath;
    private readonly string _rootPath;

    public SourceTextLocationResolver(SourceText source, IReadOnlyDictionary<string, string> filesByPath,
        string rootPath)
    {
        _source = source;
        _filesByPath = filesByPath;
        _rootPath = rootPath;
    }

    public (string Path, LinePositionSpanInfo Span) Resolve(TextSpan span)
    {
        if (_source.TryGetLocation(span.Start, out var segment, out var offsetInFile))
        {
            var path = segment.Path.Length == 0 ? _rootPath : segment.Path;
            var text = _filesByPath.TryGetValue(path, out var t) ? t : string.Empty;
            return (path, LineMap.GetLinePositionSpan(text, offsetInFile, span.Length));
        }

        return (_rootPath, new LinePositionSpanInfo(0, 0, 0, 0));
    }
}

/// <summary>
///     Resolves against a <c>*.additionalfile</c> manifest's pre-resolved location table — every
///     <see cref="TextSpan" /> produced by <see cref="ShaderManifest.TryRead" /> is a synthetic id
///     (<see cref="TextSpan.Start" />) into <see cref="ManifestData.Locations" />, not a real composite
///     offset, since a manifest carries no HLSL source text to resolve one against.
/// </summary>
internal sealed class ManifestLocationResolver : IEmitLocationResolver
{
    private readonly IReadOnlyDictionary<int, ManifestLocation> _locations;
    private readonly string _rootPath;

    public ManifestLocationResolver(IReadOnlyDictionary<int, ManifestLocation> locations, string rootPath)
    {
        _locations = locations;
        _rootPath = rootPath;
    }

    public (string Path, LinePositionSpanInfo Span) Resolve(TextSpan span)
    {
        if (_locations.TryGetValue(span.Start, out var loc))
            return (loc.Path, new LinePositionSpanInfo(loc.StartLine, loc.StartChar, loc.EndLine, loc.EndChar));

        return (_rootPath, new LinePositionSpanInfo(0, 0, 0, 0));
    }
}
