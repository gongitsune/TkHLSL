using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;
using TkHLSL.Unity;

namespace TkHLSL.SourceGeneration.Tests;

/// <summary>
///     Drives <see cref="ComputeShaderBindingGenerator" /> directly through Roslyn's
///     <see cref="GeneratorDriver" />, for tests that need to inspect the generated source text or
///     assert on specific diagnostic ids/messages — <see cref="TkHlslGeneratorVerifier" /> (the
///     <c>Microsoft.CodeAnalysis.Testing</c> wrapper) is better suited to "does the whole thing compile"
///     tests, but doesn't expose generated text directly (see docs/IMPLEMENTATION_PLAN.md §9 Phase 9.5).
/// </summary>
internal static class GeneratorDriverHarness
{
    public static Result Run(string userSource, params (string Path, string Content)[] additionalFiles)
    {
        var compilation = CSharpCompilation.Create(
            "TestAssembly",
            [CSharpSyntaxTree.ParseText(userSource), CSharpSyntaxTree.ParseText(UnityStub.Source)],
            GetReferences(),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var additionalTexts = new List<AdditionalText>();
        foreach (var (path, content) in additionalFiles)
            additionalTexts.Add(new InMemoryAdditionalText(path, content));

        var driver = CSharpGeneratorDriver.Create(
            [new ComputeShaderBindingGenerator().AsSourceGenerator()],
            additionalTexts,
            optionsProvider: null);

        driver = (CSharpGeneratorDriver)driver.RunGeneratorsAndUpdateCompilation(compilation, out var outputCompilation,
            out var diagnostics);

        var runResult = driver.GetRunResult();
        var sources = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var result in runResult.Results)
        foreach (var generated in result.GeneratedSources)
            sources[generated.HintName] = generated.SourceText.ToString();

        return new Result(runResult.Diagnostics, sources);
    }

    /// <summary>
    ///     Every runtime assembly the current process was loaded with, plus TkHLSL.Unity — the simplest reliable way to
    ///     get a complete BCL reference set for an in-process test compilation.
    /// </summary>
    private static IReadOnlyList<MetadataReference> GetReferences()
    {
        var trustedPlatformAssemblies =
            (AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") as string)?.Split(Path.PathSeparator) ?? [];

        var references = new List<MetadataReference>(trustedPlatformAssemblies.Length + 1);
        foreach (var path in trustedPlatformAssemblies)
            references.Add(MetadataReference.CreateFromFile(path));

        references.Add(MetadataReference.CreateFromFile(typeof(ComputeShaderBindingAttribute).Assembly.Location));
        return references;
    }

    public readonly record struct Result(
        ImmutableArray<Diagnostic> Diagnostics,
        IReadOnlyDictionary<string, string> GeneratedSources);

    private sealed class InMemoryAdditionalText(string path, string content) : AdditionalText
    {
        private readonly SourceText _text = SourceText.From(content);

        public override string Path { get; } = path;

        public override SourceText GetText(CancellationToken cancellationToken = default)
        {
            return _text;
        }
    }
}