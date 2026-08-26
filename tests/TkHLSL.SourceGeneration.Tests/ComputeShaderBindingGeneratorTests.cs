namespace TkHLSL.SourceGeneration.Tests;

public class ComputeShaderBindingGeneratorTests
{
    private const string BlurCompute = """
        #pragma kernel CSMain

        Texture2D<float4> _Input;
        RWTexture2D<float4> _Output;
        RWStructuredBuffer<float> _Weights : register(u1);

        [numthreads(8,8,1)]
        void CSMain(uint3 id : SV_DispatchThreadID)
        {
            _Output[id.xy] = _Input[id.xy] * _Weights[0];
        }
        """;

    // --- "does it compile end-to-end" tests, via Microsoft.CodeAnalysis.Testing -----------------------

    [Fact]
    public async Task SingleKernel_CompilesSuccessfully()
    {
        const string userSource = """
            using TkHLSL.Unity;

            namespace MyGame
            {
                [ComputeShaderBinding("Shaders/Blur.compute")]
                public partial class BlurShader { }
            }
            """;

        var test = TkHlslGeneratorVerifier.Create(userSource, ("Shaders/Blur.compute", BlurCompute));
        await test.RunAsync();
    }

    [Fact]
    public async Task MultipleKernels_CompilesSuccessfully()
    {
        const string source = """
            #pragma kernel Downsample
            #pragma kernel Upsample

            Texture2D<float4> _Input;
            RWTexture2D<float4> _Output;

            [numthreads(8,8,1)]
            void Downsample(uint3 id : SV_DispatchThreadID) { _Output[id.xy] = _Input[id.xy]; }

            [numthreads(8,8,1)]
            void Upsample(uint3 id : SV_DispatchThreadID) { _Output[id.xy] = _Input[id.xy]; }
            """;

        const string userSource = """
            using TkHLSL.Unity;

            namespace MyGame
            {
                [ComputeShaderBinding("Resize.compute")]
                public partial class ResizeShader { }
            }
            """;

        var test = TkHlslGeneratorVerifier.Create(userSource, ("Resize.compute", source));
        await test.RunAsync();
    }

    [Fact]
    public async Task AllResourceKinds_CompilesSuccessfully()
    {
        const string source = """
            #pragma kernel CSMain

            Texture2D<float4> _Tex2D;
            Texture2DArray<float4> _Tex2DArray;
            Texture3D<float4> _Tex3D;
            TextureCube<float4> _TexCube;
            TextureCubeArray<float4> _TexCubeArray;
            RWTexture2D<float4> _RWTex2D;
            RWTexture2DArray<float4> _RWTex2DArray;
            RWTexture3D<float4> _RWTex3D;
            StructuredBuffer<float> _SBuf;
            RWStructuredBuffer<float> _RWSBuf;
            AppendStructuredBuffer<float> _AppendBuf;
            ConsumeStructuredBuffer<float> _ConsumeBuf;
            ByteAddressBuffer _ByteBuf;
            RWByteAddressBuffer _RWByteBuf;
            SamplerState _Samp;
            float4 _PlainGlobal;

            cbuffer Params
            {
                float _Radius;
                int _Count;
            };

            [numthreads(1,1,1)]
            void CSMain(uint3 id : SV_DispatchThreadID)
            {
                _RWSBuf[id.x] = _SBuf[id.x] + _RWTex2D[id.xy].x + _RWTex2DArray[id.xyz].x
                    + _RWTex3D[id.xyz].x + _Tex2D.Sample(_Samp, float2(0,0)).x
                    + _Tex2DArray.Sample(_Samp, float3(0,0,0)).x + _Tex3D.Sample(_Samp, float3(0,0,0)).x
                    + _TexCube.Sample(_Samp, float3(0,0,0)).x + _TexCubeArray.Sample(_Samp, float4(0,0,0,0)).x
                    + _ByteBuf.Load(0) + _PlainGlobal.x + _Radius + _Count;
                _RWByteBuf.Store(0, 1);
                _AppendBuf.Append(1.0);
                _ConsumeBuf.Consume();
            }
            """;

        const string userSource = """
            using TkHLSL.Unity;

            [ComputeShaderBinding("All.compute")]
            public partial class AllShader { }
            """;

        var test = TkHlslGeneratorVerifier.Create(userSource, ("All.compute", source));
        await test.RunAsync();
    }

    [Fact]
    public async Task Include_IsResolvedFromAdditionalFiles_AndCompilesSuccessfully()
    {
        const string include = """
            float4 Tint(float4 c) { return c; }
            """;

        const string source = """
            #include "Common.cginc"
            #pragma kernel CSMain

            RWTexture2D<float4> _Output;

            [numthreads(8,8,1)]
            void CSMain(uint3 id : SV_DispatchThreadID)
            {
                _Output[id.xy] = Tint(float4(1,1,1,1));
            }
            """;

        const string userSource = """
            using TkHLSL.Unity;

            [ComputeShaderBinding("Shaders/WithInclude.compute")]
            public partial class WithIncludeShader { }
            """;

        var test = TkHlslGeneratorVerifier.Create(userSource,
            ("Shaders/WithInclude.compute", source),
            ("Shaders/Common.cginc", include));
        await test.RunAsync();
    }

    [Fact]
    public async Task CBufferMembersAndStructuredBufferElementStruct_CompileSuccessfully()
    {
        const string source = """
            #pragma kernel CSMain

            struct Particle { float3 position; float3 velocity; };

            cbuffer Params
            {
                float4x4 _WorldToObject;
                float _DeltaTime;
            };

            StructuredBuffer<Particle> _Particles;
            RWStructuredBuffer<Particle> _ParticlesOut;

            [numthreads(64,1,1)]
            void CSMain(uint3 id : SV_DispatchThreadID)
            {
                Particle p = _Particles[id.x];
                p.position += p.velocity * _DeltaTime;
                _ParticlesOut[id.x] = p;
            }
            """;

        const string userSource = """
            using TkHLSL.Unity;

            [ComputeShaderBinding("Particles.compute")]
            public partial class ParticleShader { }
            """;

        var test = TkHlslGeneratorVerifier.Create(userSource, ("Particles.compute", source));
        await test.RunAsync();
    }

    [Fact]
    public async Task NestedNamespaceAndType_CompilesSuccessfully()
    {
        const string userSource = """
            using TkHLSL.Unity;

            namespace MyGame.Rendering
            {
                public partial class Outer
                {
                    [ComputeShaderBinding("Shaders/Blur.compute")]
                    public partial class BlurShader { }
                }
            }
            """;

        var test = TkHlslGeneratorVerifier.Create(userSource, ("Shaders/Blur.compute", BlurCompute));
        await test.RunAsync();
    }
}
