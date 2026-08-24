using TkHLSL.Lexing;
using TkHLSL.Preprocessing;
using TkHLSL.Syntax;

const string source = """
                      #pragma kernel CSMain
                      
                      RWStructuredBuffer<float> _Result : register(u0);
                      Texture2D<float4> _InputTexture : register(t0);
                      SamplerState sampler_InputTexture : register(s0);
                      
                      cbuffer Params : register(b0)
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

var lexResult = Lexer.Tokenize(source);
var processResult = Preprocessor.Process(source, lexResult.Tokens, new HlslParseOptions());
var parseResult = TopLevelParser.Parse(source, processResult.Tokens, processResult.KernelNames);

Console.WriteLine($"EntryPoints: {string.Join(", ", parseResult.EntryPoints)}");
Console.WriteLine($"Functions: {string.Join(", ", parseResult.Functions)}");
