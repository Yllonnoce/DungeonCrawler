using DungeonCrawler.Combat;
using DungeonCrawler.Core;

namespace DungeonCrawler.Enemies;

/// <summary>What an elite is: its affixes and its titled name. Value equality over the affixes so two resolves of one plan compare equal.</summary>
public sealed record EliteRoll(IReadOnlyList<EliteAffix> Affixes, string Name)
{
    public bool Equals(EliteRoll? other) => other != null && Name == other.Name && Affixes.SequenceEqual(other.Affixes);
    public override int GetHashCode() { var h = new HashCode(); h.Add(Name); foreach (var a in Affixes) h.Add(a); return h.ToHashCode(); }
}

/// <summary>
/// Every elite number and every elite roll (Depths spec, section 2). Engine-free: the schedule, the affix draw from the floor seed and the
/// spawn index, the planner's fill, and the stat changes stamped onto an <see cref="EnemyProfile"/>. Behaviour lives in Enemy; tells in EliteVisuals.
/// </summary>
public static class EliteRules
{
    public const int FirstEliteFloor = 2, SecondEliteFloor = 8, ThirdEliteFloor = 14;
    public const float SwiftMove = 1.35f, SwiftWindup = 0.8f;
    public const float HulkingHp = 2.2f, HulkingScale = 1.3f, HulkingDamage = 1.25f;
    public const float WardFraction = 0.25f, WardRegrowSeconds = 10f;
    public const float VampiricFraction = 0.4f;
    public const float VolatileRadius = 4f, VolatileFraction = 0.6f, VolatileBurnSeconds = 4f;
    public const float ThornedFraction = 0.3f;
    public const float ShroudRadius = 4f;
    public const float HexSlowSeconds = 2f;
    public const float AlarmRadius = 25f;
    public const float JuggernautArmor = 20f;
    public const float VulnerableSeconds = 6f;
    public const float XpMultiplier = 2.5f;
    public const int SplitCount = 2;
    public const int MaxNameLength = 40;

    /// <summary>How many planned creatures are elites on a floor: none on floor 1 whatever the extras ("from floor 2").</summary>
    public static int CountFor(int floor, int extra)
        => floor < FirstEliteFloor ? 0 : (floor >= ThirdEliteFloor ? 3 : floor >= SecondEliteFloor ? 2 : 1) + Math.Max(0, extra);

    public static (int Min, int Max) AffixRange(int floor) => floor >= ThirdEliteFloor ? (2, 3) : floor >= SecondEliteFloor ? (1, 2) : (1, 1);

    /// <summary>Shrouded (melee only) and Hexed (shoot it) would leave no way in.</summary>
    public static bool Conflict(EliteAffix a, EliteAffix b)
        => (a == EliteAffix.Shrouded && b == EliteAffix.Hexed) || (a == EliteAffix.Hexed && b == EliteAffix.Shrouded);

    public static int RollSeed(int floorSeed, int spawnIndex)
        => unchecked((int)Rng.Mix(((ulong)(uint)floorSeed << 20) ^ (ulong)(uint)spawnIndex ^ 0xE117EUL));

    /// <summary>The elite standing at a spawn: the same on every peer and every reload.</summary>
    public static EliteRoll Roll(int floorSeed, int floor, int spawnIndex, string faction = "")
    {
        var rng = new Random(RollSeed(floorSeed, spawnIndex));
        var affixes = DrawAffixes(rng, floor);
        return new EliteRoll(affixes, EliteNames.Make(rng, affixes, faction));
    }

    private static List<EliteAffix> DrawAffixes(Random rng, int floor)
    {
        var (min, max) = AffixRange(floor);
        var count = rng.Next(min, max + 1);
        var all = Enum.GetValues<EliteAffix>();
        var drawn = new List<EliteAffix>(count);
        var guard = 0;
        while (drawn.Count < count && guard++ < 200)
        {
            var a = all[rng.Next(all.Length)];
            if (drawn.Contains(a) || drawn.Any(d => Conflict(d, a))) continue;
            drawn.Add(a);
        }
        return drawn;
    }

    /// <summary>Which eligible spawns become elites: a partial shuffle from the floor seed, returned in spawn order.</summary>
    public static IReadOnlyList<int> Choose(int floorSeed, IReadOnlyList<int> eligibleSpawnIndices, int count)
    {
        var pool = eligibleSpawnIndices.ToList();
        var take = Math.Min(Math.Max(0, count), pool.Count);
        if (take == 0) return Array.Empty<int>();
        var rng = new Random(unchecked((int)Rng.Mix((ulong)(uint)floorSeed ^ 0xE117E5UL)));
        for (var i = 0; i < take; i++)
        {
            var j = i + rng.Next(pool.Count - i);
            (pool[i], pool[j]) = (pool[j], pool[i]);
        }
        var chosen = pool.Take(take).ToList();
        chosen.Sort();
        return chosen;
    }

