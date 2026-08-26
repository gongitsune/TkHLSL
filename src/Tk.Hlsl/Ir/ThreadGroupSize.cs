namespace Tk.Hlsl.Ir;

/// <summary>
///     The <c>[numthreads(x, y, z)]</c> dimensions of a compute kernel's thread group.
/// </summary>
public readonly struct ThreadGroupSize(int x, int y, int z) : IEquatable<ThreadGroupSize>
{
    public int X { get; } = x;

    public int Y { get; } = y;

    public int Z { get; } = z;

    public bool Equals(ThreadGroupSize other)
    {
        return X == other.X && Y == other.Y && Z == other.Z;
    }

    public override bool Equals(object? obj)
    {
        return obj is ThreadGroupSize other && Equals(other);
    }

    public override int GetHashCode()
    {
        unchecked
        {
            var hash = X;
            hash = (hash * 397) ^ Y;
            hash = (hash * 397) ^ Z;
            return hash;
        }
    }

    public override string ToString()
    {
        return $"[numthreads({X}, {Y}, {Z})]";
    }

    public static bool operator ==(ThreadGroupSize left, ThreadGroupSize right)
    {
        return left.Equals(right);
    }

    public static bool operator !=(ThreadGroupSize left, ThreadGroupSize right)
    {
        return !left.Equals(right);
    }
}