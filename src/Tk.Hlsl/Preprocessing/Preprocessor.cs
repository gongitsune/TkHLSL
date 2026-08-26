using Tk.Hlsl.Diagnostics;
using Tk.Hlsl.Lexing;
using Tk.Hlsl.Text;

namespace Tk.Hlsl.Preprocessing;

/// <summary>
///     Resolves <c>#pragma</c>/<c>#define</c>/<c>#undef</c>/<c>#if</c> family/<c>#include</c>
///     directives over a raw <see cref="Lexer" /> token stream, producing the final token stream the
///     Phase 3 parser consumes.
/// </summary>
/// <remarks>
///     <para>
///         Allocation policy: the scan is a single forward pass over each file's input token list (itself
///         never copied), mirroring <see cref="Lexer.Tokenize" />. Directive keywords and macro identifiers
///         are matched via <see cref="TokenExtensions.GetSpan(Token, string)" /> comparisons (zero
///         allocation); a <see cref="string" /> is materialized only at the (comparatively rare) points
///         where one is structurally required — a macro's name for the macro table, a symbol name for the
///         <see cref="HlslParseOptions.DefinedSymbols" /> lookup, an include's resolved path, or a
///         diagnostic message. Object macros with a non-empty body copy their captured
///         <see cref="Token" /> values (two ints each) at expansion sites; expansion is a single
///         non-recursive substitution — a macro whose body references another macro name is not expanded
///         further, a documented limitation shared with <c>multi_compile</c>/<c>shader_feature</c> variant
///         expansion, which this phase does not perform.
///     </para>
///     <para>
///         <c>#include</c> resolution is delegated to <see cref="IIncludeResolver" />; the resolved content
///         is tokenized and spliced into the output stream. <see cref="Token" /> stays a two-int
///         (<see cref="Lexing.TokenKind" />, <see cref="TextSpan" />) struct — no per-token source reference —
///         by keeping every included file's tokens file-relative while they're scanned and shifting only at
///         the two points a span leaves its file: when a code token is appended to the output
///         (<c>Emit</c>) and when a diagnostic is recorded (<c>AddDiagnostic</c>). A macro body captured
///         while scanning an included file is shifted once, at capture time, so expanding it later never
///         shifts again. All included files' text is appended, in encounter order, to one composite
///         <see cref="Text.SourceText" /> (see <see cref="PreprocessResult.Source" />) that every emitted
///         span is an offset into — see docs/IMPLEMENTATION_PLAN.md §13, "Unity 組込み include の扱い".
///     </para>
/// </remarks>
public static class Preprocessor
{
    public static PreprocessResult Process(string source, IReadOnlyList<Token> tokens, HlslParseOptions options)
    {
        if (source is null) throw new ArgumentNullException(nameof(source));

        if (tokens is null) throw new ArgumentNullException(nameof(tokens));

        if (options is null) throw new ArgumentNullException(nameof(options));

        var state = new State(source, options);
        state.RunFrame(source, tokens, 0, options.SourcePath);

        var sourceText = state.Builder.Build();
        state.Output.Add(new Token(TokenKind.EndOfFile, new TextSpan(sourceText.Text.Length, 0)));

        IReadOnlyList<Diagnostic> diagnostics = state.Diagnostics is null
            ? Array.Empty<Diagnostic>()
            : state.Diagnostics;

        return new PreprocessResult(state.Output, state.KernelNames, diagnostics, sourceText);
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

        /// <summary>Already shifted to composite coordinates at push time (see class remarks).</summary>
        public readonly TextSpan DirectiveSpan = directiveSpan;
    }

    private sealed class State(string rootSource, HlslParseOptions options)
    {
        private readonly List<ConditionalFrame> _conditionalStack = [];
        private readonly List<MacroDefinition> _macros = [];
        private readonly List<string> _openIncludes = [];
        private readonly HashSet<string> _pragmaOnce = new(StringComparer.Ordinal);

        // --- current-frame state; saved/restored around a recursive #include (see ProcessInclude) ---
        private string _source = string.Empty;
        private IReadOnlyList<Token> _tokens = Array.Empty<Token>();
        private int _offset;
        private string? _path;
        private int _depth;
        private int _conditionalBase;

