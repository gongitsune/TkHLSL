using TkHLSL.Diagnostics;
using TkHLSL.Lexing;
using TkHLSL.Text;

namespace TkHLSL.Preprocessing;

/// <summary>
/// Resolves <c>#pragma</c>/<c>#define</c>/<c>#undef</c>/<c>#if</c> family/<c>#include</c>
/// directives over a raw <see cref="Lexer"/> token stream, producing the final token stream the
/// Phase 3 parser consumes.
/// </summary>
/// <remarks>
/// <para>
/// Allocation policy: the scan is a single forward pass over the input token list (itself never
/// copied), mirroring <see cref="Lexer.Tokenize"/>. Directive keywords and macro identifiers are
/// matched via <see cref="TokenExtensions.GetSpan"/> comparisons (zero allocation); a
/// <see cref="string"/> is materialized only at the (comparatively rare) points where one is
/// structurally required — a macro's name for the macro table, a symbol name for the
/// <see cref="HlslParseOptions.DefinedSymbols"/> lookup, or a diagnostic message. Object macros
/// with a non-empty body copy their captured <see cref="Token"/> values (two ints each) at
/// expansion sites; expansion is a single non-recursive substitution — a macro whose body
/// references another macro name is not expanded further, a documented limitation shared with
/// <c>multi_compile</c>/<c>shader_feature</c> variant expansion, which this phase does not perform.
/// </para>
/// <para>
/// <c>#include</c> resolution is delegated to <see cref="IIncludeResolver"/>, but the resolved
/// content is only checked for resolvability here — it is not tokenized or spliced into the output
/// stream. Doing so safely would require every <see cref="Token"/> to know which source string its
/// <see cref="TextSpan"/> applies to, which the current single-source-string design does not
/// support (see docs/IMPLEMENTATION_PLAN.md §13, "Unity 組込み include の扱い").
/// </para>
/// </remarks>
public static class Preprocessor
{
    public static PreprocessResult Process(string source, IReadOnlyList<Token> tokens, HlslParseOptions options)
    {
        if (source is null)
        {
            throw new ArgumentNullException(nameof(source));
        }

        if (tokens is null)
        {
            throw new ArgumentNullException(nameof(tokens));
        }

        if (options is null)
        {
            throw new ArgumentNullException(nameof(options));
        }

        var state = new State(source, tokens, options);
        state.Run();
        state.Output.Add(new Token(TokenKind.EndOfFile, new TextSpan(source.Length, 0)));

        IReadOnlyList<Diagnostic> diagnostics = state.Diagnostics is null
            ? Array.Empty<Diagnostic>()
            : state.Diagnostics;

        return new PreprocessResult(state.Output, state.KernelNames, diagnostics);
    }

    private readonly struct MacroDefinition
    {
        public MacroDefinition(string name, Token[] body)
        {
            Name = name;
            Body = body;
        }

        public string Name { get; }

        public Token[] Body { get; }
    }

    private struct ConditionalFrame
    {
        public ConditionalFrame(bool parentEmitting, bool branchActive, bool anyBranchTaken, TextSpan directiveSpan)
        {
            ParentEmitting = parentEmitting;
            BranchActive = branchActive;
            AnyBranchTaken = anyBranchTaken;
            DirectiveSpan = directiveSpan;
        }

        public bool ParentEmitting;
        public bool BranchActive;
        public bool AnyBranchTaken;
        public TextSpan DirectiveSpan;
    }

    private sealed class State
    {
        private readonly string _source;
        private readonly IReadOnlyList<Token> _tokens;
        private readonly HlslParseOptions _options;
        private readonly List<MacroDefinition> _macros = [];
        private readonly List<ConditionalFrame> _conditionalStack = [];

        public State(string source, IReadOnlyList<Token> tokens, HlslParseOptions options)
        {
            _source = source;
            _tokens = tokens;
            _options = options;
            Output = new List<Token>(tokens.Count);
        }

        public List<Token> Output { get; }

        public List<string> KernelNames { get; } = [];

        public List<Diagnostic>? Diagnostics { get; private set; }

        public void Run()
        {
            int count = _tokens.Count;
            int i = 0;
            bool atLineStart = true;

            while (i < count)
            {
                Token token = _tokens[i];
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
                        if (IsEmitting())
                        {
                            Emit(token);
                        }
                        i++;
                        continue;
                }
            }

