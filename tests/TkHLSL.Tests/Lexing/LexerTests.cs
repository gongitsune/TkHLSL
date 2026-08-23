using TkHLSL.Lexing;

namespace TkHLSL.Tests.Lexing;

public class LexerTests
{
    [Fact]
    public void Tokenize_EmptySource_YieldsOnlyEndOfFile()
    {
        var result = Lexer.Tokenize(string.Empty);

        var kind = Assert.Single(result.Tokens).Kind;
        Assert.Equal(TokenKind.EndOfFile, kind);
        Assert.Empty(result.Diagnostics);
    }

    [Fact]
    public void Tokenize_NullSource_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => Lexer.Tokenize(null!));
    }

    [Fact]
    public void Tokenize_Identifiers_AreClassifiedWithoutKeywordInterpretation()
    {
        const string source = "RWStructuredBuffer buf struct";

        var result = Lexer.Tokenize(source);

        var identifiers = result.Tokens.Where(t => t.Kind == TokenKind.Identifier).ToList();
        Assert.Equal(3, identifiers.Count);
        Assert.Equal("RWStructuredBuffer", identifiers[0].GetText(source));
        Assert.Equal("buf", identifiers[1].GetText(source));
        Assert.Equal("struct", identifiers[2].GetText(source));
    }

    [Fact]
    public void GetSpan_MatchesGetText_WithoutAllocatingAString()
    {
        const string source = "RWStructuredBuffer buf";

        var result = Lexer.Tokenize(source);
        var identifier = Assert.Single(result.Tokens, t => t.Kind == TokenKind.Identifier && t.Span.Start == 0);

        Assert.True(identifier.GetSpan(source).SequenceEqual("RWStructuredBuffer".AsSpan()));
        Assert.Equal(identifier.GetText(source), identifier.GetSpan(source).ToString());
    }

    [Theory]
    [InlineData("0", TokenKind.IntLiteral)]
    [InlineData("123", TokenKind.IntLiteral)]
    [InlineData("10u", TokenKind.IntLiteral)]
    [InlineData("10U", TokenKind.IntLiteral)]
    [InlineData("0x1F", TokenKind.IntLiteral)]
    [InlineData("0x1Fu", TokenKind.IntLiteral)]
    [InlineData("1.5", TokenKind.FloatLiteral)]
    [InlineData(".5f", TokenKind.FloatLiteral)]
    [InlineData("1f", TokenKind.FloatLiteral)]
    [InlineData("1h", TokenKind.FloatLiteral)]
    [InlineData("1e-3", TokenKind.FloatLiteral)]
    [InlineData("1e3f", TokenKind.FloatLiteral)]
    [InlineData("1.0h", TokenKind.FloatLiteral)]
    public void Tokenize_NumberLiteral_ClassifiedAndSpansWholeLiteral(string literal, TokenKind expectedKind)
    {
        var result = Lexer.Tokenize(literal);

        var token = Assert.Single(result.Tokens, t => t.Kind != TokenKind.EndOfFile);
        Assert.Equal(expectedKind, token.Kind);
        Assert.Equal(literal, token.GetText(literal));
        Assert.Empty(result.Diagnostics);
    }

    [Fact]
    public void Tokenize_StringLiteral_IncludesQuotesInSpan()
    {
        const string source = "#include \"UnityCG.cginc\"";

        var result = Lexer.Tokenize(source);

        var stringToken = Assert.Single(result.Tokens, t => t.Kind == TokenKind.StringLiteral);
        Assert.Equal("\"UnityCG.cginc\"", stringToken.GetText(source));
        Assert.Empty(result.Diagnostics);
    }

    [Fact]
    public void Tokenize_UnterminatedStringLiteral_ProducesErrorTokenAndDiagnosticInsteadOfThrowing()
    {
        const string source = "#include \"abc";

        var result = Lexer.Tokenize(source);

        var errorToken = Assert.Single(result.Tokens, t => t.Kind == TokenKind.Error);
        Assert.Equal("\"abc", errorToken.GetText(source));
        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(TkHLSL.Diagnostics.DiagnosticSeverity.Error, diagnostic.Severity);
    }

    [Fact]
    public void Tokenize_UnterminatedStringLiteral_StopsAtNewLine()
    {
        const string source = "\"abc\ndef";

        var result = Lexer.Tokenize(source);

        var errorToken = Assert.Single(result.Tokens, t => t.Kind == TokenKind.Error);
        Assert.Equal("\"abc", errorToken.GetText(source));
        Assert.Single(result.Diagnostics);
    }

    [Fact]
    public void Tokenize_UnterminatedBlockComment_ProducesErrorTokenAndDiagnostic()
    {
        const string source = "/* comment never closes";

        var result = Lexer.Tokenize(source);

        var errorToken = Assert.Single(result.Tokens, t => t.Kind == TokenKind.Error);
        Assert.Equal(source, errorToken.GetText(source));
        Assert.Single(result.Diagnostics);
    }

    [Fact]
    public void Tokenize_LineAndBlockComments_ArePreservedAsTokens()
    {
        const string source = "// line comment\n/* block */";

        var result = Lexer.Tokenize(source);

        var lineComment = Assert.Single(result.Tokens, t => t.Kind == TokenKind.LineComment);
        Assert.Equal("// line comment", lineComment.GetText(source));

        var blockComment = Assert.Single(result.Tokens, t => t.Kind == TokenKind.BlockComment);
        Assert.Equal("/* block */", blockComment.GetText(source));
    }

    [Fact]
    public void Tokenize_UnrecognizedCharacter_ProducesErrorTokenAndDiagnosticInsteadOfThrowing()
    {
        const string source = "int x = 1 @ 2;";

        var result = Lexer.Tokenize(source);

        var errorToken = Assert.Single(result.Tokens, t => t.Kind == TokenKind.Error);
        Assert.Equal("@", errorToken.GetText(source));
        Assert.Single(result.Diagnostics);
    }

    [Fact]
    public void Tokenize_NewLines_AreEmittedAsSeparateTokens()
    {
        const string source = "a\nb\nc";

        var result = Lexer.Tokenize(source);

        Assert.Equal(2, result.Tokens.Count(t => t.Kind == TokenKind.NewLine));
    }

    [Fact]
    public void Tokenize_BackslashNewLineLineContinuation_DoesNotEmitNewLineToken()
    {
        const string source = "#define FOO \\\n1";

        var result = Lexer.Tokenize(source);

        Assert.DoesNotContain(result.Tokens, t => t.Kind == TokenKind.NewLine);
        Assert.DoesNotContain(result.Tokens, t => t.Kind == TokenKind.Error);
    }

    [Fact]
    public void Tokenize_HashPragmaLine_ProducesHashFollowedByIdentifiers()
    {
        const string source = "#pragma kernel CSMain";

        var result = Lexer.Tokenize(source);

        var kinds = result.Tokens.Select(t => t.Kind).ToArray();
        Assert.Equal(
            [TokenKind.Hash, TokenKind.Identifier, TokenKind.Identifier, TokenKind.Identifier, TokenKind.EndOfFile],
            kinds);
        Assert.Equal("pragma", result.Tokens[1].GetText(source));
        Assert.Equal("kernel", result.Tokens[2].GetText(source));
        Assert.Equal("CSMain", result.Tokens[3].GetText(source));
    }

    [Theory]
    [InlineData("+", TokenKind.Plus)]
    [InlineData("++", TokenKind.Increment)]
    [InlineData("+=", TokenKind.PlusAssign)]
    [InlineData("-", TokenKind.Minus)]
    [InlineData("--", TokenKind.Decrement)]
    [InlineData("-=", TokenKind.MinusAssign)]
    [InlineData("*", TokenKind.Star)]
    [InlineData("*=", TokenKind.StarAssign)]
    [InlineData("/", TokenKind.Slash)]
    [InlineData("/=", TokenKind.SlashAssign)]
    [InlineData("%", TokenKind.Percent)]
    [InlineData("%=", TokenKind.PercentAssign)]
    [InlineData("=", TokenKind.Assign)]
    [InlineData("==", TokenKind.Equal)]
    [InlineData("!", TokenKind.Bang)]
    [InlineData("!=", TokenKind.NotEqual)]
    [InlineData("<", TokenKind.Less)]
    [InlineData("<=", TokenKind.LessEqual)]
    [InlineData("<<", TokenKind.LeftShift)]
    [InlineData("<<=", TokenKind.LeftShiftAssign)]
    [InlineData(">", TokenKind.Greater)]
    [InlineData(">=", TokenKind.GreaterEqual)]
    [InlineData(">>", TokenKind.RightShift)]
    [InlineData(">>=", TokenKind.RightShiftAssign)]
    [InlineData("&", TokenKind.Amp)]
    [InlineData("&&", TokenKind.AmpAmp)]
    [InlineData("&=", TokenKind.AmpAssign)]
    [InlineData("|", TokenKind.Pipe)]
    [InlineData("||", TokenKind.PipePipe)]
    [InlineData("|=", TokenKind.PipeAssign)]
    [InlineData("^", TokenKind.Caret)]
    [InlineData("^=", TokenKind.CaretAssign)]
    [InlineData("~", TokenKind.Tilde)]
    [InlineData("?", TokenKind.Question)]
    [InlineData(".", TokenKind.Dot)]
    [InlineData(",", TokenKind.Comma)]
    [InlineData(";", TokenKind.Semicolon)]
    [InlineData(":", TokenKind.Colon)]
    [InlineData("(", TokenKind.OpenParen)]
    [InlineData(")", TokenKind.CloseParen)]
    [InlineData("{", TokenKind.OpenBrace)]
    [InlineData("}", TokenKind.CloseBrace)]
    [InlineData("[", TokenKind.OpenBracket)]
    [InlineData("]", TokenKind.CloseBracket)]
    public void Tokenize_Operator_ClassifiedAsWholeToken(string op, TokenKind expectedKind)
    {
        var result = Lexer.Tokenize(op);

        var token = Assert.Single(result.Tokens, t => t.Kind != TokenKind.EndOfFile);
        Assert.Equal(expectedKind, token.Kind);
        Assert.Equal(op, token.GetText(op));
    }

    [Fact]
    public void Tokenize_RepresentativeComputeShader_ProducesNoErrorTokens()
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

        var result = Lexer.Tokenize(source);

        Assert.DoesNotContain(result.Tokens, t => t.Kind == TokenKind.Error);
        Assert.Empty(result.Diagnostics);
        Assert.Equal(TokenKind.EndOfFile, result.Tokens[^1].Kind);
        Assert.Contains(result.Tokens, t => t.Kind == TokenKind.Hash);
        Assert.Contains(result.Tokens, t => t.Kind == TokenKind.FloatLiteral);
        Assert.Contains(result.Tokens, t => t.Kind == TokenKind.IntLiteral);
    }
}
