using Tk.Hlsl.Arena;
using Tk.Hlsl.Ir;
using Tk.Hlsl.Lexing;

namespace Tk.Hlsl.Analysis;

/// <summary>
///     Resolves, for every <see cref="Function" /> and <see cref="EntryPoint" /> in a <see cref="Module" />,
///     which <see cref="GlobalVariable" />s it uses — directly or transitively through calls to other
///     functions — in a single forward pass over <see cref="Module.Functions" /> followed by one pass
///     over <see cref="Module.EntryPoints" />. Mirrors naga's <c>valid::analyzer</c> (see
///     docs/IMPLEMENTATION_PLAN.md §2.3, §9 Phase 4).
/// </summary>
/// <remarks>
///     <para>
///         Ordering: <see cref="Module.Functions" /> is built in "callee before caller" order (see
///         docs/IMPLEMENTATION_PLAN.md §2.2), so scanning it front-to-back guarantees a callee's
///         <see cref="FunctionInfo" /> is already computed by the time a caller needs to merge it in.
///         No per-entry-point graph traversal is performed — merging a call site is an O(1) reference to
///         an already-finished result (plus the O(size) cost of unioning its global set in).
///     </para>
///     <para>
///         Allocation policy: identifiers are matched against the global/function name tables via
///         <see cref="TokenExtensions.GetSpan" /> (no per-token substring). On net9.0+ this lookup is
///         genuinely zero-allocation, using <see cref="Dictionary{TKey,TValue}.GetAlternateLookup{TAlternateKey}" />
///         to query the <see cref="string" />-keyed name tables with a <see cref="ReadOnlySpan{T}" /> directly;
///         netstandard2.0 lacks that API, so it falls back to materializing a string only for tokens that
///         reach the lookup. A function's <see cref="FunctionInfo.GlobalUses" /> set is allocated lazily
///         (only once it actually gains a member) and functions that touch no global reuse a single shared
///         empty set (see <see cref="FunctionInfo" />).
///     </para>
///     <para>
///         Known limitations (see docs/IMPLEMENTATION_PLAN.md §9 Phase 4): local variables that shadow a
///         global identifier are not detected — the identifier is still treated as a global reference.
///         Calls to identifiers that resolve to neither a global nor a known function (HLSL intrinsics
///         such as <c>mul</c>/<c>dot</c>/<c>saturate</c>, type constructors, or genuinely unresolved calls)
///         are silently skipped rather than reported as a diagnostic — Tk.Hlsl has no intrinsic-function
///         table, so such an identifier is indistinguishable from a call to a builtin, and warning on every
///         intrinsic call would make <see cref="Module.Diagnostics" /> unusable on real shaders. A member
///         access such as <c>tex.Sample(sampler, uv)</c> is handled by skipping any identifier immediately
///         preceded by a <see cref="TokenKind.Dot" /> token, so only the leading <c>tex</c> is considered.
///     </para>
/// </remarks>
public static class Analyzer
{
    public static ModuleInfo Analyze(string source, IReadOnlyList<Token> tokens, Module module)
    {
        if (source is null) throw new ArgumentNullException(nameof(source));

        if (tokens is null) throw new ArgumentNullException(nameof(tokens));

        if (module is null) throw new ArgumentNullException(nameof(module));

        return new State(source, tokens, module).Run();
    }

    private sealed class State
    {
        private readonly Dictionary<string, Handle<GlobalVariable>> _globalsByName;
        private readonly Dictionary<string, int> _functionIndexByName;
        private readonly FunctionInfo[] _functionInfos;
        private readonly Module _module;
        private readonly string _source;
        private readonly IReadOnlyList<Token> _tokens;

#if NET9_0_OR_GREATER
        private readonly Dictionary<string, Handle<GlobalVariable>>.AlternateLookup<ReadOnlySpan<char>> _globalsLookup;
        private readonly Dictionary<string, int>.AlternateLookup<ReadOnlySpan<char>> _functionLookup;
#endif

