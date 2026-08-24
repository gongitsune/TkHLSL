using TkHLSL.Text;

namespace TkHLSL.Ir;

/// <summary>
/// A compute kernel: a function carrying a <c>[numthreads(x, y, z)]</c> attribute whose name also
/// appears in a <c>#pragma kernel</c> line. Mirrors naga's <c>Function</c>/<c>EntryPoint</c> split
/// (see docs/IMPLEMENTATION_PLAN.md §2.2) — <see cref="Function"/> is embedded directly rather than
/// stored in <see cref="Module.Functions"/>, since a kernel can never be called from other code.
/// </summary>
public sealed record EntryPoint(string Name, ThreadGroupSize ThreadGroupSize, Function Function, TextSpan Location);
