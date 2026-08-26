using Tk.Hlsl.Arena;
using Tk.Hlsl.Text;

namespace Tk.Hlsl.Ir;

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
public sealed record GlobalVariable(
    string Name,
    ResourceKind Kind,
    Handle<TypeInfo>? ElementType,
    ResourceRegister? Register,
    TextSpan Location);
