using Tk.Hlsl.Ir;
using Tk.Hlsl.Text;

namespace Tk.Hlsl.Model;

/// <summary>
///     A single top-level resource declaration, projected into a flat, name-based shape for external
///     consumers. Appears both in <see cref="HlslCompilationResult.AllResources" /> (every declaration,
///     used or not) and in <see cref="KernelBindingInfo.Bindings" /> (only those a given kernel actually
///     reads or writes) — see docs/IMPLEMENTATION_PLAN.md §7.3.
/// </summary>
/// <param name="Fields">
///     The members declared inside a <c>cbuffer { ... }</c> block, in declaration order. Empty for
///     every other <see cref="Ir.ResourceKind" /> (see docs/IMPLEMENTATION_PLAN.md §9 Phase 7).
/// </param>
public sealed record ResourceBinding(
    string Name,
    ResourceKind ResourceKind,
    string? ElementTypeName,
    ResourceRegister? ExplicitRegister,
    TextSpan Location,
    IReadOnlyList<HlslField> Fields = null!)
{
    public IReadOnlyList<HlslField> Fields { get; init; } = Fields ?? [];
}
