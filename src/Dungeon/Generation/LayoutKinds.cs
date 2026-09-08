namespace DungeonCrawler.Dungeon.Generation;

/// <summary>The overall shape of a floor. Standard is the room-and-corridor floor every tier started with.</summary>
public enum LayoutKind : byte
{
    Standard = 0,
    /// <summary>A maze of tight one-cell passages with small chambers set into it.</summary>
    Labyrinth = 1,
    /// <summary>A great central rotunda (the arena on a boss floor) with rooms around it on a ring corridor and spokes in.</summary>
    Ring = 2,
    /// <summary>Long narrow galleries, timbered, with dead-end drifts dug into the rock, shafts and heaps of spoil.</summary>
    Mine = 3,
    /// <summary>Most rooms lie under knee-deep water; the halls between drip.</summary>
    Flooded = 4,
    /// <summary>Fires burn across the room floors; standing in one burns.</summary>
    Burning = 5,
}

/// <summary>
/// Which shape each floor of a descent takes, and the generator knobs each shape needs. Engine-free and deterministic:
/// the same seed always deals the same twenty shapes, with every kind used at least once and no two floors in a row alike.
/// </summary>
public static class LayoutKinds
{
    public static readonly LayoutKind[] All = { LayoutKind.Standard, LayoutKind.Labyrinth, LayoutKind.Ring, LayoutKind.Mine, LayoutKind.Flooded, LayoutKind.Burning };
    /// <summary>Kinds a boss floor may take: the arena must stay a clean room with plain ways in.</summary>
    public static readonly LayoutKind[] BossKinds = { LayoutKind.Standard, LayoutKind.Ring, LayoutKind.Flooded, LayoutKind.Burning };

    public static string Id(LayoutKind kind) => kind.ToString().ToLowerInvariant();

    public static bool TryParse(string? id, out LayoutKind kind)
    {
        kind = LayoutKind.Standard;
        return !string.IsNullOrWhiteSpace(id) && Enum.TryParse(id.Trim(), ignoreCase: true, out kind) && Enum.IsDefined(kind);
    }

    /// <summary>One line per kind, for the planner's prompt and the debug log.</summary>
    public static string Describe(LayoutKind kind) => kind switch
    {
        LayoutKind.Labyrinth => "a maze of tight passages with small chambers set into it; no upper level",
        LayoutKind.Ring => "a great central rotunda (the arena on a boss floor) with rooms around it on a ring corridor and spokes leading in",
        LayoutKind.Mine => "long timbered galleries, dead-end drifts dug into the rock, shafts down to cellars and heaps of spoil",
        LayoutKind.Flooded => "most rooms lie under knee-deep water that slows everyone wading through it",
        LayoutKind.Burning => "fires burn across the room floors and anyone standing in one burns; no water",
        _ => "the ordinary rooms-and-corridors floor",
    };

    // How much each tier likes each shape (Labyrinth, Ring, Mine, Flooded, Burning): crypts wind, caves flood and are mined, forges burn.
    private static readonly float[][] TierWeights =
    {
        new[] { 3f, 2f, 1f, 2f, 0.5f },
        new[] { 1f, 1.5f, 3f, 3f, 0.5f },
        new[] { 1.5f, 2f, 2.5f, 0.5f, 3f },
        new[] { 2.5f, 2.5f, 0.5f, 1.5f, 3f },
    };
    private static readonly LayoutKind[] Special = { LayoutKind.Labyrinth, LayoutKind.Ring, LayoutKind.Mine, LayoutKind.Flooded, LayoutKind.Burning };

    /// <summary>The shape a floor takes when the campaign leaves it to the game.</summary>
    public static LayoutKind Pick(ulong gameSeed, int floor)
    {
        if (floor < 1) return LayoutKind.Standard;
        if (floor <= TierRules.FinalFloor) return Deal(gameSeed)[floor - 1];
        // endless: anything goes, the ordinary floor twice as often, boss floors keep to boss shapes
        var rng = new Random(unchecked((int)Core.Rng.Mix(gameSeed ^ ((ulong)floor * 0xA5A5A5A5UL) ^ 0x1A70UL)));
        var pool = TierRules.IsBossFloor(floor) ? BossKinds : All;
        var weights = pool.Select(k => k == LayoutKind.Standard ? 2f : 1f).ToArray();
        return pool[Items.LootRoller.WeightedIndex(weights, rng)];
    }

