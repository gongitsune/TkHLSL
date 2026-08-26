namespace TkHLSL.SourceGeneration.Emit;

/// <summary>
///     Maps HLSL scalar/vector/matrix type names to the C# type and <c>ComputeShader.Set*</c> method
///     used to bind a uniform of that type, and to the C# field type used inside a generated
///     <c>[StructLayout(LayoutKind.Sequential)]</c> element struct — see
///     docs/IMPLEMENTATION_PLAN.md §9 Phase 9.4. Only the HLSL types actually reachable from a
///     <c>cbuffer</c> member, a plain global, or a <c>StructuredBuffer&lt;T&gt;</c> element are mapped;
///     anything else (a nested user struct, a resource-typed field, <c>double</c>, ...) is reported via
///     <c>TKH1005</c> and skipped.
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

    public readonly struct UniformMapping(string cSharpType, string setterMethod, int size)
    {
        /// <summary>The scalar C# type (e.g. <c>float</c>, <c>Vector4</c>).</summary>
        public string CSharpType { get; } = cSharpType;

        /// <summary>The <c>ComputeShader</c> method base name (e.g. <c>SetFloat</c>) — append <c>Array</c> for the array overload.</summary>
        public string SetterMethod { get; } = setterMethod;

        /// <summary>The HLSL constant-buffer packing size in bytes, used by the struct-packing check.</summary>
        public int Size { get; } = size;
    }

    public readonly struct FieldMapping(string cSharpType, int size)
    {
        /// <summary>The C# field type for a generated element struct.</summary>
        public string CSharpType { get; } = cSharpType;

        /// <summary>The HLSL constant-buffer packing size in bytes.</summary>
        public int Size { get; } = size;
    }
}