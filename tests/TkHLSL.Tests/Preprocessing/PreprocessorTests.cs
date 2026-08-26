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

    private static string TextOf(PreprocessResult result) =>
        string.Concat(result.Tokens
            .Where(t => t.Kind != TokenKind.EndOfFile)
            .Select(t => t.GetText(result.Source) + " "));

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
    public void Process_DefinedMacro_IsVisibleToIfdef()
    {
        const string source = "#define FOO\n#ifdef FOO\nint x;\n#endif\n";

        var result = Preprocess(source);

        Assert.Equal("int x ; ", TextOf(result, source));
        Assert.Empty(result.Diagnostics);
    }

    [Fact]
    public void Process_UndefinedMacro_IsNoLongerVisibleToIfdef()
    {
        const string source = "#define FOO\n#undef FOO\n#ifdef FOO\nint x;\n#endif\n";

        var result = Preprocess(source);

        Assert.Equal("", TextOf(result, source));
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

    /// <summary>A resolver over an in-memory file set, keyed by the requested path (no relative resolution).</summary>
    private sealed class MultiIncludeResolver(Dictionary<string, string> files) : IIncludeResolver
    {
        public bool TryResolve(string requestedPath, string? includerPath, out string? resolvedPath, out string? content)
        {
            if (files.TryGetValue(requestedPath, out var found))
            {
                resolvedPath = requestedPath;
                content = found;
                return true;
            }

            resolvedPath = null;
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
        Assert.Single(result.Source.Segments);
    }

    [Fact]
    public void Process_Include_SplicesContentIntoOutput()
    {
        const string source = "#include \"Common.cginc\"\nint x;\n";
        var options = new HlslParseOptions(includeResolver: new StubIncludeResolver("Common.cginc", "float y;"));

        var result = Preprocess(source, options);

        Assert.Equal("float y ; int x ; ", TextOf(result));
    }

    [Fact]
    public void Process_NestedInclude_SplicesBothFiles()
    {
        const string source = "#include \"A.cginc\"\nint root;\n";
        var options = new HlslParseOptions(includeResolver: new MultiIncludeResolver(new Dictionary<string, string>
        {
            ["A.cginc"] = "#include \"B.cginc\"\nfloat a;\n",
            ["B.cginc"] = "float b;\n",
        }));

        var result = Preprocess(source, options);

        Assert.Empty(result.Diagnostics);
        Assert.Equal("float b ; float a ; int root ; ", TextOf(result));
    }

    [Fact]
    public void Process_MacroDefinedInInclude_IsVisibleInIncluder()
    {
        const string source = "#include \"Common.cginc\"\nint arr[THREADS];\n";
        var options = new HlslParseOptions(includeResolver: new StubIncludeResolver("Common.cginc", "#define THREADS 8\n"));

        var result = Preprocess(source, options);

        Assert.Contains(result.Tokens, t => t.Kind == TokenKind.IntLiteral && t.GetText(result.Source) == "8");
    }

    [Fact]
    public void Process_MacroFromIncludeExpandsToIncludedFileSpan()
    {
        const string source = "#include \"Common.cginc\"\nTHREADS\n";
        var options = new HlslParseOptions(includeResolver: new StubIncludeResolver("Common.cginc", "#define THREADS 8\n"));

        var result = Preprocess(source, options);

        var token = Assert.Single(result.Tokens, t => t.Kind == TokenKind.IntLiteral);
        Assert.Equal("8", token.GetText(result.Source));
        Assert.True(result.Source.TryGetLocation(token.Span.Start, out var segment, out _));
        Assert.Equal("Common.cginc", segment.Path);
    }

    [Fact]
    public void Process_IncludeGuard_PreventsDoubleDefinition()
    {
        const string source = "#include \"Common.cginc\"\n#include \"Common.cginc\"\n";
        var options = new HlslParseOptions(includeResolver: new StubIncludeResolver("Common.cginc",
            "#ifndef COMMON_INCLUDED\n#define COMMON_INCLUDED\nint g;\n#endif\n"));

        var result = Preprocess(source, options);

        Assert.Empty(result.Diagnostics);
        Assert.Equal("int g ; ", TextOf(result));
    }

    [Fact]
    public void Process_PragmaOnce_PreventsDoubleInclusion()
    {
        const string source = "#include \"Common.cginc\"\n#include \"Common.cginc\"\n";
        var options = new HlslParseOptions(includeResolver: new StubIncludeResolver("Common.cginc",
            "#pragma once\nint g;\n"));

        var result = Preprocess(source, options);

        Assert.Empty(result.Diagnostics);
        Assert.Equal("int g ; ", TextOf(result));
    }

    [Fact]
    public void Process_SelfIncludingFile_ProducesCycleDiagnosticAndTerminates()
    {
        const string source = "#include \"A.cginc\"\n";
        var options = new HlslParseOptions(includeResolver: new MultiIncludeResolver(new Dictionary<string, string>
        {
            ["A.cginc"] = "#include \"A.cginc\"\nint a;\n",
        }));

        var result = Preprocess(source, options);

        Assert.Single(result.Diagnostics);
        Assert.Equal("int a ; ", TextOf(result));
    }

    [Fact]
    public void Process_MutualIncludeCycle_ProducesCycleDiagnosticAndTerminates()
    {
        const string source = "#include \"A.cginc\"\n";
        var options = new HlslParseOptions(includeResolver: new MultiIncludeResolver(new Dictionary<string, string>
        {
            ["A.cginc"] = "#include \"B.cginc\"\nint a;\n",
            ["B.cginc"] = "#include \"A.cginc\"\nint b;\n",
        }));

        var result = Preprocess(source, options);

        Assert.Single(result.Diagnostics);
        Assert.Equal("int b ; int a ; ", TextOf(result));
    }

    [Fact]
    public void Process_IncludeDepthExceedsCap_ProducesDiagnostic()
    {
        const string source = "#include \"Chain.cginc\"\n";
        var options = new HlslParseOptions(
            includeResolver: new MultiIncludeResolver(new Dictionary<string, string>
            {
                ["Chain.cginc"] = "#include \"Chain2.cginc\"\n",
                ["Chain2.cginc"] = "#include \"Chain3.cginc\"\n",
                ["Chain3.cginc"] = "#include \"Chain4.cginc\"\n",
                ["Chain4.cginc"] = "int deep;\n",
            }),
            maxIncludeDepth: 2);

        var result = Preprocess(source, options);

        Assert.Single(result.Diagnostics);
        Assert.DoesNotContain("deep", TextOf(result));
    }

    [Fact]
    public void Process_UnterminatedIfInInclude_ReportsAgainstIncludeSpanAndIncluderContinues()
    {
        const string source = "#include \"Common.cginc\"\nint after;\n";
        var options = new HlslParseOptions(includeResolver: new StubIncludeResolver("Common.cginc", "#ifdef FOO\nint x;\n"));

        var result = Preprocess(source, options);

        Assert.Single(result.Diagnostics);
        Assert.True(result.Source.TryGetLocation(result.Diagnostics[0].Span.Start, out var segment, out _));
        Assert.Equal("Common.cginc", segment.Path);
        Assert.Contains(result.Tokens, t => t.Kind == TokenKind.Identifier && t.GetText(result.Source) == "after");
    }

    [Fact]
    public void Process_EndifInIncludeCannotCloseIncludersIf()
    {
        const string source = "#ifdef FOO\n#include \"Common.cginc\"\nint stillOpen;\n#endif\n";
        var options = new HlslParseOptions(["FOO"],
            includeResolver: new StubIncludeResolver("Common.cginc", "#endif\nint fromInclude;\n"));

        var result = Preprocess(source, options);

        // The bare #endif inside the include is unmatched (reported), and the includer's own
        // #ifdef FOO/#endif still balances correctly, so `stillOpen` is still emitted.
        Assert.NotEmpty(result.Diagnostics);
        Assert.Contains(result.Tokens, t => t.Kind == TokenKind.Identifier && t.GetText(result.Source) == "stillOpen");
    }

    [Fact]
    public void Process_DiagnosticInIncludedFile_PointsIntoIncludedContent()
    {
        const string source = "#include \"Common.cginc\"\n";
        var options = new HlslParseOptions(includeResolver: new StubIncludeResolver("Common.cginc", "#unknowndirective\n"));

        var result = Preprocess(source, options);

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.True(result.Source.TryGetLocation(diagnostic.Span.Start, out var segment, out var offsetInFile));
        Assert.Equal("Common.cginc", segment.Path);
        Assert.True(offsetInFile >= 0);
    }

    [Fact]
    public void Process_LexErrorInIncludedFile_IsReportedWithShiftedSpan()
    {
        const string source = "#include \"Common.cginc\"\n";
        var options = new HlslParseOptions(includeResolver: new StubIncludeResolver("Common.cginc", "\"unterminated"));

        var result = Preprocess(source, options);

        Assert.NotEmpty(result.Diagnostics);
        var diagnostic = result.Diagnostics[0];
        Assert.True(result.Source.TryGetLocation(diagnostic.Span.Start, out var segment, out _));
        Assert.Equal("Common.cginc", segment.Path);
    }

    [Fact]
    public void Process_NoIncludes_SourceTextIsRootIdentity()
    {
        const string source = "int x;\n";

        var result = Preprocess(source);

        Assert.Same(source, result.Source.Text);
    }

    [Fact]
    public void Process_EofToken_IsAtCompositeEnd()
    {
        const string source = "#include \"Common.cginc\"\nint x;\n";
        var options = new HlslParseOptions(includeResolver: new StubIncludeResolver("Common.cginc", "float y;"));

        var result = Preprocess(source, options);

        var eof = result.Tokens[^1];
        Assert.Equal(TokenKind.EndOfFile, eof.Kind);
        Assert.Equal(result.Source.Text.Length, eof.Span.Start);
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
