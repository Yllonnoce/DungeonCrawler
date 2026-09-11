namespace DungeonCrawler.Dungeon;

/// <summary>Which tier a floor belongs to and how enemies scale with depth. Engine-free.</summary>
public static class TierRules
{
    public const int FloorsPerTier = 5;
    public const int TierCount = 4;
    public const int FinalFloor = FloorsPerTier * TierCount;

    /// <summary>0 = Crypt, 1 = Caves, 2 = Forge, 3 = Abyss. Floors past the final boss stay in the last tier.</summary>
    public static int TierIndex(int floor) => Math.Clamp((floor - 1) / FloorsPerTier, 0, TierCount - 1);
    public static int FloorInTier(int floor) => (floor - 1) % FloorsPerTier + 1;
    public static bool IsBossFloor(int floor) => floor >= FloorsPerTier && floor % FloorsPerTier == 0 && floor <= FinalFloor;
    public static bool IsFinalBossFloor(int floor) => floor == FinalFloor;
    public static bool IsEndless(int floor) => floor > FinalFloor;

    /// <summary>
    /// How much harder the dungeon hits than its base numbers: every enemy attack and every trap, fire or gas the party
    /// suffers is multiplied by this. 1 is the original tuning, 3 the default. Settings own it; it never touches the
    /// party's own traps against enemies, fall damage, or the enemy budgets a floor is planned with.
    /// </summary>
    public static float Lethality { get; set; } = DefaultLethality;
    public const float DefaultLethality = 3f;
    public const float MinLethality = 1f, MaxLethality = 3f;

    /// <summary>An NPC's hit points on a floor: enemies hit them with the dungeon's lethality, so they carry it too and last as long as they always did.</summary>
    public static float NpcHp(int floor) => (40f + 12f * floor) * Lethality;

    private static float Endless(int floor) => IsEndless(floor) ? 0.15f * (floor - FinalFloor) : 0f;
    public static float HpScale(int floor) => 1f + 0.08f * (floor - 1) + Endless(floor);
    public static float DamageScale(int floor) => 1f + 0.05f * (floor - 1) + Endless(floor) * 0.6f;
    public static float XpScale(int floor) => 1f + 0.15f * (floor - 1) + Endless(floor);
    /// <summary>Item level used for loot on a floor; keeps climbing in endless mode.</summary>
    public static int ItemLevel(int floor) => floor;
}
