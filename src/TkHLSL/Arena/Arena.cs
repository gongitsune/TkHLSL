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

    /// <summary>
    /// Enumerates every item paired with the <see cref="Handle{T}"/> that <see cref="Add"/> returned
    /// for it (mirrors naga's <c>Arena::iter()</c>; see docs/IMPLEMENTATION_PLAN.md §2.1) — the only
    /// way to recover a <see cref="Handle{T}"/> from outside this assembly, since its constructor is
    /// internal.
    /// </summary>
    public IEnumerable<(Handle<T> Handle, T Value)> WithHandles()
    {
        for (var i = 0; i < _items.Count; i++) yield return (new Handle<T>(i), _items[i]);
    }
}
