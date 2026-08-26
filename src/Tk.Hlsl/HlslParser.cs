using Tk.Hlsl.Analysis;
using Tk.Hlsl.Arena;
using Tk.Hlsl.Diagnostics;
using Tk.Hlsl.Ir;
using Tk.Hlsl.Lexing;
using Tk.Hlsl.Model;
using Tk.Hlsl.Preprocessing;
using Tk.Hlsl.Syntax;

namespace Tk.Hlsl;

/// <summary>
///     Tk.Hlsl's public entry point. Runs the Lexer (Phase 1), <see cref="Preprocessor" /> (Phase 2),
///     <see cref="TopLevelParser" /> (Phase 3), and <see cref="Analyzer" /> (Phase 4) over an HLSL
///     Compute Shader source in sequence, then projects their output — <see cref="Ir.Module" /> and
///     <see cref="ModuleInfo" /> — into the public, name-based <see cref="HlslCompilationResult" /> (see
///     docs/IMPLEMENTATION_PLAN.md §7.3, §9 Phase 5).
/// </summary>
/// <remarks>
///     Allocation policy: each <see cref="ResourceBinding" /> is built once per <see cref="GlobalVariable" />
///     and shared by reference between <see cref="HlslCompilationResult.AllResources" /> and every
///     <see cref="KernelBindingInfo.Bindings" /> that references it, rather than being re-allocated per
///     kernel. Every output array (<see cref="HlslCompilationResult.AllResources" />,
///     <see cref="HlslCompilationResult.Kernels" />, each <see cref="KernelBindingInfo.Bindings" />, and
///     <see cref="HlslCompilationResult.Diagnostics" />) is allocated once at its exact final length —
///     via <see cref="FunctionInfo.GlobalUses" />'s count for a kernel's bindings and the summed source
///     counts for diagnostics — instead of growing a <see cref="List{T}" />.
/// </remarks>
public static class HlslParser
{
    public static HlslCompilationResult Parse(string sourceText, HlslParseOptions options)
    {
        if (sourceText is null) throw new ArgumentNullException(nameof(sourceText));

        if (options is null) throw new ArgumentNullException(nameof(options));

        var lexResult = Lexer.Tokenize(sourceText);
        var preprocessResult = Preprocessor.Process(sourceText, lexResult.Tokens, options);
        var composite = preprocessResult.Source.Text;
        var module = TopLevelParser.Parse(composite, preprocessResult.Tokens, preprocessResult.KernelNames);
        var moduleInfo = Analyzer.Analyze(composite, preprocessResult.Tokens, module);

        var allResources = BuildAllResources(module);
        var kernels = BuildKernels(module, moduleInfo, allResources);
        var diagnostics =
            CombineDiagnostics(lexResult.Diagnostics, preprocessResult.Diagnostics, module.Diagnostics);

        return new HlslCompilationResult(kernels, allResources, diagnostics) { Source = preprocessResult.Source };
    }

    private static ResourceBinding[] BuildAllResources(Module module)
    {
        var count = module.GlobalVariables.Count;
        if (count == 0) return [];

        var resources = new ResourceBinding[count];
        for (var i = 0; i < count; i++)
        {
            var global = module.GlobalVariables[new Handle<GlobalVariable>(i)];
            resources[i] = new ResourceBinding(
                global.Name,
                global.Kind,
                global.ElementType is { } elementType ? module.Types[elementType].Name : null,
                global.Register,
                global.Location);
        }

        return resources;
    }

    private static KernelBindingInfo[] BuildKernels(Module module, ModuleInfo moduleInfo,
        ResourceBinding[] allResources)
    {
        var count = module.EntryPoints.Count;
        if (count == 0) return [];

        var kernels = new KernelBindingInfo[count];
        for (var i = 0; i < count; i++)
        {
            var entryPoint = module.EntryPoints[i];
            kernels[i] = new KernelBindingInfo(
                entryPoint.Name,
                entryPoint.ThreadGroupSize,
                BuildBindings(moduleInfo.EntryPoints[i], allResources),
                entryPoint.Location);
        }

        return kernels;
    }

    /// <summary>
    ///     Resolves one kernel's <see cref="FunctionInfo.GlobalUses" /> to <see cref="ResourceBinding" />s,
    ///     walking <paramref name="allResources" /> in declaration order (rather than the unordered
    ///     <see cref="FunctionInfo.GlobalUses" /> set) so the result is deterministic, and stopping as soon
    ///     as every used global has been found.
    /// </summary>
    private static ResourceBinding[] BuildBindings(FunctionInfo info, ResourceBinding[] allResources)
    {
        var usedCount = info.GlobalUses.Count;
        if (usedCount == 0) return [];

        var bindings = new ResourceBinding[usedCount];
        var written = 0;
        for (var i = 0; i < allResources.Length && written < usedCount; i++)
            if (info.UsesGlobal(new Handle<GlobalVariable>(i)))
                bindings[written++] = allResources[i];

        return bindings;
    }

    private static Diagnostic[] CombineDiagnostics(
        IReadOnlyList<Diagnostic> lexDiagnostics,
        IReadOnlyList<Diagnostic> preprocessDiagnostics,
        IReadOnlyList<Diagnostic> parseDiagnostics)
    {
        var total = lexDiagnostics.Count + preprocessDiagnostics.Count + parseDiagnostics.Count;
        if (total == 0) return [];

        var diagnostics = new Diagnostic[total];
        var index = 0;
        CopyInto(lexDiagnostics, diagnostics, ref index);
        CopyInto(preprocessDiagnostics, diagnostics, ref index);
        CopyInto(parseDiagnostics, diagnostics, ref index);
        return diagnostics;
    }

    private static void CopyInto(IReadOnlyList<Diagnostic> source, Diagnostic[] destination, ref int index)
    {
        foreach (var t in source)
            destination[index++] = t;
    }
}