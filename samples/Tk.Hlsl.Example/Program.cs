using Tk.Hlsl;
using Tk.Hlsl.Model;
using Tk.Hlsl.Preprocessing;
using Tk.Hlsl.Text;

const string source = """
                      #pragma kernel CSMain

                      RWStructuredBuffer<float> _Result;
                      Texture2D<float4> _InputTexture;
                      SamplerState sampler_InputTexture;

                      cbuffer Params
                      {
                          float4 _Params;
                          int _Count;
                      };

                      float square(float x)
                      {
                          return x * x;
                      }

                      [numthreads(8, 8, 1)]
                      void CSMain(uint3 id : SV_DispatchThreadID)
                      {
                          float4 color = _InputTexture.Sample(sampler_InputTexture, float2(0.5, 0.5));
                          _Result[id.x] = square(color.r) + _Params.x;
                      }
                      """;

var result = HlslParser.Parse(source, new HlslParseOptions());

foreach (var kernel in result.Kernels)
{
    Console.WriteLine($"Kernel {kernel.Name} {kernel.ThreadGroupSize}");
    foreach (var binding in kernel.Bindings)
        Console.WriteLine($"  {binding.ResourceKind} {binding.Name} ({binding.ElementTypeName ?? "-"})");
}

foreach (var diagnostic in result.Diagnostics)
    Console.WriteLine(diagnostic);

Console.WriteLine();
Console.WriteLine("--- #include demo ---");
RunIncludeDemo();
return;

static void RunIncludeDemo()
{
    // A minimal in-memory IIncludeResolver: Tk.Hlsl performs no file I/O itself, so a host maps
    // requested paths to content however it likes (disk, a virtual FS, a bundled asset, ...).
    const string includedSource = """
                                  #pragma once

                                  #define THREADS 8

                                  RWStructuredBuffer<float> _Result : register(u0);
                                  """;

    const string includeSource = """
                                 #pragma kernel CSMain
                                 #include "Common.cginc"

                                 [numthreads(THREADS, THREADS, 1)]
                                 void CSMain(uint3 id : SV_DispatchThreadID)
                                 {
                                     _Result[id.x] = 1.0;
                                 }
                                 """;

    var options = new HlslParseOptions(
        includeResolver: new InMemoryIncludeResolver(new Dictionary<string, string>
        {
            ["Common.cginc"] = includedSource
        }),
        sourcePath: "Main.compute");

    var includeResult = HlslParser.Parse(includeSource, options);

    foreach (var kernel in includeResult.Kernels)
    {
        Console.WriteLine($"Kernel {kernel.Name} {kernel.ThreadGroupSize}");
        foreach (var binding in kernel.Bindings)
            // The resource is declared inside Common.cginc, not Main.compute — SourceText.TryGetLocation
            // maps its composite Location back to the file and file-relative offset it came from.
            Console.WriteLine($"  {binding.ResourceKind} {binding.Name} ({binding.ElementTypeName ?? "-"})" +
                              $" declared in {LocationOf(includeResult, binding.Location)}");
    }

    foreach (var diagnostic in includeResult.Diagnostics)
        Console.WriteLine($"{diagnostic} in {LocationOf(includeResult, diagnostic.Span)}");

    // A typo'd directive inside the include shows a diagnostic mapped back into Common.cginc.
    var brokenOptions = new HlslParseOptions(
        includeResolver: new InMemoryIncludeResolver(new Dictionary<string, string>
        {
            ["Common.cginc"] = "#defone THREADS 8\n"
        }),
        sourcePath: "Main.compute");

    var brokenResult = HlslParser.Parse("#include \"Common.cginc\"\n", brokenOptions);
    foreach (var diagnostic in brokenResult.Diagnostics)
        Console.WriteLine($"{diagnostic} in {LocationOf(brokenResult, diagnostic.Span)}");
}

static string LocationOf(HlslCompilationResult result, TextSpan span)
{
    return result.Source.TryGetLocation(span.Start, out var segment, out var offsetInFile)
        ? $"{(segment.Path.Length == 0 ? "<root>" : segment.Path)}:{offsetInFile}"
        : "<unknown>";
}

file sealed class InMemoryIncludeResolver(Dictionary<string, string> files) : IIncludeResolver
{
    public bool TryResolve(string requestedPath, string? includerPath, out string? resolvedPath, out string? content)
    {
        if (files.TryGetValue(requestedPath, out var found))
        {
            resolvedPath = requestedPath;
            content = found;
            return true;
        }

        resolvedPath = null;
        content = null;
        return false;
    }
}