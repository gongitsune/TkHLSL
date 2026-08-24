namespace TkHLSL.Ir;

/// <summary>
///     An explicit <c>register(tN[, spaceM])</c> binding slot.
/// </summary>
public readonly struct ResourceRegister(char slotType, int slotIndex, int? space) : IEquatable<ResourceRegister>
{
    /// <summary>The slot type character: <c>t</c> (SRV), <c>u</c> (UAV), <c>b</c> (CBV), or <c>s</c> (sampler).</summary>
    public char SlotType { get; } = slotType;

    public int SlotIndex { get; } = slotIndex;

    /// <summary>The explicit <c>spaceN</c> qualifier, or <see langword="null" /> if omitted (implicit <c>space0</c>).</summary>
    public int? Space { get; } = space;

    public bool Equals(ResourceRegister other)
    {
        return SlotType == other.SlotType && SlotIndex == other.SlotIndex && Space == other.Space;
    }

    public override bool Equals(object? obj)
    {
        return obj is ResourceRegister other && Equals(other);
    }

    public override int GetHashCode()
    {
        unchecked
        {
            var hash = SlotType.GetHashCode();
            hash = (hash * 397) ^ SlotIndex;
            hash = (hash * 397) ^ (Space ?? -1);
            return hash;
        }
    }

    public override string ToString()
    {
        return Space is { } space
            ? $"register({SlotType}{SlotIndex}, space{space})"
            : $"register({SlotType}{SlotIndex})";
    }

    public static bool operator ==(ResourceRegister left, ResourceRegister right)
    {
        return left.Equals(right);
    }

    public static bool operator !=(ResourceRegister left, ResourceRegister right)
    {
        return !left.Equals(right);
    }
}