            if (_conditionalStack.Count > 0)
            {
                ConditionalFrame unterminated = _conditionalStack[_conditionalStack.Count - 1];
                AddDiagnostic("対応する #endif が見つからない #if/#ifdef/#ifndef です。", unterminated.DirectiveSpan);
            }
        }

        private void Emit(Token token)
        {
            if (token.Kind == TokenKind.Identifier && _macros.Count > 0)
            {
                ReadOnlySpan<char> text = token.GetSpan(_source);
                for (int m = 0; m < _macros.Count; m++)
                {
                    if (!text.SequenceEqual(_macros[m].Name.AsSpan()))
                    {
                        continue;
                    }

                    Token[] body = _macros[m].Body;
                    for (int b = 0; b < body.Length; b++)
                    {
                        Output.Add(body[b]);
                    }
                    return;
                }
            }

            Output.Add(token);
        }

        private bool IsEmitting()
        {
            if (_conditionalStack.Count == 0)
            {
                return true;
            }

            ConditionalFrame top = _conditionalStack[_conditionalStack.Count - 1];
            return top.ParentEmitting && top.BranchActive;
        }

        private int ProcessDirective(int hashIndex)
        {
            int lineEnd = FindLineEnd(hashIndex);
            int i = SkipTrivia(hashIndex + 1, lineEnd);

            if (i >= lineEnd)
            {
                return lineEnd;
            }

            Token nameToken = _tokens[i];
            if (nameToken.Kind != TokenKind.Identifier)
            {
                if (IsEmitting())
                {
                    AddDiagnostic("不明なプリプロセッサディレクティブです。", nameToken.Span);
                }
                return lineEnd;
            }

            ReadOnlySpan<char> name = nameToken.GetSpan(_source);
            i++;

            if (name.SequenceEqual("if".AsSpan()))
            {
                return ProcessIf(i, lineEnd, nameToken.Span);
            }

            if (name.SequenceEqual("ifdef".AsSpan()))
            {
                return ProcessIfdef(negate: false, i, lineEnd, nameToken.Span);
            }

            if (name.SequenceEqual("ifndef".AsSpan()))
            {
                return ProcessIfdef(negate: true, i, lineEnd, nameToken.Span);
            }

            if (name.SequenceEqual("elif".AsSpan()))
            {
                return ProcessElif(i, lineEnd, nameToken.Span);
            }

            if (name.SequenceEqual("else".AsSpan()))
            {
                return ProcessElse(lineEnd, nameToken.Span);
            }

            if (name.SequenceEqual("endif".AsSpan()))
            {
                return ProcessEndif(lineEnd, nameToken.Span);
            }

            if (name.SequenceEqual("define".AsSpan()))
            {
                return ProcessDefine(i, lineEnd);
            }

            if (name.SequenceEqual("undef".AsSpan()))
            {
                return ProcessUndef(i, lineEnd);
            }

            if (name.SequenceEqual("pragma".AsSpan()))
            {
                return ProcessPragma(i, lineEnd);
            }

            if (name.SequenceEqual("include".AsSpan()))
            {
                return ProcessInclude(i, lineEnd);
            }

            if (IsEmitting())
            {
                AddDiagnostic($"不明なプリプロセッサディレクティブ '#{nameToken.GetText(_source)}' です。", nameToken.Span);
            }
            return lineEnd;
        }

        private int ProcessIf(int i, int lineEnd, TextSpan directiveSpan)
        {
            bool parentEmitting = IsEmitting();
            bool branchActive = parentEmitting && EvalDefinedExpression(i, lineEnd);
            _conditionalStack.Add(new ConditionalFrame(parentEmitting, branchActive, branchActive, directiveSpan));
            return lineEnd;
        }

