using Microsoft.CodeAnalysis;

namespace Tk.Hlsl.SourceGeneration;

/// <summary>
///     Everything the generator needs about one <c>[ComputeShaderBinding]</c>-attributed type,
///     projected out of the Roslyn symbol model into a small, structurally-equatable record.
/// </summary>
/// <remarks>
///     Deliberately does not hold an <see cref="Microsoft.CodeAnalysis.ISymbol" /> — an
///     <c>IIncrementalGenerator</c> pipeline step that returns a symbol breaks incremental caching,
///     because symbols compare by reference and are recreated on every recompilation (see
///     docs/IMPLEMENTATION_PLAN.md §9 Phase 9.2).
/// </remarks>
internal sealed record AttributeTargetInfo(
    string Namespace,
    EquatableArray<TypeChainEntry> TypeChain,
    bool IsPartial,
    string Path,
    EquatableArray<string> Defines,
    string DiagnosticLocationFilePath,
    LinePositionSpanInfo DiagnosticLocationSpan);

/// <summary>One level of a (possibly nested) type declaration, outermost first.</summary>
internal readonly record struct TypeChainEntry(string Keyword, string Name);

/// <summary>A structurally-equatable stand-in for <see cref="Microsoft.CodeAnalysis.Text.LinePositionSpan" />.</summary>
internal readonly record struct LinePositionSpanInfo(int StartLine, int StartChar, int EndLine, int EndChar)
{
    public Microsoft.CodeAnalysis.Text.LinePositionSpan ToLinePositionSpan()
    {
        return new Microsoft.CodeAnalysis.Text.LinePositionSpan(
            new Microsoft.CodeAnalysis.Text.LinePosition(StartLine, StartChar),
            new Microsoft.CodeAnalysis.Text.LinePosition(EndLine, EndChar));
    }
}