        public State(string source, IReadOnlyList<Token> tokens, Module module)
        {
            _source = source;
            _tokens = tokens;
            _module = module;

            _globalsByName = new Dictionary<string, Handle<GlobalVariable>>(module.GlobalVariables.Count, StringComparer.Ordinal);
            foreach (var (handle, global) in module.GlobalVariables.WithHandles())
                _globalsByName[global.Name] = handle;

            _functionIndexByName = new Dictionary<string, int>(module.Functions.Count, StringComparer.Ordinal);
            foreach (var (handle, function) in module.Functions.WithHandles())
                _functionIndexByName[function.Name] = handle.Index;

            _functionInfos = module.Functions.Count == 0 ? [] : new FunctionInfo[module.Functions.Count];

#if NET9_0_OR_GREATER
            _globalsLookup = _globalsByName.GetAlternateLookup<ReadOnlySpan<char>>();
            _functionLookup = _functionIndexByName.GetAlternateLookup<ReadOnlySpan<char>>();
#endif
        }

        public ModuleInfo Run()
        {
            foreach (var (handle, function) in _module.Functions.WithHandles())
                _functionInfos[handle.Index] = AnalyzeBody(function.BodyTokenRange, handle.Index);

            var entryPointCount = _module.EntryPoints.Count;
            var entryPointInfos = entryPointCount == 0 ? [] : new FunctionInfo[entryPointCount];

            // Every helper function is fully resolved by now, so an entry point may merge any of them
            // (selfIndex is a sentinel greater than every valid function index).
            for (var i = 0; i < entryPointCount; i++)
                entryPointInfos[i] = AnalyzeBody(_module.EntryPoints[i].Function.BodyTokenRange, _module.Functions.Count);

            return new ModuleInfo(_functionInfos, entryPointInfos);
        }

        private FunctionInfo AnalyzeBody(TokenRange range, int selfIndex)
        {
            HashSet<Handle<GlobalVariable>>? globalUses = null;
            var previousKind = TokenKind.EndOfFile;

            for (var i = range.Start; i < range.End; i++)
            {
                var kind = _tokens[i].Kind;

                if (kind == TokenKind.Identifier && previousKind != TokenKind.Dot)
                    globalUses = ScanIdentifier(i, range.End, selfIndex, globalUses);

                previousKind = kind;
            }

            return new FunctionInfo(globalUses);
        }

        private HashSet<Handle<GlobalVariable>>? ScanIdentifier(int tokenIndex, int rangeEnd, int selfIndex,
            HashSet<Handle<GlobalVariable>>? globalUses)
        {
            var text = _tokens[tokenIndex].GetSpan(_source);

            if (TryGetGlobal(text, out var globalHandle))
            {
                globalUses ??= [];
                globalUses.Add(globalHandle);
                return globalUses;
            }

            var followedByCall = tokenIndex + 1 < rangeEnd && _tokens[tokenIndex + 1].Kind == TokenKind.OpenParen;
            if (!followedByCall || !TryGetFunctionIndex(text, out var calleeIndex) || calleeIndex >= selfIndex)
                // Not a global, and either not a call, or a call to something that isn't a known,
                // already-analyzed function (an HLSL intrinsic, a type constructor, self-recursion, or a
                // forward reference — none of which Tk.Hlsl resolves; see the "Known limitations" remarks).
                return globalUses;

            var calleeUses = _functionInfos[calleeIndex].GlobalUses;
            if (calleeUses.Count == 0) return globalUses;

            globalUses ??= [];
            globalUses.UnionWith(calleeUses);
            return globalUses;
        }

        private bool TryGetGlobal(ReadOnlySpan<char> name, out Handle<GlobalVariable> handle)
        {
#if NET9_0_OR_GREATER
            return _globalsLookup.TryGetValue(name, out handle);
#else
            return _globalsByName.TryGetValue(name.ToString(), out handle);
#endif
        }

        private bool TryGetFunctionIndex(ReadOnlySpan<char> name, out int index)
        {
#if NET9_0_OR_GREATER
            return _functionLookup.TryGetValue(name, out index);
#else
            return _functionIndexByName.TryGetValue(name.ToString(), out index);
#endif
        }
    }
}
