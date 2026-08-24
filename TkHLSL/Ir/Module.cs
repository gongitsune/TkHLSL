using TkHLSL.Arena;
using TkHLSL.Diagnostics;

namespace TkHLSL.Ir;

/// <summary>
/// The parsed representation of an HLSL source: types, resource declarations, helper functions, and
/// kernel entry points, plus any diagnostics raised while building it. Built by
/// <see cref="TkHLSL.Syntax.TopLevelParser.Parse"/> and consumed by the Phase 4 Analyzer (see
/// docs/IMPLEMENTATION_PLAN.md §5, §7.1).
/// </summary>
/// <remarks>
/// <see cref="Functions"/> is populated in source order, which — because HLSL requires a function to
/// be declared before any of its callers and does not support recursion — is also "callee before
/// caller" order (see docs/IMPLEMENTATION_PLAN.md §2.2). The Phase 4 Analyzer relies on this
/// invariant to resolve call graphs in a single forward pass.
/// </remarks>
public sealed class Module
{
    public UniqueArena<TypeInfo> Types { get; } = new();

    public Arena<GlobalVariable> GlobalVariables { get; } = new();

    public Arena<Function> Functions { get; } = new();

    public List<EntryPoint> EntryPoints { get; } = [];

    public List<Diagnostic> Diagnostics { get; } = [];
}
