using TkHLSL.Lexing;
using TkHLSL.Preprocessing;

namespace TkHLSL.Tests.Preprocessing;

public class PreprocessorTests
{
    private static PreprocessResult Preprocess(string source, HlslParseOptions? options = null)
    {
        var lexResult = Lexer.Tokenize(source);
        return Preprocessor.Process(source, lexResult.Tokens, options ?? new HlslParseOptions());
    }

    private static string TextOf(PreprocessResult result, string source) =>
        string.Concat(result.Tokens
            .Where(t => t.Kind != TokenKind.EndOfFile)
            .Select(t => t.GetText(source) + " "));

    [Fact]
    public void Process_NullSource_Throws()
    {
        var lexResult = Lexer.Tokenize("");
        Assert.Throws<ArgumentNullException>(() => Preprocessor.Process(null!, lexResult.Tokens, new HlslParseOptions()));
    }

    [Fact]
    public void Process_NullOptions_Throws()
    {
        var lexResult = Lexer.Tokenize("");
        Assert.Throws<ArgumentNullException>(() => Preprocessor.Process("", lexResult.Tokens, null!));
    }

    [Fact]
    public void Process_EmptySource_YieldsOnlyEndOfFile()
    {
        var result = Preprocess("");

        var kind = Assert.Single(result.Tokens).Kind;
        Assert.Equal(TokenKind.EndOfFile, kind);
        Assert.Empty(result.KernelNames);
        Assert.Empty(result.Diagnostics);
    }

    [Fact]
    public void Process_StripsCommentsAndNewLinesFromOutput()
    {
        const string source = "int x; // trailing comment\n/* block */ int y;\n";

        var result = Preprocess(source);

        Assert.DoesNotContain(result.Tokens, t => t.Kind is TokenKind.LineComment or TokenKind.BlockComment or TokenKind.NewLine or TokenKind.Hash);
    }

    [Fact]
    public void Process_PragmaKernel_IsRecordedAndRemovedFromOutput()
    {
        const string source = "#pragma kernel CSMain\nvoid CSMain() {}\n";

        var result = Preprocess(source);

        Assert.Equal(["CSMain"], result.KernelNames);
        Assert.DoesNotContain(result.Tokens, t => t.Kind == TokenKind.Hash);
        Assert.Empty(result.Diagnostics);
    }

    [Fact]
    public void Process_MultiplePragmaKernels_AreRecordedInOrder()
    {
        const string source = "#pragma kernel First\n#pragma kernel Second\n";

        var result = Preprocess(source);

        Assert.Equal(["First", "Second"], result.KernelNames);
    }

    [Fact]
    public void Process_PragmaKernelWithoutName_ProducesDiagnostic()
    {
        const string source = "#pragma kernel\n";

        var result = Preprocess(source);

        Assert.Empty(result.KernelNames);
        Assert.Single(result.Diagnostics);
    }

    [Fact]
    public void Process_MultiCompileAndShaderFeature_AreIgnoredWithoutDiagnostics()
    {
        const string source = "#pragma multi_compile A B\n#pragma shader_feature FOO\nint x;\n";

        var result = Preprocess(source);

        Assert.Empty(result.KernelNames);
        Assert.Empty(result.Diagnostics);
        Assert.Contains(result.Tokens, t => t.Kind == TokenKind.Identifier);
    }

    [Fact]
    public void Process_ObjectMacro_ExpandsAtUseSite()
    {
        const string source = "#define COUNT 4\nint arr[COUNT];\n";

        var result = Preprocess(source);

        Assert.Equal("int arr [ 4 ] ; ", TextOf(result, source));
        Assert.Empty(result.Diagnostics);
    }

    [Fact]
    public void Process_FlagMacro_ExpandsToNothing()
    {
        const string source = "#define FLAG\nint x FLAG;\n";

        var result = Preprocess(source);

        Assert.Equal("int x ; ", TextOf(result, source));
    }

    [Fact]
    public void Process_MacroIsNotExpandedRecursively()
    {
        const string source = "#define A B\n#define B 1\nA\n";

        var result = Preprocess(source);

        // Single-level substitution only: A expands to the identifier "B", not "1".
        var identifiers = result.Tokens.Where(t => t.Kind == TokenKind.Identifier).ToList();
        var identifier = Assert.Single(identifiers);
        Assert.Equal("B", identifier.GetText(source));
    }

