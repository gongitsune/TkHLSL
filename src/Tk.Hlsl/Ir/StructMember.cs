using Tk.Hlsl.Text;

namespace Tk.Hlsl.Ir;

/// <summary>
/// One field of a <c>struct</c> declaration or a <c>cbuffer</c> block (see
/// <see cref="StructDefinition"/> and <see cref="GlobalVariable.Members"/>).
/// </summary>
/// <param name="TypeName">
/// The member's declared type name, kept as a string (not resolved to a <see cref="TypeInfo"/>
/// handle) — a member's type may itself be a user struct that has not been registered yet at the
/// point the member is parsed, and downstream consumers only need the name to map it to a host
/// language type (see docs/IMPLEMENTATION_PLAN.md §9 Phase 7).
/// </param>
/// <param name="ArrayLength">The member's array length (e.g. the <c>4</c> in <c>float4 x[4];</c>), or <see langword="null"/> if not an array.</param>
/// <param name="Semantic">The member's semantic (e.g. <c>SV_Position</c>), or <see langword="null"/> if none.</param>
public sealed record StructMember(
    string Name,
    string TypeName,
    int? ArrayLength,
    string? Semantic,
    TextSpan Location);
