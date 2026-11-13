using DungeonCrawler.Combat;

namespace DungeonCrawler.Items;

/// <summary>
/// How a weapon is held and swung, finer than its <see cref="WeaponKind"/>: a greatsword is a Sword but not swung like
/// one, a wand is a Staff in the rules but a flick in the hand. Picks the first-person model and the move set.
/// </summary>
public enum WeaponStyle { Fists, Dagger, Sword, Sabre, Greatsword, Axe, BattleAxe, Mace, WarHammer, Spear, Glaive, Staff, Wand, Bow, Crossbow }

/// <summary>How much a weapon's swings carry: light ones snap, heavy ones wind up and land with a jolt.</summary>
public enum WeaponWeight { Light, Medium, Heavy }

public static class WeaponStyles
{
    /// <summary>The style of a weapon from its kind, its hands and its item tags ("sabre", "glaive", "wand", "crossbow").</summary>
    public static WeaponStyle Of(WeaponKind kind, bool twoHanded, IReadOnlyCollection<string>? tags = null)
    {
        bool Has(string tag) => tags != null && tags.Contains(tag, StringComparer.OrdinalIgnoreCase);
        return kind switch
        {
            WeaponKind.Dagger => WeaponStyle.Dagger,
            WeaponKind.Sword => twoHanded ? WeaponStyle.Greatsword : Has("sabre") ? WeaponStyle.Sabre : WeaponStyle.Sword,
            WeaponKind.Axe => twoHanded ? WeaponStyle.BattleAxe : WeaponStyle.Axe,
            WeaponKind.Mace => twoHanded || Has("hammer") ? WeaponStyle.WarHammer : WeaponStyle.Mace,
            WeaponKind.Polearm => Has("glaive") ? WeaponStyle.Glaive : WeaponStyle.Spear,
            WeaponKind.Staff => Has("wand") ? WeaponStyle.Wand : WeaponStyle.Staff,
            WeaponKind.Bow => Has("crossbow") ? WeaponStyle.Crossbow : WeaponStyle.Bow,
            _ => WeaponStyle.Fists,
        };
    }

    /// <summary>The style of the weapon an item is, or Fists for none.</summary>
    public static WeaponStyle Of(ItemInstance? item)
        => item?.BaseWeapon is { } w ? Of(w.Kind, item.TwoHanded, item.Tags) : WeaponStyle.Fists;

    public static WeaponWeight Weight(WeaponStyle style) => style switch
    {
        WeaponStyle.Dagger or WeaponStyle.Sabre or WeaponStyle.Wand or WeaponStyle.Fists => WeaponWeight.Light,
        WeaponStyle.Greatsword or WeaponStyle.BattleAxe or WeaponStyle.WarHammer or WeaponStyle.Glaive => WeaponWeight.Heavy,
        _ => WeaponWeight.Medium,
    };

    public static bool IsRanged(WeaponStyle style) => style is WeaponStyle.Bow or WeaponStyle.Crossbow;
}