    [Fact]
    public void Process_Undef_RemovesMacroSoIdentifierPassesThroughUnexpanded()
    {
        const string source = "#define COUNT 4\n#undef COUNT\nint arr[COUNT];\n";

        var result = Preprocess(source);

        Assert.Equal("int arr [ COUNT ] ; ", TextOf(result, source));
    }

    [Fact]
    public void Process_Redefine_UsesLatestBody()
    {
        const string source = "#define COUNT 4\n#define COUNT 8\nCOUNT\n";

        var result = Preprocess(source);

        var token = Assert.Single(result.Tokens, t => t.Kind != TokenKind.EndOfFile);
        Assert.Equal(TokenKind.IntLiteral, token.Kind);
        Assert.Equal("8", token.GetText(source));
    }

    [Fact]
    public void Process_FunctionLikeMacro_IsUnsupportedAndProducesDiagnostic()
    {
        const string source = "#define SQ(x) ((x) * (x))\nSQ(2)\n";

        var result = Preprocess(source);

        Assert.Single(result.Diagnostics);
        // The macro was never registered, so SQ(2) passes through unexpanded.
        Assert.Contains(result.Tokens, t => t.Kind == TokenKind.Identifier && t.GetText(source) == "SQ");
    }

    [Fact]
    public void Process_ObjectMacroValueStartingWithParen_IsNotTreatedAsFunctionLike()
    {
        const string source = "#define WRAPPED (1 + 2)\nWRAPPED\n";

        var result = Preprocess(source);

        Assert.Empty(result.Diagnostics);
        Assert.Equal("( 1 + 2 ) ", TextOf(result, source));
    }

    [Theory]
    [InlineData("#ifdef FOO\nint x;\n#endif\n", true, "int x ; ")]
    [InlineData("#ifdef FOO\nint x;\n#endif\n", false, "")]
    [InlineData("#ifndef FOO\nint x;\n#endif\n", false, "int x ; ")]
    [InlineData("#ifndef FOO\nint x;\n#endif\n", true, "")]
    public void Process_IfdefIfndef_ResolvesAgainstDefinedSymbols(string source, bool defineFoo, string expected)
    {
        var options = new HlslParseOptions(defineFoo ? ["FOO"] : null);

        var result = Preprocess(source, options);

        Assert.Equal(expected, TextOf(result, source));
        Assert.Empty(result.Diagnostics);
    }

    [Theory]
    [InlineData("#if defined(FOO)\nint x;\n#endif\n", true, "int x ; ")]
    [InlineData("#if defined(FOO)\nint x;\n#endif\n", false, "")]
    [InlineData("#if !defined(FOO)\nint x;\n#endif\n", false, "int x ; ")]
    [InlineData("#if defined FOO\nint x;\n#endif\n", true, "int x ; ")]
    public void Process_IfDefined_ResolvesAgainstDefinedSymbols(string source, bool defineFoo, string expected)
    {
        var options = new HlslParseOptions(defineFoo ? ["FOO"] : null);

        var result = Preprocess(source, options);

        Assert.Equal(expected, TextOf(result, source));
        Assert.Empty(result.Diagnostics);
    }

    [Fact]
    public void Process_ElseBranch_TakenWhenIfConditionFalse()
    {
        const string source = "#ifdef FOO\nint a;\n#else\nint b;\n#endif\n";

        var result = Preprocess(source);

        Assert.Equal("int b ; ", TextOf(result, source));
    }

    [Fact]
    public void Process_ElifChain_TakesFirstMatchingBranchOnly()
    {
        const string source =
            "#ifdef A\nint a;\n#elif defined(B)\nint b;\n#elif defined(C)\nint c;\n#else\nint d;\n#endif\n";

        var result = Preprocess(source, new HlslParseOptions(["B", "C"]));

        Assert.Equal("int b ; ", TextOf(result, source));
    }

    [Fact]
    public void Process_NestedConditionals_InnerFalseBranchDoesNotLeakOuterTrueBranch()
    {
        const string source = "#ifdef OUTER\nint a;\n#ifdef INNER\nint b;\n#else\nint c;\n#endif\nint d;\n#endif\n";

        var result = Preprocess(source, new HlslParseOptions(["OUTER"]));

        Assert.Equal("int a ; int c ; int d ; ", TextOf(result, source));
    }

