namespace Tk.Hlsl.Analysis;

/// <summary>
///     The result of <see cref="Analyzer.Analyze" />: a <see cref="FunctionInfo" /> for every entry in
///     <see cref="Ir.Module.Functions" /> and <see cref="Ir.Module.EntryPoints" />, at the same index as
///     the corresponding <see cref="Ir.Module" /> entry (see docs/IMPLEMENTATION_PLAN.md §7.2).
/// </summary>
public sealed class ModuleInfo
{
    internal ModuleInfo(IReadOnlyList<FunctionInfo> functions, IReadOnlyList<FunctionInfo> entryPoints)
    {
        Functions = functions;
        EntryPoints = entryPoints;
    }

    /// <summary>Parallel to <see cref="Ir.Module.Functions" /> — same length, same order.</summary>
    public IReadOnlyList<FunctionInfo> Functions { get; }

    /// <summary>Parallel to <see cref="Ir.Module.EntryPoints" /> — same length, same order.</summary>
    public IReadOnlyList<FunctionInfo> EntryPoints { get; }
}
