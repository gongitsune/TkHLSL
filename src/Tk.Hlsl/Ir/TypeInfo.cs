namespace Tk.Hlsl.Ir;

/// <summary>
/// A named type reference stored in <see cref="Module.Types"/>: either a user <c>struct</c>
/// declaration or a built-in type name (e.g. <c>float4</c>) used as a resource's element type.
/// Phase 3 does not parse struct members (see docs/IMPLEMENTATION_PLAN.md §9 Phase 3), so a
/// <see cref="TypeInfo"/> carries only the name — equality by name is exactly what
/// <see cref="Tk.Hlsl.Arena.UniqueArena{T}"/> needs to deduplicate repeated references to the same type
/// (e.g. every <c>StructuredBuffer&lt;float&gt;</c> in a file shares one <c>Handle&lt;TypeInfo&gt;</c>).
/// </summary>
public sealed record TypeInfo(string Name);
