using TkHLSL.Arena;
using TkHLSL.Diagnostics;
using TkHLSL.Ir;
using TkHLSL.Lexing;
using TkHLSL.Text;

namespace TkHLSL.Syntax;

/// <summary>
///     Converts the Phase 2 <see cref="Preprocessing.Preprocessor" /> output into a <see cref="Module" />
///     IR: resource declarations, <c>struct</c> types, and kernel/helper function signatures. Function
///     and kernel bodies are not parsed — only their token range is recorded (brace-depth counting), per
///     docs/IMPLEMENTATION_PLAN.md §9 Phase 3.
/// </summary>
/// <remarks>
///     Allocation policy: the scan is a single forward pass over the input token list (never copied),
///     mirroring <see cref="Lexer.Tokenize" /> and <see cref="Preprocessing.Preprocessor.Process" />.
///     Keyword/qualifier recognition and register-slot parsing are done via
///     <see cref="TokenExtensions.GetSpan" /> comparisons (zero allocation). A <see cref="string" /> is
///     materialized only where one is structurally required: a declaration's name (stored on the IR
///     node), a type name (the <see cref="UniqueArena{T}" /> key for <see cref="TypeInfo" />), or a
///     diagnostic message.
/// </remarks>
public static class TopLevelParser
{
    private static readonly string[] Qualifiers =
    [
        "static", "const", "inline", "uniform", "groupshared", "volatile", "extern", "precise"
    ];

    public static Module Parse(string source, IReadOnlyList<Token> tokens, IReadOnlyList<string> kernelNames)
    {
        if (source is null) throw new ArgumentNullException(nameof(source));

        if (tokens is null) throw new ArgumentNullException(nameof(tokens));

        if (kernelNames is null) throw new ArgumentNullException(nameof(kernelNames));

        var module = new Module();
        var state = new State(source, tokens, kernelNames, module);
        state.Run();
        return module;
    }

    private sealed class State
    {
        private readonly int _count;
        private readonly Token _eofToken;
        private readonly Module _module;
        private readonly List<string> _pendingKernelNames;
        private readonly string _source;
        private readonly IReadOnlyList<Token> _tokens;

        public State(string source, IReadOnlyList<Token> tokens, IReadOnlyList<string> kernelNames, Module module)
        {
            _source = source;
            _tokens = tokens;
            _module = module;
            _pendingKernelNames = new List<string>(kernelNames);
            _count = tokens.Count;
            _eofToken = _count > 0
                ? tokens[_count - 1]
                : new Token(TokenKind.EndOfFile, new TextSpan(source.Length, 0));
        }

        public void Run()
        {
            var i = 0;
            while (i < _count && At(i).Kind != TokenKind.EndOfFile) i = ParseTopLevelItem(i);

            foreach (var t in _pendingKernelNames)
                AddDiagnostic(
                    DiagnosticSeverity.Warning,
                    $"'#pragma kernel {t}' に対応する [numthreads] 関数が見つかりません。",
                    new TextSpan(_source.Length, 0));
        }

        private Token At(int i)
        {
            return i < _count ? _tokens[i] : _eofToken;
        }

        private int ParseTopLevelItem(int i)
        {
            ThreadGroupSize? threadGroupSize = null;
            while (i < _count && At(i).Kind == TokenKind.OpenBracket) i = ParseAttribute(i, ref threadGroupSize);

            if (i >= _count || At(i).Kind == TokenKind.EndOfFile) return _count;

            var token = At(i);
            if (token.Kind != TokenKind.Identifier)
            {
                AddDiagnostic(DiagnosticSeverity.Error, "トップレベルで予期しないトークンです。", token.Span);
                return SkipToNextTopLevelBoundary(i);
            }

            var text = token.GetSpan(_source);

            if (text.SequenceEqual("struct".AsSpan())) return ParseStruct(i);

            if (text.SequenceEqual("cbuffer".AsSpan())) return ParseCBuffer(i);

            if (TryGetResourceKind(text, out var kind)) return ParseResourceDeclaration(i, kind);

            return ParseTypedDeclaration(i, threadGroupSize);
        }

        // --- Attributes: "[numthreads(x, y, z)]" -----------------------------------------------

