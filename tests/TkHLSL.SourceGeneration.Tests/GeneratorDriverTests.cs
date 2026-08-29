namespace TkHLSL.SourceGeneration.Tests;

/// <summary>
///     Diagnostic-id and generated-content assertions, driven directly through
///     <see cref="GeneratorDriverHarness" /> rather than the full-compile
///     <see cref="TkHlslGeneratorVerifier" /> (see docs/IMPLEMENTATION_PLAN.md §9 Phase 9.5).
/// </summary>
public class GeneratorDriverTests
{
    private const string UserSource = """
        using TkHLSL.Unity;

        [ComputeShaderBinding("Blur.compute")]
        public partial class BlurShader { }
        """;

    [Fact]
    public void FileNotFound_ReportsTKH1001_AndGeneratesNothing()
    {
        var result = GeneratorDriverHarness.Run(UserSource);

        Assert.Contains(result.Diagnostics, d => d.Id == "TKH1001");
        Assert.Empty(result.GeneratedSources);
    }

    [Fact]
    public void AmbiguousFile_ReportsTKH1002()
    {
        const string source = "#pragma kernel CSMain\n[numthreads(1,1,1)]\nvoid CSMain(uint3 id : SV_DispatchThreadID) {}\n";
        var result = GeneratorDriverHarness.Run(UserSource,
            ("A/Blur.compute", source), ("B/Blur.compute", source));

        Assert.Contains(result.Diagnostics, d => d.Id == "TKH1002");
    }

    [Fact]
    public void NonPartialType_ReportsTKH1003_AndGeneratesNothing()
    {
        const string userSource = """
            using TkHLSL.Unity;

            [ComputeShaderBinding("Blur.compute")]
            public class BlurShader { }
            """;
        const string source = "#pragma kernel CSMain\n[numthreads(1,1,1)]\nvoid CSMain(uint3 id : SV_DispatchThreadID) {}\n";

        var result = GeneratorDriverHarness.Run(userSource, ("Blur.compute", source));

        Assert.Contains(result.Diagnostics, d => d.Id == "TKH1003");
        Assert.Empty(result.GeneratedSources);
    }

    [Fact]
    public void NoPragmaKernel_ReportsTKH1004Warning()
    {
        const string source = "RWStructuredBuffer<float> _Out;\n";
        var result = GeneratorDriverHarness.Run(UserSource, ("Blur.compute", source));

        Assert.Contains(result.Diagnostics,
            d => d.Id == "TKH1004" && d.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Warning);
        // Generation still proceeds (there's simply nothing kernel-shaped to bind).
        Assert.Single(result.GeneratedSources);
    }

    [Fact]
    public void HlslParseError_ReportsTKH0001_AndSkipsGeneration()
    {
        const string source = "struct Foo { float x;\n"; // unterminated struct body
        var result = GeneratorDriverHarness.Run(UserSource, ("Blur.compute", source));

        Assert.Contains(result.Diagnostics, d => d.Id == "TKH0001");
        Assert.Empty(result.GeneratedSources);
    }

    [Fact]
    public void GeneratedSource_ContainsExpectedMembers()
    {
        const string source = """
            #pragma kernel CSMain

            Texture2D<float4> _Input;
            RWTexture2D<float4> _Output;

            [numthreads(8,8,1)]
            void CSMain(uint3 id : SV_DispatchThreadID)
            {
                _Output[id.xy] = _Input[id.xy];
            }
            """;

        var result = GeneratorDriverHarness.Run(UserSource, ("Blur.compute", source));

        Assert.Empty(result.Diagnostics.Where(d =>
            d.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error));
        var generated = Assert.Single(result.GeneratedSources).Value;

        Assert.Contains("partial class BlurShader", generated);
        Assert.Contains("public readonly struct CSMainKernel", generated);
        Assert.Contains("public const int NumThreadsX = 8;", generated);
        Assert.Contains("Set_Input(global::UnityEngine.Texture value)", generated);
        Assert.Contains("Set_Output(global::UnityEngine.RenderTexture value)", generated);
        Assert.Contains("DispatchThreads(int threadsX, int threadsY, int threadsZ)", generated);
        Assert.Contains("DispatchGroups(int groupsX, int groupsY, int groupsZ)", generated);
        Assert.Contains("Shader.PropertyToID(\"_Input\")", generated);
    }

