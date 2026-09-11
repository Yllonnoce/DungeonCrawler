namespace DungeonCrawler.Dungeon.Generation;

/// <summary>
/// What a floor asks of the party beyond reaching the stairs. Dealt into the descent like <see cref="LayoutKind"/>,
/// as a second axis on top of the shape; every kind but Toxic seals the down stairs until it is done.
/// </summary>
public enum FloorObjectiveKind : byte
{
    None = 0,
    /// <summary>Villagers flee the patrols; the caught are marched to a jail; free every one to open the way down.</summary>
    Jailbreak = 1,
    /// <summary>Poison rises from the low rooms; the stairs lie low and far; leave before it kills you.</summary>
    Toxic = 2,
    /// <summary>A ritualist channels at a shrine while breaches pour creatures; hold the line until the rite is done.</summary>
    Ritual = 3,
    /// <summary>The stairs sit in a sealed vault; levers shift the floor's gates; find the setting that opens it.</summary>
    ShiftingVault = 4,
}

/// <summary>
/// Which floors of a descent carry an objective and which. Engine-free and deterministic: the same seed always deals
/// the same eight, two per tier, never floor 1 or a boss floor, never two in a row, every kind at least once, and
/// only ever a kind the floor's shape can hold.
/// </summary>
public static class FloorObjectives
{
    public static readonly FloorObjectiveKind[] All = { FloorObjectiveKind.Jailbreak, FloorObjectiveKind.Toxic, FloorObjectiveKind.Ritual, FloorObjectiveKind.ShiftingVault };
    /// <summary>Objective floors per tier: the two quiet-shaped slots between the tier's first floor and its boss.</summary>
    public const int PerTier = 2;
    public const int PerRun = PerTier * TierRules.TierCount;
    /// <summary>Chance an endless floor carries one.</summary>
    public const float EndlessChance = 0.4f;

    /// <summary>Kinds the game can run today. A kind outside this set is never dealt, so the descent stays playable while the rest are built.</summary>
    public static readonly HashSet<FloorObjectiveKind> Implemented = new() { FloorObjectiveKind.Toxic, FloorObjectiveKind.ShiftingVault, FloorObjectiveKind.Ritual, FloorObjectiveKind.Jailbreak };

    public static string Id(FloorObjectiveKind kind) => kind == FloorObjectiveKind.None ? "none" : kind.ToString().ToLowerInvariant();

    public static bool TryParse(string? id, out FloorObjectiveKind kind)
    {
        kind = FloorObjectiveKind.None;
        if (string.IsNullOrWhiteSpace(id)) return false;
        var s = id.Trim().Replace("_", "").Replace("-", "");
        if (s.Equals("none", StringComparison.OrdinalIgnoreCase)) { kind = FloorObjectiveKind.None; return true; }
        return Enum.TryParse(s, ignoreCase: true, out kind) && Enum.IsDefined(kind) && kind != FloorObjectiveKind.None;
    }

    /// <summary>One line per kind, for the planner's prompt, the debug log and the HUD.</summary>
    public static string Describe(FloorObjectiveKind kind) => kind switch
    {
        FloorObjectiveKind.Jailbreak => "villagers wander the floor fleeing the patrols; any creature that reaches one marches it to a jail; jailers carry the keys; free every prisoner to open the stairs",
        FloorObjectiveKind.Toxic => "poison gas rises room by room from the low ground; the stairs lie low and far behind a lever gate; the party must find them before the gas kills them",
        FloorObjectiveKind.Ritual => "a ritualist channels a rite at a shrine while breaches around it pour creatures; the party closes breaches with levers, lays traps and holds until the rite is done, which opens the stairs",
        FloorObjectiveKind.ShiftingVault => "the stairs sit in a sealed vault; levers about the floor each shift a set of gates (and the patrols reroute with them); plaques hint at the setting that opens the vault",
        _ => "nothing beyond reaching the stairs",
    };

    /// <summary>A short name for the HUD and lock text.</summary>
    public static string Title(FloorObjectiveKind kind) => kind switch
    {
        FloorObjectiveKind.Jailbreak => "Jailbreak",
        FloorObjectiveKind.Toxic => "Rising Gas",
        FloorObjectiveKind.Ritual => "The Ritual",
        FloorObjectiveKind.ShiftingVault => "The Shifting Vault",
        _ => "",
    };

    /// <summary>Whether a floor of this shape can hold the objective: gas needs two heights to climb, the vault puzzle needs corridors between rooms.</summary>
    public static bool Compatible(FloorObjectiveKind kind, LayoutKind layout) => kind switch
    {
        FloorObjectiveKind.None => true,
        FloorObjectiveKind.Toxic => layout is not (LayoutKind.Labyrinth or LayoutKind.Flooded),
        FloorObjectiveKind.ShiftingVault => layout != LayoutKind.Labyrinth,
        FloorObjectiveKind.Ritual => layout != LayoutKind.Flooded, // a flooded room's walls take no breach
        _ => true,
    };

    /// <summary>Every objective but the gas seals the way down until it is met; the gas is its own gate.</summary>
    public static bool LocksStairs(FloorObjectiveKind kind) => kind is FloorObjectiveKind.Jailbreak or FloorObjectiveKind.Ritual or FloorObjectiveKind.ShiftingVault;

