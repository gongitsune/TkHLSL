using TkHLSL.Arena;
using TkHLSL.Ir;

namespace TkHLSL.Analysis;

/// <summary>
///     The result of analyzing one <see cref="Function" />'s (or <see cref="EntryPoint" />'s) body: the
///     set of <see cref="GlobalVariable" />s it references, directly or via calls to other
///     already-analyzed functions. Mirrors naga's <c>FunctionInfo</c> (see
///     docs/IMPLEMENTATION_PLAN.md §2.3, §7.2), minus the code-generation-oriented fields naga carries
///     (uniformity, sampling sets, etc.) that TkHLSL does not need.
/// </summary>
/// <remarks>
///     <see cref="GlobalUses" /> is typed <see cref="IReadOnlyCollection{T}" /> rather than the BCL's
///     <c>IReadOnlySet&lt;T&gt;</c> because that interface does not exist on netstandard2.0 (see
///     docs/IMPLEMENTATION_PLAN.md §13); <see cref="UsesGlobal" /> gives back the O(1) membership test a
///     set would otherwise provide.
/// </remarks>
public sealed class FunctionInfo
{
    private static readonly HashSet<Handle<GlobalVariable>> Empty = [];

    private readonly HashSet<Handle<GlobalVariable>> _globalUses;

    internal FunctionInfo(HashSet<Handle<GlobalVariable>>? globalUses)
    {
        _globalUses = globalUses is null or { Count: 0 } ? Empty : globalUses;
    }

    public IReadOnlyCollection<Handle<GlobalVariable>> GlobalUses => _globalUses;

    public bool UsesGlobal(Handle<GlobalVariable> global) => _globalUses.Contains(global);
}
