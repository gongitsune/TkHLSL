namespace TkHLSL.SourceGeneration.Emit;

/// <summary>
///     Maps HLSL scalar/vector/matrix type names to the C# type and <c>ComputeShader.Set*</c> method
///     used to bind a uniform of that type, and to the C# field type used inside a generated
///     <c>[StructLayout(LayoutKind.Sequential)]</c> element struct — see
///     docs/IMPLEMENTATION_PLAN.md §9 Phase 9.4. Only the HLSL types actually reachable from a
///     <c>cbuffer</c> member, a plain global, or a <c>StructuredBuffer&lt;T&gt;</c> element are mapped;
///     anything else (a nested user struct, a resource-typed field, <c>double</c>, ...) is reported via
///     <c>TKH1005</c> and skipped. <c>int2/3/4</c> and <c>uint2/3/4</c> uniforms are mapped to
///     <c>ComputeShader.SetInts</c> with one C# argument per component (there is no
///     <c>ComputeShader</c> array overload for them, so the array form of those types is still
///     reported via <c>TKH1005</c> — see <see cref="UniformMapping.ComponentCount" />).
/// </summary>
internal static class HlslTypeMap
{
    public static bool TryMapUniform(string hlslType, out UniformMapping mapping)
    {
        switch (hlslType)
        {
            case "float":
                mapping = new UniformMapping("float", "SetFloat", 4);
                return true;
            case "int":
                mapping = new UniformMapping("int", "SetInt", 4);
                return true;
            case "uint":
                // ComputeShader has no SetUInt overload; SetInt reinterprets the same 4 bytes.
                mapping = new UniformMapping("int", "SetInt", 4);
                return true;
            case "bool":
                mapping = new UniformMapping("bool", "SetBool", 4);
                return true;
            case "float2":
                mapping = new UniformMapping("global::UnityEngine.Vector4", "SetVector", 8);
                return true;
            case "float3":
                mapping = new UniformMapping("global::UnityEngine.Vector4", "SetVector", 12);
                return true;
            case "float4":
                mapping = new UniformMapping("global::UnityEngine.Vector4", "SetVector", 16);
                return true;
            case "float3x3":
                mapping = new UniformMapping("global::UnityEngine.Matrix4x4", "SetMatrix", 48);
                return true;
            case "float4x4":
                mapping = new UniformMapping("global::UnityEngine.Matrix4x4", "SetMatrix", 64);
                return true;
            case "int2":
                mapping = new UniformMapping("int", "SetInts", 8, 2);
                return true;
            case "int3":
                mapping = new UniformMapping("int", "SetInts", 12, 3);
                return true;
            case "int4":
                mapping = new UniformMapping("int", "SetInts", 16, 4);
                return true;
            case "uint2":
                // ComputeShader has no SetUInts overload; SetInts reinterprets the same 4 bytes per component.
                mapping = new UniformMapping("uint", "SetInts", 8, 2, "int");
                return true;
            case "uint3":
                mapping = new UniformMapping("uint", "SetInts", 12, 3, "int");
                return true;
            case "uint4":
                mapping = new UniformMapping("uint", "SetInts", 16, 4, "int");
                return true;
            default:
                mapping = default;
                return false;
        }
    }

    public static bool TryMapField(string hlslType, out FieldMapping mapping)
    {
        switch (hlslType)
        {
            case "float":
                mapping = new FieldMapping("float", 4);
                return true;
            case "int":
                mapping = new FieldMapping("int", 4);
                return true;
            case "uint":
                mapping = new FieldMapping("uint", 4);
                return true;
            case "bool":
                // HLSL bool is a 4-byte word in a constant buffer, not the 1-byte C# bool.
                mapping = new FieldMapping("int", 4);
                return true;
            case "float2":
                mapping = new FieldMapping("global::UnityEngine.Vector2", 8);
                return true;
            case "float3":
                mapping = new FieldMapping("global::UnityEngine.Vector3", 12);
                return true;
            case "float4":
                mapping = new FieldMapping("global::UnityEngine.Vector4", 16);
                return true;
            case "float3x3":
                mapping = new FieldMapping("global::UnityEngine.Matrix4x4", 48);
                return true;
            case "float4x4":
                mapping = new FieldMapping("global::UnityEngine.Matrix4x4", 64);
                return true;
            default:
                mapping = default;
                return false;
        }
    }

    public readonly struct UniformMapping(string cSharpType, string setterMethod, int size,
        int componentCount = 0, string? componentCast = null)
    {
        /// <summary>
        ///     The C# type of a single value (e.g. <c>float</c>, <c>Vector4</c>) — or, when
        ///     <see cref="ComponentCount" /> is non-zero, the C# type of a single vector component
        ///     (e.g. <c>int</c> for <c>int3</c>/<c>uint3</c>).
        /// </summary>
        public string CSharpType { get; } = cSharpType;

        /// <summary>The <c>ComputeShader</c> method base name (e.g. <c>SetFloat</c>) — append <c>Array</c> for the array overload.</summary>
        public string SetterMethod { get; } = setterMethod;

        /// <summary>The HLSL constant-buffer packing size in bytes, used by the struct-packing check.</summary>
        public int Size { get; } = size;

        /// <summary>
        ///     <c>0</c> for a mapping with a single-value setter (the common case). Otherwise the
        ///     vector component count (2–4) for a mapping whose setter instead takes one C# argument
        ///     per component (e.g. <c>int3</c>/<c>uint3</c> → <c>SetInts(id, x, y, z)</c>), because
        ///     <c>ComputeShader</c> has no single-vector overload for those types.
        /// </summary>
        public int ComponentCount { get; } = componentCount;

        /// <summary>
        ///     When <see cref="ComponentCount" /> is non-zero and this is non-null, the C# type each
        ///     component argument is <c>unchecked</c>-cast to before being passed to
        ///     <see cref="SetterMethod" /> (e.g. <c>"int"</c> for <c>uint2/3/4</c>, whose components are
        ///     reinterpreted as <c>int</c> the same way the scalar <c>uint</c> mapping reinterprets a
        ///     single value for <c>SetInt</c>).
        /// </summary>
        public string? ComponentCast { get; } = componentCast;
    }

    public readonly struct FieldMapping(string cSharpType, int size)
    {
        /// <summary>The C# field type for a generated element struct.</summary>
        public string CSharpType { get; } = cSharpType;

        /// <summary>The HLSL constant-buffer packing size in bytes.</summary>
        public int Size { get; } = size;
    }
}