// Mirrors: Rust bodycache.rs:81 touch, :98 put (evict the least recently used while over a bound, never the entry just added), :123 remove.
// Rust kept the order in a VecDeque (O(n) touch); a linked list plus a dictionary gives the same order in O(1). Not thread-safe: BodyCache
// holds its lock around every call.

using System.Diagnostics.CodeAnalysis;

namespace Barbican.Core.Mail.Cache;

/// <summary>Values by key in least-recently-used order, with a byte total.</summary>
internal sealed class LruMap<T>
    where T : class
{
    private readonly Dictionary<CacheKey, LinkedListNode<Entry>> _map = [];
    private readonly LinkedList<Entry> _order = new();

    public int Count => _map.Count;

    public long Bytes { get; private set; }

    public IEnumerable<CacheKey> Keys => _map.Keys;

    public bool Contains(CacheKey key) => _map.ContainsKey(key);

    /// <summary>The value, counted as a use.</summary>
    public bool TryGet(CacheKey key, [NotNullWhen(true)] out T? value)
    {
        if (!_map.TryGetValue(key, out var node))
        {
            value = null;
            return false;
        }

        _order.Remove(node);
        _order.AddLast(node);
        value = node.Value.Value;
        return true;
    }

    /// <summary>The value without counting a use.</summary>
    public T? Peek(CacheKey key) => _map.TryGetValue(key, out var node) ? node.Value.Value : null;

    /// <summary>Adds or replaces, then evicts the oldest entries while over a bound (never the one just added).</summary>
    public void Put(CacheKey key, T value, long weight, int maxEntries, long maxBytes)
    {
        if (_map.Remove(key, out var old))
        {
            Bytes -= old.Value.Weight;
            _order.Remove(old);
        }

        var node = _order.AddLast(new Entry(key, value, weight));
        _map[key] = node;
        Bytes += weight;
        while ((_map.Count > maxEntries || Bytes > maxBytes) && _order.Count > 1)
        {
            var oldest = _order.First!;
            _order.RemoveFirst();
            _map.Remove(oldest.Value.Key);
            Bytes -= oldest.Value.Weight;
        }
    }

    /// <summary>Replaces a value in place (same weight, same recency), when present.</summary>
    public void Update(CacheKey key, Func<T, T> change)
    {
        if (_map.TryGetValue(key, out var node))
        {
            node.Value = node.Value with { Value = change(node.Value.Value) };
        }
    }

    public void Remove(CacheKey key)
    {
        if (_map.Remove(key, out var node))
        {
            Bytes -= node.Value.Weight;
            _order.Remove(node);
        }
    }

    public void Clear()
    {
        _map.Clear();
        _order.Clear();
        Bytes = 0;
    }

    private readonly record struct Entry(CacheKey Key, T Value, long Weight);
}
