namespace Tk.Hlsl.Text;

/// <summary>
///     One file's contiguous region within a <see cref="SourceText" />'s composite text: the
///     file's identity (<see cref="Path" />, empty for the root source) plus its
///     [<see cref="Start" />, <see cref="Start" /> + <see cref="Length" />) range of composite offsets.
/// </summary>
public readonly struct SourceSegment(string path, int start, int length)
{
    public string Path { get; } = path;

    public int Start { get; } = start;

    public int Length { get; } = length;

    public int End => Start + Length;
}

/// <summary>
///     A composite source: the root text plus every <c>#include</c> target spliced in by
///     <see cref="Preprocessing.Preprocessor" />, laid out as one contiguous string so
///     <see cref="Lexing.Token" /> can keep its two-int <see cref="TextSpan" /> — every token's span is an
///     offset into <see cref="Text" /> regardless of which file it came from. <see cref="TryGetLocation" />
///     maps a composite offset back to the originating file and its file-relative offset.
/// </summary>
/// <remarks>
///     <see cref="Slice(TextSpan)" /> is the primitive; <see cref="Text" /> is derived from it (currently by
///     eager concatenation in <see cref="Preprocessing.SourceTextBuilder" />). A future revision could make
///     <see cref="Text" /> lazily materialize from <see cref="Segments" /> without ever concatenating —
///     designing around <see cref="Slice(TextSpan)" /> from the start keeps that change cheap.
/// </remarks>
public sealed class SourceText
{
    private readonly SourceSegment[] _segments;
    private int _lastSegmentIndex;

    /// <summary>
    ///     Constructs a <see cref="SourceText" /> directly from a composite <paramref name="text" /> and its
    ///     <paramref name="segments" /> table. <paramref name="segments" /> must be sorted by
    ///     <see cref="SourceSegment.Start" /> with no gaps or overlaps and no entry extending past
    ///     <paramref name="text" />'s length — <see cref="Preprocessing.SourceTextBuilder" /> upholds this
    ///     invariant when assembling includes; most callers should go through
    ///     <see cref="Preprocessing.Preprocessor.Process" /> or <see cref="FromRoot" /> instead of calling
    ///     this directly.
    /// </summary>
    public SourceText(string text, IReadOnlyList<SourceSegment> segments)
    {
        Text = text;
        _segments = [.. segments];
    }

    /// <summary>The composite text: the root source followed by every spliced include, in encounter order.</summary>
    public string Text { get; }

    /// <summary>
    ///     Every file that makes up <see cref="Text" />, in ascending, non-overlapping, contiguous
    ///     <see cref="SourceSegment.Start" /> order. <c>Segments[0]</c> is always the root (its
    ///     <see cref="SourceSegment.Path" /> is <see cref="string.Empty" /> unless
    ///     <see cref="Preprocessing.HlslParseOptions" /> supplied one).
    /// </summary>
    public IReadOnlyList<SourceSegment> Segments => _segments;

    /// <summary>A <see cref="SourceText" /> over a single root string with no includes spliced in.</summary>
    public static SourceText FromRoot(string root, string? rootPath = null)
    {
        if (root is null) throw new ArgumentNullException(nameof(root));

        return new SourceText(root, [new SourceSegment(rootPath ?? string.Empty, 0, root.Length)]);
    }

    /// <summary>Zero-allocation view of <paramref name="span" /> into <see cref="Text" />.</summary>
    public ReadOnlySpan<char> Slice(TextSpan span)
    {
        return Text.AsSpan(span.Start, span.Length);
    }

    /// <summary>
    ///     Resolves a composite offset (e.g. <see cref="Diagnostics.Diagnostic.Span" />'s <see cref="TextSpan.Start" />)
    ///     back to the file it falls in and that file's local offset. Returns <see langword="false" /> only
    ///     for an offset outside every segment (e.g. one of the newline separators between spliced files, or
    ///     past the end of <see cref="Text" />).
    /// </summary>
    public bool TryGetLocation(int offset, out SourceSegment segment, out int offsetInFile)
    {
        var cached = _lastSegmentIndex;
        if (cached < _segments.Length && Contains(_segments[cached], offset))
        {
            segment = _segments[cached];
            offsetInFile = offset - segment.Start;
            return true;
        }

        var lo = 0;
        var hi = _segments.Length - 1;
        while (lo <= hi)
        {
            var mid = lo + (hi - lo) / 2;
            var candidate = _segments[mid];
            if (offset < candidate.Start)
            {
                hi = mid - 1;
            }
            else if (offset >= candidate.End)
            {
                lo = mid + 1;
            }
            else
            {
                _lastSegmentIndex = mid;
                segment = candidate;
                offsetInFile = offset - candidate.Start;
                return true;
            }
        }

        segment = default;
        offsetInFile = 0;
        return false;
    }

    private static bool Contains(SourceSegment segment, int offset)
    {
        return offset >= segment.Start && offset < segment.End;
    }
}
