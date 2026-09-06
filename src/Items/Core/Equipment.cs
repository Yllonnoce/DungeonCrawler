using DungeonCrawler.Characters;

namespace DungeonCrawler.Items;

/// <summary>One character's worn items. Validation lives in <see cref="WhyCannotEquip"/>; callers check it first.</summary>
public sealed class Equipment
{
    private readonly Dictionary<EquipSlot, ItemInstance?> _slots = SlotRules.AllSlots.ToDictionary(s => s, _ => (ItemInstance?)null);

    public ItemInstance? this[EquipSlot slot] => _slots[slot];
    public IEnumerable<(EquipSlot Slot, ItemInstance Item)> Items => _slots.Where(kv => kv.Value != null).Select(kv => (kv.Key, kv.Value!));
    public IEnumerable<StatModifier> Modifiers() => Items.SelectMany(t => t.Item.Modifiers);

    public bool HasTwoHandedWeapon => _slots[EquipSlot.Weapon]?.TwoHanded == true;

    public static string? WhyCannotEquip(CharacterState c, ItemInstance item, EquipSlot slot)
    {
        if (!item.IsEquippable) return $"{item.DisplayName} cannot be equipped";
        if (SlotRules.KindOf(slot) != item.Kind) return $"{item.DisplayName} does not go in the {SlotRules.Label(slot)} slot";
        var cls = c.Class;
        switch (item.Kind)
        {
            case SlotKind.Weapon when item.BaseWeapon != null && !cls.CanWield(item.BaseWeapon.Kind):
                return $"{cls.Name}s cannot wield a {item.BaseWeapon.Kind}";
            case SlotKind.Weapon when item.TwoHanded && c.Equipment[EquipSlot.Offhand] != null:
                return "Two-handed: unequip the offhand first";
            case SlotKind.Offhand when !cls.AllowedOffhands.Contains(item.Offhand):
                return $"{cls.Name}s cannot use a {item.Offhand}";
            case SlotKind.Offhand when c.Equipment.HasTwoHandedWeapon:
                return "Both hands are holding the weapon";
        }
        if (SlotRules.IsArmor(item.Kind) && item.ArmorClass != ArmorClass.None && !cls.AllowedArmor.Contains(item.ArmorClass))
            return $"{cls.Name}s cannot wear {item.ArmorClass.ToString().ToLower()} armor";
        return null;
    }

    /// <summary>Put an item in a slot, returning whatever was there. Does not validate.</summary>
    public ItemInstance? Equip(EquipSlot slot, ItemInstance item)
    {
        var old = _slots[slot];
        _slots[slot] = item;
        return old;
    }

    public ItemInstance? Unequip(EquipSlot slot)
    {
        var old = _slots[slot];
        _slots[slot] = null;
        return old;
    }

    /// <summary>First slot that would accept the item: an empty one if possible, else the first matching slot.</summary>
    public EquipSlot? PreferredSlot(CharacterState c, ItemInstance item)
    {
        var candidates = SlotRules.SlotsFor(item.Kind).Where(s => WhyCannotEquip(c, item, s) == null).ToList();
        if (candidates.Count == 0) return null;
        return candidates.FirstOrDefault(s => _slots[s] == null, candidates[0]);
    }
}
