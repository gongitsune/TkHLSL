using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using TkHLSL.SourceGeneration.Diagnostics;

namespace TkHLSL.SourceGeneration;

/// <summary>
///     Reads Unity ComputeShader (<c>.compute</c>) files from Roslyn <c>AdditionalFiles</c> and
///     generates typed C# bindings for every type marked
///     <c>[TkHLSL.Unity.ComputeShaderBinding("...")]</c> — see docs/IMPLEMENTATION_PLAN.md §9 Phase 9.
///     Never reads from disk itself: the target file (and anything it <c>#include</c>s) must already
///     be an <c>AdditionalFile</c> of the compiling project.
/// </summary>
[Generator(LanguageNames.CSharp)]
public sealed class ComputeShaderBindingGenerator : IIncrementalGenerator
{
    private const string AttributeMetadataName = "TkHLSL.Unity.ComputeShaderBindingAttribute";
    private const string HlslExtensions1 = ".compute";
    private const string HlslExtensions2 = ".hlsl";
    private const string HlslExtensions3 = ".cginc";
    private const string HlslExtensions4 = ".hlslinc";

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var targets = context.SyntaxProvider
            .ForAttributeWithMetadataName(AttributeMetadataName, IsCandidate, ToTargetInfo)
            .Where(t => t is not null)
            .Select((t, _) => t!);

        var files = context.AdditionalTextsProvider
            .Where(IsHlslFile)
            .Select(ToAdditionalHlslFile)
            .Collect()
            .Select((arr, _) => new EquatableArray<AdditionalHlslFile>(arr.ToArray()));

        var combined = targets.Combine(files);
        var generated = combined.Select((input, _) => PipelineCompute.Compute(input));

        context.RegisterSourceOutput(generated, Report);
    }

    private static bool IsCandidate(SyntaxNode node, CancellationToken _)
    {
        return node is ClassDeclarationSyntax or StructDeclarationSyntax;
    }

    private static AttributeTargetInfo? ToTargetInfo(GeneratorAttributeSyntaxContext ctx, CancellationToken _)
    {
        if (ctx.TargetSymbol is not INamedTypeSymbol symbol) return null;

        var attributeData = ctx.Attributes[0];
        if (attributeData.ConstructorArguments.Length == 0) return null;
        var path = attributeData.ConstructorArguments[0].Value as string;
        if (path is null) return null;

        var defines = Array.Empty<string>();
        foreach (var namedArg in attributeData.NamedArguments)
            if (namedArg is { Key: "Defines", Value.Values: { IsDefault: false } values })
            {
                var list = new List<string>(values.Length);
                foreach (var v in values)
                    if (v.Value is string s)
                        list.Add(s);
                defines = [.. list];
            }

        var chain = new List<TypeChainEntry>();
        var isPartial = true;
        for (var current = symbol; current is not null; current = current.ContainingType)
        {
            var keyword = current.TypeKind == TypeKind.Struct ? "struct" : "class";
            chain.Insert(0, new TypeChainEntry(keyword, current.Name));
            var declaration = current.DeclaringSyntaxReferences.Length > 0
                ? current.DeclaringSyntaxReferences[0].GetSyntax() as TypeDeclarationSyntax
                : null;
            if (declaration is null || !declaration.Modifiers.Any(SyntaxKind.PartialKeyword)) isPartial = false;
        }

        var ns = symbol.ContainingNamespace is { IsGlobalNamespace: false } nsSymbol
            ? nsSymbol.ToDisplayString()
            : string.Empty;

        var location = attributeData.ApplicationSyntaxReference is { } appRef
            ? Location.Create(appRef.SyntaxTree, appRef.Span)
            : symbol.Locations.FirstOrDefault() ?? Location.None;
        var lineSpan = location.GetLineSpan();

        return new AttributeTargetInfo(
            ns,
            new EquatableArray<TypeChainEntry>([.. chain]),
            isPartial,
            path,
            new EquatableArray<string>(defines),
            location.SourceTree?.FilePath ?? string.Empty,
            new LinePositionSpanInfo(
                lineSpan.StartLinePosition.Line, lineSpan.StartLinePosition.Character,
                lineSpan.EndLinePosition.Line, lineSpan.EndLinePosition.Character));
    }

    private static bool IsHlslFile(AdditionalText file)
    {
        var path = file.Path;
        return path.EndsWith(HlslExtensions1, StringComparison.OrdinalIgnoreCase) ||
               path.EndsWith(HlslExtensions2, StringComparison.OrdinalIgnoreCase) ||
               path.EndsWith(HlslExtensions3, StringComparison.OrdinalIgnoreCase) ||
               path.EndsWith(HlslExtensions4, StringComparison.OrdinalIgnoreCase);
    }

    private static AdditionalHlslFile ToAdditionalHlslFile(AdditionalText file, CancellationToken cancellationToken)
    {
        var text = file.GetText(cancellationToken)?.ToString() ?? string.Empty;
        return new AdditionalHlslFile(PathMatching.Normalize(file.Path), text);
    }

    private static void Report(SourceProductionContext context, GenerationResult result)
    {
        if (result.Source is { } source) context.AddSource(SanitizeHintName(result.HintName), source);

        foreach (var d in result.Diagnostics)
        {
            if (!TkHlslDiagnostics.ById.TryGetValue(d.DescriptorId, out var descriptor)) continue;

            var location = Location.Create(d.FilePath, TextSpan.FromBounds(0, 0), d.Span.ToLinePositionSpan());
            var args = new object[d.MessageArgs.Count];
            for (var i = 0; i < d.MessageArgs.Count; i++) args[i] = d.MessageArgs[i];
            context.ReportDiagnostic(Diagnostic.Create(descriptor, location, args));
        }
    }

    private static string SanitizeHintName(string hintName)
    {
        return hintName.Replace('/', '_').Replace('\\', '_');
    }
}