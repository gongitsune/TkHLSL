using TkHLSL.Diagnostics;
using TkHLSL.Lexing;
using TkHLSL.Text;

namespace TkHLSL.Preprocessing;

/// <summary>
///     Resolves <c>#pragma</c>/<c>#define</c>/<c>#undef</c>/<c>#if</c> family/<c>#include</c>
///     directives over a raw <see cref="Lexer" /> token stream, producing the final token stream the
///     Phase 3 parser consumes.
/// </summary>
/// <remarks>
///     <para>
///         Allocation policy: the scan is a single forward pass over the input token list (itself never
///         copied), mirroring <see cref="Lexer.Tokenize" />. Directive keywords and macro identifiers are
///         matched via <see cref="TokenExtensions.GetSpan" /> comparisons (zero allocation); a
///         <see cref="string" /> is materialized only at the (comparatively rare) points where one is
///         structurally required — a macro's name for the macro table, a symbol name for the
///         <see cref="HlslParseOptions.DefinedSymbols" /> lookup, or a diagnostic message. Object macros
///         with a non-empty body copy their captured <see cref="Token" /> values (two ints each) at
///         expansion sites; expansion is a single non-recursive substitution — a macro whose body
///         references another macro name is not expanded further, a documented limitation shared with
///         <c>multi_compile</c>/<c>shader_feature</c> variant expansion, which this phase does not perform.
///     </para>
///     <para>
///         <c>#include</c> resolution is delegated to <see cref="IIncludeResolver" />, but the resolved
///         content is only checked for resolvability here — it is not tokenized or spliced into the output
///         stream. Doing so safely would require every <see cref="Token" /> to know which source string its
///         <see cref="TextSpan" /> applies to, which the current single-source-string design does not
///         support (see docs/IMPLEMENTATION_PLAN.md §13, "Unity 組込み include の扱い").
///     </para>
/// </remarks>
public static class Preprocessor
{
    public static PreprocessResult Process(string source, IReadOnlyList<Token> tokens, HlslParseOptions options)
    {
        if (source is null) throw new ArgumentNullException(nameof(source));

        if (tokens is null) throw new ArgumentNullException(nameof(tokens));

        if (options is null) throw new ArgumentNullException(nameof(options));

        var state = new State(source, tokens, options);
        state.Run();
        state.Output.Add(new Token(TokenKind.EndOfFile, new TextSpan(source.Length, 0)));

        IReadOnlyList<Diagnostic> diagnostics = state.Diagnostics is null
            ? Array.Empty<Diagnostic>()
            : state.Diagnostics;

        return new PreprocessResult(state.Output, state.KernelNames, diagnostics);
    }

    private readonly struct MacroDefinition(string name, Token[] body)
    {
        public string Name { get; } = name;

        public Token[] Body { get; } = body;
    }

    private struct ConditionalFrame(bool parentEmitting, bool branchActive, bool anyBranchTaken, TextSpan directiveSpan)
    {
        public readonly bool ParentEmitting = parentEmitting;
        public bool BranchActive = branchActive;
        public bool AnyBranchTaken = anyBranchTaken;
        public readonly TextSpan DirectiveSpan = directiveSpan;
    }

    private sealed class State(string source, IReadOnlyList<Token> tokens, HlslParseOptions options)
    {
        private readonly List<ConditionalFrame> _conditionalStack = [];
        private readonly List<MacroDefinition> _macros = [];

        public List<Token> Output { get; } = new(tokens.Count);

        public List<string> KernelNames { get; } = [];

        public List<Diagnostic>? Diagnostics { get; private set; }

        public void Run()
        {
            var count = tokens.Count;
            var i = 0;
            var atLineStart = true;

            while (i < count)
            {
                var token = tokens[i];
                switch (token.Kind)
                {
                    case TokenKind.EndOfFile:
                        i = count;
                        continue;
                    case TokenKind.NewLine:
                        atLineStart = true;
                        i++;
                        continue;
                    case TokenKind.LineComment:
                    case TokenKind.BlockComment:
                        i++;
                        continue;
                    case TokenKind.Hash when atLineStart:
                        i = ProcessDirective(i);
                        atLineStart = true;
                        continue;
                    default:
                        atLineStart = false;
                        if (IsEmitting()) Emit(token);
                        i++;
                        continue;
                }
            }

            if (_conditionalStack.Count > 0)
            {
                var unterminated = _conditionalStack[^1];
                AddDiagnostic("対応する #endif が見つからない #if/#ifdef/#ifndef です。", unterminated.DirectiveSpan);
            }
        }

