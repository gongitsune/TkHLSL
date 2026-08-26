using Tk.Hlsl.Text;

namespace Tk.Hlsl.Model;

/// <summary>
///     A single member of a <c>struct</c> declaration (<see cref="HlslStruct.Fields" />) or of a
///     <c>cbuffer</c> block (<see cref="ResourceBinding.Fields" />), projected into a flat, name-based
///     shape for external consumers (see docs/IMPLEMENTATION_PLAN.md §9 Phase 7).
/// </summary>
public sealed record HlslField(
    string Name,
    string TypeName,
    int? ArrayLength,
    string? Semantic,
    TextSpan Location);