        private int ParseAttribute(int i, ref ThreadGroupSize? threadGroupSize)
        {
            var bracketEnd = SkipBalanced(i, TokenKind.OpenBracket, TokenKind.CloseBracket, "属性 '['");
            var c = i + 1;

            if (c >= bracketEnd - 1 || At(c).Kind != TokenKind.Identifier ||
                !At(c).GetSpan(_source).SequenceEqual("numthreads".AsSpan())) return bracketEnd;
            var nameIndex = c;
            c++;

            if (c < bracketEnd - 1 && At(c).Kind == TokenKind.OpenParen)
            {
                var parenEnd = SkipBalanced(c, TokenKind.OpenParen, TokenKind.CloseParen, "'numthreads('");
                var p = c + 1;
                var limit = parenEnd - 1;

                if (TryReadIntArg(ref p, limit, out var x) &&
                    TryExpect(ref p, limit, TokenKind.Comma) &&
                    TryReadIntArg(ref p, limit, out var y) &&
                    TryExpect(ref p, limit, TokenKind.Comma) &&
                    TryReadIntArg(ref p, limit, out var z))
                    threadGroupSize = new ThreadGroupSize(x, y, z);
                else
                    AddDiagnostic(DiagnosticSeverity.Error, "'numthreads(x, y, z)' の引数が不正です。", At(nameIndex).Span);
            }
            else
            {
                AddDiagnostic(DiagnosticSeverity.Error, "'numthreads' には '(' が必要です。", At(nameIndex).Span);
            }

            return bracketEnd;
        }

        private bool TryReadIntArg(ref int p, int limit, out int value)
        {
            if (p < limit && At(p).Kind == TokenKind.IntLiteral)
            {
                value = ParseIntLiteralValue(At(p).GetSpan(_source));
                p++;
                return true;
            }

            value = 0;
            return false;
        }

        private bool TryExpect(ref int p, int limit, TokenKind kind)
        {
            if (p < limit && At(p).Kind == kind)
            {
                p++;
                return true;
            }

            return false;
        }

        // --- struct ------------------------------------------------------------------------------

        private int ParseStruct(int i)
        {
            var keywordToken = At(i);
            i++; // skip 'struct'

            if (i >= _count || At(i).Kind != TokenKind.Identifier)
            {
                AddDiagnostic(DiagnosticSeverity.Error, "'struct' に型名がありません。", keywordToken.Span);
                return SkipToNextTopLevelBoundary(i);
            }

            var nameToken = At(i);
            var name = nameToken.GetText(_source);
            i++;

            if (i >= _count || At(i).Kind != TokenKind.OpenBrace)
            {
                AddDiagnostic(DiagnosticSeverity.Error, $"'struct {name}' に本体 '{{' がありません。", nameToken.Span);
                return SkipToNextTopLevelBoundary(i);
            }

            i = SkipBalanced(i, TokenKind.OpenBrace, TokenKind.CloseBrace, $"'struct {name}'");

            if (i < _count && At(i).Kind == TokenKind.Semicolon) i++;

            _module.Types.Insert(new TypeInfo(name));
            return i;
        }

        // --- cbuffer -----------------------------------------------------------------------------

        private int ParseCBuffer(int i)
        {
            var keywordToken = At(i);
            i++; // skip 'cbuffer'

            if (i >= _count || At(i).Kind != TokenKind.Identifier)
            {
                AddDiagnostic(DiagnosticSeverity.Error, "'cbuffer' に名前がありません。", keywordToken.Span);
                return SkipToNextTopLevelBoundary(i);
            }

            var nameToken = At(i);
            var name = nameToken.GetText(_source);
            i++;

            ResourceRegister? register = null;
            if (i < _count && At(i).Kind == TokenKind.Colon) i = ParseRegister(i + 1, out register);

            if (i >= _count || At(i).Kind != TokenKind.OpenBrace)
            {
                AddDiagnostic(DiagnosticSeverity.Error, $"'cbuffer {name}' に本体 '{{' がありません。", nameToken.Span);
                return SkipToNextTopLevelBoundary(i);
            }

            i = SkipBalanced(i, TokenKind.OpenBrace, TokenKind.CloseBrace, $"'cbuffer {name}'");

            if (i < _count && At(i).Kind == TokenKind.Semicolon) i++;

            _module.GlobalVariables.Add(new GlobalVariable(name, ResourceKind.CBuffer, null, register, nameToken.Span));
            return i;
        }

