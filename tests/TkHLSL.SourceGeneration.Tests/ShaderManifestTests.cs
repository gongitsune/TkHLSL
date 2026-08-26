using TkHLSL;
using TkHLSL.Model;
using TkHLSL.Preprocessing;
using TkHLSL.SourceGeneration;
using TkHLSL.SourceGeneration.Manifest;
using TkHLSL.Text;

namespace TkHLSL.SourceGeneration.Tests;

/// <summary>
///     Tests for <see cref="ShaderManifest" /> — the structured, source-text-free serialization of an
///     <see cref="HlslCompilationResult" /> a Unity Editor-side importer writes in place of a
///     hand-maintained <c>csc.rsp</c> (see docs/IMPLEMENTATION_PLAN.md, "csc.rsp を廃止し..." plan §1).
/// </summary>
public class ShaderManifestTests
{
    /// <summary>Mimics what the Unity Editor-side <c>ShaderManifestBuilder</c> does: parse with a real <see cref="SourceText" />, then write a manifest that resolves every span to <c>path:line:col</c> up front.</summary>
    private static string BuildManifest(string root, IReadOnlyDictionary<string, string> filesByPath,
        string[]? defines = null)
    {
        var options = new HlslParseOptions(defines, new AdditionalFileIncludeResolver(filesByPath), root);
        var result = HlslParser.Parse(filesByPath[root], options);

        var inputs = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var segment in result.Source.Segments)
        {
            var path = segment.Path.Length == 0 ? root : segment.Path;
            if (seen.Add(path)) inputs.Add(path);
        }

        return ShaderManifest.Write(root, defines ?? [], inputs, result, span =>
        {
            if (!result.Source.TryGetLocation(span.Start, out var segment, out var offset))
                return new ManifestLocation(root, 0, 0, 0, 0);
            var path = segment.Path.Length == 0 ? root : segment.Path;
            var text = filesByPath.TryGetValue(path, out var t) ? t : string.Empty;
            var lineSpan = LineMap.GetLinePositionSpan(text, offset, span.Length);
            return new ManifestLocation(path, lineSpan.StartLine, lineSpan.StartChar, lineSpan.EndLine,
                lineSpan.EndChar);
        });
    }

    private const string RootSource = """
        #pragma kernel CSMain
        #include "Common.hlsl"

        Texture2D<float4> _Input;
        RWStructuredBuffer<Particle> _Out;
        cbuffer Params { float _Dt; };

        [numthreads(8,8,1)]
        void CSMain(uint3 id : SV_DispatchThreadID)
        {
            _Out[id.x].position += _Dt;
        }
        """;

    private const string IncludeSource = "struct Particle { float3 position; float3 velocity; };\n";

    private static IReadOnlyDictionary<string, string> Files()
    {
        return new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["Blur.compute"] = RootSource,
            ["Common.hlsl"] = IncludeSource
        };
    }

    [Fact]
    public void RoundTrip_PreservesKernelsResourcesStructsAndDiagnostics()
    {
        var files = Files();
        var expected = HlslParser.Parse(RootSource,
            new HlslParseOptions(null, new AdditionalFileIncludeResolver(files), "Blur.compute"));

        var manifestText = BuildManifest("Blur.compute", files);
        Assert.True(ShaderManifest.TryRead(manifestText, out var data));
        Assert.NotNull(data);
        var actual = data!.Result;

        Assert.Equal("Blur.compute", data.Root);
        Assert.Contains("Blur.compute", data.Inputs);
        Assert.Contains("Common.hlsl", data.Inputs);

        Assert.Equal(expected.Kernels.Count, actual.Kernels.Count);
        Assert.Equal(expected.Kernels[0].Name, actual.Kernels[0].Name);
        Assert.Equal(expected.Kernels[0].ThreadGroupSize.X, actual.Kernels[0].ThreadGroupSize.X);
        Assert.Equal(expected.Kernels[0].Bindings.Select(b => b.Name),
            actual.Kernels[0].Bindings.Select(b => b.Name));

        Assert.Equal(expected.AllResources.Select(r => (r.Name, r.ResourceKind, r.ElementTypeName)),
            actual.AllResources.Select(r => (r.Name, r.ResourceKind, r.ElementTypeName)));

        Assert.Single(actual.Structs);
        Assert.Equal("Particle", actual.Structs[0].Name);
        Assert.Equal(expected.Structs[0].Fields.Select(f => (f.Name, f.TypeName)),
            actual.Structs[0].Fields.Select(f => (f.Name, f.TypeName)));

        Assert.Empty(actual.Diagnostics);
    }

    [Fact]
    public void RoundTrip_PreservesDiagnosticLocation_InIncludedFile()
    {
        var files = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["Blur.compute"] = "#pragma kernel CSMain\n#include \"Common.hlsl\"\n" +
                                "[numthreads(1,1,1)]\nvoid CSMain(uint3 id : SV_DispatchThreadID) {}\n",
            ["Common.hlsl"] = "struct Bad float x; };\n" // missing '{' after the struct name
        };

        var manifestText = BuildManifest("Blur.compute", files);
        Assert.True(ShaderManifest.TryRead(manifestText, out var data));
        Assert.NotNull(data);

        var diag = Assert.Single(data!.Result.Diagnostics, d => d.Message.Contains("Bad"));
        Assert.True(data.Locations.TryGetValue(diag.Span.Start, out var loc));
        Assert.Equal("Common.hlsl", loc.Path);
        Assert.Equal(0, loc.StartLine);
    }

    [Fact]
    public void TryRead_RejectsUnknownVersion()
    {
        Assert.False(ShaderManifest.TryRead("tkhlsl-manifest\t99\nroot\tBlur.compute\n", out _));
    }

    [Fact]
    public void TryRead_RejectsEmptyOrGarbageText()
    {
        Assert.False(ShaderManifest.TryRead("", out _));
        Assert.False(ShaderManifest.TryRead("not a manifest at all", out _));
    }

    [Fact]
    public void WriteRead_EscapesTabsNewlinesAndBackslashesInFreeText()
    {
        // A diagnostic message is free text and could in principle contain any character,
        // including the tab/newline bytes this format uses as field/record delimiters, or a
        // literal backslash (the escape character itself) — none of those should ever be mistaken
        // for a delimiter or corrupt a later field.
        const string message = "line one\twith a tab\nline two\\with a backslash";
        var result = new HlslCompilationResult(
            [],
            [],
            [new TkHLSL.Diagnostics.Diagnostic(TkHLSL.Diagnostics.DiagnosticSeverity.Warning, message,
                new TextSpan(3, 5))]);

        var text = ShaderManifest.Write("Blur.compute", [], ["Blur.compute"], result,
            _ => new ManifestLocation("Blur.compute", 0, 0, 0, 1));

        Assert.True(ShaderManifest.TryRead(text, out var data));
        var diag = Assert.Single(data!.Result.Diagnostics);
        Assert.Equal(message, diag.Message);
    }

    [Fact]
    public void TryRead_IgnoresUnknownRecordTypes_ForForwardCompatibility()
    {
        var files = Files();
        var manifestText = BuildManifest("Blur.compute", files);
        var withUnknownLine = manifestText.TrimEnd('\n') + "\nfuture-field\tsome\tdata\n";

        Assert.True(ShaderManifest.TryRead(withUnknownLine, out var data));
        Assert.NotNull(data);
        Assert.Single(data!.Result.Kernels);
    }
}
