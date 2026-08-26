using System.Collections;

namespace TkHLSL.SourceGeneration;

/// <summary>
///     A thin wrapper over an array that compares by element (<see cref="IEquatable{T}" />/
///     <see cref="object.Equals(object?)" />) rather than by reference, so a
///     record holding one participates correctly in <c>IIncrementalGenerator</c> caching — a plain
///     array field would make every pipeline value compare unequal to its predecessor even when its
///     contents didn't change, defeating incremental caching entirely.
/// </summary>
internal readonly struct EquatableArray<T>(T[] items) : IEquatable<EquatableArray<T>>, IReadOnlyList<T>
    where T : IEquatable<T>
{
    private readonly T[] _items = items;

    public static EquatableArray<T> Empty { get; } = new([]);

    public int Count => _items.Length;

    public T this[int index] => _items[index];

    public bool Equals(EquatableArray<T> other)
    {
        if (_items == other._items) return true;
        if (_items is null || other._items is null) return _items == other._items;
        if (_items.Length != other._items.Length) return false;

        return !_items.Where((t, i) => !t.Equals(other._items[i])).Any();
    }

    public override bool Equals(object? obj)
    {
        return obj is EquatableArray<T> other && Equals(other);
    }

    public override int GetHashCode()
    {
        if (_items is null) return 0;

        unchecked
        {
            return _items.Aggregate(17, (current, item) => current * 31 + item.GetHashCode());
        }
    }

    public IEnumerator<T> GetEnumerator()
    {
        return ((IEnumerable<T>)(_items ?? [])).GetEnumerator();
    }

    IEnumerator IEnumerable.GetEnumerator()
    {
        return GetEnumerator();
    }
}