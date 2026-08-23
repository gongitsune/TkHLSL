using System.Collections;

namespace TkHLSL.Arena;

/// <summary>
/// Like <see cref="Arena{T}"/>, but deduplicates values by equality: inserting a value equal to
/// one already present returns the existing <see cref="Handle{T}"/> instead of adding a new entry.
/// </summary>
public sealed class UniqueArena<T> : IEnumerable<T>
    where T : notnull
{
    private readonly List<T> _items = [];
    private readonly Dictionary<T, Handle<T>> _handlesByValue;

    public UniqueArena(IEqualityComparer<T>? comparer = null)
    {
        _handlesByValue = new Dictionary<T, Handle<T>>(comparer ?? EqualityComparer<T>.Default);
    }

    public int Count => _items.Count;

    public Handle<T> Insert(T item)
    {
        if (_handlesByValue.TryGetValue(item, out var existing))
        {
            return existing;
        }

        _items.Add(item);
        var handle = new Handle<T>(_items.Count - 1);
        _handlesByValue.Add(item, handle);
        return handle;
    }

    public T this[Handle<T> handle] => _items[handle.Index];

    public IEnumerator<T> GetEnumerator() => _items.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
