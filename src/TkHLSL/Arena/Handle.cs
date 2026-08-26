namespace TkHLSL.Arena;

/// <summary>
/// A lightweight, type-safe index into an <see cref="Arena{T}"/> or <see cref="UniqueArena{T}"/>.
/// </summary>
public readonly struct Handle<T> : IEquatable<Handle<T>>
{
    internal Handle(int index)
    {
        Index = index;
    }

    /// <summary>
    /// The zero-based index into the owning arena's backing storage.
    /// </summary>
    public int Index { get; }

    public bool Equals(Handle<T> other) => Index == other.Index;

    public override bool Equals(object? obj) => obj is Handle<T> other && Equals(other);

    public override int GetHashCode() => Index;

    public override string ToString() => $"Handle<{typeof(T).Name}>({Index})";

    public static bool operator ==(Handle<T> left, Handle<T> right) => left.Equals(right);

    public static bool operator !=(Handle<T> left, Handle<T> right) => !left.Equals(right);
}
