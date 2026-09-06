using DungeonCrawler.Dungeon;
using DungeonCrawler.Dungeon.Generation;
using DungeonCrawler.Enemies;

namespace DungeonCrawler.Adventure;

/// <summary>What the party looks like, for budgeting a floor.</summary>
public sealed record PartySnapshot(float AverageLevel, float AverageItemLevel, int MemberCount)
{
    public static PartySnapshot Expected(int floor) => new(floor, floor, 4);
}

public sealed record RoomInfo(
    int Index, int X, int Y, int W, int H, int Level,
    bool IsStart, bool IsExit, bool IsArena, string? VaultKind,
    bool HasLedge, bool HasCellar, bool HasSecret, bool DeadEnd,
    int Degree, int HopsFromStart, IReadOnlyList<int> Neighbours, int FreeCells, IReadOnlyList<int> Chests,
    string? Structure = null, string? Water = null, int Mouths = 0)
{
    public int Area => W * H;
    public bool IsVault => VaultKind != null;
    /// <summary>A dilapidated hut with a campfire: bandits live here.</summary>
    public bool IsCamp => Structure == "Camp";
    /// <summary>A wall across the room with one arched opening.</summary>
    public bool HasGate => Structure == "Gate";
    /// <summary>A collapsed colonnade: broken columns, rubble, a heaved floor.</summary>
    public bool IsRuin => Structure == "Ruin";
    /// <summary>Breaches in the walls a horde can pour from.</summary>
    public bool HasNest => Mouths > 0;
}

/// <param name="Faction">"bandits" for camp dwellers, who may only be posted in camp rooms; empty for the tier's own creatures.</param>
public sealed record EnemyOption(string Id, string Name, EnemyKind Kind, float Cost, float Weight, string Faction = "")
{
    public bool IsBandit => Faction == FloorMenu.BanditFaction;
    /// <summary>Swarm fodder that only ever comes out of a nest, dozens at a time.</summary>
    public bool IsHorde => Faction == FloorMenu.HordeFaction;
}

/// <summary>
/// The designer's view of a generated floor: rooms with their properties and graph, the enemy roster with costs,
/// and a budget derived from the party. Engine-free; also rendered into the AI prompt.
/// </summary>
public sealed class FloorMenu
{
    public int Floor { get; init; }
    public int TierIndex { get; init; }
    public string TierName { get; init; } = "";
    public bool BossFloor { get; init; }
    public string? BossId { get; init; }
    public string? BossName { get; init; }
    public IReadOnlyList<RoomInfo> Rooms { get; init; } = Array.Empty<RoomInfo>();
    public IReadOnlyList<EnemyOption> Roster { get; init; } = Array.Empty<EnemyOption>();
    /// <summary>How many enemies the generator would have placed; designers aim near this.</summary>
    public int TargetEnemies { get; init; }
    public int MinEnemies { get; init; }
    public int MaxEnemies { get; init; } = 16;
    /// <summary>Sum of enemy costs the plan may spend.</summary>
    public float Budget { get; init; }
    public int VaultCount { get; init; }
    public int GeneratorChests { get; init; }
    public string Ascii { get; init; } = "";
    public IReadOnlyList<string> TrapKinds { get; init; } = new[] { "Spikes", "Fire", "Gas" };

    public const string BanditFaction = "bandits";
    public const string HordeFaction = "horde";
    public const int MinHorde = 3;
    /// <summary>Non-swarm creatures may march with a horde from this floor on, at most a quarter of it.</summary>
    public const int BigMembersFromFloor = 4;
    public const int MaxBigMembers = 8;
    public const int MaxHorde = 60;
    public const int MaxExtraChests = 2;
    public const int MaxExtraTraps = 3;
    public const float BudgetTolerance = 1.15f;