        private int ProcessIfdef(bool negate, int i, int lineEnd, TextSpan directiveSpan)
        {
            bool parentEmitting = IsEmitting();
            bool branchActive = parentEmitting && EvalIdentifierDefined(i, lineEnd, negate);
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

            int topIndex = _conditionalStack.Count - 1;
            ConditionalFrame frame = _conditionalStack[topIndex];
            bool shouldEval = frame.ParentEmitting && !frame.AnyBranchTaken;
            bool branchActive = shouldEval && EvalDefinedExpression(i, lineEnd);
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

            int topIndex = _conditionalStack.Count - 1;
            ConditionalFrame frame = _conditionalStack[topIndex];
            bool branchActive = frame.ParentEmitting && !frame.AnyBranchTaken;
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

        /// <summary>Evaluates <c>defined(NAME)</c>, <c>defined NAME</c>, or their <c>!</c>-negated
        /// form — the only <c>#if</c>/<c>#elif</c> expression shape this phase supports.</summary>
        private bool EvalDefinedExpression(int i, int end)
        {
            i = SkipTrivia(i, end);
            bool negate = false;
            if (i < end && _tokens[i].Kind == TokenKind.Bang)
            {
                negate = true;
                i++;
                i = SkipTrivia(i, end);
            }

            if (i >= end || _tokens[i].Kind != TokenKind.Identifier || !_tokens[i].GetSpan(_source).SequenceEqual("defined".AsSpan()))
            {
                AddDiagnostic("サポートされていない #if/#elif 式です（'defined(NAME)' の形式のみ対応）。", SpanAt(i, end));
                return false;
            }
            i++;
            i = SkipTrivia(i, end);

            bool parenthesized = i < end && _tokens[i].Kind == TokenKind.OpenParen;
            if (parenthesized)
            {
                i++;
                i = SkipTrivia(i, end);
            }

            if (i >= end || _tokens[i].Kind != TokenKind.Identifier)
            {
                AddDiagnostic("'defined' にシンボル名がありません。", SpanAt(i, end));
                return false;
            }

            bool defined = _options.DefinedSymbols.Contains(_tokens[i].GetText(_source));
            i++;

            if (parenthesized)
            {
                i = SkipTrivia(i, end);
                if (i < end && _tokens[i].Kind == TokenKind.CloseParen)
                {
                    i++;
                }
                else
                {
                    AddDiagnostic("'defined(' に対応する ')' がありません。", SpanAt(i, end));
                }
            }

            return negate ? !defined : defined;
        }

        private bool EvalIdentifierDefined(int i, int end, bool negate)
        {
            i = SkipTrivia(i, end);
            if (i >= end || _tokens[i].Kind != TokenKind.Identifier)
            {
                AddDiagnostic("シンボル名がありません。", SpanAt(i, end));
                return false;
            }

            bool defined = _options.DefinedSymbols.Contains(_tokens[i].GetText(_source));
            return negate ? !defined : defined;
        }

        private int ProcessPragma(int i, int lineEnd)
        {
            if (!IsEmitting())
            {
                return lineEnd;
            }

            i = SkipTrivia(i, lineEnd);
            if (i >= lineEnd || _tokens[i].Kind != TokenKind.Identifier)
            {
                return lineEnd;
            }

            if (_tokens[i].GetSpan(_source).SequenceEqual("kernel".AsSpan()))
            {
                Token pragmaToken = _tokens[i];
                i++;
                i = SkipTrivia(i, lineEnd);
                if (i < lineEnd && _tokens[i].Kind == TokenKind.Identifier)
                {
                    KernelNames.Add(_tokens[i].GetText(_source));
                }
                else
                {
                    AddDiagnostic("'#pragma kernel' にカーネル名がありません。", pragmaToken.Span);
                }
            }

            // '#pragma multi_compile'/'shader_feature' and any other pragma are intentionally
            // ignored: no variant matrix is expanded (known limitation, see docs/IMPLEMENTATION_PLAN.md §9 Phase 2).
            return lineEnd;
        }

        private int ProcessDefine(int i, int lineEnd)
        {
            bool emitting = IsEmitting();
            i = SkipTrivia(i, lineEnd);

            if (i >= lineEnd || _tokens[i].Kind != TokenKind.Identifier)
            {
                if (emitting)
                {
                    AddDiagnostic("'#define' にマクロ名がありません。", SpanAt(i, lineEnd));
                }
                return lineEnd;
            }

            Token nameToken = _tokens[i];
            i++;

            // No space between the name and '(' means a function-like macro, which is unsupported.
            if (i < lineEnd && _tokens[i].Kind == TokenKind.OpenParen && _tokens[i].Span.Start == nameToken.Span.End)
            {
                if (emitting)
                {
                    AddDiagnostic("関数形式マクロ（引数付き #define）は未対応です。", nameToken.Span);
                }
                return lineEnd;
            }

            if (!emitting)
            {
                return lineEnd;
            }

            i = SkipTrivia(i, lineEnd);

            Token[] body;
            if (i >= lineEnd)
            {
                body = Array.Empty<Token>();
            }
            else
            {
                var bodyList = new List<Token>(lineEnd - i);
                for (int b = i; b < lineEnd; b++)
                {
                    if (_tokens[b].Kind != TokenKind.LineComment && _tokens[b].Kind != TokenKind.BlockComment)
                    {
                        bodyList.Add(_tokens[b]);
                    }
                }
                body = [.. bodyList];
            }

            DefineMacro(nameToken.GetText(_source), body);
            return lineEnd;
        }

        private void DefineMacro(string name, Token[] body)
        {
            for (int m = 0; m < _macros.Count; m++)
            {
                if (string.Equals(_macros[m].Name, name, StringComparison.Ordinal))
                {
                    _macros[m] = new MacroDefinition(name, body);
                    return;
                }
            }

            _macros.Add(new MacroDefinition(name, body));
        }

        private int ProcessUndef(int i, int lineEnd)
        {
            bool emitting = IsEmitting();
            i = SkipTrivia(i, lineEnd);

            if (i >= lineEnd || _tokens[i].Kind != TokenKind.Identifier)
            {
                if (emitting)
                {
                    AddDiagnostic("'#undef' にマクロ名がありません。", SpanAt(i, lineEnd));
                }
                return lineEnd;
            }

            if (emitting)
            {
                string name = _tokens[i].GetText(_source);
                for (int m = 0; m < _macros.Count; m++)
                {
                    if (string.Equals(_macros[m].Name, name, StringComparison.Ordinal))
                    {
                        _macros.RemoveAt(m);
                        break;
                    }
                }
            }

            return lineEnd;
        }

        private int ProcessInclude(int i, int lineEnd)
        {
            if (!IsEmitting())
            {
                return lineEnd;
            }

            i = SkipTrivia(i, lineEnd);
            if (i >= lineEnd || _tokens[i].Kind != TokenKind.StringLiteral)
            {
                AddDiagnostic("'#include' にはファイルパスの文字列リテラルが必要です。", SpanAt(i, lineEnd));
                return lineEnd;
            }

            Token pathToken = _tokens[i];
            string quoted = pathToken.GetText(_source);
            string path = quoted.Length >= 2 ? quoted.Substring(1, quoted.Length - 2) : string.Empty;

            if (_options.IncludeResolver is null)
            {
                AddDiagnostic($"IncludeResolver が設定されていないため '#include \"{path}\"' を解決できません。", pathToken.Span);
            }
            else if (!_options.IncludeResolver.TryResolve(path, out _))
            {
                AddDiagnostic($"'#include \"{path}\"' を解決できませんでした。", pathToken.Span);
            }

            // The resolved content (if any) is intentionally not tokenized or merged into Output —
            // see the "Allocation policy" remarks on Preprocessor for why.
            return lineEnd;
        }

        private int FindLineEnd(int hashIndex)
        {
            int i = hashIndex + 1;
            int count = _tokens.Count;
            while (i < count && _tokens[i].Kind != TokenKind.NewLine && _tokens[i].Kind != TokenKind.EndOfFile)
            {
                i++;
            }
            return i;
        }

        private int SkipTrivia(int i, int end)
        {
            while (i < end && (_tokens[i].Kind == TokenKind.LineComment || _tokens[i].Kind == TokenKind.BlockComment))
            {
                i++;
            }
            return i;
        }

        /// <summary>Span of token <paramref name="i"/> if it still lies within the current
        /// directive line (<paramref name="end"/>-exclusive); otherwise the span of the
        /// line-terminating token at <paramref name="end"/> itself, for "ran out of tokens on this
        /// directive line" diagnostics.</summary>
        private TextSpan SpanAt(int i, int end)
        {
            int idx = i < end ? i : end;
            return idx < _tokens.Count ? _tokens[idx].Span : new TextSpan(_source.Length, 0);
        }

        private void AddDiagnostic(string message, TextSpan span)
        {
            Diagnostics ??= [];
            Diagnostics.Add(new Diagnostic(DiagnosticSeverity.Error, message, span));
        }
    }
}
