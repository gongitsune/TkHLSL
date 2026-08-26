using System.Text;
using Tk.Hlsl.Text;

namespace Tk.Hlsl.Preprocessing;

/// <summary>
///     Incrementally assembles the composite <see cref="SourceText" /> that <see cref="Preprocessor" />
///     splices <c>#include</c> targets into. The root text is never copied unless at least one include is
///     actually spliced (<see cref="HasSplices" />), so a source with no <c>#include</c> directives pays
///     zero extra allocation — <see cref="Build" /> returns <see cref="SourceText.FromRoot" /> over the
///     original root string.
/// </summary>
internal sealed class SourceTextBuilder(string root, string rootPath)
{
    private readonly List<SourceSegment> _segments = [new SourceSegment(rootPath, 0, root.Length)];
    private StringBuilder? _builder;

    internal bool HasSplices => _builder is not null;

    /// <summary>
    ///     Appends <paramref name="content" /> (an include target's resolved text) to the composite,
    ///     reserving its region before the caller recurses into tokenizing/scanning it, and returns the
    ///     composite offset at which it starts.
    /// </summary>
    internal int Reserve(string path, string content)
    {
        if (_builder is null)
        {
            _builder = new StringBuilder(root.Length + content.Length + 1);
            _builder.Append(root).Append('\n');
        }

        var start = _builder.Length;
        _segments.Add(new SourceSegment(path, start, content.Length));
        _builder.Append(content).Append('\n');
        return start;
    }

    internal SourceText Build()
    {
        return _builder is null
            ? SourceText.FromRoot(root, rootPath)
            : new SourceText(_builder.ToString(), [.. _segments]);
    }
}
