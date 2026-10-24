namespace DungeonCrawler.Dungeon.Generation;

/// <summary>Generator parameters for one floor. Built from a TierDef on the Godot side; plain data here.</summary>
public sealed record FloorSpec
{
    /// <summary>The floor's overall shape; see <see cref="LayoutKinds"/>.</summary>
    public LayoutKind Layout { get; init; } = LayoutKind.Standard;
    /// <summary>What the floor asks of the party beyond the stairs; see <see cref="FloorObjectives"/>. None leaves generation exactly as it was.</summary>
    public FloorObjectiveKind Objective { get; init; } = FloorObjectiveKind.None;
    public int Width { get; init; } = 48;
    public int Height { get; init; } = 48;
    public int MinRooms { get; init; } = 8;
    public int MaxRooms { get; init; } = 15;
    public int MinRoomSize { get; init; } = 4;
    public int MaxRoomSize { get; init; } = 10;
    /// <summary>Wall thickness kept between rooms so corridors have somewhere to run.</summary>
    public int RoomMargin { get; init; } = 2;
    /// <summary>Chance, per non-MST edge, of adding a loop corridor.</summary>
    public double ExtraEdgeChance { get; init; } = 0.15;
    public int EnemyCount { get; init; } = 6;
    public int ChestCount { get; init; } = 2;
    public int BreakableCount { get; init; } = 6;
    public int TrapCount { get; init; } = 4;
    /// <summary>Dead-end rooms turned into sealed treasure rooms (needs suitable rooms; may place fewer).</summary>
    public int VaultCount { get; init; } = 1;
    /// <summary>Ordinary chests that get a lock, each with a key placed elsewhere.</summary>
    public int LockedChestCount { get; init; } = 1;

    /// <summary>Chance a room sits on the upper level (4 m higher). 0 disables verticality.</summary>
    public float HighRoomChance { get; init; } = 0.4f;
    public int LedgeCount { get; init; } = 1;
    public int PitCount { get; init; } = 1;
    public int SecretCount { get; init; } = 1;
    /// <summary>Gatehouses, bandit camps or ruined colonnades built inside big rooms (needs rooms at least 6x6; may place fewer).</summary>
    public int StructureCount { get; init; } = 2;
    /// <summary>Rubble mounds heaped on room floors (bodies path around them).</summary>
    public int RubbleCount { get; init; } = 2;
    /// <summary>Rooms given breaches in their walls, out of which a horde can pour.</summary>
    public int NestCount { get; init; } = 6;
    /// <summary>Ponds or streams laid in rooms at least 5x5 (may place fewer).</summary>
    public int WaterCount { get; init; } = 1;
    /// <summary>Fires burning on room floors (a Burning floor sets this; nothing else has any).</summary>
    public int FireCount { get; init; }
    /// <summary>Tier-final floor: a large arena room is placed and the down stairs live inside it.</summary>
    public bool BossFloor { get; init; }
    public int BossArenaSize { get; init; } = 14;

    /// <summary>The floor number (1-based); the lethal hazards start on floor 3 and scale by tier.</summary>
    public int Floor { get; init; } = 1;
    /// <summary>False leaves the floor exactly as the generator made it before the lethal hazards existed.</summary>
    public bool HazardsEnabled { get; init; } = true;
    /// <summary>Testing: force one hazard of this kind as close to the up stairs as the rules allow, on any floor (--hazard=press|spikes).</summary>
    public HazardKind? ForceHazard { get; init; }

    public static FloorSpec Default { get; } = new();
}
