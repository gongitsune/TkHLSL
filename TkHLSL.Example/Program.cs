using TkHLSL;
using TkHLSL.Preprocessing;

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