        // --- register(...) -------------------------------------------------------------------------

        private int ParseRegister(int i, out ResourceRegister? register)
        {
            register = null;

            if (i >= _count || At(i).Kind != TokenKind.Identifier ||
                !At(i).GetSpan(_source).SequenceEqual("register".AsSpan()))
            {
                AddDiagnostic(DiagnosticSeverity.Error, "':' の後に 'register' が必要です。", At(i).Span);
                return i;
            }

            var registerKeywordToken = At(i);
            i++;

            if (i >= _count || At(i).Kind != TokenKind.OpenParen)
            {
                AddDiagnostic(DiagnosticSeverity.Error, "'register' には '(' が必要です。", registerKeywordToken.Span);
                return i;
            }

            var parenEnd = SkipBalanced(i, TokenKind.OpenParen, TokenKind.CloseParen, "'register('");
            var p = i + 1;
            var limit = parenEnd - 1;

            if (p >= limit || At(p).Kind != TokenKind.Identifier)
            {
                AddDiagnostic(DiagnosticSeverity.Error, "'register(...)' にスロット指定がありません。", registerKeywordToken.Span);
                return parenEnd;
            }

            if (!TryParseSlot(At(p).GetSpan(_source), out var slotType, out var slotIndex))
            {
                AddDiagnostic(DiagnosticSeverity.Error, $"不正なレジスタスロット '{At(p).GetText(_source)}' です。", At(p).Span);
                return parenEnd;
            }

            p++;

            int? space = null;
            if (p < limit && At(p).Kind == TokenKind.Comma)
            {
                p++;
                if (p < limit && At(p).Kind == TokenKind.Identifier &&
                    TryParseSpace(At(p).GetSpan(_source), out var spaceValue)) space = spaceValue;
            }

            register = new ResourceRegister(slotType, slotIndex, space);
            return parenEnd;
        }

        // --- resource declarations (Texture2D, StructuredBuffer<T>, SamplerState, ...) ------------

        private int ParseResourceDeclaration(int i, ResourceKind kind)
        {
            var keywordToken = At(i);
            i++;

            Handle<TypeInfo>? elementType = null;
            if (i < _count && At(i).Kind == TokenKind.Less) i = ParseGenericArgument(i, out elementType);

            if (i >= _count || At(i).Kind != TokenKind.Identifier)
            {
                AddDiagnostic(DiagnosticSeverity.Error, $"'{keywordToken.GetText(_source)}' に変数名がありません。",
                    keywordToken.Span);
                return SkipToNextTopLevelBoundary(i);
            }

            var nameToken = At(i);
            var name = nameToken.GetText(_source);
            i++;

            i = SkipArrayBrackets(i);

            ResourceRegister? register = null;
            if (i < _count && At(i).Kind == TokenKind.Colon) i = ParseRegister(i + 1, out register);

            if (i >= _count || At(i).Kind != TokenKind.Semicolon)
            {
                AddDiagnostic(DiagnosticSeverity.Error, $"'{name}' の宣言が ';' で終わっていません。", nameToken.Span);
                return SkipToNextTopLevelBoundary(i);
            }

            i++;

            _module.GlobalVariables.Add(new GlobalVariable(name, kind, elementType, register, nameToken.Span));
            return i;
        }

        private int ParseGenericArgument(int i, out Handle<TypeInfo>? elementType)
        {
            var lessToken = At(i);
            i++; // skip '<'

            elementType = null;
            if (i >= _count || At(i).Kind != TokenKind.Identifier)
            {
                AddDiagnostic(DiagnosticSeverity.Error, "'<' にテンプレート引数の型名がありません。", lessToken.Span);
                return i;
            }

            elementType = _module.Types.Insert(new TypeInfo(At(i).GetText(_source)));
            i++;

            if (i < _count && At(i).Kind == TokenKind.Greater)
                i++;
            else
                AddDiagnostic(DiagnosticSeverity.Error, "テンプレート引数を閉じる '>' がありません。", lessToken.Span);

            return i;
        }

        private int SkipArrayBrackets(int i)
        {
            while (i < _count && At(i).Kind == TokenKind.OpenBracket)
                i = SkipBalanced(i, TokenKind.OpenBracket, TokenKind.CloseBracket, "配列宣言子 '['");
            return i;
        }

