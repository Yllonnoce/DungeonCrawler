namespace DungeonCrawler.Adventure.Voice;

/// <summary>
/// A few values kept by key, the least recently used dropped once it is full: the voice recordings loaded to read a line's
/// length and kept for its playing a moment later, so a line is read from disk once. Engine-free.
/// </summary>
public sealed class RecentCache<TKey, TValue> where TKey : notnull
{
    private readonly int _capacity;
    private readonly Dictionary<TKey, LinkedListNode<(TKey Key, TValue Value)>> _nodes = new();
    private readonly LinkedList<(TKey Key, TValue Value)> _order = new(); // most recently used first

    public RecentCache(int capacity)
    {
        if (capacity < 1) throw new ArgumentOutOfRangeException(nameof(capacity), "a cache holds one value at least");
        _capacity = capacity;
    }

    public int Count => _nodes.Count;

    /// <summary>The value kept for <paramref name="key"/>, which becomes the most recently used.</summary>
    public bool TryGet(TKey key, out TValue value)
    {
        if (!_nodes.TryGetValue(key, out var node)) { value = default!; return false; }
        _order.Remove(node);
        _order.AddFirst(node);
        value = node.Value.Value;
        return true;
    }

    /// <summary>Keep <paramref name="value"/> for <paramref name="key"/> (replacing an older one); the least recently used goes when full.</summary>
    public void Put(TKey key, TValue value)
    {
        if (_nodes.TryGetValue(key, out var old)) { _order.Remove(old); _nodes.Remove(key); }
        else if (_nodes.Count >= _capacity && _order.Last is { } last) { _order.RemoveLast(); _nodes.Remove(last.Value.Key); }
        _nodes[key] = _order.AddFirst((key, value));
    }

    public void Clear()
    {
        _nodes.Clear();
        _order.Clear();
    }
}
