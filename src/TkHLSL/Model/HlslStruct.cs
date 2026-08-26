using TkHLSL.Text;

namespace TkHLSL.Model;

/// <summary>
///     A user <c>struct Name { ... };</c> declaration and its members, in declaration order. Appears in
///     <see cref="HlslCompilationResult.Structs" /> — every <c>struct</c> found in the source, whether
///     used as a resource's element type or not (see docs/IMPLEMENTATION_PLAN.md §9 Phase 7).
/// </summary>
public sealed record HlslStruct(
    string Name,
    IReadOnlyList<HlslField> Fields,
    TextSpan Location);
