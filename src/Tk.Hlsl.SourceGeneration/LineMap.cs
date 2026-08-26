namespace Tk.Hlsl.SourceGeneration;

/// <summary>
///     Converts a plain character offset into a 0-based (line, column) pair by scanning for
///     newlines — used to turn a <see cref="Tk.Hlsl.Text.TextSpan" /> (from a
///     <see cref="Tk.Hlsl.Diagnostics.Diagnostic" /> or an IR node's <c>Location</c>) into a
///     <see cref="LinePositionSpanInfo" /> without depending on <c>Microsoft.CodeAnalysis.Text</c> in
///     the cached pipeline value (see docs/IMPLEMENTATION_PLAN.md §9 Phase 9.3).
/// </summary>
internal static class LineMap
{
    public static LinePositionSpanInfo GetLinePositionSpan(string text, int start, int length)
    {
        var (startLine, startChar) = OffsetToPosition(text, start);
        var (endLine, endChar) = OffsetToPosition(text, Math.Min(start + length, text.Length));
        return new LinePositionSpanInfo(startLine, startChar, endLine, endChar);
    }

    private static (int Line, int Char) OffsetToPosition(string text, int offset)
    {
        var limit = Math.Min(Math.Max(offset, 0), text.Length);
        var line = 0;
        var lineStart = 0;
        for (var i = 0; i < limit; i++)
            if (text[i] == '\n')
            {
                line++;
                lineStart = i + 1;
            }

        return (line, limit - lineStart);
    }
}