        // --- typed declarations: plain globals and functions (kernel or helper) -------------------

        private int ParseTypedDeclaration(int i, ThreadGroupSize? threadGroupSize)
        {
            var declStartToken = At(i);
            i = SkipQualifiers(i);

            if (i >= _count || At(i).Kind != TokenKind.Identifier)
            {
                AddDiagnostic(DiagnosticSeverity.Error, "宣言に型がありません。", declStartToken.Span);
                return SkipToNextTopLevelBoundary(i);
            }

            var typeToken = At(i);
            var typeName = typeToken.GetText(_source);
            i++;

            if (i >= _count || At(i).Kind != TokenKind.Identifier)
            {
                AddDiagnostic(DiagnosticSeverity.Error, $"'{typeName}' の後に名前がありません。", typeToken.Span);
                return SkipToNextTopLevelBoundary(i);
            }

            var nameToken = At(i);
            var name = nameToken.GetText(_source);
            i++;

            if (i < _count && At(i).Kind == TokenKind.OpenParen)
                return ParseFunctionDeclaration(i, name, nameToken, threadGroupSize);

            return ParseGlobalVariableDeclaration(i, typeName, name, nameToken);
        }

        private int SkipQualifiers(int i)
        {
            while (i < _count && At(i).Kind == TokenKind.Identifier && IsQualifier(At(i).GetSpan(_source))) i++;
            return i;
        }

        private static bool IsQualifier(ReadOnlySpan<char> text)
        {
            foreach (var t in Qualifiers)
                if (text.SequenceEqual(t.AsSpan()))
                    return true;

            return false;
        }

        private int ParseFunctionDeclaration(int i, string name, Token nameToken, ThreadGroupSize? threadGroupSize)
        {
            i = SkipBalanced(i, TokenKind.OpenParen, TokenKind.CloseParen, $"'{name}(' のパラメータリスト");

            if (i < _count && At(i).Kind == TokenKind.Colon)
            {
                i++; // skip ':'
                if (i < _count &&
                    At(i).Kind == TokenKind.Identifier) i++; // skip return-value semantic (e.g. ': SV_Target')
            }

            if (i < _count && At(i).Kind == TokenKind.Semicolon)
                // Prototype-only declaration ("<ret> name(params);") — known limitation: the
                // prototype is not registered, only the later definition is (see
                // docs/IMPLEMENTATION_PLAN.md §9 Phase 3).
                return i + 1;

            if (i >= _count || At(i).Kind != TokenKind.OpenBrace)
            {
                AddDiagnostic(DiagnosticSeverity.Error, $"関数 '{name}' に本体がありません。", nameToken.Span);
                return SkipToNextTopLevelBoundary(i);
            }

            var bodyStart = i;
            var bodyEnd = SkipBalanced(i, TokenKind.OpenBrace, TokenKind.CloseBrace, $"関数 '{name}' の本体");
            var bodyRange = new TokenRange(bodyStart, bodyEnd);

            if (threadGroupSize is { } size)
            {
                if (RemovePendingKernelName(name))
                {
                    var kernelFunction = new Function(name, bodyRange, nameToken.Span);
                    _module.EntryPoints.Add(new EntryPoint(name, size, kernelFunction, nameToken.Span));
                    return bodyEnd;
                }

                AddDiagnostic(DiagnosticSeverity.Warning, $"'[numthreads]' 関数 '{name}' に対応する '#pragma kernel' がありません。",
                    nameToken.Span);
            }

            _module.Functions.Add(new Function(name, bodyRange, nameToken.Span));
            return bodyEnd;
        }

        private bool RemovePendingKernelName(string name)
        {
            for (var k = 0; k < _pendingKernelNames.Count; k++)
                if (string.Equals(_pendingKernelNames[k], name, StringComparison.Ordinal))
                {
                    _pendingKernelNames.RemoveAt(k);
                    return true;
                }

            return false;
        }

