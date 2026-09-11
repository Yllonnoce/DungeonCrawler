using DungeonCrawler.Dungeon.Generation;

namespace DungeonCrawler.Adventure;

/// <summary>One enemy to instantiate. SpawnIndex is the save key and must be stable for a given layout and plan.</summary>
public sealed record EnemyPlacement(int SpawnIndex, string EnemyId, GridPos Cell, bool Under, string? CollectiveId = null, int Ordinal = 0);

/// <summary>A chest added by the plan on top of the generator's. Indices start at <see cref="FloorContents.ExtraIndexBase"/>.</summary>
public sealed record ChestPlacement(int SpawnIndex, GridPos Cell, bool Locked, string? QuestId);

public sealed record TrapPlacement(int SpawnIndex, GridPos Cell, string Kind);

public sealed record RoomContents(int Index, RoomRole Role, string Name);

/// <summary>A horde resolved onto its nest: the mouths it streams from and the way each one faces into the room.</summary>
public sealed record HordeContents(string Id, string Name, int Room, IReadOnlyList<(string EnemyId, int Count)> Members, HordeTrigger Trigger, IReadOnlyList<GridPos> Mouths, IReadOnlyList<GridPos> Outward, Line Line)
{
    public int Total => Members.Sum(m => m.Count);
    /// <summary>The order they come out: the fodder first, the bigger ones spread evenly through the stream.</summary>
    public List<string> SpawnOrder(Func<string, bool> isFodder)
    {
        var fodder = Members.Where(m => isFodder(m.EnemyId)).SelectMany(m => Enumerable.Repeat(m.EnemyId, m.Count)).ToList();
        var big = Members.Where(m => !isFodder(m.EnemyId)).SelectMany(m => Enumerable.Repeat(m.EnemyId, m.Count)).ToList();
        var order = new List<string>(fodder);
        for (var i = 0; i < big.Count; i++)
        {
            var at = Math.Min(order.Count, (int)((i + 1) * (double)fodder.Count / (big.Count + 1)) + i);
            order.Insert(at, big[i]);
        }
        return order;
    }
}

public sealed record NpcPlacement(string Id, GridPos Cell);

/// <summary>An item a Recover quest asks for, dropped in its room.</summary>
public sealed record QuestItemPlacement(string QuestId, string ItemName, GridPos Cell, int Ordinal = 0);

/// <summary>A resolved enemy group: who is in it and what it protects, in grid terms.</summary>
public sealed record CollectiveContents(
    string Id, string Name, GoalKind Goal, int Room, IReadOnlyList<GridPos> PostCells,
    ObjectiveKind? ObjectiveKind, string? ObjectiveRef, GridPos? ObjectiveCell,
    IReadOnlyList<int> Route, IReadOnlyList<GridPos> RouteCells,
    string? AlarmTarget, int? RallyRoom, GridPos? RallyCell, IReadOnlyList<int> MemberSpawnIndices);

/// <summary>
/// Everything the builder needs to populate a generated <see cref="FloorLayout"/>. Produced deterministically by a
/// populator (legacy random, or a resolved plan); consumed by the Godot-side DungeonBuilder.
/// Engine-free and never saved: it is re-derived from the layout plus the floor plan.
/// </summary>
public sealed class FloorContents
{
    /// <summary>Spawn indices of plan-added chests and traps start here so they never collide with generator indices.</summary>
    public const int ExtraIndexBase = 1000;

    public FloorPlan? Plan { get; init; }
    public List<EnemyPlacement> Enemies { get; } = new();
    /// <summary>Boss id and cell when this floor has one (spawn index -2).</summary>
    public string? BossId { get; set; }
    public GridPos? BossCell { get; set; }
    public string? BossSignature { get; set; }
    public List<ChestPlacement> ExtraChests { get; } = new();
    public List<TrapPlacement> ExtraTraps { get; } = new();
    public List<RoomContents> Rooms { get; } = new();
    public List<CollectiveContents> Collectives { get; } = new();
    public List<NpcPlacement> Npcs { get; } = new();
    public List<HordeContents> Hordes { get; } = new();
    public List<QuestItemPlacement> QuestItems { get; } = new();
    /// <summary>The floor objective's people and texts, when the floor has one (derived from the layout, not the plan).</summary>
    public Objectives.ObjectiveContents? Objective { get; set; }
    /// <summary>Members or props the resolver could not place (room full); logged, never fatal.</summary>
    public List<string> Dropped { get; } = new();

    public int EnemyCount => Enemies.Count;
    public RoomContents? RoomFor(int index) => Rooms.FirstOrDefault(r => r.Index == index);
}