        private void Emit(Token token)
        {
            if (token.Kind == TokenKind.Identifier && _macros.Count > 0)
            {
                var text = token.GetSpan(source);
                foreach (var macro in _macros)
                {
                    if (!text.SequenceEqual(macro.Name.AsSpan())) continue;

                    var body = macro.Body;
                    foreach (var t in body)
                        Output.Add(t);

                    return;
                }
            }

            Output.Add(token);
        }

        private bool IsEmitting()
        {
            if (_conditionalStack.Count == 0) return true;

            var top = _conditionalStack[^1];
            return top is { ParentEmitting: true, BranchActive: true };
        }

        private int ProcessDirective(int hashIndex)
        {
            var lineEnd = FindLineEnd(hashIndex);
            var i = SkipTrivia(hashIndex + 1, lineEnd);

            if (i >= lineEnd) return lineEnd;

            var nameToken = tokens[i];
            if (nameToken.Kind != TokenKind.Identifier)
            {
                if (IsEmitting()) AddDiagnostic("不明なプリプロセッサディレクティブです。", nameToken.Span);
                return lineEnd;
            }

            var name = nameToken.GetSpan(source);
            i++;

            if (name.SequenceEqual("if".AsSpan())) return ProcessIf(i, lineEnd, nameToken.Span);

            if (name.SequenceEqual("ifdef".AsSpan())) return ProcessIfdef(false, i, lineEnd, nameToken.Span);

            if (name.SequenceEqual("ifndef".AsSpan())) return ProcessIfdef(true, i, lineEnd, nameToken.Span);

            if (name.SequenceEqual("elif".AsSpan())) return ProcessElif(i, lineEnd, nameToken.Span);

            if (name.SequenceEqual("else".AsSpan())) return ProcessElse(lineEnd, nameToken.Span);

            if (name.SequenceEqual("endif".AsSpan())) return ProcessEndif(lineEnd, nameToken.Span);

            if (name.SequenceEqual("define".AsSpan())) return ProcessDefine(i, lineEnd);

            if (name.SequenceEqual("undef".AsSpan())) return ProcessUndef(i, lineEnd);

            if (name.SequenceEqual("pragma".AsSpan())) return ProcessPragma(i, lineEnd);

            if (name.SequenceEqual("include".AsSpan())) return ProcessInclude(i, lineEnd);

            if (IsEmitting()) AddDiagnostic($"不明なプリプロセッサディレクティブ '#{nameToken.GetText(source)}' です。", nameToken.Span);
            return lineEnd;
        }

        private int ProcessIf(int i, int lineEnd, TextSpan directiveSpan)
        {
            var parentEmitting = IsEmitting();
            var branchActive = parentEmitting && EvalDefinedExpression(i, lineEnd);
            _conditionalStack.Add(new ConditionalFrame(parentEmitting, branchActive, branchActive, directiveSpan));
            return lineEnd;
        }

        private int ProcessIfdef(bool negate, int i, int lineEnd, TextSpan directiveSpan)
        {
            var parentEmitting = IsEmitting();
            var branchActive = parentEmitting && EvalIdentifierDefined(i, lineEnd, negate);
            _conditionalStack.Add(new ConditionalFrame(parentEmitting, branchActive, branchActive, directiveSpan));
            return lineEnd;
        }

        private int ProcessElif(int i, int lineEnd, TextSpan directiveSpan)
        {
            if (_conditionalStack.Count == 0)
            {
                AddDiagnostic("対応する #if がない #elif です。", directiveSpan);
                return lineEnd;
            }

            var topIndex = _conditionalStack.Count - 1;
            var frame = _conditionalStack[topIndex];
            var shouldEval = frame is { ParentEmitting: true, AnyBranchTaken: false };
            var branchActive = shouldEval && EvalDefinedExpression(i, lineEnd);
            frame.BranchActive = branchActive;
            frame.AnyBranchTaken = frame.AnyBranchTaken || branchActive;
            _conditionalStack[topIndex] = frame;
            return lineEnd;
        }