        public SourceTextBuilder Builder { get; } = new(rootSource, options.SourcePath ?? string.Empty);

        public List<Token> Output { get; } = [];

        public List<string> KernelNames { get; } = [];

        public List<Diagnostic>? Diagnostics { get; private set; }

        /// <summary>
        ///     Scans one file's token list, appending emitted tokens (shifted to composite coordinates) to
        ///     the shared <see cref="Output" />. Recurses into itself (via <see cref="ProcessInclude" />) for
        ///     every resolved <c>#include</c>, so the call stack mirrors the include stack.
        /// </summary>
        public void RunFrame(string fileSource, IReadOnlyList<Token> fileTokens, int offset, string? path)
        {
            _source = fileSource;
            _tokens = fileTokens;
            _offset = offset;
            _path = path;
            var conditionalBase = _conditionalStack.Count;
            _conditionalBase = conditionalBase;

            var count = fileTokens.Count;
            var i = 0;
            var atLineStart = true;

            while (i < count)
            {
                var token = fileTokens[i];
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

            if (_conditionalStack.Count > conditionalBase)
            {
                var unterminated = _conditionalStack[^1];
                _conditionalStack.RemoveRange(conditionalBase, _conditionalStack.Count - conditionalBase);
                AddDiagnosticAbsolute(DiagnosticSeverity.Error,
                    "対応する #endif が見つからない #if/#ifdef/#ifndef です。", unterminated.DirectiveSpan);
            }
        }

        private void Emit(Token token)
        {
            if (token.Kind == TokenKind.Identifier && _macros.Count > 0)
            {
                var text = token.GetSpan(_source);
                foreach (var macro in _macros)
                {
                    if (!text.SequenceEqual(macro.Name.AsSpan())) continue;

                    // Macro bodies were shifted once, at capture time in ProcessDefine — do not shift again.
                    var body = macro.Body;
                    foreach (var t in body)
                        Output.Add(t);

                    return;
                }
            }

            Output.Add(Shift(token));
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

            var nameToken = _tokens[i];
            if (nameToken.Kind != TokenKind.Identifier)
            {
                if (IsEmitting()) AddDiagnostic("不明なプリプロセッサディレクティブです。", nameToken.Span);
                return lineEnd;
            }

            var name = nameToken.GetSpan(_source);
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

            if (IsEmitting()) AddDiagnostic($"不明なプリプロセッサディレクティブ '#{nameToken.GetText(_source)}' です。", nameToken.Span);
            return lineEnd;
        }

        private int ProcessIf(int i, int lineEnd, TextSpan directiveSpan)
        {
            var parentEmitting = IsEmitting();
            var branchActive = parentEmitting && EvalDefinedExpression(i, lineEnd);
            _conditionalStack.Add(new ConditionalFrame(parentEmitting, branchActive, branchActive, Shift(directiveSpan)));
            return lineEnd;
        }

        private int ProcessIfdef(bool negate, int i, int lineEnd, TextSpan directiveSpan)
        {
            var parentEmitting = IsEmitting();
            var branchActive = parentEmitting && EvalIdentifierDefined(i, lineEnd, negate);
            _conditionalStack.Add(new ConditionalFrame(parentEmitting, branchActive, branchActive, Shift(directiveSpan)));
            return lineEnd;
        }

        private int ProcessElif(int i, int lineEnd, TextSpan directiveSpan)
        {
            if (_conditionalStack.Count <= _conditionalBase)
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
            if (_conditionalStack.Count <= _conditionalBase)
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
            if (_conditionalStack.Count <= _conditionalBase)
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
            if (i < end && _tokens[i].Kind == TokenKind.Bang)
            {
                negate = true;
                i++;
                i = SkipTrivia(i, end);
            }

            if (i >= end || _tokens[i].Kind != TokenKind.Identifier ||
                !_tokens[i].GetSpan(_source).SequenceEqual("defined".AsSpan()))
            {
                AddDiagnostic("サポートされていない #if/#elif 式です（'defined(NAME)' の形式のみ対応）。", SpanAt(i, end));
                return false;
            }

            i++;
            i = SkipTrivia(i, end);

            var parenthesized = i < end && _tokens[i].Kind == TokenKind.OpenParen;
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

            var defined = IsSymbolDefined(_tokens[i].GetSpan(_source));
            i++;

            if (!parenthesized) return negate ? !defined : defined;

            i = SkipTrivia(i, end);
            if (i < end && _tokens[i].Kind == TokenKind.CloseParen)
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
            if (i >= end || _tokens[i].Kind != TokenKind.Identifier)
            {
                AddDiagnostic("シンボル名がありません。", SpanAt(i, end));
                return false;
            }

            var defined = IsSymbolDefined(_tokens[i].GetSpan(_source));
            return negate ? !defined : defined;
        }

        /// <summary>
        ///     Whether <paramref name="name" /> counts as defined for <c>#ifdef</c>/<c>#ifndef</c>/
        ///     <c>defined(...)</c> resolution: either an active <c>#define</c> macro (checked first, via a
        ///     zero-allocation span comparison) or a symbol listed in
        ///     <see cref="HlslParseOptions.DefinedSymbols" /> (a <see cref="string" /> is materialized only
        ///     on this fallback path). This is what makes <c>#ifndef GUARD</c> / <c>#define GUARD</c> include
        ///     guards actually suppress a second inclusion.
        /// </summary>
        private bool IsSymbolDefined(ReadOnlySpan<char> name)
        {
            foreach (var macro in _macros)
                if (name.SequenceEqual(macro.Name.AsSpan()))
                    return true;

            return options.DefinedSymbols.Contains(name.ToString());
        }

        private int ProcessPragma(int i, int lineEnd)
        {
            if (!IsEmitting()) return lineEnd;

            i = SkipTrivia(i, lineEnd);
            if (i >= lineEnd || _tokens[i].Kind != TokenKind.Identifier) return lineEnd;

            var pragmaNameToken = _tokens[i];
            var pragmaName = pragmaNameToken.GetSpan(_source);

            if (pragmaName.SequenceEqual("once".AsSpan()))
            {
                if (_path is not null) _pragmaOnce.Add(_path);
                return lineEnd;
            }

            if (!pragmaName.SequenceEqual("kernel".AsSpan())) return lineEnd;

            i++;
            i = SkipTrivia(i, lineEnd);
            if (i < lineEnd && _tokens[i].Kind == TokenKind.Identifier)
                KernelNames.Add(_tokens[i].GetText(_source));
            else
                AddDiagnostic("'#pragma kernel' にカーネル名がありません。", pragmaNameToken.Span);

            // '#pragma multi_compile'/'shader_feature' and any other pragma are intentionally
            // ignored: no variant matrix is expanded (known limitation, see docs/IMPLEMENTATION_PLAN.md §9 Phase 2).
            return lineEnd;
        }

        private int ProcessDefine(int i, int lineEnd)
        {
            var emitting = IsEmitting();
            i = SkipTrivia(i, lineEnd);

            if (i >= lineEnd || _tokens[i].Kind != TokenKind.Identifier)
            {
                if (emitting) AddDiagnostic("'#define' にマクロ名がありません。", SpanAt(i, lineEnd));
                return lineEnd;
            }

            var nameToken = _tokens[i];
            i++;

            // No space between the name and '(' means a function-like macro, which is unsupported.
            if (i < lineEnd && _tokens[i].Kind == TokenKind.OpenParen && _tokens[i].Span.Start == nameToken.Span.End)
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
                    if (_tokens[b].Kind != TokenKind.LineComment && _tokens[b].Kind != TokenKind.BlockComment)
                        bodyList.Add(Shift(_tokens[b]));

                body = [.. bodyList];
            }

            DefineMacro(nameToken.GetText(_source), body);
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

            if (i >= lineEnd || _tokens[i].Kind != TokenKind.Identifier)
            {
                if (emitting) AddDiagnostic("'#undef' にマクロ名がありません。", SpanAt(i, lineEnd));
                return lineEnd;
            }

            if (!emitting) return lineEnd;

            var name = _tokens[i].GetText(_source);
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
            if (i >= lineEnd || _tokens[i].Kind != TokenKind.StringLiteral)
            {
                AddDiagnostic("'#include' にはファイルパスの文字列リテラルが必要です。", SpanAt(i, lineEnd));
                return lineEnd;
            }

            var pathToken = _tokens[i];
            var quoted = pathToken.GetText(_source);
            var path = quoted.Length >= 2 ? quoted.Substring(1, quoted.Length - 2) : string.Empty;

            if (options.IncludeResolver is null)
            {
                AddDiagnostic($"IncludeResolver が設定されていないため '#include \"{path}\"' を解決できません。", pathToken.Span);
                return lineEnd;
            }

            if (_depth >= options.MaxIncludeDepth)
            {
                AddDiagnostic($"'#include \"{path}\"' の入れ子が深すぎます（上限 {options.MaxIncludeDepth}）。", pathToken.Span);
                return lineEnd;
            }

            if (!options.IncludeResolver.TryResolve(path, _path, out var resolvedPath, out var content) ||
                content is null)
            {
                AddDiagnostic($"'#include \"{path}\"' を解決できませんでした。", pathToken.Span);
                return lineEnd;
            }

            var resolved = resolvedPath ?? path;

            if (_pragmaOnce.Contains(resolved)) return lineEnd;

            if (_openIncludes.Contains(resolved))
            {
                AddDiagnostic($"'#include \"{path}\"' が循環しています。", pathToken.Span);
                return lineEnd;
            }

            var childLex = Lexer.Tokenize(content);
            var childOffset = Builder.Reserve(resolved, content);

            foreach (var d in childLex.Diagnostics)
                AddDiagnosticAbsolute(d.Severity, d.Message,
                    new TextSpan(d.Span.Start + childOffset, d.Span.Length));

            var savedSource = _source;
            var savedTokens = _tokens;
            var savedOffset = _offset;
            var savedPath = _path;
            var savedConditionalBase = _conditionalBase;

            _openIncludes.Add(resolved);
            _depth++;

            RunFrame(content, childLex.Tokens, childOffset, resolved);

            _depth--;
            _openIncludes.RemoveAt(_openIncludes.Count - 1);

            _source = savedSource;
            _tokens = savedTokens;
            _offset = savedOffset;
            _path = savedPath;
            _conditionalBase = savedConditionalBase;

            return lineEnd;
        }

