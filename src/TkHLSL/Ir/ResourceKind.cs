namespace TkHLSL.Ir;

/// <summary>
/// The concrete HLSL resource type of a <see cref="GlobalVariable"/>. Unlike naga's
/// <c>AddressSpace</c> (an abstract WGSL-level address space), TkHLSL enumerates HLSL's actual
/// resource types directly (see docs/IMPLEMENTATION_PLAN.md §2.4).
/// </summary>
public enum ResourceKind
{
    Texture2D,
    Texture2DArray,
    Texture3D,
    TextureCube,
    TextureCubeArray,

    RWTexture2D,
    RWTexture2DArray,
    RWTexture3D,

    StructuredBuffer,
    RWStructuredBuffer,
    AppendStructuredBuffer,
    ConsumeStructuredBuffer,

    ByteAddressBuffer,
    RWByteAddressBuffer,

    ConstantBuffer,

    SamplerState,
    SamplerComparisonState,

    /// <summary>A <c>cbuffer Name { ... }</c> block.</summary>
    CBuffer,

    /// <summary>A top-level global variable declared outside any <c>cbuffer</c>.</summary>
    PlainGlobal,
}
