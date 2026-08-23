using TkHLSL.Lexing;

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

var result = Lexer.Tokenize(source);
foreach (var token in result.Tokens)
{
    Console.WriteLine($"Kind: {token.Kind}, Value: '{source[token.Span.Start..token.Span.End]}'");
}