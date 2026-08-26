using TkHLSL.Ir;
using TkHLSL.Text;

namespace TkHLSL.Model;

/// <summary>
///     One compute kernel and the resources it must have bound before dispatch. Combines an
///     <see cref="EntryPoint" />'s signature with its <see cref="Analysis.FunctionInfo.GlobalUses" />,
///     resolved to <see cref="ResourceBinding" />s in declaration order (see
///     docs/IMPLEMENTATION_PLAN.md §7.3, §9 Phase 5).
/// </summary>
public sealed record KernelBindingInfo(
    string Name,
    ThreadGroupSize ThreadGroupSize,
    IReadOnlyList<ResourceBinding> Bindings,
    TextSpan Location);
