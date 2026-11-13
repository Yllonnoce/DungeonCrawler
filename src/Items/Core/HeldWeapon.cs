using DungeonCrawler.Combat;

namespace DungeonCrawler.Items;

/// <summary>Where a held weapon's model comes from: the Quaternius hand models, the KayKit item pack, or a mesh built in code.</summary>
public enum HeldSource { Quaternius, KayKit, Built }

/// <summary>One weapon model for the first-person view: its source and its name within that source.</summary>
public readonly record struct HeldModel(HeldSource Source, string Name);

/// <summary>
/// Which model the first-person view draws for the weapon in the lead's hands. The Quaternius models that were
/// already right stay (sword, dagger, staff, bow); the kinds that used to borrow them get the same model the item
/// shows lying on the floor, so the axe you picked up is the axe you hold.
/// </summary>
public static class HeldWeapon
{
    public static HeldModel? For(WeaponKind kind, bool twoHanded) => kind switch
    {
        WeaponKind.Sword => twoHanded ? new(HeldSource.KayKit, "sword_2handed") : new(HeldSource.Quaternius, "Warrior_Sword"),
        WeaponKind.Axe => new(HeldSource.KayKit, twoHanded ? "axe_2handed" : "axe_1handed"),
        WeaponKind.Mace => new(HeldSource.Built, "mace"),
        WeaponKind.Dagger => new(HeldSource.Quaternius, "Rogue_Dagger"),
        WeaponKind.Polearm => new(HeldSource.Built, "spear"),
        WeaponKind.Staff => new(HeldSource.Quaternius, "Wizard_Staff"),
        WeaponKind.Bow => new(HeldSource.Quaternius, "Ranger_Bow"),
        _ => null,
    };
}
