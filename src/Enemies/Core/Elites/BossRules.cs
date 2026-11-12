namespace DungeonCrawler.Enemies;

/// <summary>What a boss does to its arena at phase two (Depths spec, section 2). BossArena spawns it.</summary>
public enum ArenaChange { None, Darken, Webs, Embers, GravityWell }

/// <summary>Boss phases, engine-free: the roar at half health, phase two's speed, the second signature alternating with the first, the Heralds' names.</summary>
public static class BossRules
{
    public const float RoarSeconds = 1.2f, PhaseTwoSpeed = 1.2f, HalfFraction = 0.5f;
    public const int BruteSummonFromFloor = 8, SlowPatchCount = 3, EmberPatchCount = 2;
    public const float EmberSpeed = 0.5f, EmberOrbit = 3f, GravityWellRadius = 6f, GravityWellTick = 0.5f, GravityWellSlowSeconds = 1f, SlowPatchRadius = 1.2f, SlowPatchSeconds = 2f;

    /// <summary>Horde fodder a boss calls becomes the tier's brute from floor 8; anything else is called as named.</summary>
    public static string SummonFor(string summonId, int floor)
        => floor >= BruteSummonFromFloor && summonId.StartsWith("enemy.horde_") && !summonId.EndsWith("_brute") ? summonId + "_brute" : summonId;

    /// <summary>Phase one (or no second kit): the first. Phase two: the two alternate on the shared cooldown, the second first.</summary>
    public static Signature NextSignature(Signature first, Signature second, int phase, ref bool useSecond)
    {
        if (phase < 2 || second == Signature.None) return first;
        useSecond = !useSecond;
        return useSecond ? second : first;
    }

    private static readonly Signature[] Kits = { Signature.Slam, Signature.Volley, Signature.Charge, Signature.Summon };

    /// <summary>An endless Herald's second kit: any of the four but the one it already has.</summary>
    public static Signature RandomSecond(Signature first, Random rng)
    {
        var pool = Kits.Where(k => k != first).ToArray();
        return pool[rng.Next(pool.Length)];
    }

    private static readonly string[] Ordinals = { "First", "Second", "Third", "Fourth", "Fifth", "Sixth", "Seventh", "Eighth", "Ninth", "Tenth", "Eleventh", "Twelfth" };

    /// <summary>"the First Herald" on floor 25, "the Second Herald" on 30 … "the 14th Herald" past the twelfth.</summary>
    public static string HeraldName(int floor)
    {
        var n = Math.Max(1, (floor - Dungeon.TierRules.FinalFloor) / Dungeon.TierRules.FloorsPerTier);
        if (n <= Ordinals.Length) return $"the {Ordinals[n - 1]} Herald";
        var suffix = n % 100 is >= 11 and <= 13 ? "th" : (n % 10) switch { 1 => "st", 2 => "nd", 3 => "rd", _ => "th" };
        return $"the {n}{suffix} Herald";
    }

    public static bool PhaseTwoAt(float hp, float maxHp) => hp > 0 && hp <= maxHp * HalfFraction;

    /// <summary>
    /// The name a boss is shown by: the plan's boss name when it wears the campaign villain's own body (he is there in person,
    /// not the stock creature), else the creature's. Only the name: the creature's stats stay.
    /// </summary>
    public static string ShownName(string creatureName, string? planBossName, bool villainBodied)
        => villainBodied && !string.IsNullOrWhiteSpace(planBossName) ? planBossName.Trim() : creatureName;
}
