namespace DungeonCrawler.Items;

/// <summary>The party's shared bag: a fixed grid of cells, one item (or stack) per cell.</summary>
public sealed class Inventory
{
    public const int DefaultCapacity = 40;

    private readonly ItemInstance?[] _slots;

    public Inventory(int capacity = DefaultCapacity) => _slots = new ItemInstance?[capacity];

    public int Capacity => _slots.Length;
    public ItemInstance? this[int index] => index >= 0 && index < _slots.Length ? _slots[index] : null;
    public IEnumerable<ItemInstance> Items => _slots.Where(s => s != null)!;
    public int FreeSlots => _slots.Count(s => s == null);
    public int IndexOf(ItemInstance item) => Array.IndexOf(_slots, item);

    /// <summary>Merge into existing stacks first, then take the first empty cell. False if nothing fits.</summary>
    public bool TryAdd(ItemInstance item)
    {
        if (item.Stackable)
        {
            foreach (var s in _slots)
            {
                if (s == null || !s.Stackable || s.DefId != item.DefId || s.StackCount >= s.MaxStack) continue;
                var move = Math.Min(item.StackCount, s.MaxStack - s.StackCount);
                s.StackCount += move;
                item.StackCount -= move;
                if (item.StackCount <= 0) return true;
            }
        }
        var empty = Array.IndexOf(_slots, null);
        if (empty < 0) return false;
        _slots[empty] = item;
        return true;
    }

    public bool Remove(ItemInstance item)
    {
        var i = IndexOf(item);
        if (i < 0) return false;
        _slots[i] = null;
        return true;
    }

    public ItemInstance? RemoveAt(int index)
    {
        var item = this[index];
        if (item != null) _slots[index] = null;
        return item;
    }

    public void Set(int index, ItemInstance? item)
    {
        if (index >= 0 && index < _slots.Length) _slots[index] = item;
    }

    /// <summary>Swap two cells, merging when both hold the same stackable.</summary>
    public void Swap(int a, int b)
    {
        if (a == b || this[a] == null && this[b] == null) return;
        var ia = _slots[a];
        var ib = _slots[b];
        if (ia != null && ib != null && ia.Stackable && ib.Stackable && ia.DefId == ib.DefId)
        {
            var move = Math.Min(ia.StackCount, ib.MaxStack - ib.StackCount);
            ib.StackCount += move;
            ia.StackCount -= move;
            if (ia.StackCount <= 0) _slots[a] = null;
            return;
        }
        (_slots[a], _slots[b]) = (ib, ia);
    }

    public int Count(string defId) => _slots.Where(s => s != null && s.DefId == defId).Sum(s => s!.StackCount);

    /// <summary>Remove <paramref name="amount"/> units of a stackable. Returns false (and takes nothing) if short.</summary>
    public bool Consume(string defId, int amount)
    {
        if (amount <= 0) return true;
        if (Count(defId) < amount) return false;
        for (var i = _slots.Length - 1; i >= 0 && amount > 0; i--)
        {
            var it = _slots[i];
            if (it == null || it.DefId != defId) continue;
            var take = Math.Min(it.StackCount, amount);
            it.StackCount -= take;
            amount -= take;
            if (it.StackCount <= 0) _slots[i] = null;
        }
        return true;
    }

    /// <summary>Take one unit off a stack (or the whole item if not stackable).</summary>
    public void TakeOne(ItemInstance item)
    {
        if (item.Stackable && item.StackCount > 1) item.StackCount--;
        else Remove(item);
    }
}
