namespace DungeonCrawler.Items;

/// <summary>What kind of thing an item is; decides which equipment slots accept it.</summary>
public enum SlotKind { Weapon, Offhand, Head, Body, Hands, Feet, Ring, Amulet, Consumable, Ammo, Junk }

/// <summary>A character's paper-doll slots.</summary>
public enum EquipSlot { Weapon, Offhand, Head, Body, Hands, Feet, Ring1, Ring2, Amulet }

public enum Rarity { Common, Magic, Rare, Legendary }
public enum ArmorClass { None, Cloth, Leather, Plate }
public enum OffhandKind { None, Shield, Quiver, Focus }

public static class SlotRules
{
    public static readonly EquipSlot[] AllSlots = Enum.GetValues<EquipSlot>();

    public static SlotKind KindOf(EquipSlot slot) => slot switch
    {
        EquipSlot.Weapon => SlotKind.Weapon,
        EquipSlot.Offhand => SlotKind.Offhand,
        EquipSlot.Head => SlotKind.Head,
        EquipSlot.Body => SlotKind.Body,
        EquipSlot.Hands => SlotKind.Hands,
        EquipSlot.Feet => SlotKind.Feet,
        EquipSlot.Ring1 or EquipSlot.Ring2 => SlotKind.Ring,
        _ => SlotKind.Amulet,
    };

    public static IEnumerable<EquipSlot> SlotsFor(SlotKind kind) => AllSlots.Where(s => KindOf(s) == kind);

    public static bool IsEquippable(SlotKind kind) => kind is not (SlotKind.Consumable or SlotKind.Ammo or SlotKind.Junk);
    public static bool IsArmor(SlotKind kind) => kind is SlotKind.Head or SlotKind.Body or SlotKind.Hands or SlotKind.Feet;

    public static string Label(EquipSlot slot) => slot switch
    {
        EquipSlot.Ring1 => "Ring", EquipSlot.Ring2 => "Ring", _ => slot.ToString(),
    };
}

public static class RarityInfo
{
    public static string ColorHex(Rarity r) => r switch
    {
        Rarity.Magic => "#4f8cff",
        Rarity.Rare => "#ffd23f",
        Rarity.Legendary => "#ff8a1f",
        _ => "#d0d0d0",
    };
}
