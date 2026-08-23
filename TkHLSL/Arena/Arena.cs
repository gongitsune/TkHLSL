using System.Collections;

namespace TkHLSL.Arena;

/// <summary>
/// An append-only collection of <typeparamref name="T"/> values, indexed by <see cref="Handle{T}"/>.
/// </summary>
public sealed class Arena<T> : IEnumerable<T>
{
    private readonly List<T> _items = [];

    public int Count => _items.Count;

    public Handle<T> Add(T item)
    {
        _items.Add(item);
        return new Handle<T>(_items.Count - 1);
    }

    public T this[Handle<T> handle] => _items[handle.Index];

    public IEnumerator<T> GetEnumerator() => _items.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
