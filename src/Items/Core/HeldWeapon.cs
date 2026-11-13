namespace DungeonCrawler.Items;

/// <summary>Where a held weapon's model comes from: the Quaternius hand models, the KayKit item pack, or a mesh built in code.</summary>
public enum HeldSource { Quaternius, KayKit, Built }

/// <summary>One weapon model for the first-person view: its source and its name within that source.</summary>
public readonly record struct HeldModel(HeldSource Source, string Name);

/// <summary>
/// Which model the first-person view draws for the weapon in the lead's hands. The Quaternius models that were
/// already right stay (sword, staff, bow); every other style gets the same model the item shows lying on the floor,
/// so the axe you picked up is the axe you hold. The dagger joined them: the Quaternius one is a dark sliver in the hand.
/// </summary>
public static class HeldWeapon
{
    public static HeldModel? For(WeaponStyle style) => style switch
    {
        WeaponStyle.Sword => new(HeldSource.Quaternius, "Warrior_Sword"),
        WeaponStyle.Sabre => new(HeldSource.KayKit, "sword_1handed"),
        WeaponStyle.Greatsword => new(HeldSource.KayKit, "sword_2handed"),
        WeaponStyle.Axe => new(HeldSource.KayKit, "axe_1handed"),
        WeaponStyle.BattleAxe => new(HeldSource.KayKit, "axe_2handed"),
        WeaponStyle.Mace => new(HeldSource.Built, "mace"),
        WeaponStyle.WarHammer => new(HeldSource.Built, "war_hammer"),
        WeaponStyle.Dagger => new(HeldSource.KayKit, "dagger"),
        WeaponStyle.Spear => new(HeldSource.Built, "spear"),
        WeaponStyle.Glaive => new(HeldSource.Built, "glaive"),
        WeaponStyle.Staff => new(HeldSource.Quaternius, "Wizard_Staff"),
        WeaponStyle.Wand => new(HeldSource.KayKit, "wand"),
        WeaponStyle.Bow => new(HeldSource.Quaternius, "Ranger_Bow"),
        WeaponStyle.Crossbow => new(HeldSource.KayKit, "crossbow_2handed"),
        _ => null,
    };
}