        private int ProcessElse(int lineEnd, TextSpan directiveSpan)
        {
            if (_conditionalStack.Count == 0)
            {
                AddDiagnostic("対応する #if がない #else です。", directiveSpan);
                return lineEnd;
            }

            var topIndex = _conditionalStack.Count - 1;
            var frame = _conditionalStack[topIndex];
            var branchActive = frame is { ParentEmitting: true, AnyBranchTaken: false };
            frame.BranchActive = branchActive;
            frame.AnyBranchTaken = frame.AnyBranchTaken || branchActive;
            _conditionalStack[topIndex] = frame;
            return lineEnd;
        }

        private int ProcessEndif(int lineEnd, TextSpan directiveSpan)
        {
            if (_conditionalStack.Count == 0)
            {
                AddDiagnostic("対応する #if がない #endif です。", directiveSpan);
                return lineEnd;
            }

            _conditionalStack.RemoveAt(_conditionalStack.Count - 1);
            return lineEnd;
        }

        /// <summary>
        ///     Evaluates <c>defined(NAME)</c>, <c>defined NAME</c>, or their <c>!</c>-negated
        ///     form — the only <c>#if</c>/<c>#elif</c> expression shape this phase supports.
        /// </summary>
        private bool EvalDefinedExpression(int i, int end)
        {
            i = SkipTrivia(i, end);
            var negate = false;
            if (i < end && tokens[i].Kind == TokenKind.Bang)
            {
                negate = true;
                i++;
                i = SkipTrivia(i, end);
            }

            if (i >= end || tokens[i].Kind != TokenKind.Identifier ||
                !tokens[i].GetSpan(source).SequenceEqual("defined".AsSpan()))
            {
                AddDiagnostic("サポートされていない #if/#elif 式です（'defined(NAME)' の形式のみ対応）。", SpanAt(i, end));
                return false;
            }

            i++;
            i = SkipTrivia(i, end);

            var parenthesized = i < end && tokens[i].Kind == TokenKind.OpenParen;
            if (parenthesized)
            {
                i++;
                i = SkipTrivia(i, end);
            }

            if (i >= end || tokens[i].Kind != TokenKind.Identifier)
            {
                AddDiagnostic("'defined' にシンボル名がありません。", SpanAt(i, end));
                return false;
            }

            var defined = options.DefinedSymbols.Contains(tokens[i].GetText(source));
            i++;

            if (!parenthesized) return negate ? !defined : defined;

            i = SkipTrivia(i, end);
            if (i < end && tokens[i].Kind == TokenKind.CloseParen)
            {
            }
            else
            {
                AddDiagnostic("'defined(' に対応する ')' がありません。", SpanAt(i, end));
            }

            return negate ? !defined : defined;
        }

        private bool EvalIdentifierDefined(int i, int end, bool negate)
        {
            i = SkipTrivia(i, end);
            if (i >= end || tokens[i].Kind != TokenKind.Identifier)
            {
                AddDiagnostic("シンボル名がありません。", SpanAt(i, end));
                return false;
            }

            var defined = options.DefinedSymbols.Contains(tokens[i].GetText(source));
            return negate ? !defined : defined;
        }

        private int ProcessPragma(int i, int lineEnd)
        {
            if (!IsEmitting()) return lineEnd;

            i = SkipTrivia(i, lineEnd);
            if (i >= lineEnd || tokens[i].Kind != TokenKind.Identifier ||
                !tokens[i].GetSpan(source).SequenceEqual("kernel".AsSpan())) return lineEnd;

            var pragmaToken = tokens[i];
            i++;
            i = SkipTrivia(i, lineEnd);
            if (i < lineEnd && tokens[i].Kind == TokenKind.Identifier)
                KernelNames.Add(tokens[i].GetText(source));
            else
                AddDiagnostic("'#pragma kernel' にカーネル名がありません。", pragmaToken.Span);

            // '#pragma multi_compile'/'shader_feature' and any other pragma are intentionally
            // ignored: no variant matrix is expanded (known limitation, see docs/IMPLEMENTATION_PLAN.md §9 Phase 2).
            return lineEnd;
        }

