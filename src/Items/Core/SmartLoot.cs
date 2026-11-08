using DungeonCrawler.Characters;

namespace DungeonCrawler.Items;

/// <summary>Who can use a base: six rolled bases in ten are steered toward what a living member can wield or wear.</summary>
public static class SmartLoot
{
    /// <summary>A weapon the class can wield, an offhand it may hold, armour of a class it may wear; rings, amulets and non-equippables fit everyone.</summary>
    public static bool Usable(ItemBase b, ClassProfile cls)
    {
        if (!b.IsEquippable) return true;
        if (b.Weapon != null && !cls.CanWield(b.Weapon.Kind)) return false;
        if (b.Kind == SlotKind.Offhand && !cls.AllowedOffhands.Contains(b.Offhand)) return false;
        if (b.ArmorClass != ArmorClass.None && !cls.AllowedArmor.Contains(b.ArmorClass)) return false;
        return true;
    }

    public static bool UsableByAny(ItemBase b, IEnumerable<ClassProfile> classes) => classes.Any(c => Usable(b, c));
}