    public RoomInfo? Room(int index) => index >= 0 && index < Rooms.Count ? Rooms[index] : null;
    public RoomInfo? StartRoom => Rooms.FirstOrDefault(r => r.IsStart);
    public RoomInfo? ExitRoom => Rooms.FirstOrDefault(r => r.IsExit);
    public EnemyOption? Enemy(string id) => Roster.FirstOrDefault(e => e.Id == id);
    public bool IsBandit(string id) => Enemy(id)?.IsBandit == true;
    public IEnumerable<RoomInfo> CampRooms => Rooms.Where(r => r.IsCamp);
    public bool IsHorde(string id) => Enemy(id)?.IsHorde == true;
    public IEnumerable<RoomInfo> NestRooms => Rooms.Where(r => r.HasNest);
    public float CostOf(string id) => Enemy(id)?.Cost ?? MeanCost;
    public float MeanCost => Roster.Count == 0 ? 1f : Roster.Average(e => e.Cost);

    public static float EnemyCost(EnemyProfile p, int floor)
        => p.ScaledHp(floor) * ((p.MinDamage + p.MaxDamage) / 2f * TierRules.DamageScale(floor)) / 300f;

    /// <summary>How much stronger than expected the party is on this floor; scales the enemy budget.</summary>
    public static float PartyPower(PartySnapshot party, int floor)
    {
        var levelEdge = party.AverageLevel - floor;
        var gearEdge = party.AverageItemLevel - floor;
        return Math.Clamp(1f + 0.05f * levelEdge + 0.03f * gearEdge, 0.7f, 1.5f);
    }

    public static FloorMenu Build(FloorLayout layout, FloorSpec spec, TierProfile? tier, IReadOnlyDictionary<string, EnemyProfile> roster, int floor, PartySnapshot party)
    {
        var startRoom = layout.RoomAt(layout.UpStairs);
        var exitRoom = layout.RoomAt(layout.DownStairs);
        var startIndex = startRoom == null ? -1 : layout.Rooms.IndexOf(startRoom);
        var neighbours = Enumerable.Range(0, layout.Rooms.Count).Select(_ => new List<int>()).ToArray();
        foreach (var (a, b) in layout.Connections)
        {
            if (a < 0 || b < 0 || a >= neighbours.Length || b >= neighbours.Length || a == b) continue;
            if (!neighbours[a].Contains(b)) neighbours[a].Add(b);
            if (!neighbours[b].Contains(a)) neighbours[b].Add(a);
        }
        var hops = Hops(startIndex, neighbours);

        var rooms = new List<RoomInfo>();
        for (var i = 0; i < layout.Rooms.Count; i++)
        {
            var room = layout.Rooms[i];
            var vault = layout.Vaults.FirstOrDefault(v => v.Room == room);
            var chests = new List<int>();
            for (var c = 0; c < layout.ChestSpawns.Count; c++) if (room.Contains(layout.ChestSpawns[c])) chests.Add(c);
            var structure = layout.StructureAt(room);
            var water = layout.Water.FirstOrDefault(w => w.Room == room);
            rooms.Add(new RoomInfo(
                i, room.X, room.Y, room.W, room.H, layout.LevelOf(room.Center),
                room == startRoom, room == exitRoom, layout.BossArena == room, vault?.Kind.ToString(),
                layout.Ledges.Any(l => l.Room == room), layout.Cellars.Any(c => c.Room == room), layout.Secrets.Any(s => room.Contains(s.Facing)),
                neighbours[i].Count == 1, neighbours[i].Count, hops[i], neighbours[i], RoomCells.Free(layout, room).Count, chests,
                structure?.Kind.ToString(), water?.Kind.ToString(), layout.NestAt(room)?.Mouths.Count ?? 0));
        }

        var options = new List<EnemyOption>();
        if (tier != null)
            for (var i = 0; i < tier.EnemyPool.Count; i++)
            {
                var id = tier.EnemyPool[i];
                if (!roster.TryGetValue(id, out var p)) continue;
                options.Add(new EnemyOption(id, p.Name, p.Kind, EnemyCost(p, floor), i < tier.EnemyWeights.Count ? tier.EnemyWeights[i] : 1f));
            }
        var meanCost = options.Count == 0 ? 1f : options.Average(o => o.Cost);
        // bandits join the menu only where there is a camp for them; weight 0 keeps random picks off them
        if (layout.Structures.Any(s => s.Kind == StructureKind.Camp))
            foreach (var p in roster.Values.Where(p => p.Faction == BanditFaction && !p.IsBoss).OrderBy(p => p.Id))
                if (options.All(o => o.Id != p.Id)) options.Add(new EnemyOption(p.Id, p.Name, p.Kind, EnemyCost(p, floor), 0f, p.Faction));
        // horde fodder joins where there is a nest; the tier's own swarm, or any without a tier
        var tierIndex = TierRules.TierIndex(floor);
        if (layout.Nests.Count > 0)
            foreach (var p in roster.Values.Where(p => p.Faction == HordeFaction && !p.IsBoss && (p.TierIndex < 0 || p.TierIndex == tierIndex)).OrderBy(p => p.Id))
                if (options.All(o => o.Id != p.Id)) options.Add(new EnemyOption(p.Id, p.Name, p.Kind, EnemyCost(p, floor), 0f, p.Faction));
        var bossId = TierRules.IsBossFloor(floor) && tier != null && tier.BossId != "" ? tier.BossId : null;
        return new FloorMenu
        {
            Floor = floor, TierIndex = TierRules.TierIndex(floor), TierName = tier?.DisplayName ?? "Dungeon",
            BossFloor = TierRules.IsBossFloor(floor), BossId = bossId, BossName = bossId != null && roster.TryGetValue(bossId, out var bp) ? bp.Name : null,
            Rooms = rooms, Roster = options,
            TargetEnemies = spec.EnemyCount, MinEnemies = Math.Max(3, spec.EnemyCount / 2), MaxEnemies = 16,
            Budget = spec.EnemyCount * meanCost * PartyPower(party, floor),
            VaultCount = layout.Vaults.Count, GeneratorChests = layout.ChestSpawns.Count,
            Ascii = layout.ToAscii(),
        };
    }

