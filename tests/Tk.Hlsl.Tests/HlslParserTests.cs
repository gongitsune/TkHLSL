using Tk.Hlsl.Ir;
using Tk.Hlsl.Preprocessing;

namespace Tk.Hlsl.Tests;

public class HlslParserTests
{
    [Fact]
    public void Parse_NullSource_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => HlslParser.Parse(null!, new HlslParseOptions()));
    }

    [Fact]
    public void Parse_NullOptions_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => HlslParser.Parse("", null!));
    }

    [Fact]
    public void Parse_EmptySource_YieldsEmptyResult()
    {
        var result = HlslParser.Parse("", new HlslParseOptions());

        Assert.Empty(result.Kernels);
        Assert.Empty(result.AllResources);
        Assert.Empty(result.Diagnostics);
    }

    [Fact]
    public void Parse_SingleKernelSingleBuffer_ProducesOneKernelWithOneBinding()
    {
        const string source = """
                              #pragma kernel CSMain
                              RWStructuredBuffer<float> _Buf : register(u0);

                              [numthreads(8,1,1)]
                              void CSMain(uint3 id : SV_DispatchThreadID)
                              {
                                  _Buf[id.x] = 1.0;
                              }
                              """;

        var result = HlslParser.Parse(source, new HlslParseOptions());

        Assert.Empty(result.Diagnostics);
        var kernel = Assert.Single(result.Kernels);
        Assert.Equal("CSMain", kernel.Name);
        Assert.Equal(new ThreadGroupSize(8, 1, 1), kernel.ThreadGroupSize);
        var binding = Assert.Single(kernel.Bindings);
        Assert.Equal("_Buf", binding.Name);
        Assert.Equal(ResourceKind.RWStructuredBuffer, binding.ResourceKind);
        Assert.Equal("float", binding.ElementTypeName);

        var resource = Assert.Single(result.AllResources);
        Assert.Same(resource, binding);
    }

    [Fact]
    public void Parse_MultipleKernelsSharingBuffer_BothKernelsReferenceSameBindingInstance()
    {
        const string source = """
                              #pragma kernel CSFirst
                              #pragma kernel CSSecond
                              RWStructuredBuffer<float> _Shared : register(u0);
                              RWStructuredBuffer<float> _OnlyFirst : register(u1);

                              [numthreads(8,1,1)]
                              void CSFirst(uint3 id : SV_DispatchThreadID)
                              {
                                  _Shared[id.x] = 1.0;
                                  _OnlyFirst[id.x] = 2.0;
                              }

                              [numthreads(8,1,1)]
                              void CSSecond(uint3 id : SV_DispatchThreadID)
                              {
                                  _Shared[id.x] = 3.0;
                              }
                              """;

        var result = HlslParser.Parse(source, new HlslParseOptions());

        Assert.Empty(result.Diagnostics);
        Assert.Equal(2, result.Kernels.Count);
        Assert.Equal(2, result.AllResources.Count);

        var first = result.Kernels.Single(k => k.Name == "CSFirst");
        var second = result.Kernels.Single(k => k.Name == "CSSecond");

        Assert.Equal(2, first.Bindings.Count);
        var sharedBinding = Assert.Single(second.Bindings);
        Assert.Equal("_Shared", sharedBinding.Name);

        // The same global's binding is shared by reference across kernels, not re-allocated.
        Assert.Same(first.Bindings.Single(b => b.Name == "_Shared"), sharedBinding);
    }

    [Fact]
    public void Parse_MultiLevelHelperChain_PropagatesBufferToKernelBinding()
    {
        const string source = """
                              RWStructuredBuffer<float> _Buf : register(u0);

                              float HelperB(float x)
                              {
                                  return _Buf[0] + x;
                              }

                              float HelperA(float x)
                              {
                                  return HelperB(x) * 2.0;
                              }

                              #pragma kernel CSMain
                              [numthreads(8,1,1)]
                              void CSMain(uint3 id : SV_DispatchThreadID)
                              {
                                  _Buf[id.x] = HelperA(1.0);
                              }
                              """;

        var result = HlslParser.Parse(source, new HlslParseOptions());

        Assert.Empty(result.Diagnostics);
        var kernel = Assert.Single(result.Kernels);
        var binding = Assert.Single(kernel.Bindings);
        Assert.Equal("_Buf", binding.Name);
    }

    [Fact]
    public void Parse_TextureSamplerAndCBufferMix_ProducesExpectedBindingsAndUnusedResource()
    {
        const string source = """
                              #pragma kernel CSMain
                              Texture2D<float4> _Tex : register(t0);
                              SamplerState _Samp : register(s0);
                              RWStructuredBuffer<float4> _Out : register(u0);
                              RWStructuredBuffer<float4> _Unused : register(u1);

                              cbuffer Params : register(b0)
                              {
                                  float4 _Params;
                              };

                              [numthreads(8,1,1)]
                              void CSMain(uint3 id : SV_DispatchThreadID)
                              {
                                  _Out[id.x] = _Tex.Sample(_Samp, float2(0, 0)) * _Params;
                              }
                              """;

        var result = HlslParser.Parse(source, new HlslParseOptions());

        Assert.Empty(result.Diagnostics);
        Assert.Equal(5, result.AllResources.Count);

        var kernel = Assert.Single(result.Kernels);
        var bindingNames = kernel.Bindings.Select(b => b.Name).ToHashSet();
        Assert.Equal(["_Tex", "_Samp", "_Out"], bindingNames);
        Assert.DoesNotContain("_Unused", bindingNames);

        var texBinding = kernel.Bindings.Single(b => b.Name == "_Tex");
        Assert.Equal(ResourceKind.Texture2D, texBinding.ResourceKind);
        Assert.Equal("float4", texBinding.ElementTypeName);

        // Known limitation (docs/IMPLEMENTATION_PLAN.md §9 Phase 4): the Analyzer matches identifiers
        // by name, and a cbuffer's members are accessed under their own names (e.g. `_Params`), not the
        // block's name (`Params`) — so cbuffer usage is not detected via member access. The declaration
        // still appears in AllResources.
        Assert.Contains(result.AllResources, r => r is { Name: "Params", ResourceKind: ResourceKind.CBuffer });
    }

    [Fact]
    public void Parse_LexerDiagnostics_AreIncludedInResult()
    {
        const string source = "\"unterminated";

        var result = HlslParser.Parse(source, new HlslParseOptions());

        Assert.NotEmpty(result.Diagnostics);
    }

    private sealed class StubIncludeResolver(string knownPath, string knownContent) : IIncludeResolver
    {
        public bool TryResolve(string requestedPath, string? includerPath, out string? resolvedPath, out string? content)
        {
            if (requestedPath == knownPath)
            {
                resolvedPath = knownPath;
                content = knownContent;
                return true;
            }

            resolvedPath = null;
            content = null;
            return false;
        }
    }

    [Fact]
    public void Parse_ResourceDeclaredInInclude_IsBoundToKernel()
    {
        const string source = """
                              #pragma kernel CSMain
                              #include "Res.cginc"

                              [numthreads(8,1,1)]
                              void CSMain(uint3 id : SV_DispatchThreadID)
                              {
                                  _Result[id.x] = 1.0;
                              }
                              """;
        var options = new HlslParseOptions(
            includeResolver: new StubIncludeResolver("Res.cginc",
                "RWStructuredBuffer<float> _Result : register(u0);\n"));

        var result = HlslParser.Parse(source, options);

        Assert.Empty(result.Diagnostics);
        var resource = Assert.Single(result.AllResources);
        Assert.Equal("_Result", resource.Name);
        var kernel = Assert.Single(result.Kernels);
        Assert.Same(resource, Assert.Single(kernel.Bindings));

        Assert.True(result.Source.TryGetLocation(resource.Location.Start, out var segment, out _));
        Assert.Equal("Res.cginc", segment.Path);
    }

    [Fact]
    public void Parse_NumthreadsFromIncludedMacro_ResolvesThreadGroupSize()
    {
        const string source = """
                              #pragma kernel CSMain
                              #include "Common.cginc"

                              [numthreads(THREADS,THREADS,1)]
                              void CSMain(uint3 id : SV_DispatchThreadID)
                              {
                              }
                              """;
        var options = new HlslParseOptions(
            includeResolver: new StubIncludeResolver("Common.cginc", "#define THREADS 8\n"));

        var result = HlslParser.Parse(source, options);

        Assert.Empty(result.Diagnostics);
        var kernel = Assert.Single(result.Kernels);
        Assert.Equal(new ThreadGroupSize(8, 8, 1), kernel.ThreadGroupSize);
    }

    [Fact]
    public void Parse_UnresolvedInclude_StillProducesKernels()
    {
        const string source = """
                              #pragma kernel CSMain
                              #include "Missing.cginc"

                              [numthreads(8,1,1)]
                              void CSMain(uint3 id : SV_DispatchThreadID)
                              {
                              }
                              """;

        var result = HlslParser.Parse(source, new HlslParseOptions());

        Assert.NotEmpty(result.Diagnostics);
        var kernel = Assert.Single(result.Kernels);
        Assert.Equal("CSMain", kernel.Name);
    }
}