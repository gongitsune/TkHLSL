namespace TkHLSL.Text;

/// <summary>
/// A half-open [Start, Start+Length) range of character offsets into a source text.
/// </summary>
public readonly struct TextSpan : IEquatable<TextSpan>
{
    public TextSpan(int start, int length)
    {
        Start = start;
        Length = length;
    }

    public int Start { get; }

    public int Length { get; }

    public int End => Start + Length;

    public bool Equals(TextSpan other) => Start == other.Start && Length == other.Length;

    public override bool Equals(object? obj) => obj is TextSpan other && Equals(other);

    public override int GetHashCode()
    {
        unchecked
        {
            return (Start * 397) ^ Length;
        }
    }

    public override string ToString() => $"[{Start}..{End})";

    public static bool operator ==(TextSpan left, TextSpan right) => left.Equals(right);

    public static bool operator !=(TextSpan left, TextSpan right) => !left.Equals(right);
}