        private int FindLineEnd(int hashIndex)
        {
            var i = hashIndex + 1;
            var count = _tokens.Count;
            while (i < count && _tokens[i].Kind != TokenKind.NewLine && _tokens[i].Kind != TokenKind.EndOfFile) i++;
            return i;
        }

        private int SkipTrivia(int i, int end)
        {
            while (i < end && (_tokens[i].Kind == TokenKind.LineComment ||
                               _tokens[i].Kind == TokenKind.BlockComment)) i++;
            return i;
        }

        /// <summary>
        ///     Span of token <paramref name="i" /> if it still lies within the current
        ///     directive line (<paramref name="end" />-exclusive); otherwise the span of the
        ///     line-terminating token at <paramref name="end" /> itself, for "ran out of tokens on this
        ///     directive line" diagnostics. File-relative — shifted by <see cref="AddDiagnostic" />.
        /// </summary>
        private TextSpan SpanAt(int i, int end)
        {
            var idx = i < end ? i : end;
            return idx < _tokens.Count ? _tokens[idx].Span : new TextSpan(_source.Length, 0);
        }

        private Token Shift(Token token) => _offset == 0 ? token : new Token(token.Kind, Shift(token.Span));

        private TextSpan Shift(TextSpan span) => _offset == 0 ? span : new TextSpan(span.Start + _offset, span.Length);

        /// <summary>Records a diagnostic for a file-relative <paramref name="span" />, shifting it to composite coordinates.</summary>
        private void AddDiagnostic(string message, TextSpan span)
        {
            AddDiagnosticAbsolute(DiagnosticSeverity.Error, message, Shift(span));
        }

        /// <summary>Records a diagnostic for a <paramref name="span" /> that is already in composite coordinates.</summary>
        private void AddDiagnosticAbsolute(DiagnosticSeverity severity, string message, TextSpan span)
        {
            Diagnostics ??= [];
            Diagnostics.Add(new Diagnostic(severity, message, span));
        }
    }
}
