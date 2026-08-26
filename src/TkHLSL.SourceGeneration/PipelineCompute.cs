using TkHLSL.Preprocessing;
using TkHLSL.SourceGeneration.Emit;

namespace TkHLSL.SourceGeneration;

/// <summary>
///     The incremental pipeline's cached computation step: given one <c>[ComputeShaderBinding]</c>
///     target and the full set of candidate AdditionalFiles, resolves the target file, parses it with
///     <see cref="TkHLSL.HlslParser" />, and renders the generated source — see
///     docs/IMPLEMENTATION_PLAN.md §9 Phase 9.2. A pure function of its inputs so
///     <c>IIncrementalGenerator</c> caching actually applies.
/// </summary>
internal static class PipelineCompute
{
    public static GenerationResult Compute(
        (AttributeTargetInfo Target, EquatableArray<AdditionalHlslFile> Files) input)
    {
        var (target, files) = input;
        var hintName = BuildHintName(target);

        if (!target.IsPartial)
            return new GenerationResult(hintName, null,
                Single("TKH1003", target, target.TypeChain[target.TypeChain.Count - 1].Name));

        var paths = new List<string>(files.Count);
        foreach (var f in files) paths.Add(f.NormalizedPath);
        var matches = PathMatching.FindAllBySuffix(paths, target.Path);

        if (matches.Count == 0)
            return new GenerationResult(hintName, null, Single("TKH1001", target, target.Path));

        if (matches.Count > 1)
            return new GenerationResult(hintName, null,
                Single("TKH1002", target, target.Path, string.Join(", ", matches)));

        var chosenPath = matches[0];
        var filesByPath = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var f in files) filesByPath[f.NormalizedPath] = f.Text;
        var chosenText = filesByPath[chosenPath];

        var options =
            new HlslParseOptions(target.Defines, new AdditionalFileIncludeResolver(filesByPath), chosenPath);
        var result = HlslParser.Parse(chosenText, options);

        var diagnostics = new List<EmitDiagnosticInfo>();
        var hasError = false;
        foreach (var d in result.Diagnostics)
        {
            var id = d.Severity == TkHLSL.Diagnostics.DiagnosticSeverity.Error ? "TKH0001" : "TKH0002";
            if (id == "TKH0001") hasError = true;
            diagnostics.Add(MakeHlslDiagnostic(id, result, d.Span, filesByPath, chosenPath, chosenText, d.Message));
        }

        if (hasError)
            return new GenerationResult(hintName, null, new EquatableArray<EmitDiagnosticInfo>(diagnostics.ToArray()));

        if (result.Kernels.Count == 0)
            diagnostics.Add(TargetDiagnostic("TKH1004", target, target.Path));

        var (source, emitDiagnostics) = CodeEmitter.Emit(target, result, filesByPath, chosenPath);
        diagnostics.AddRange(emitDiagnostics);

        return new GenerationResult(hintName, source, new EquatableArray<EmitDiagnosticInfo>(diagnostics.ToArray()));
    }

    private static string BuildHintName(AttributeTargetInfo target)
    {
        var names = new List<string>(target.TypeChain.Count);
        foreach (var t in target.TypeChain) names.Add(t.Name);
        var name = string.Join(".", names);
        return (target.Namespace.Length > 0 ? target.Namespace + "." : "") + name + ".g.cs";
    }

    private static EquatableArray<EmitDiagnosticInfo> Single(string id, AttributeTargetInfo target,
        params string[] args)
    {
        return new EquatableArray<EmitDiagnosticInfo>([TargetDiagnostic(id, target, args)]);
    }

    private static EmitDiagnosticInfo TargetDiagnostic(string id, AttributeTargetInfo target, params string[] args)
    {
        return new EmitDiagnosticInfo(id, new EquatableArray<string>(args), target.DiagnosticLocationFilePath,
            target.DiagnosticLocationSpan);
    }

    /// <summary>
    ///     Resolves an HLSL composite-source span back to its originating file (root or spliced
    ///     include) and converts the offset to a line/column via <see cref="LineMap" />, so the
    ///     reported <see cref="Microsoft.CodeAnalysis.Location" /> points at the actual <c>.compute</c>/
    ///     <c>.cginc</c> line, not just the root file.
    /// </summary>
    internal static EmitDiagnosticInfo MakeHlslDiagnostic(string id, TkHLSL.Model.HlslCompilationResult result,
        TkHLSL.Text.TextSpan span, IReadOnlyDictionary<string, string> filesByPath, string fallbackPath,
        string fallbackText, params string[] args)
    {
        if (result.Source.TryGetLocation(span.Start, out var segment, out var offsetInFile))
        {
            var path = segment.Path.Length == 0 ? fallbackPath : segment.Path;
            var text = filesByPath.TryGetValue(path, out var t) ? t : fallbackText;
            return new EmitDiagnosticInfo(id, new EquatableArray<string>(args), path,
                LineMap.GetLinePositionSpan(text, offsetInFile, span.Length));
        }

        return new EmitDiagnosticInfo(id, new EquatableArray<string>(args), fallbackPath,
            new LinePositionSpanInfo(0, 0, 0, 0));
    }
}