    /// <summary>
    /// Stamp the floor's elites onto the placements: the planner's (already carrying a roll) count first, the rest are chosen from the eligible
    /// spawns (a roster profile, not horde, not a boss) by the floor seed and rolled by their spawn index. Never moves or renumbers a spawn.
    /// </summary>
    public static void Assign(List<Adventure.EnemyPlacement> enemies, IReadOnlyDictionary<string, EnemyProfile> roster, int floorSeed, int floor, int extra)
    {
        var preset = enemies.Count(e => e.Elite != null);
        var want = CountFor(floor, extra) - preset;
        if (want <= 0) return;
        var eligible = enemies.Where(e => e.Elite == null && roster.TryGetValue(e.EnemyId, out var p) && p.Faction != "horde" && !p.IsBoss).Select(e => e.SpawnIndex).ToList();
        var chosen = Choose(floorSeed, eligible, want).ToHashSet();
        for (var i = 0; i < enemies.Count; i++)
        {
            var e = enemies[i];
            if (!chosen.Contains(e.SpawnIndex)) continue;
            enemies[i] = e with { Elite = Roll(floorSeed, floor, e.SpawnIndex, roster[e.EnemyId].Faction) };
        }
    }

    /// <summary>The planner's elite, finished: its affixes when it gave sane ones, else rolled; its name when it gave one, else made.</summary>
    public static EliteRoll Complete(IReadOnlyList<EliteAffix>? wanted, string? name, int floorSeed, int floor, int spawnIndex, string faction = "")
    {
        var rng = new Random(RollSeed(floorSeed, spawnIndex));
        var affixes = wanted is { Count: > 0 } ? Sanitize(wanted.Select(a => a.ToString()), floor) : null;
        if (affixes == null || affixes.Count == 0) affixes = DrawAffixes(rng, floor);
        var finalName = string.IsNullOrWhiteSpace(name) ? EliteNames.Make(rng, affixes, faction) : Clip(name.Trim(), MaxNameLength);
        return new EliteRoll(affixes, finalName);
    }

    public static string Clip(string s, int max) => s.Length <= max ? s : s[..max].TrimEnd();

    /// <summary>Parse the planner's affix names: unknown dropped, duplicates dropped, the later of a conflicting pair dropped, trimmed to the floor's most.</summary>
    public static List<EliteAffix> Sanitize(IEnumerable<string> names, int floor)
    {
        var result = new List<EliteAffix>();
        var max = AffixRange(floor).Max;
        foreach (var raw in names)
        {
            if (!Enum.TryParse<EliteAffix>(raw?.Trim() ?? "", ignoreCase: true, out var a)) continue;
            if (result.Contains(a) || result.Any(r => Conflict(r, a))) continue;
            result.Add(a);
            if (result.Count >= max) break;
        }
        return result;
    }

    /// <summary>The roll stamped onto a profile: Swift, Hulking and Juggernaut change numbers; the rest act in Enemy.</summary>
    public static EnemyProfile Apply(EnemyProfile p, EliteRoll roll)
    {
        var a = roll.Affixes;
        var swift = a.Contains(EliteAffix.Swift); var hulking = a.Contains(EliteAffix.Hulking); var jugg = a.Contains(EliteAffix.Juggernaut);
        return p with
        {
            Affixes = a, EliteName = roll.Name,
            MoveSpeed = swift ? p.MoveSpeed * SwiftMove : p.MoveSpeed,
            WindupSeconds = swift ? p.WindupSeconds * SwiftWindup : p.WindupSeconds,
            MaxHp = hulking ? p.MaxHp * HulkingHp : p.MaxHp,
            Scale = hulking ? p.Scale * HulkingScale : p.Scale,
            MinDamage = hulking ? p.MinDamage * HulkingDamage : p.MinDamage,
            MaxDamage = hulking ? p.MaxDamage * HulkingDamage : p.MaxDamage,
            Armor = jugg ? p.Armor + JuggernautArmor : p.Armor,
        };
    }

    /// <summary>Cursing: every hit applies Vulnerable.</summary>
    public static AttackProfile ApplyAttack(AttackProfile attack, IReadOnlyList<EliteAffix> affixes)
        => affixes.Contains(EliteAffix.Cursing) ? attack.WithExtraOnHit(new[] { new StatusApplication(StatusKind.Vulnerable, VulnerableSeconds, 0f) }) : attack;

    /// <summary>The wire form of an affix list: comma-joined enum names.</summary>
    public static string Encode(IReadOnlyList<EliteAffix> affixes) => string.Join(",", affixes);
    /// <summary>Back from the wire; unknown names dropped, null or blank → empty.</summary>
    public static List<EliteAffix> Decode(string? encoded)
        => string.IsNullOrWhiteSpace(encoded) ? new List<EliteAffix>() : encoded.Split(',').Select(n => Enum.TryParse<EliteAffix>(n.Trim(), true, out var a) ? (EliteAffix?)a : null).Where(a => a != null).Select(a => a!.Value).ToList();

    /// <summary>"Swift", "Warded and Vampiric", "Swift, Warded and Vampiric".</summary>
    public static string Describe(IReadOnlyList<EliteAffix> affixes)
    {
        if (affixes.Count == 0) return "";
        if (affixes.Count == 1) return affixes[0].ToString();
        return string.Join(", ", affixes.Take(affixes.Count - 1)) + " and " + affixes[^1];
    }

    /// <summary>The tier's horde brute (what a Splitting elite splits into), or null when the roster has none.</summary>
    public static string? HordeBruteFor(IReadOnlyDictionary<string, EnemyProfile> roster, int tierIndex)
        => roster.Where(kv => kv.Value.Faction == "horde" && kv.Value.TierIndex == tierIndex && kv.Value.Id.EndsWith("_brute")).Select(kv => kv.Key).OrderBy(k => k, StringComparer.Ordinal).FirstOrDefault();
}
