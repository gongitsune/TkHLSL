using TkHLSL.Diagnostics;
using TkHLSL.Model;
using TkHLSL.Preprocessing;
using TkHLSL.SourceGeneration.Emit;
using TkHLSL.SourceGeneration.Manifest;

namespace TkHLSL.SourceGeneration;

/// <summary>
///     The incremental pipeline's cached computation step: given one <c>[ComputeShaderBinding]</c>
///     target and the full set of candidate AdditionalFiles, resolves the target — either against a
///     raw <c>.compute</c>/<c>.hlsl</c> AdditionalFile (parsed on the fly with
///     <see cref="TkHLSL.HlslParser" />) or a pre-analyzed <c>*.additionalfile</c> shader manifest (see
///     <see cref="ShaderManifest" />) — and renders the generated source — see
///     docs/IMPLEMENTATION_PLAN.md §9 Phase 9.2 and the "csc.rsp を廃止し..." plan. A pure function of
///     its inputs so <c>IIncrementalGenerator</c> caching actually applies.
/// </summary>
internal static class PipelineCompute
{
    public static GenerationResult Compute(
        (AttributeTargetInfo Target, EquatableArray<AdditionalHlslFile> RawFiles,
            EquatableArray<AdditionalHlslFile> ManifestFiles) input)
    {
        var (target, rawFiles, manifestFiles) = input;
        var hintName = BuildHintName(target);

        if (!target.IsPartial)
            return new GenerationResult(hintName, null,
                Single("TKH1003", target, target.TypeChain[^1].Name));

        var rawPaths = new List<string>(rawFiles.Count);
        foreach (var f in rawFiles) rawPaths.Add(f.NormalizedPath);
        var rawMatches = PathMatching.FindAllBySuffix(rawPaths, target.Path);

        if (rawMatches.Count > 1)
            return new GenerationResult(hintName, null,
                Single("TKH1002", target, target.Path, string.Join(", ", rawMatches)));

        if (rawMatches.Count == 1)
            return ComputeFromRawFile(target, hintName, rawMatches[0], rawFiles);

        return ComputeFromManifest(target, hintName, manifestFiles);
    }

    private static GenerationResult ComputeFromRawFile(AttributeTargetInfo target, string hintName,
        string chosenPath, EquatableArray<AdditionalHlslFile> files)
    {
        var filesByPath = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var f in files) filesByPath[f.NormalizedPath] = f.Text;
        var chosenText = filesByPath[chosenPath];

        var options =
            new HlslParseOptions(target.Defines, new AdditionalFileIncludeResolver(filesByPath), chosenPath);
        var result = HlslParser.Parse(chosenText, options);
        var locationResolver = new SourceTextLocationResolver(result.Source, filesByPath, chosenPath);

        return FinishFromResult(target, hintName, result, locationResolver);
    }

    private static GenerationResult ComputeFromManifest(AttributeTargetInfo target, string hintName,
        EquatableArray<AdditionalHlslFile> manifestFiles)
    {
        var candidates = new List<ManifestData>();
        var roots = new List<string>();
        foreach (var m in manifestFiles)
            if (ShaderManifest.TryRead(m.Text, out var data) && data is not null)
            {
                roots.Add(data.Root);
                candidates.Add(data);
            }

        var matchedRoots = PathMatching.FindAllBySuffix(roots, target.Path);
        if (matchedRoots.Count == 0)
            return new GenerationResult(hintName, null, Single("TKH1001", target, target.Path));

        var matched = new List<ManifestData>();
        foreach (var data in candidates)
            if (matchedRoots.Contains(data.Root))
                matched.Add(data);

        var exact = new List<ManifestData>();
        foreach (var data in matched)
            if (DefinesEqual(data.Defines, target.Defines))
                exact.Add(data);

        if (exact.Count > 1)
            return new GenerationResult(hintName, null,
                Single("TKH1002", target, target.Path, string.Join(", ", DistinctRoots(exact))));

        if (exact.Count == 0)
        {
            // Ambiguous root match (multiple different shaders whose root suffix-matches the
            // attribute path) takes precedence over "no matching Defines" — the latter is only
            // meaningful once a single shader has been identified.
            var distinctRoots = DistinctRoots(matched);
            if (distinctRoots.Count > 1)
                return new GenerationResult(hintName, null,
                    Single("TKH1002", target, target.Path, string.Join(", ", distinctRoots)));

            return new GenerationResult(hintName, null,
                Single("TKH1007", target, target.Path, string.Join(", ", target.Defines)));
        }

        var manifest = exact[0];
        var locationResolver = new ManifestLocationResolver(manifest.Locations, manifest.Root);
        return FinishFromResult(target, hintName, manifest.Result, locationResolver);
    }

    private static GenerationResult FinishFromResult(AttributeTargetInfo target, string hintName,
        HlslCompilationResult result, IEmitLocationResolver locationResolver)
    {
        var diagnostics = new List<EmitDiagnosticInfo>();
        var hasError = false;
        foreach (var d in result.Diagnostics)
        {
            var id = d.Severity == DiagnosticSeverity.Error ? "TKH0001" : "TKH0002";
            if (id == "TKH0001") hasError = true;
            var (path, span) = locationResolver.Resolve(d.Span);
            diagnostics.Add(new EmitDiagnosticInfo(id, new EquatableArray<string>([target.Path, d.Message]), path,
                span));
        }

        if (hasError)
            return new GenerationResult(hintName, null, new EquatableArray<EmitDiagnosticInfo>(diagnostics.ToArray()));

        if (result.Kernels.Count == 0)
            diagnostics.Add(TargetDiagnostic("TKH1004", target, target.Path));

        var (source, emitDiagnostics) = CodeEmitter.Emit(target, result, locationResolver);
        diagnostics.AddRange(emitDiagnostics);

        return new GenerationResult(hintName, source, new EquatableArray<EmitDiagnosticInfo>(diagnostics.ToArray()));
    }

    private static bool DefinesEqual(IReadOnlyList<string> a, EquatableArray<string> b)
    {
        if (a.Count != b.Count) return false;
        var set = new HashSet<string>(a, StringComparer.Ordinal);
        foreach (var d in b)
            if (!set.Contains(d))
                return false;
        return true;
    }

    private static List<string> DistinctRoots(IReadOnlyList<ManifestData> data)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var result = new List<string>();
        foreach (var d in data)
            if (seen.Add(d.Root))
                result.Add(d.Root);
        return result;
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
}
