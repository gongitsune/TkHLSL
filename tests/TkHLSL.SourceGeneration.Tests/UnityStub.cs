namespace TkHLSL.SourceGeneration.Tests;

/// <summary>
///     A minimal stand-in for the <c>UnityEngine</c> API surface the generated code calls into —
///     enough for generated bindings to actually compile inside a source-generator test, without
///     pulling in a real Unity reference assembly (see docs/IMPLEMENTATION_PLAN.md §9 Phase 9.5).
/// </summary>
internal static class UnityStub
{
    public const string Source = """
        namespace UnityEngine
        {
            public struct Vector2 { public float x, y; }
            public struct Vector3 { public float x, y, z; }
            public struct Vector4 { public float x, y, z, w; }
            public struct Matrix4x4 { public float m00; }

            public class Texture { }
            public class RenderTexture : Texture { }
            public class ComputeBuffer { }
            public class GraphicsBuffer { }

            public static class Shader
            {
                public static int PropertyToID(string name) => name.GetHashCode();
            }

            public class ComputeShader
            {
                public int FindKernel(string name) => 0;
                public void SetTexture(int kernel, int id, Texture value) { }
                public void SetBuffer(int kernel, int id, ComputeBuffer value) { }
                public void SetBuffer(int kernel, int id, GraphicsBuffer value) { }
                public void SetFloat(int id, float value) { }
                public void SetFloatArray(int id, float[] values) { }
                public void SetInt(int id, int value) { }
                public void SetIntArray(int id, int[] values) { }
                public void SetBool(int id, bool value) { }
                public void SetVector(int id, Vector4 value) { }
                public void SetVectorArray(int id, Vector4[] values) { }
                public void SetMatrix(int id, Matrix4x4 value) { }
                public void SetConstantBuffer(int id, ComputeBuffer value, int offset, int size) { }
                public void Dispatch(int kernel, int groupsX, int groupsY, int groupsZ) { }
            }
        }
        """;
}
