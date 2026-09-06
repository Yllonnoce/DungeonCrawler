using DungeonCrawler.Items;

namespace DungeonCrawler.UI.Inventory;

/// <summary>Addresses one cell: a bag index, a member's equipment slot, or the drop zone. Serialises to a Dictionary for drag data.</summary>
public readonly record struct SlotRef(int Member, EquipSlot Slot, int BagIndex, bool IsDrop = false, bool IsDestroy = false)
{
    public bool IsBag => Member < 0 && !IsDrop && !IsDestroy;
    public bool IsEquip => Member >= 0;
    /// <summary>A target cell that holds nothing: the drop zone or the destroy zone.</summary>
    public bool IsZone => IsDrop || IsDestroy;

    public static SlotRef Bag(int index) => new(-1, EquipSlot.Weapon, index);
    public static SlotRef Equip(int member, EquipSlot slot) => new(member, slot, -1);
    public static SlotRef Drop => new(-1, EquipSlot.Weapon, -1, true);
    public static SlotRef Destroy => new(-1, EquipSlot.Weapon, -1, false, true);

    public DungeonCrawler.Party.BagSlot ToSlot() => new(Member, Slot, BagIndex, IsDrop, IsDestroy);

    public Godot.Collections.Dictionary ToDict() => new() { ["m"] = Member, ["s"] = (int)Slot, ["b"] = BagIndex, ["d"] = IsDrop, ["x"] = IsDestroy };

    public static SlotRef FromDict(Godot.Collections.Dictionary d)
        => new((int)d["m"], (EquipSlot)(int)d["s"], (int)d["b"], (bool)d["d"], d.ContainsKey("x") && (bool)d["x"]);
}
