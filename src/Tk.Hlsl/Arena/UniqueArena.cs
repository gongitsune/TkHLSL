using System.Collections;

namespace Tk.Hlsl.Arena;

/// <summary>
///     Like <see cref="Arena{T}" />, but deduplicates values by equality: inserting a value equal to
///     one already present returns the existing <see cref="Handle{T}" /> instead of adding a new entry.
/// </summary>
public sealed class UniqueArena<T>(IEqualityComparer<T>? comparer = null) : IEnumerable<T>
    where T : notnull
{
    private readonly Dictionary<T, Handle<T>> _handlesByValue = new(comparer ?? EqualityComparer<T>.Default);
    private readonly List<T> _items = [];

    public int Count => _items.Count;

    public T this[Handle<T> handle] => _items[handle.Index];

    public IEnumerator<T> GetEnumerator()
    {
        return _items.GetEnumerator();
    }

    IEnumerator IEnumerable.GetEnumerator()
    {
        return GetEnumerator();
    }

    public Handle<T> Insert(T item)
    {
        if (_handlesByValue.TryGetValue(item, out var existing)) return existing;

        _items.Add(item);
        var handle = new Handle<T>(_items.Count - 1);
        _handlesByValue.Add(item, handle);
        return handle;
    }
}