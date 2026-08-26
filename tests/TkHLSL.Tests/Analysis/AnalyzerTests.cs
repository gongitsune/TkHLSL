using TkHLSL.Analysis;
using TkHLSL.Arena;
using TkHLSL.Ir;
using TkHLSL.Lexing;
using TkHLSL.Preprocessing;
using TkHLSL.Syntax;

namespace TkHLSL.Tests.Analysis;

public class AnalyzerTests
{
    private static (Module Module, ModuleInfo Info) Analyze(string source, HlslParseOptions? options = null)
    {
        var lexResult = Lexer.Tokenize(source);
        var preprocessResult = Preprocessor.Process(source, lexResult.Tokens, options ?? new HlslParseOptions());
        var module = TopLevelParser.Parse(source, preprocessResult.Tokens, preprocessResult.KernelNames);
        var moduleInfo = Analyzer.Analyze(source, preprocessResult.Tokens, module);
        return (module, moduleInfo);
    }

    private static Handle<GlobalVariable> FindHandle(Module module, string name)
    {
        foreach (var (handle, global) in module.GlobalVariables.WithHandles())
            if (global.Name == name)
                return handle;

        throw new InvalidOperationException($"'{name}' is not a global variable in this module.");
    }

    [Fact]
    public void Analyze_NullSource_Throws()
    {
        var module = TopLevelParser.Parse("", [], []);
        Assert.Throws<ArgumentNullException>(() => Analyzer.Analyze(null!, [], module));
    }

    [Fact]
    public void Analyze_NullTokens_Throws()
    {
        var module = TopLevelParser.Parse("", [], []);
        Assert.Throws<ArgumentNullException>(() => Analyzer.Analyze("", null!, module));
    }