    [Fact]
    public void Process_NestedConditionals_OuterFalseSuppressesInnerEntirely()
    {
        const string source = "#ifdef OUTER\nint a;\n#ifdef INNER\nint b;\n#endif\nint c;\n#endif\nint d;\n";

        var result = Preprocess(source);

        Assert.Equal("int d ; ", TextOf(result, source));
        Assert.Empty(result.Diagnostics);
    }

    [Fact]
    public void Process_DefineInsideFalseBranch_IsNotRegistered()
    {
        const string source = "#ifdef FOO\n#define COUNT 4\n#endif\nCOUNT\n";

        var result = Preprocess(source);

        var token = Assert.Single(result.Tokens, t => t.Kind != TokenKind.EndOfFile);
        Assert.Equal("COUNT", token.GetText(source));
    }

    [Fact]
    public void Process_StrayEndif_ProducesDiagnostic()
    {
        var result = Preprocess("#endif\n");

        Assert.Single(result.Diagnostics);
    }

    [Fact]
    public void Process_StrayElse_ProducesDiagnostic()
    {
        var result = Preprocess("#else\n");

        Assert.Single(result.Diagnostics);
    }

    [Fact]
    public void Process_StrayElif_ProducesDiagnostic()
    {
        var result = Preprocess("#elif defined(FOO)\n");

        Assert.Single(result.Diagnostics);
    }

    [Fact]
    public void Process_UnterminatedIf_ProducesDiagnostic()
    {
        var result = Preprocess("#ifdef FOO\nint x;\n");

        Assert.Single(result.Diagnostics);
    }

    [Fact]
    public void Process_UnsupportedIfExpression_ProducesDiagnosticAndTreatsBranchAsFalse()
    {
        const string source = "#if FOO == 1\nint x;\n#endif\n";

        var result = Preprocess(source);

        Assert.Single(result.Diagnostics);
        Assert.Equal("", TextOf(result, source));
    }

    private sealed class StubIncludeResolver(string knownPath, string knownContent) : IIncludeResolver
    {
        public bool TryResolve(string requestedPath, out string? content)
        {
            if (requestedPath == knownPath)
            {
                content = knownContent;
                return true;
            }

            content = null;
            return false;
        }
    }

    [Fact]
    public void Process_IncludeResolves_ProducesNoDiagnostic()
    {
        const string source = "#include \"Common.cginc\"\nint x;\n";
        var options = new HlslParseOptions(includeResolver: new StubIncludeResolver("Common.cginc", "float unused;"));

        var result = Preprocess(source, options);

        Assert.Empty(result.Diagnostics);
    }

    [Fact]
    public void Process_IncludeUnresolvable_ProducesDiagnostic()
    {
        const string source = "#include \"Missing.cginc\"\nint x;\n";
        var options = new HlslParseOptions(includeResolver: new StubIncludeResolver("Common.cginc", "float unused;"));

        var result = Preprocess(source, options);

        Assert.Single(result.Diagnostics);
    }

    [Fact]
    public void Process_IncludeWithoutResolver_ProducesDiagnostic()
    {
        const string source = "#include \"Common.cginc\"\nint x;\n";

        var result = Preprocess(source);

        Assert.Single(result.Diagnostics);
    }

    [Fact]
    public void Process_IncludeInsideFalseBranch_DoesNotInvokeResolver()
    {
        const string source = "#ifdef FOO\n#include \"Missing.cginc\"\n#endif\nint x;\n";
        var options = new HlslParseOptions(includeResolver: new StubIncludeResolver("Common.cginc", "float unused;"));

        var result = Preprocess(source, options);

        Assert.Empty(result.Diagnostics);
    }

    [Fact]
    public void Process_RepresentativeComputeShader_ExpandsMacrosAndResolvesConditionals()
    {
        const string source = """
            #pragma kernel CSMain
            #define THREADS 8

            #ifdef USE_TEXTURE
            Texture2D<float4> _Tex : register(t0);
            #endif

            RWStructuredBuffer<float> _Result : register(u0);

            [numthreads(THREADS, THREADS, 1)]
            void CSMain(uint3 id : SV_DispatchThreadID)
            {
                _Result[id.x] = 1;
            }
            """;

        var result = Preprocess(source);

        Assert.Equal(["CSMain"], result.KernelNames);
        Assert.Empty(result.Diagnostics);
        Assert.DoesNotContain(result.Tokens, t => t.Kind == TokenKind.Identifier && t.GetText(source) == "_Tex");
        Assert.Contains(result.Tokens, t => t.Kind == TokenKind.IntLiteral && t.GetText(source) == "8");
    }
}
