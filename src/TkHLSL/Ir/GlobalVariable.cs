using TkHLSL.Arena;
using TkHLSL.Text;

namespace TkHLSL.Ir;

/// <summary>
/// A top-level resource or variable declaration: a buffer, texture, sampler, <c>cbuffer</c> block,
/// or a plain global declared outside any <c>cbuffer</c>.
/// </summary>
/// <param name="ElementType">
/// The buffer/texture's generic type argument (e.g. the <c>float4</c> in
/// <c>StructuredBuffer&lt;float4&gt;</c>), or a plain global's declared type. <see langword="null"/>
/// for resource kinds with no element type (<see cref="ResourceKind.SamplerState"/>,
/// <see cref="ResourceKind.ByteAddressBuffer"/>, <see cref="ResourceKind.CBuffer"/>, etc.).
/// </param>
/// <param name="Members">
/// The members declared inside a <c>cbuffer { ... }</c> block, in declaration order. Empty for every
/// other <see cref="ResourceKind"/> (see docs/IMPLEMENTATION_PLAN.md §9 Phase 7).
/// </param>
public sealed record GlobalVariable(
    string Name,
    ResourceKind Kind,
    Handle<TypeInfo>? ElementType,
    ResourceRegister? Register,
    TextSpan Location,
    IReadOnlyList<StructMember> Members = null!)
{
    public IReadOnlyList<StructMember> Members { get; init; } = Members ?? [];
}
