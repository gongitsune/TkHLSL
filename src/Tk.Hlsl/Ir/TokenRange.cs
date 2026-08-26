namespace Tk.Hlsl.Ir;

/// <summary>
///     A half-open [Start, End) range of indices into a token list — the "body" analogue of
///     <see cref="Tk.Hlsl.Text.TextSpan" />, but indexing tokens instead of characters. Used so a
///     function's body can be recorded without copying or reparsing its tokens (see
///     docs/IMPLEMENTATION_PLAN.md §9 Phase 3: "関数本体はトークン範囲のみ記録し、パースしない").
/// </summary>
public readonly struct TokenRange(int start, int end) : IEquatable<TokenRange>
{
    public int Start { get; } = start;

    public int End { get; } = end;

    public int Length => End - Start;

    public bool Equals(TokenRange other)
    {
        return Start == other.Start && End == other.End;
    }

    public override bool Equals(object? obj)
    {
        return obj is TokenRange other && Equals(other);
    }

    public override int GetHashCode()
    {
        unchecked
        {
            return (Start * 397) ^ End;
        }
    }

    public override string ToString()
    {
        return $"[{Start}..{End})";
    }

    public static bool operator ==(TokenRange left, TokenRange right)
    {
        return left.Equals(right);
    }

    public static bool operator !=(TokenRange left, TokenRange right)
    {
        return !left.Equals(right);
    }
}