    /// <summary>The knobs an objective needs, over whatever the shape and the campaign asked for.</summary>
    public static FloorSpec Tune(FloorSpec spec) => spec.Objective switch
    {
        FloorObjectiveKind.Toxic => spec with { HighRoomChance = Math.Max(spec.HighRoomChance, 0.45f) },
        FloorObjectiveKind.Ritual => spec with { NestCount = Math.Max(spec.NestCount, 3) },
        FloorObjectiveKind.Jailbreak => spec with { VaultCount = Math.Max(spec.VaultCount, 1) },
        _ => spec,
    };

    /// <summary>The objective a floor carries when the campaign leaves it to the game.</summary>
    public static FloorObjectiveKind Pick(ulong gameSeed, int floor) => Pick(gameSeed, floor, LayoutKinds.Pick(gameSeed, floor));

    /// <param name="layout">The shape the floor actually takes (a campaign may have overridden the deal), so the pick is always compatible.</param>
    public static FloorObjectiveKind Pick(ulong gameSeed, int floor, LayoutKind layout)
    {
        if (floor < 2 || TierRules.IsBossFloor(floor)) return FloorObjectiveKind.None;
        if (floor <= TierRules.FinalFloor)
        {
            var dealt = Deal(gameSeed, LayoutKinds.Deal(gameSeed))[floor - 1];
            return dealt == FloorObjectiveKind.None || Compatible(dealt, layout) ? dealt : Substitute(gameSeed, floor, layout);
        }
        // endless: a fair roll, never right after another objective floor
        if (PickEndless(gameSeed, floor - 1) != FloorObjectiveKind.None) return FloorObjectiveKind.None;
        var kind = PickEndless(gameSeed, floor);
        return Compatible(kind, layout) ? kind : FloorObjectiveKind.None;
    }

    /// <summary>A campaign gave the floor a shape the dealt kind cannot live in: the slot keeps an objective, one the shape can hold, chosen from the seed.</summary>
    private static FloorObjectiveKind Substitute(ulong gameSeed, int floor, LayoutKind layout)
    {
        var fits = All.Where(k => Compatible(k, layout)).ToList();
        if (fits.Count == 0) return FloorObjectiveKind.None;
        var rng = new Random(unchecked((int)Core.Rng.Mix(gameSeed ^ ((ulong)floor * 0x5A5A5A5AUL) ^ 0x5B57UL)));
        return fits[rng.Next(fits.Count)];
    }

    private static FloorObjectiveKind PickEndless(ulong gameSeed, int floor)
    {
        if (floor <= TierRules.FinalFloor) return floor >= 2 && !TierRules.IsBossFloor(floor) ? Deal(gameSeed, LayoutKinds.Deal(gameSeed))[floor - 1] : FloorObjectiveKind.None;
        var rng = new Random(unchecked((int)Core.Rng.Mix(gameSeed ^ ((ulong)floor * 0xB5B5B5B5UL) ^ 0x0B1EC7UL)));
        if (rng.NextDouble() >= EndlessChance) return FloorObjectiveKind.None;
        return All[rng.Next(All.Length)];
    }

    /// <summary>
    /// The objectives of a descent, one entry per floor (None where the floor has none). Each tier gets two on
    /// floors that are neither its boss floor nor adjacent to each other (floor 1 never), each a kind its shape can
    /// hold; then every kind is made to appear at least once by swapping the most repeated kind out where it fits.
    /// </summary>
    public static FloorObjectiveKind[] Deal(ulong gameSeed, LayoutKind[] layouts)
    {
        var rng = new Random(unchecked((int)Core.Rng.Mix(gameSeed ^ 0x0B1EC7UL)));
        var deal = new FloorObjectiveKind[TierRules.FinalFloor];
        var slots = new List<int>();
        for (var t = 0; t < TierRules.TierCount; t++)
        {
            var first = t * TierRules.FloorsPerTier;
            var candidates = Enumerable.Range(first, TierRules.FloorsPerTier).Where(i => i > 0 && !TierRules.IsBossFloor(i + 1)).ToList();
            var pairs = new List<(int, int)>();
            for (var a = 0; a < candidates.Count; a++)
                for (var b = a + 1; b < candidates.Count; b++)
                    if (candidates[b] - candidates[a] >= 2) pairs.Add((candidates[a], candidates[b]));
            if (pairs.Count == 0) continue;
            var (x, y) = pairs[rng.Next(pairs.Count)];
            slots.Add(x); slots.Add(y);
        }
        foreach (var i in slots)
        {
            var fits = All.Where(k => Compatible(k, LayoutAt(layouts, i))).ToList();
            if (fits.Count == 0) continue;
            deal[i] = fits[rng.Next(fits.Count)];
        }
        // every kind at least once: a kind that never came up takes the place of one that came up most, where the shape allows
        foreach (var missing in All.Where(k => !deal.Contains(k)))
        {
            var counts = All.ToDictionary(k => k, k => deal.Count(d => d == k));
            var best = -1;
            foreach (var i in slots)
            {
                if (deal[i] == FloorObjectiveKind.None || counts[deal[i]] < 2 || !Compatible(missing, LayoutAt(layouts, i))) continue;
                if (best < 0 || counts[deal[i]] > counts[deal[best]]) best = i;
            }
            if (best >= 0) deal[best] = missing;
        }
        return deal;
    }

    private static LayoutKind LayoutAt(LayoutKind[] layouts, int i) => i >= 0 && i < layouts.Length ? layouts[i] : LayoutKind.Standard;
}