    [Fact]
    public void Analyze_NullModule_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => Analyzer.Analyze("", [], null!));
    }

    [Fact]
    public void Analyze_EmptySource_YieldsEmptyModuleInfo()
    {
        var (_, info) = Analyze("");

        Assert.Empty(info.Functions);
        Assert.Empty(info.EntryPoints);
    }

    [Fact]
    public void Analyze_KernelDirectlyReferencesBuffer_RecordsGlobalUse()
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

        var (module, info) = Analyze(source);

        Assert.Empty(module.Diagnostics);
        var bufHandle = FindHandle(module, "_Buf");
        var kernelInfo = Assert.Single(info.EntryPoints);
        Assert.True(kernelInfo.UsesGlobal(bufHandle));
        Assert.Single(kernelInfo.GlobalUses);
    }

    [Fact]
    public void Analyze_UnusedBuffer_IsNotInKernelGlobalUses()
    {
        const string source = """
                               #pragma kernel CSMain
                               RWStructuredBuffer<float> _Used : register(u0);
                               RWStructuredBuffer<float> _Unused : register(u1);

                               [numthreads(8,1,1)]
                               void CSMain(uint3 id : SV_DispatchThreadID)
                               {
                                   _Used[id.x] = 1.0;
                               }
                               """;

        var (module, info) = Analyze(source);

        var usedHandle = FindHandle(module, "_Used");
        var unusedHandle = FindHandle(module, "_Unused");
        var kernelInfo = Assert.Single(info.EntryPoints);
        Assert.True(kernelInfo.UsesGlobal(usedHandle));
        Assert.False(kernelInfo.UsesGlobal(unusedHandle));
    }

    [Fact]
    public void Analyze_ThreeLevelCallChain_PropagatesGlobalUseToKernel()
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

        var (module, info) = Analyze(source);

        Assert.Empty(module.Diagnostics);
        Assert.Equal(2, info.Functions.Count);
        var bufHandle = FindHandle(module, "_Buf");

        // Functions is in declaration ("callee before caller") order: HelperB(0), HelperA(1).
        Assert.True(info.Functions[0].UsesGlobal(bufHandle)); // HelperB
        Assert.True(info.Functions[1].UsesGlobal(bufHandle)); // HelperA (via HelperB)

        var kernelInfo = Assert.Single(info.EntryPoints);
        Assert.True(kernelInfo.UsesGlobal(bufHandle));
    }

    [Fact]
    public void Analyze_MultipleKernelsSharingBuffer_BothReferenceSameHandle()
    {
        const string source = """
                               #pragma kernel CSFirst
                               #pragma kernel CSSecond
                               RWStructuredBuffer<float> _Shared : register(u0);

                               [numthreads(8,1,1)]
                               void CSFirst(uint3 id : SV_DispatchThreadID)
                               {
                                   _Shared[id.x] = 1.0;
                               }

                               [numthreads(8,1,1)]
                               void CSSecond(uint3 id : SV_DispatchThreadID)
                               {
                                   _Shared[id.x] = 2.0;
                               }
                               """;

        var (module, info) = Analyze(source);

        Assert.Empty(module.Diagnostics);
        var sharedHandle = FindHandle(module, "_Shared");
        Assert.Equal(2, info.EntryPoints.Count);
        Assert.True(info.EntryPoints[0].UsesGlobal(sharedHandle));
        Assert.True(info.EntryPoints[1].UsesGlobal(sharedHandle));
    }

    [Fact]
    public void Analyze_TextureSampleMemberAccess_DetectsTextureAndSamplerButNotMethodName()
    {
        const string source = """
                               #pragma kernel CSMain
                               Texture2D _Tex : register(t0);
                               SamplerState _Samp : register(s0);
                               RWStructuredBuffer<float4> _Out : register(u0);

                               [numthreads(8,1,1)]
                               void CSMain(uint3 id : SV_DispatchThreadID)
                               {
                                   _Out[id.x] = _Tex.Sample(_Samp, float2(0, 0));
                               }
                               """;

        var (module, info) = Analyze(source);

        Assert.Empty(module.Diagnostics);
        var texHandle = FindHandle(module, "_Tex");
        var sampHandle = FindHandle(module, "_Samp");
        var outHandle = FindHandle(module, "_Out");
        var kernelInfo = Assert.Single(info.EntryPoints);

        Assert.True(kernelInfo.UsesGlobal(texHandle));
        Assert.True(kernelInfo.UsesGlobal(sampHandle));
        Assert.True(kernelInfo.UsesGlobal(outHandle));
        Assert.Equal(3, kernelInfo.GlobalUses.Count);
    }

    [Fact]
    public void Analyze_BuiltinIntrinsicCalls_ProduceNoDiagnosticsAndAreIgnored()
    {
        const string source = """
                               #pragma kernel CSMain
                               RWStructuredBuffer<float3> _Out : register(u0);

                               [numthreads(8,1,1)]
                               void CSMain(uint3 id : SV_DispatchThreadID)
                               {
                                   float3 a = float3(1, 2, 3);
                                   float3 b = normalize(a);
                                   _Out[id.x] = saturate(dot(a, b)) * a;
                               }
                               """;

        var (module, info) = Analyze(source);

        Assert.Empty(module.Diagnostics);
        var outHandle = FindHandle(module, "_Out");
        var kernelInfo = Assert.Single(info.EntryPoints);
        Assert.True(kernelInfo.UsesGlobal(outHandle));
        Assert.Single(kernelInfo.GlobalUses);
    }

    [Fact]
    public void Analyze_HelperFunctionWithNoResourceAccess_HasEmptyGlobalUses()
    {
        const string source = """
                               float Square(float x)
                               {
                                   return x * x;
                               }
                               """;

        var (_, info) = Analyze(source);

        var helperInfo = Assert.Single(info.Functions);
        Assert.Empty(helperInfo.GlobalUses);
    }

    [Fact]
    public void Analyze_FunctionsAndEntryPointsAreParallelToModule()
    {
        const string source = """
                               float Helper(float x) { return x; }

                               #pragma kernel CSMain
                               [numthreads(1,1,1)]
                               void CSMain(uint3 id : SV_DispatchThreadID) { }
                               """;

        var (module, info) = Analyze(source);

        Assert.Equal(module.Functions.Count, info.Functions.Count);
        Assert.Equal(module.EntryPoints.Count, info.EntryPoints.Count);
    }
}