        private int ParseGlobalVariableDeclaration(int i, string typeName, string name, Token nameToken)
        {
            var elementType = _module.Types.Insert(new TypeInfo(typeName));

            while (true)
            {
                i = SkipArrayBrackets(i);
                i = SkipInitializer(i);

                _module.GlobalVariables.Add(new GlobalVariable(name, ResourceKind.PlainGlobal, elementType, null,
                    nameToken.Span));

                if (i >= _count || At(i).Kind != TokenKind.Comma) break;
                i++; // skip ','

                if (i >= _count || At(i).Kind != TokenKind.Identifier)
                {
                    AddDiagnostic(DiagnosticSeverity.Error, $"'{typeName}' の宣言子リストに変数名がありません。", nameToken.Span);
                    return SkipToNextTopLevelBoundary(i);
                }

                nameToken = At(i);
                name = nameToken.GetText(_source);
                i++;
            }

            if (i < _count && At(i).Kind == TokenKind.Semicolon) return i + 1;
            AddDiagnostic(DiagnosticSeverity.Error, $"'{name}' の宣言が ';' で終わっていません。", nameToken.Span);
            return SkipToNextTopLevelBoundary(i);
        }

        private int SkipInitializer(int i)
        {
            if (i >= _count || At(i).Kind != TokenKind.Assign) return i;
            i++;

            var depth = 0;
            while (i < _count)
            {
                var kind = At(i).Kind;
                if (kind == TokenKind.EndOfFile) break;

                if (depth == 0 && kind is TokenKind.Semicolon or TokenKind.Comma) break;

                switch (kind)
                {
                    case TokenKind.OpenParen or TokenKind.OpenBrace or TokenKind.OpenBracket:
                        depth++;
                        break;
                    case TokenKind.CloseParen or TokenKind.CloseBrace or TokenKind.CloseBracket:
                    {
                        if (depth > 0)
                            depth--;
                        break;
                    }
                }

                i++;
            }

            return i;
        }

        // --- shared helpers ------------------------------------------------------------------------

        /// <summary>
        ///     Skips a balanced bracket pair, tracking nesting depth of <paramref name="open" />/
        ///     <paramref name="close" /> only (mirrors the brace-depth counting used for function bodies,
        ///     see docs/IMPLEMENTATION_PLAN.md §9 Phase 3). <paramref name="openIndex" /> must be the index
        ///     of the opening token; returns the index just past the matching closing token.
        /// </summary>
        private int SkipBalanced(int openIndex, TokenKind open, TokenKind close, string what)
        {
            var depth = 0;
            var i = openIndex;
            while (i < _count)
            {
                var kind = At(i).Kind;
                if (kind == TokenKind.EndOfFile) break;

                if (kind == open)
                {
                    depth++;
                }
                else if (kind == close)
                {
                    depth--;
                    if (depth == 0) return i + 1;
                }

                i++;
            }

            AddDiagnostic(DiagnosticSeverity.Error, $"対応する閉じ括弧が見つかりません（{what} に対応）。", At(openIndex).Span);
            return _count;
        }

        /// <summary>
        ///     Recovery for a malformed top-level declaration: scans forward (tracking
        ///     paren/brace/bracket depth so nested punctuation is not mistaken for the boundary) to the
        ///     next top-level ';', consuming it, or to end of file.
        /// </summary>
        private int SkipToNextTopLevelBoundary(int i)
        {
            var depth = 0;
            while (i < _count)
            {
                var kind = At(i).Kind;
                switch (kind)
                {
                    case TokenKind.EndOfFile:
                        return i;
                    case TokenKind.OpenParen or TokenKind.OpenBrace or TokenKind.OpenBracket:
                        depth++;
                        break;
                    case TokenKind.CloseParen or TokenKind.CloseBrace or TokenKind.CloseBracket:
                    {
                        if (depth > 0) depth--;
                        break;
                    }
                    case TokenKind.Semicolon when depth == 0:
                        return i + 1;
                }

                i++;
            }

            return i;
        }

