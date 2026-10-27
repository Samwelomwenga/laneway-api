using System.Collections;

namespace Laneway.Api.Tests;

// The in-memory exporters add from whichever thread ends a span or emits a log.
internal sealed class Shipped<T> : ICollection<T>
{
    private readonly List<T> _items = [];

    public int Count
    {
        get
        {
            lock (_items)
            {
                return _items.Count;
            }
        }
    }

    public bool IsReadOnly => false;

    public void Add(T item)
    {
        lock (_items)
        {
            if (!_items.Contains(item))
            {
                _items.Add(item);
            }
        }
    }

    public void Clear()
    {
        lock (_items)
        {
            _items.Clear();
        }
    }

    public bool Contains(T item)
    {
        lock (_items)
        {
            return _items.Contains(item);
        }
    }

    public void CopyTo(T[] array, int arrayIndex)
    {
        lock (_items)
        {
            _items.CopyTo(array, arrayIndex);
        }
    }

    public bool Remove(T item)
    {
        lock (_items)
        {
            return _items.Remove(item);
        }
    }

    public IReadOnlyList<T> Snapshot()
    {
        lock (_items)
        {
            return [.. _items];
        }
    }

    public IEnumerator<T> GetEnumerator() => Snapshot().GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