    /// <summary>
    /// The twenty shapes of a descent. Each tier gets the ordinary floor plus three different special shapes it favours
    /// in a shuffled order, then a boss floor; floor 1 is always ordinary, every kind turns up at least once, and no two
    /// floors in a row share a shape.
    /// </summary>
    public static LayoutKind[] Deal(ulong gameSeed)
    {
        var rng = new Random(unchecked((int)Core.Rng.Mix(gameSeed ^ 0x1A70UL)));
        var deal = new LayoutKind[TierRules.FinalFloor];
        for (var t = 0; t < TierRules.TierCount; t++)
        {
            var first = t * TierRules.FloorsPerTier;
            var weights = TierWeights[Math.Min(t, TierWeights.Length - 1)];
            var picks = SampleDistinct(rng, weights, TierRules.FloorsPerTier - 2);
            var slots = new List<LayoutKind> { LayoutKind.Standard };
            slots.AddRange(picks.Select(i => Special[i]));
            FloorGenerator.Shuffle(rng, slots);
            var previous = first == 0 ? (LayoutKind?)null : deal[first - 1];
            if (t == 0 && slots[0] != LayoutKind.Standard) Swap(slots, 0, slots.IndexOf(LayoutKind.Standard));
            else if (previous != null && slots[0] == previous) Swap(slots, 0, 1);
            for (var i = 0; i < slots.Count; i++) deal[first + i] = slots[i];
            // the boss floor: usually ordinary, else one of the shapes an arena sits well in
            var last = deal[first + slots.Count - 1];
            var bossPool = BossKinds.Where(k => k != last).ToArray();
            var bossWeights = bossPool.Select(k => k == LayoutKind.Standard ? 4f : weights[Array.IndexOf(Special, k)]).ToArray();
            deal[first + TierRules.FloorsPerTier - 1] = bossPool[Items.LootRoller.WeightedIndex(bossWeights, rng)];
        }
        // every kind at least once: a kind that never came up takes the place of one that came up most, never on floor 1 or a boss floor
        foreach (var missing in Special.Where(k => !deal.Contains(k)))
        {
            var counts = Special.ToDictionary(k => k, k => deal.Count(d => d == k));
            var best = -1;
            for (var i = 1; i < deal.Length; i++)
            {
                if (TierRules.IsBossFloor(i + 1) || deal[i] == LayoutKind.Standard || counts[deal[i]] < 2) continue;
                if (deal[i - 1] == missing || (i + 1 < deal.Length && deal[i + 1] == missing)) continue;
                if (best < 0 || counts[deal[i]] > counts[deal[best]]) best = i;
            }
            if (best >= 0) deal[best] = missing;
        }
        return deal;
    }

    private static void Swap(List<LayoutKind> list, int a, int b) { if (a >= 0 && b >= 0 && a != b) (list[a], list[b]) = (list[b], list[a]); }

    /// <summary>Weighted sampling without replacement: indices into <paramref name="weights"/>.</summary>
    private static List<int> SampleDistinct(Random rng, float[] weights, int count)
    {
        var left = weights.ToArray();
        var picks = new List<int>();
        for (var n = 0; n < count && left.Any(w => w > 0); n++)
        {
            var i = Items.LootRoller.WeightedIndex(left, rng);
            picks.Add(i);
            left[i] = 0f;
        }
        return picks;
    }

    /// <summary>The knobs a shape needs, over whatever the tier and campaign asked for.</summary>
    public static FloorSpec Tune(FloorSpec spec) => spec.Layout switch
    {
        LayoutKind.Labyrinth => spec with
        {
            MinRooms = 6, MaxRooms = 9, MinRoomSize = 3, MaxRoomSize = 5, RoomMargin = 3,
            HighRoomChance = 0f, LedgeCount = 0, PitCount = 0, StructureCount = 0,
            WaterCount = Math.Min(spec.WaterCount, 1), RubbleCount = Math.Min(spec.RubbleCount, 2),
        },
        LayoutKind.Ring => spec with
        {
            MinRooms = Math.Clamp(spec.MinRooms, 6, 9), MaxRooms = Math.Clamp(spec.MaxRooms, 7, 10), MaxRoomSize = Math.Min(spec.MaxRoomSize, 9),
        },
        LayoutKind.Mine => spec with
        {
            MinRooms = Math.Max(spec.MinRooms, 9), MaxRooms = Math.Max(spec.MaxRooms, 14),
            HighRoomChance = Math.Max(spec.HighRoomChance, 0.5f), PitCount = spec.BossFloor ? 0 : Math.Max(spec.PitCount, 2),
            RubbleCount = Math.Max(spec.RubbleCount, 5), StructureCount = Math.Min(spec.StructureCount, 1), WaterCount = Math.Min(spec.WaterCount, 1),
        },
        // flat: water beds sink into the floor, which an upper room's plinth has no room for, so every room can flood
        LayoutKind.Flooded => spec with { HighRoomChance = 0f, LedgeCount = 0, PitCount = 0, StructureCount = Math.Min(spec.StructureCount, 1) },
        LayoutKind.Burning => spec with { WaterCount = 0, FireCount = Math.Max(spec.FireCount, 14), RubbleCount = Math.Max(spec.RubbleCount, 3), StructureCount = Math.Min(spec.StructureCount, 1) },
        _ => spec,
    };
}