    private static int[] Hops(int start, List<int>[] neighbours)
    {
        var hops = Enumerable.Repeat(-1, neighbours.Length).ToArray();
        if (start < 0 || start >= hops.Length) return hops;
        var queue = new Queue<int>();
        hops[start] = 0;
        queue.Enqueue(start);
        while (queue.Count > 0)
        {
            var r = queue.Dequeue();
            foreach (var n in neighbours[r])
                if (hops[n] < 0) { hops[n] = hops[r] + 1; queue.Enqueue(n); }
        }
        return hops;
    }
}

/// <summary>Cells inside a room where something may be placed without blocking stairs, keys, levers, ledges or existing props.</summary>
public static class RoomCells
{
    public static List<GridPos> Free(FloorLayout layout, Room room)
    {
        var level = layout.LevelOf(room.Center);
        var list = new List<GridPos>();
        foreach (var p in room.Cells())
        {
            if (layout[p] != Cell.Floor || layout.LevelOf(p) != level) continue;
            if (p == layout.UpStairs || p == layout.DownStairs) continue;
            if (layout.Vaults.Any(v => v.Key == p || v.Lever == p || v.Door == p)) continue;
            if (layout.IsReserved(p) || layout.IsWater(p)) continue;
            if (layout.Ledges.Any(l => l.Cells.Contains(p) || l.RampBottom == p || l.RampTop == p)) continue;
            if (layout.ChestSpawns.Contains(p) || layout.BreakableSpawns.Contains(p) || layout.TrapSpawns.Contains(p) || layout.KeySpawns.Contains(p)) continue;
            list.Add(p);
        }
        return list;
    }
}