    [Fact]
    public void GeneratedSource_CBufferMember_UsesSetFloat()
    {
        const string source = """
            #pragma kernel CSMain
            cbuffer Params { float _Radius; };
            RWStructuredBuffer<float> _Out;
            [numthreads(1,1,1)]
            void CSMain(uint3 id : SV_DispatchThreadID) { _Out[id.x] = _Radius; }
            """;

        var result = GeneratorDriverHarness.Run(UserSource, ("Blur.compute", source));
        var generated = Assert.Single(result.GeneratedSources).Value;

        Assert.Contains("Set_Radius(float value)", generated);
        Assert.Contains("Shader.SetFloat(Properties._Radius, value)", generated);
        Assert.Contains("Set_Params(global::UnityEngine.ComputeBuffer value, int offset, int size)", generated);
    }

    [Fact]
    public void GeneratedSource_IntVectorMember_UsesSetInts()
    {
        const string source = """
            #pragma kernel CSMain
            cbuffer Params { int3 _Size; };
            RWStructuredBuffer<float> _Out;
            [numthreads(1,1,1)]
            void CSMain(uint3 id : SV_DispatchThreadID) { _Out[id.x] = _Size.x; }
            """;

        var result = GeneratorDriverHarness.Run(UserSource, ("Blur.compute", source));
        var generated = Assert.Single(result.GeneratedSources).Value;

        Assert.Contains("Set_Size(int x, int y, int z)", generated);
        Assert.Contains("Shader.SetInts(Properties._Size, x, y, z)", generated);
    }

    [Fact]
    public void GeneratedSource_UIntVectorMember_UsesSetIntsWithReinterpretedCast()
    {
        const string source = """
            #pragma kernel CSMain
            cbuffer Params { uint4 _Mask; };
            RWStructuredBuffer<float> _Out;
            [numthreads(1,1,1)]
            void CSMain(uint3 id : SV_DispatchThreadID) { _Out[id.x] = _Mask.x; }
            """;

        var result = GeneratorDriverHarness.Run(UserSource, ("Blur.compute", source));
        var generated = Assert.Single(result.GeneratedSources).Value;

        Assert.Contains("Set_Mask(uint x, uint y, uint z, uint w)", generated);
        Assert.Contains(
            "Shader.SetInts(Properties._Mask, unchecked((int)x), unchecked((int)y), unchecked((int)z), unchecked((int)w))",
            generated);
    }

    [Fact]
    public void GeneratedSource_IntVectorArrayMember_ReportsTkh1005()
    {
        const string source = """
            #pragma kernel CSMain
            cbuffer Params { int3 _Sizes[4]; };
            RWStructuredBuffer<float> _Out;
            [numthreads(1,1,1)]
            void CSMain(uint3 id : SV_DispatchThreadID) { _Out[id.x] = _Sizes[0].x; }
            """;

        var result = GeneratorDriverHarness.Run(UserSource, ("Blur.compute", source));
        var generated = Assert.Single(result.GeneratedSources).Value;

        Assert.DoesNotContain("Set_Sizes", generated);
        Assert.Contains(result.Diagnostics, d => d.Id == "TKH1005");
    }

    [Fact]
    public void GeneratedSource_StructuredBufferOfUserStruct_EmitsElementStruct()
    {
        const string source = """
            #pragma kernel CSMain
            struct Particle { float3 position; float lifetime; };
            StructuredBuffer<Particle> _In;
            RWStructuredBuffer<Particle> _Out;
            [numthreads(1,1,1)]
            void CSMain(uint3 id : SV_DispatchThreadID) { _Out[id.x] = _In[id.x]; }
            """;

        var result = GeneratorDriverHarness.Run(UserSource, ("Blur.compute", source));
        var generated = Assert.Single(result.GeneratedSources).Value;

        Assert.Contains("public struct Particle", generated);
        Assert.Contains("public global::UnityEngine.Vector3 position;", generated);
        Assert.Contains("public float lifetime;", generated);
        Assert.Contains("public static int Stride =>", generated);
    }

    [Fact]
    public void Generation_IsDeterministic_AcrossRepeatedRuns()
    {
        const string source = """
            #pragma kernel CSMain
            struct Particle { float3 position; float3 velocity; };
            cbuffer Params { float4x4 _World; float _Dt; };
            StructuredBuffer<Particle> _In;
            RWStructuredBuffer<Particle> _Out;
            Texture2D<float4> _Tex;
            SamplerState _Samp;
            [numthreads(1,1,1)]
            void CSMain(uint3 id : SV_DispatchThreadID)
            {
                _Out[id.x] = _In[id.x];
                float4 c = _Tex.Sample(_Samp, float2(0,0));
            }
            """;

        var first = GeneratorDriverHarness.Run(UserSource, ("Blur.compute", source));
        var second = GeneratorDriverHarness.Run(UserSource, ("Blur.compute", source));

        Assert.Equal(first.GeneratedSources.Single().Value, second.GeneratedSources.Single().Value);
    }
}