        private static bool TryGetResourceKind(ReadOnlySpan<char> text, out ResourceKind kind)
        {
            if (text.SequenceEqual("Texture2D".AsSpan()))
            {
                kind = ResourceKind.Texture2D;
                return true;
            }

            if (text.SequenceEqual("Texture2DArray".AsSpan()))
            {
                kind = ResourceKind.Texture2DArray;
                return true;
            }

            if (text.SequenceEqual("Texture3D".AsSpan()))
            {
                kind = ResourceKind.Texture3D;
                return true;
            }

            if (text.SequenceEqual("TextureCube".AsSpan()))
            {
                kind = ResourceKind.TextureCube;
                return true;
            }

            if (text.SequenceEqual("TextureCubeArray".AsSpan()))
            {
                kind = ResourceKind.TextureCubeArray;
                return true;
            }

            if (text.SequenceEqual("RWTexture2D".AsSpan()))
            {
                kind = ResourceKind.RWTexture2D;
                return true;
            }

            if (text.SequenceEqual("RWTexture2DArray".AsSpan()))
            {
                kind = ResourceKind.RWTexture2DArray;
                return true;
            }

            if (text.SequenceEqual("RWTexture3D".AsSpan()))
            {
                kind = ResourceKind.RWTexture3D;
                return true;
            }

            if (text.SequenceEqual("StructuredBuffer".AsSpan()))
            {
                kind = ResourceKind.StructuredBuffer;
                return true;
            }

            if (text.SequenceEqual("RWStructuredBuffer".AsSpan()))
            {
                kind = ResourceKind.RWStructuredBuffer;
                return true;
            }

            if (text.SequenceEqual("AppendStructuredBuffer".AsSpan()))
            {
                kind = ResourceKind.AppendStructuredBuffer;
                return true;
            }

            if (text.SequenceEqual("ConsumeStructuredBuffer".AsSpan()))
            {
                kind = ResourceKind.ConsumeStructuredBuffer;
                return true;
            }

            if (text.SequenceEqual("ByteAddressBuffer".AsSpan()))
            {
                kind = ResourceKind.ByteAddressBuffer;
                return true;
            }

            if (text.SequenceEqual("RWByteAddressBuffer".AsSpan()))
            {
                kind = ResourceKind.RWByteAddressBuffer;
                return true;
            }

            if (text.SequenceEqual("ConstantBuffer".AsSpan()))
            {
                kind = ResourceKind.ConstantBuffer;
                return true;
            }

            if (text.SequenceEqual("SamplerState".AsSpan()))
            {
                kind = ResourceKind.SamplerState;
                return true;
            }

            if (text.SequenceEqual("SamplerComparisonState".AsSpan()))
            {
                kind = ResourceKind.SamplerComparisonState;
                return true;
            }

            kind = default;
            return false;
        }

        private static bool TryParseSlot(ReadOnlySpan<char> text, out char slotType, out int slotIndex)
        {
            slotType = '\0';
            slotIndex = 0;

            if (text.Length < 2) return false;

            var first = text[0];
            if (first is not ('t' or 'u' or 'b' or 's' or 'T' or 'U' or 'B' or 'S')) return false;

            var digits = text.Slice(1);
            if (!TryParseDecimal(digits, out slotIndex)) return false;

            slotType = char.ToLowerInvariant(first);
            return true;
        }

        private static bool TryParseSpace(ReadOnlySpan<char> text, out int value)
        {
            value = 0;
            if (text.Length <= 5 || !text.Slice(0, 5).SequenceEqual("space".AsSpan())) return false;

            return TryParseDecimal(text.Slice(5), out value);
        }

        private static bool TryParseDecimal(ReadOnlySpan<char> digits, out int value)
        {
            value = 0;
            if (digits.Length == 0) return false;

            foreach (var c in digits)
            {
                if (c is < '0' or > '9') return false;
                value = value * 10 + (c - '0');
            }

            return true;
        }

        private static int ParseIntLiteralValue(ReadOnlySpan<char> text)
        {
            if (text.Length >= 2 && text[0] == '0' && (text[1] == 'x' || text[1] == 'X'))
            {
                var value = 0;
                for (var k = 2; k < text.Length; k++)
                {
                    var digit = text[k] switch
                    {
                        >= '0' and <= '9' => text[k] - '0',
                        >= 'a' and <= 'f' => text[k] - 'a' + 10,
                        >= 'A' and <= 'F' => text[k] - 'A' + 10,
                        _ => -1
                    };
                    if (digit < 0) break;
                    value = value * 16 + digit;
                }

                return value;
            }

            var result = 0;
            foreach (var c in text)
            {
                if (c is < '0' or > '9') break;
                result = result * 10 + (c - '0');
            }

            return result;
        }

        private void AddDiagnostic(DiagnosticSeverity severity, string message, TextSpan span)
        {
            _module.Diagnostics.Add(new Diagnostic(severity, message, span));
        }
    }
}