using Tk.Hlsl.Diagnostics;
using Tk.Hlsl.Ir;
using Tk.Hlsl.Lexing;
using Tk.Hlsl.Preprocessing;
using Tk.Hlsl.Syntax;

namespace Tk.Hlsl.Tests.Syntax;

public class TopLevelParserTests
{
    private static Module ParseModule(string source, HlslParseOptions? options = null)
    {
        var lexResult = Lexer.Tokenize(source);
        var preprocessResult = Preprocessor.Process(source, lexResult.Tokens, options ?? new HlslParseOptions());
        return TopLevelParser.Parse(source, preprocessResult.Tokens, preprocessResult.KernelNames);
    }

    [Fact]
    public void Parse_NullSource_Throws()
    {
        var lexResult = Lexer.Tokenize("");
        var pre = Preprocessor.Process("", lexResult.Tokens, new HlslParseOptions());
        Assert.Throws<ArgumentNullException>(() => TopLevelParser.Parse(null!, pre.Tokens, pre.KernelNames));
    }

    [Fact]
    public void Parse_NullTokens_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => TopLevelParser.Parse("", null!, Array.Empty<string>()));
    }

    [Fact]
    public void Parse_NullKernelNames_Throws()
    {
        var lexResult = Lexer.Tokenize("");
        Assert.Throws<ArgumentNullException>(() => TopLevelParser.Parse("", lexResult.Tokens, null!));
    }

    [Fact]
    public void Parse_EmptySource_YieldsEmptyModule()
    {
        var module = ParseModule("");

        Assert.Equal(0, module.GlobalVariables.Count);
        Assert.Equal(0, module.Functions.Count);
        Assert.Empty(module.EntryPoints);
        Assert.Empty(module.Diagnostics);
    }

    [Theory]
    [InlineData("StructuredBuffer<float> _Buf : register(t0);", ResourceKind.StructuredBuffer)]
    [InlineData("RWStructuredBuffer<float> _Buf : register(u0);", ResourceKind.RWStructuredBuffer)]
    [InlineData("AppendStructuredBuffer<float> _Buf : register(u0);", ResourceKind.AppendStructuredBuffer)]
    [InlineData("ConsumeStructuredBuffer<float> _Buf : register(u0);", ResourceKind.ConsumeStructuredBuffer)]
    [InlineData("Texture2D<float4> _Buf : register(t0);", ResourceKind.Texture2D)]
    [InlineData("Texture2DArray<float4> _Buf : register(t0);", ResourceKind.Texture2DArray)]
    [InlineData("Texture3D<float4> _Buf : register(t0);", ResourceKind.Texture3D)]
    [InlineData("TextureCube<float4> _Buf : register(t0);", ResourceKind.TextureCube)]
    [InlineData("TextureCubeArray<float4> _Buf : register(t0);", ResourceKind.TextureCubeArray)]
    [InlineData("RWTexture2D<float4> _Buf : register(u0);", ResourceKind.RWTexture2D)]
    [InlineData("RWTexture2DArray<float4> _Buf : register(u0);", ResourceKind.RWTexture2DArray)]
    [InlineData("RWTexture3D<float4> _Buf : register(u0);", ResourceKind.RWTexture3D)]
    [InlineData("ByteAddressBuffer _Buf : register(t0);", ResourceKind.ByteAddressBuffer)]
    [InlineData("RWByteAddressBuffer _Buf : register(u0);", ResourceKind.RWByteAddressBuffer)]
    [InlineData("ConstantBuffer<MyCB> _Buf : register(b0);", ResourceKind.ConstantBuffer)]
    [InlineData("SamplerState _Buf : register(s0);", ResourceKind.SamplerState)]
    [InlineData("SamplerComparisonState _Buf : register(s0);", ResourceKind.SamplerComparisonState)]
    public void Parse_ResourceDeclaration_RecordsExpectedKind(string source, ResourceKind expectedKind)
    {
        var module = ParseModule(source);

        var global = Assert.Single(module.GlobalVariables);
        Assert.Equal("_Buf", global.Name);
        Assert.Equal(expectedKind, global.Kind);
        Assert.Empty(module.Diagnostics);
    }

    [Fact]
    public void Parse_Register_ParsesSlotTypeAndIndex()
    {
        var module = ParseModule("StructuredBuffer<float> _Buf : register(t3);");

        var global = Assert.Single(module.GlobalVariables);
        Assert.NotNull(global.Register);
        Assert.Equal('t', global.Register!.Value.SlotType);
        Assert.Equal(3, global.Register.Value.SlotIndex);
        Assert.Null(global.Register.Value.Space);
    }

    [Fact]
    public void Parse_RegisterWithSpace_ParsesSpace()
    {
        var module = ParseModule("RWStructuredBuffer<float> _Buf : register(u0, space1);");

        var global = Assert.Single(module.GlobalVariables);
        Assert.Equal(1, global.Register!.Value.Space);
        Assert.Empty(module.Diagnostics);
    }

    [Fact]
    public void Parse_ResourceWithoutRegister_LeavesRegisterNull()
    {
        var module = ParseModule("StructuredBuffer<float> _Buf;");

        var global = Assert.Single(module.GlobalVariables);
        Assert.Null(global.Register);
        Assert.Empty(module.Diagnostics);
    }

    [Fact]
    public void Parse_RepeatedElementType_SharesSingleTypeInfoHandle()
    {
        const string source = "StructuredBuffer<float> _A : register(t0);\nStructuredBuffer<float> _B : register(t1);\n";

        var module = ParseModule(source);

        Assert.Equal(2, module.GlobalVariables.Count);
        var handles = module.GlobalVariables.Select(g => g.ElementType!.Value).ToList();
        Assert.Equal(handles[0], handles[1]);
        Assert.Equal(1, module.Types.Count);
    }

    [Fact]
    public void Parse_ResourceArrayDeclaration_SkipsBracketsAndParsesRegister()
    {
        const string source = "Texture2D _Textures[4] : register(t0);\n";

        var module = ParseModule(source);

        var global = Assert.Single(module.GlobalVariables);
        Assert.Equal("_Textures", global.Name);
        Assert.Equal(ResourceKind.Texture2D, global.Kind);
        Assert.Null(global.ElementType);
        Assert.Equal('t', global.Register!.Value.SlotType);
        Assert.Empty(module.Diagnostics);
    }

    [Fact]
    public void Parse_StructDeclaration_RegistersTypeInfoByNameAndIsReusedAsElementType()
    {
        const string source = "struct Particle { float3 position; float3 velocity; };\nStructuredBuffer<Particle> _Particles : register(t0);\n";

        var module = ParseModule(source);

        Assert.Empty(module.Diagnostics);
        Assert.Equal(1, module.Types.Count);
        var global = Assert.Single(module.GlobalVariables);
        Assert.Equal("Particle", module.Types[global.ElementType!.Value].Name);
    }

    [Fact]
    public void Parse_CBuffer_RecordsAsSingleGlobalVariableWithRegisterAndSkipsMembers()
    {
        const string source = "cbuffer Params : register(b0)\n{\n    float4 _Params;\n    int _Count;\n};\n";

        var module = ParseModule(source);

        var global = Assert.Single(module.GlobalVariables);
        Assert.Equal("Params", global.Name);
        Assert.Equal(ResourceKind.CBuffer, global.Kind);
        Assert.Null(global.ElementType);
        Assert.Equal('b', global.Register!.Value.SlotType);
        Assert.Equal(0, global.Register.Value.SlotIndex);
        Assert.Empty(module.Diagnostics);
    }

    [Fact]
    public void Parse_CBufferWithoutRegister_ParsesSuccessfully()
    {
        const string source = "cbuffer Params\n{\n    float4 _Params;\n};\n";

        var module = ParseModule(source);

        var global = Assert.Single(module.GlobalVariables);
        Assert.Null(global.Register);
        Assert.Empty(module.Diagnostics);
    }

    [Fact]
    public void Parse_PlainGlobalVariable_RecordedOutsideCBuffer()
    {
        const string source = "float4 _Tint;\n";

        var module = ParseModule(source);

        var global = Assert.Single(module.GlobalVariables);
        Assert.Equal("_Tint", global.Name);
        Assert.Equal(ResourceKind.PlainGlobal, global.Kind);
        Assert.Equal("float4", module.Types[global.ElementType!.Value].Name);
        Assert.Empty(module.Diagnostics);
    }

    [Fact]
    public void Parse_QualifiedPlainGlobalWithInitializer_SkipsQualifiersAndInitializerExpression()
    {
        const string source = "static const float PI = 3.14159;\n";

        var module = ParseModule(source);

        var global = Assert.Single(module.GlobalVariables);
        Assert.Equal("PI", global.Name);
        Assert.Empty(module.Diagnostics);
    }

    [Fact]
    public void Parse_InitializerWithNestedCallExpression_DoesNotBreakOnInnerCommas()
    {
        const string source = "static float4 _Tint = float4(1, 2, 3, 4);\n";

        var module = ParseModule(source);

        var global = Assert.Single(module.GlobalVariables);
        Assert.Equal("_Tint", global.Name);
        Assert.Empty(module.Diagnostics);
    }

    [Fact]
    public void Parse_CommaSeparatedDeclarators_RecordsEachAsGlobalVariable()
    {
        const string source = "float a, b, c;\n";

        var module = ParseModule(source);

        Assert.Equal(3, module.GlobalVariables.Count);
        Assert.Equal(["a", "b", "c"], module.GlobalVariables.Select(g => g.Name).ToArray());
    }

    [Fact]
    public void Parse_ArrayGlobalVariable_SkipsArrayBrackets()
    {
        const string source = "float3 positions[10];\n";

        var module = ParseModule(source);

        var global = Assert.Single(module.GlobalVariables);
        Assert.Equal("positions", global.Name);
        Assert.Empty(module.Diagnostics);
    }

    [Fact]
    public void Parse_KernelWithMatchingPragma_ProducesEntryPoint()
    {
        const string source = """
            #pragma kernel CSMain

            RWStructuredBuffer<float> _Result : register(u0);

            [numthreads(8,8,1)]
            void CSMain(uint3 id : SV_DispatchThreadID)
            {
                _Result[id.x] = 1;
            }
            """;

        var module = ParseModule(source);

        var entryPoint = Assert.Single(module.EntryPoints);
        Assert.Equal("CSMain", entryPoint.Name);
        Assert.Equal(new ThreadGroupSize(8, 8, 1), entryPoint.ThreadGroupSize);
        Assert.Equal("CSMain", entryPoint.Function.Name);
        Assert.Equal(0, module.Functions.Count);
        Assert.Empty(module.Diagnostics);
    }

    [Fact]
    public void Parse_NumThreadsWithoutMatchingPragma_RegistersAsFunctionWithWarning()
    {
        const string source = """
            [numthreads(8,8,1)]
            void CSMain(uint3 id : SV_DispatchThreadID)
            {
            }
            """;

        var module = ParseModule(source);

        Assert.Empty(module.EntryPoints);
        var function = Assert.Single(module.Functions);
        Assert.Equal("CSMain", function.Name);
        var diagnostic = Assert.Single(module.Diagnostics);
        Assert.Equal(DiagnosticSeverity.Warning, diagnostic.Severity);
    }

    [Fact]
    public void Parse_PragmaKernelWithoutMatchingFunction_ProducesWarning()
    {
        const string source = "#pragma kernel Missing\nvoid Other() {}\n";

        var module = ParseModule(source);

        Assert.Empty(module.EntryPoints);
        var diagnostic = Assert.Single(module.Diagnostics);
        Assert.Equal(DiagnosticSeverity.Warning, diagnostic.Severity);
        Assert.Contains("Missing", diagnostic.Message);
    }

    [Fact]
    public void Parse_FunctionPrototypeWithoutBody_IsNotRegistered()
    {
        const string source = "float square(float x);\nfloat square(float x) { return x * x; }\n";

        var module = ParseModule(source);

        var function = Assert.Single(module.Functions);
        Assert.Equal("square", function.Name);
        Assert.Empty(module.Diagnostics);
    }

    [Fact]
    public void Parse_Functions_AreStoredInDeclarationOrder_CalleeBeforeCaller()
    {
        const string source = """
            float helperB(float x) { return x; }
            float helperA(float x) { return helperB(x) + 1; }

            #pragma kernel CSMain
            RWStructuredBuffer<float> _Result : register(u0);

            [numthreads(1,1,1)]
            void CSMain(uint3 id : SV_DispatchThreadID)
            {
                _Result[id.x] = helperA(1);
            }
            """;

        var module = ParseModule(source);

        Assert.Equal(["helperB", "helperA"], module.Functions.Select(f => f.Name).ToArray());
        Assert.Single(module.EntryPoints);
        Assert.Empty(module.Diagnostics);
    }

    [Fact]
    public void Parse_MissingSemicolon_RecordsDiagnosticAndRecoversAtNextSemicolon()
    {
        const string source = "float4 _Broken\nfloat4 _Ok;\n";

        var module = ParseModule(source);

        Assert.Single(module.Diagnostics);
        var global = Assert.Single(module.GlobalVariables);
        Assert.Equal("_Broken", global.Name);
    }

    [Fact]
    public void Parse_UnterminatedStructBody_ProducesDiagnosticInsteadOfHanging()
    {
        const string source = "struct Foo { float x;\n";

        var module = ParseModule(source);

        Assert.NotEmpty(module.Diagnostics);
    }

    [Fact]
    public void Parse_UnterminatedFunctionBody_ProducesDiagnosticInsteadOfHanging()
    {
        const string source = "float square(float x) { return x * x;\n";

        var module = ParseModule(source);

        Assert.NotEmpty(module.Diagnostics);
    }

    [Fact]
    public void Parse_UnexpectedTopLevelToken_ProducesDiagnosticAndRecovers()
    {
        const string source = ";\nfloat4 _Ok;\n";

        var module = ParseModule(source);

        Assert.NotEmpty(module.Diagnostics);
        var global = Assert.Single(module.GlobalVariables);
        Assert.Equal("_Ok", global.Name);
    }

    [Fact]
    public void Parse_RepresentativeComputeShader_ProducesExpectedModule()
    {
        const string source = """
            #pragma kernel CSMain

            RWStructuredBuffer<float> _Result : register(u0);
            Texture2D<float4> _InputTexture : register(t0);
            SamplerState sampler_InputTexture : register(s0);

            cbuffer Params : register(b0)
            {
                float4 _Params;
                int _Count;
            };

            float square(float x)
            {
                return x * x;
            }

            [numthreads(8, 8, 1)]
            void CSMain(uint3 id : SV_DispatchThreadID)
            {
                float4 color = _InputTexture.Sample(sampler_InputTexture, float2(0.5, 0.5));
                _Result[id.x] = square(color.r) + _Params.x;
            }
            """;

        var module = ParseModule(source);

        Assert.Empty(module.Diagnostics);
        Assert.Equal(4, module.GlobalVariables.Count);
        Assert.Equal(2, module.Types.Count); // element types: "float" (_Result), "float4" (_InputTexture)
        var function = Assert.Single(module.Functions);
        Assert.Equal("square", function.Name);
        var entryPoint = Assert.Single(module.EntryPoints);
        Assert.Equal("CSMain", entryPoint.Name);
        Assert.Equal(new ThreadGroupSize(8, 8, 1), entryPoint.ThreadGroupSize);
    }
}