        private int ProcessDefine(int i, int lineEnd)
        {
            var emitting = IsEmitting();
            i = SkipTrivia(i, lineEnd);

            if (i >= lineEnd || tokens[i].Kind != TokenKind.Identifier)
            {
                if (emitting) AddDiagnostic("'#define' にマクロ名がありません。", SpanAt(i, lineEnd));
                return lineEnd;
            }

            var nameToken = tokens[i];
            i++;

            // No space between the name and '(' means a function-like macro, which is unsupported.
            if (i < lineEnd && tokens[i].Kind == TokenKind.OpenParen && tokens[i].Span.Start == nameToken.Span.End)
            {
                if (emitting) AddDiagnostic("関数形式マクロ（引数付き #define）は未対応です。", nameToken.Span);
                return lineEnd;
            }

            if (!emitting) return lineEnd;

            i = SkipTrivia(i, lineEnd);

            Token[] body;
            if (i >= lineEnd)
            {
                body = Array.Empty<Token>();
            }
            else
            {
                var bodyList = new List<Token>(lineEnd - i);
                for (var b = i; b < lineEnd; b++)
                    if (tokens[b].Kind != TokenKind.LineComment && tokens[b].Kind != TokenKind.BlockComment)
                        bodyList.Add(tokens[b]);

                body = [.. bodyList];
            }

            DefineMacro(nameToken.GetText(source), body);
            return lineEnd;
        }

        private void DefineMacro(string name, Token[] body)
        {
            for (var m = 0; m < _macros.Count; m++)
                if (string.Equals(_macros[m].Name, name, StringComparison.Ordinal))
                {
                    _macros[m] = new MacroDefinition(name, body);
                    return;
                }

            _macros.Add(new MacroDefinition(name, body));
        }

        private int ProcessUndef(int i, int lineEnd)
        {
            var emitting = IsEmitting();
            i = SkipTrivia(i, lineEnd);

            if (i >= lineEnd || tokens[i].Kind != TokenKind.Identifier)
            {
                if (emitting) AddDiagnostic("'#undef' にマクロ名がありません。", SpanAt(i, lineEnd));
                return lineEnd;
            }

            if (!emitting) return lineEnd;

            var name = tokens[i].GetText(source);
            for (var m = 0; m < _macros.Count; m++)
                if (string.Equals(_macros[m].Name, name, StringComparison.Ordinal))
                {
                    _macros.RemoveAt(m);
                    break;
                }

            return lineEnd;
        }

        private int ProcessInclude(int i, int lineEnd)
        {
            if (!IsEmitting()) return lineEnd;

            i = SkipTrivia(i, lineEnd);
            if (i >= lineEnd || tokens[i].Kind != TokenKind.StringLiteral)
            {
                AddDiagnostic("'#include' にはファイルパスの文字列リテラルが必要です。", SpanAt(i, lineEnd));
                return lineEnd;
            }

            var pathToken = tokens[i];
            var quoted = pathToken.GetText(source);
            var path = quoted.Length >= 2 ? quoted.Substring(1, quoted.Length - 2) : string.Empty;

            if (options.IncludeResolver is null)
                AddDiagnostic($"IncludeResolver が設定されていないため '#include \"{path}\"' を解決できません。", pathToken.Span);
            else if (!options.IncludeResolver.TryResolve(path, out _))
                AddDiagnostic($"'#include \"{path}\"' を解決できませんでした。", pathToken.Span);

            // The resolved content (if any) is intentionally not tokenized or merged into Output —
            // see the "Allocation policy" remarks on Preprocessor for why.
            return lineEnd;
        }

        private int FindLineEnd(int hashIndex)
        {
            var i = hashIndex + 1;
            var count = tokens.Count;
            while (i < count && tokens[i].Kind != TokenKind.NewLine && tokens[i].Kind != TokenKind.EndOfFile) i++;
            return i;
        }

        private int SkipTrivia(int i, int end)
        {
            while (i < end && (tokens[i].Kind == TokenKind.LineComment ||
                               tokens[i].Kind == TokenKind.BlockComment)) i++;
            return i;
        }

        /// <summary>
        ///     Span of token <paramref name="i" /> if it still lies within the current
        ///     directive line (<paramref name="end" />-exclusive); otherwise the span of the
        ///     line-terminating token at <paramref name="end" /> itself, for "ran out of tokens on this
        ///     directive line" diagnostics.
        /// </summary>
        private TextSpan SpanAt(int i, int end)
        {
            var idx = i < end ? i : end;
            return idx < tokens.Count ? tokens[idx].Span : new TextSpan(source.Length, 0);
        }

        private void AddDiagnostic(string message, TextSpan span)
        {
            Diagnostics ??= [];
            Diagnostics.Add(new Diagnostic(DiagnosticSeverity.Error, message, span));
        }
    }
}