using TkHLSL;
using TkHLSL.Preprocessing;
using TkHLSL.SourceGeneration;
using TkHLSL.SourceGeneration.Manifest;
using TkHLSL.Text;

namespace TkHLSL.SourceGeneration.Tests;

/// <summary>
///     End-to-end tests driving <see cref="ComputeShaderBindingGenerator" /> with a
///     <c>*.additionalfile</c> manifest in place of a raw <c>.compute</c> AdditionalFile — the path a
///     Unity project takes once the Editor-side importer replaces a hand-written <c>csc.rsp</c> (see
///     docs/IMPLEMENTATION_PLAN.md, "csc.rsp を廃止し..." plan).
/// </summary>
public class ManifestGeneratorDriverTests
{
    private const string UserSource = """
        using TkHLSL.Unity;

        [ComputeShaderBinding("Blur.compute")]
        public partial class BlurShader { }
        """;

    private const string ComputeSource = """
        #pragma kernel CSMain

        Texture2D<float4> _Input;
        RWTexture2D<float4> _Output;

        [numthreads(8,8,1)]
        void CSMain(uint3 id : SV_DispatchThreadID)
        {
            _Output[id.xy] = _Input[id.xy];
        }
        """;

    /// <summary>Builds a manifest exactly as the Unity Editor-side importer would, for one root file with no includes.</summary>
    private static string BuildManifest(string root, string source, string[]? defines = null)
    {
        var options = new HlslParseOptions(defines, includeResolver: null, root);
        var result = HlslParser.Parse(source, options);

        return ShaderManifest.Write(root, defines ?? [], [root], result, span =>
        {
            result.Source.TryGetLocation(span.Start, out var segment, out var offset);
            var lineSpan = LineMap.GetLinePositionSpan(source, offset, span.Length);
            return new ManifestLocation(root, lineSpan.StartLine, lineSpan.StartChar, lineSpan.EndLine,
                lineSpan.EndChar);
        });
    }

    [Fact]
    public void ManifestPipeline_GeneratesIdenticalCode_ToRawFilePipeline()
    {
        var manifestText = BuildManifest("Blur.compute", ComputeSource);

        var rawResult = GeneratorDriverHarness.Run(UserSource, ("Blur.compute", ComputeSource));
        var manifestResult = GeneratorDriverHarness.Run(UserSource,
            ("Generated/Blur.compute.additionalfile", manifestText));

        Assert.Empty(manifestResult.Diagnostics.Where(d =>
            d.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error));
        Assert.Equal(rawResult.GeneratedSources.Single().Value, manifestResult.GeneratedSources.Single().Value);
    }

    [Fact]
    public void RawFile_TakesPrecedenceOverManifest_WhenBothPresent()
    {
        // A stale manifest alongside a raw AdditionalFile for the same shader must not shadow it —
        // the raw pipeline supports arbitrary Defines and always wins when present.
        var staleManifest = BuildManifest("Blur.compute", "#pragma kernel Stale\n" +
            "[numthreads(1,1,1)]\nvoid Stale(uint3 id : SV_DispatchThreadID) {}\n");

        var result = GeneratorDriverHarness.Run(UserSource,
            ("Blur.compute", ComputeSource),
            ("Generated/Blur.compute.additionalfile", staleManifest));

        var generated = Assert.Single(result.GeneratedSources).Value;
        Assert.Contains("CSMainKernel", generated);
        Assert.DoesNotContain("StaleKernel", generated);
    }

    [Fact]
    public void NoManifestOrRawFile_ReportsTKH1001()
    {
        var result = GeneratorDriverHarness.Run(UserSource);

        Assert.Contains(result.Diagnostics, d => d.Id == "TKH1001");
    }

    [Fact]
    public void AmbiguousManifestRoots_ReportsTKH1002()
    {
        var manifest = BuildManifest("Blur.compute", ComputeSource);

        var result = GeneratorDriverHarness.Run(UserSource,
            ("A/Generated/Blur.compute.additionalfile", manifest),
            ("B/Generated/Blur.compute.additionalfile", manifest));

        Assert.Contains(result.Diagnostics, d => d.Id == "TKH1002");
    }

    [Fact]
    public void ManifestWithNonMatchingDefines_ReportsTKH1007()
    {
        var manifest = BuildManifest("Blur.compute", ComputeSource); // built with no defines

        const string userSourceWithDefines = """
            using TkHLSL.Unity;

            [ComputeShaderBinding("Blur.compute", Defines = new[] { "FOO" })]
            public partial class BlurShader { }
            """;

        var result = GeneratorDriverHarness.Run(userSourceWithDefines,
            ("Generated/Blur.compute.additionalfile", manifest));

        Assert.Contains(result.Diagnostics, d => d.Id == "TKH1007");
        Assert.Empty(result.GeneratedSources);
    }

    [Fact]
    public void ManifestMatchingDefines_Succeeds()
    {
        var manifest = BuildManifest("Blur.compute", ComputeSource, ["FOO"]);

        const string userSourceWithDefines = """
            using TkHLSL.Unity;

            [ComputeShaderBinding("Blur.compute", Defines = new[] { "FOO" })]
            public partial class BlurShader { }
            """;

        var result = GeneratorDriverHarness.Run(userSourceWithDefines,
            ("Generated/Blur.compute.additionalfile", manifest));

        Assert.Empty(result.Diagnostics.Where(d =>
            d.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error));
        Assert.Single(result.GeneratedSources);
    }
}
