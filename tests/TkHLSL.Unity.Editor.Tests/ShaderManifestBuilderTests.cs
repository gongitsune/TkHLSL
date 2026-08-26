using TkHLSL.SourceGeneration.Manifest;
using TkHLSL.Unity.Editor;

namespace TkHLSL.Unity.Editor.Tests;

/// <summary>
///     Tests for the Unity Editor-side manifest pipeline: <see cref="FileSystemIncludeResolver" />
///     resolving <c>#include</c> against an in-memory fake filesystem, and
///     <see cref="ShaderManifestBuilder" /> serializing the result — see
///     docs/IMPLEMENTATION_PLAN.md, "csc.rsp を廃止し..." plan §3, §7.
/// </summary>
public class ShaderManifestBuilderTests
{
    private static (Func<string, string?> ReadFile, Func<string, bool> FileExists) FakeFileSystem(
        IReadOnlyDictionary<string, string> files)
    {
        return (path => files.TryGetValue(path, out var text) ? text : null, files.ContainsKey);
    }

    [Fact]
    public void Build_ResolvesIncluderRelativeInclude()
    {
        var files = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["Assets/Shaders/Blur.compute"] = "#pragma kernel CSMain\n#include \"Common.hlsl\"\n" +
                                               "RWStructuredBuffer<Particle> _Out;\n" +
                                               "[numthreads(1,1,1)]\nvoid CSMain(uint3 id : SV_DispatchThreadID) {}\n",
            ["Assets/Shaders/Common.hlsl"] = "struct Particle { float3 position; };\n"
        };
        var (readFile, fileExists) = FakeFileSystem(files);

        var manifest = ShaderManifestBuilder.Build("Assets/Shaders/Blur.compute", readFile, fileExists,
            new Dictionary<string, string>());

        Assert.True(ShaderManifest.TryRead(manifest, out var data));
        Assert.Equal("Assets/Shaders/Blur.compute", data!.Root);
        Assert.Contains("Assets/Shaders/Common.hlsl", data.Inputs);
        Assert.Single(data.Result.Structs);
        Assert.Equal("Particle", data.Result.Structs[0].Name);
        // The manifest never carries HLSL source text.
        Assert.DoesNotContain("float3 position", manifest);
    }

    [Fact]
    public void Build_ResolvesIncludeByFilenameIndex_WhenNotRelativeOrRootRelative()
    {
        var files = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["Assets/Shaders/Blur.compute"] = "#pragma kernel CSMain\n#include \"Common.hlsl\"\n" +
                                               "[numthreads(1,1,1)]\nvoid CSMain(uint3 id : SV_DispatchThreadID) {}\n",
            ["Assets/ShaderLibrary/Common.hlsl"] = "// shared\n"
        };
        var (readFile, fileExists) = FakeFileSystem(files);
        var index = new Dictionary<string, string> { ["Common.hlsl"] = "Assets/ShaderLibrary/Common.hlsl" };

        var manifest = ShaderManifestBuilder.Build("Assets/Shaders/Blur.compute", readFile, fileExists, index);

        Assert.True(ShaderManifest.TryRead(manifest, out var data));
        Assert.Contains("Assets/ShaderLibrary/Common.hlsl", data!.Inputs);
    }

    [Fact]
    public void Build_UnresolvedInclude_DoesNotThrow_AndRecordsInputsWithoutIt()
    {
        var files = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["Assets/Shaders/Blur.compute"] = "#pragma kernel CSMain\n#include \"Missing.hlsl\"\n" +
                                               "[numthreads(1,1,1)]\nvoid CSMain(uint3 id : SV_DispatchThreadID) {}\n"
        };
        var (readFile, fileExists) = FakeFileSystem(files);

        var manifest = ShaderManifestBuilder.Build("Assets/Shaders/Blur.compute", readFile, fileExists,
            new Dictionary<string, string>());

        Assert.True(ShaderManifest.TryRead(manifest, out var data));
        Assert.DoesNotContain(data!.Inputs, i => i.Contains("Missing"));
    }

    [Fact]
    public void Build_NestedAndCircularIncludes_TerminateAndResolve()
    {
        var files = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["Assets/A.compute"] = "#pragma kernel CSMain\n#include \"B.hlsl\"\n" +
                                    "[numthreads(1,1,1)]\nvoid CSMain(uint3 id : SV_DispatchThreadID) {}\n",
            ["Assets/B.hlsl"] = "#include \"C.hlsl\"\n",
            ["Assets/C.hlsl"] = "#include \"B.hlsl\"\nstruct FromC { float x; };\n" // B <-> C cycle
        };
        var (readFile, fileExists) = FakeFileSystem(files);

        var manifest = ShaderManifestBuilder.Build("Assets/A.compute", readFile, fileExists,
            new Dictionary<string, string>());

        Assert.True(ShaderManifest.TryRead(manifest, out var data));
        Assert.Contains("Assets/B.hlsl", data!.Inputs);
        Assert.Contains("Assets/C.hlsl", data.Inputs);
    }

    [Fact]
    public void Build_PreservesDefines_InManifest()
    {
        var files = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["Blur.compute"] = "#pragma kernel CSMain\n[numthreads(1,1,1)]\n" +
                                "void CSMain(uint3 id : SV_DispatchThreadID) {}\n"
        };
        var (readFile, fileExists) = FakeFileSystem(files);

        var manifest = ShaderManifestBuilder.Build("Blur.compute", readFile, fileExists,
            new Dictionary<string, string>(), ["FOO", "BAR"]);

        Assert.True(ShaderManifest.TryRead(manifest, out var data));
        Assert.Equal(["FOO", "BAR"], data!.Defines);
    }
}
