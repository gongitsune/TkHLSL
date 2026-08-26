using Tk.Hlsl.Text;

namespace Tk.Hlsl.Ir;

/// <summary>
/// A user <c>struct Name { ... };</c> declaration, with its members (see docs/IMPLEMENTATION_PLAN.md
/// §9 Phase 7). Stored separately from <see cref="Module.Types"/> — <see cref="TypeInfo"/> stays a
/// name-only, deduplicated reference so every use site of a type shares one handle, while a
/// <see cref="StructDefinition"/> is the (non-deduplicated, declaration-order) one true definition of
/// that name's members.
/// </summary>
public sealed record StructDefinition(
    string Name,
    IReadOnlyList<StructMember> Members,
    TextSpan Location);
