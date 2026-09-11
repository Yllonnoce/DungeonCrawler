using DungeonCrawler.Combat;

namespace DungeonCrawler.Dungeon;

public enum TrapKind { Spikes, Fire, Gas }

/// <summary>Numbers for floor traps, detection, disarming and lockpicking. Engine-free.</summary>
public static class TrapRules
{
    /// <summary>How far (in cells) hidden traps are revealed, from the party's best Stealth.</summary>
    /// <summary>How many cells away a character with this Perception notices a hidden trap or a loose seam: a warrior only a step short of it, a ranger from across the room.</summary>
    public static float DetectRadiusCells(float perception) => 0.7f + Math.Max(0, perception) / 16f;

    public static float DisarmChance(float dexterity) => Math.Clamp(0.35f + (dexterity - 10) * 0.06f, 0.1f, 0.95f);
    public static float LockpickChance(float dexterity) => Math.Clamp(0.25f + (dexterity - 10) * 0.06f, 0.05f, 0.95f);

    public static bool HitsWholeParty(TrapKind kind) => kind != TrapKind.Spikes;

    /// <summary>The floor's depth scaling, times the dungeon's lethality when the party is the victim.</summary>
    private static float Scale(int floor, bool lethal) => TierRules.DamageScale(floor) * (lethal ? TierRules.Lethality : 1f);

    /// <param name="lethal">The party is the victim (the default): the dungeon's <see cref="TierRules.Lethality"/> applies. False for the party's own traps springing on enemies.</param>
    public static AttackProfile Attack(TrapKind kind, int floor, bool lethal = true)
    {
        var s = Scale(floor, lethal);
        return kind switch
        {
            TrapKind.Spikes => new AttackProfile { MinDamage = 10 * s, MaxDamage = 16 * s, Type = DamageType.Physical, Kind = AttackKind.Melee, OnHit = new[] { new StatusApplication(StatusKind.Bleed, 5, 1 * s, 0.6f) } },
            TrapKind.Fire => new AttackProfile { MinDamage = 6 * s, MaxDamage = 10 * s, Type = DamageType.Fire, Kind = AttackKind.Spell, OnHit = new[] { new StatusApplication(StatusKind.Burn, 4, 3 * s) } },
            _ => new AttackProfile { MinDamage = 3 * s, MaxDamage = 5 * s, Type = DamageType.Poison, Kind = AttackKind.Spell, OnHit = new[] { new StatusApplication(StatusKind.Poison, 8, 2 * s) } },
        };
    }

    /// <summary>One tick of standing in a burning floor patch (Burning floors): a lick of fire damage and the burn that follows.</summary>
    public static AttackProfile FloorFire(int floor)
    {
        var s = Scale(floor, lethal: true);
        return Attack(TrapKind.Fire, floor) with { MinDamage = 2 * s, MaxDamage = 4 * s, OnHit = new[] { new StatusApplication(StatusKind.Burn, 3, 2 * s) } };
    }

    /// <summary>Traps the party places always trigger on enemies and deal the trap's base numbers: the dungeon's lethality is not theirs.</summary>
    public static AttackProfile PlacedAttack(TrapKind kind, int floor) => Attack(kind, floor, lethal: false);
}
