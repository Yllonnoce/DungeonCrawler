using DungeonCrawler.Dungeon;
using DungeonCrawler.Dungeon.Generation;
using DungeonCrawler.Enemies;

namespace DungeonCrawler.Adventure;

/// <summary>
/// Turns a validated <see cref="FloorPlan"/> into concrete cells and spawn indices. Deterministic for a given
/// layout, plan and seed: collectives in order, members in order, then loners, then the generator's ledge and cellar
/// spawns, then the boss. Plan-added chests and traps get indices from <see cref="FloorContents.ExtraIndexBase"/>.
/// </summary>
public static class PlanResolver
{
    /// <param name="flags">Flags the run has set so far: NPCs gated on them are placed or left out.</param>
    public static FloorContents Resolve(FloorLayout layout, FloorPlan plan, TierProfile? tier, IReadOnlyDictionary<string, EnemyProfile> roster, int floor, int seed, IReadOnlyCollection<string>? flags = null)
    {
        var contents = new FloorContents { Plan = plan };
        var rng = new Random(seed ^ 0xC011);
        var pools = layout.Rooms.Select(r => RoomCells.Free(layout, r)).ToList();
        foreach (var pool in pools) Shuffle(rng, pool);
        // a room with a structure fills from the structure outward: bandits around their fire, a gate's defenders at the opening
        for (var i = 0; i < pools.Count; i++)
            if (layout.StructureAt(layout.Rooms[i]) is { } s)
                pools[i] = pools[i].OrderByDescending(c => c.ManhattanDistance(s.Anchor)).ToList();
        GridPos? Take(int room) { if (room < 0 || room >= pools.Count || pools[room].Count == 0) return null; var p = pools[room][^1]; pools[room].RemoveAt(pools[room].Count - 1); return p; }

        for (var i = 0; i < layout.Rooms.Count; i++)
        {
            var rp = plan.RoomPlanFor(i);
            contents.Rooms.Add(new RoomContents(i, rp?.Role ?? RoomRole.Passage, rp?.Name ?? ""));
        }

        var index = 0;
        var memberCells = new Dictionary<string, List<(GridPos cell, int spawn)>>();
        foreach (var c in plan.Collectives)
        {
            var placed = new List<(GridPos, int)>();
            var ordinal = 0;
            foreach (var m in c.Members)
                for (var k = 0; k < m.Count; k++)
                {
                    if (Take(c.Goal.Room) is not { } cell) { contents.Dropped.Add($"{c.Id}: no room for {m.EnemyId}"); continue; }
                    contents.Enemies.Add(new EnemyPlacement(index, m.EnemyId, cell, false, c.Id, ordinal++));
                    placed.Add((cell, index));
                    index++;
                }
            memberCells[c.Id] = placed;
        }
        foreach (var l in plan.Loners)
        {
            if (Take(l.Room) is not { } cell) { contents.Dropped.Add($"loner: no room for {l.EnemyId}"); continue; }
            contents.Enemies.Add(new EnemyPlacement(index++, l.EnemyId, cell, false));
        }
        // ledge and cellar picks match legacy population for the same layout: burn the picks it would have made first
        var legacyRng = new Random(seed ^ 0x5EED);
        if (tier != null) foreach (var _ in layout.EnemySpawns) tier.PickEnemy(legacyRng);
        foreach (var cell in layout.LedgeSpawns)
            contents.Enemies.Add(new EnemyPlacement(index++, LegacyPopulator.PickRanged(tier, roster, legacyRng), cell, false));
        foreach (var cell in layout.CellarSpawns)
            contents.Enemies.Add(new EnemyPlacement(index++, tier?.PickEnemy(legacyRng) ?? LegacyPopulator.FallbackEnemy, cell, true));
        if (TierRules.IsBossFloor(floor) && layout.BossArena is { } arena && tier != null && tier.BossId != "")
        {
            contents.BossId = tier.BossId;
            contents.BossCell = arena.Center;
            contents.BossSignature = plan.Boss?.SignatureOverride;
        }

        for (var i = 0; i < plan.Chests.Count; i++)
        {
            var ch = plan.Chests[i];
            if (Take(ch.Room) is not { } cell) { contents.Dropped.Add($"chest {i}: room {ch.Room} full"); continue; }
            contents.ExtraChests.Add(new ChestPlacement(FloorContents.ExtraIndexBase + i, cell, ch.Locked, ch.QuestId));
        }
        for (var i = 0; i < plan.Traps.Count; i++)
        {
            var t = plan.Traps[i];
            if (Take(t.Room) is not { } cell) { contents.Dropped.Add($"trap {i}: room {t.Room} full"); continue; }
            contents.ExtraTraps.Add(new TrapPlacement(FloorContents.ExtraIndexBase + i, cell, t.Kind));
        }

        var set = flags ?? Array.Empty<string>();
        foreach (var n in plan.Npcs)
        {
            if (!FlagRules.Allowed(n.RequiresFlag, n.ForbidsFlag, set)) { contents.Dropped.Add($"npc {n.Id}: waits on flag {n.RequiresFlag ?? ("not " + n.ForbidsFlag)}"); continue; }
            if (Take(n.Room) is not { } cell) { contents.Dropped.Add($"npc {n.Id}: room {n.Room} full"); continue; }
            contents.Npcs.Add(new NpcPlacement(n.Id, cell));
        }
        foreach (var hp in plan.Hordes)
        {
            if (hp.Room < 0 || hp.Room >= layout.Rooms.Count || layout.NestAt(layout.Rooms[hp.Room]) is not { } nest) { contents.Dropped.Add($"horde {hp.Id}: room {hp.Room} has no nest"); continue; }
            contents.Hordes.Add(new HordeContents(hp.Id, hp.Name, hp.Room, hp.Members.Select(m => (m.EnemyId, m.Count)).ToList(), hp.Trigger, nest.Mouths, nest.Outward, hp.Line));
        }
        foreach (var q in plan.Quests.Where(q => FlagRules.Allowed(q.RequiresFlag, q.ForbidsFlag, set)))
        {
            if (q.Template == QuestTemplate.Recover && q.Params.ItemRoom is { } itemRoom)
            {
                if (Take(itemRoom) is not { } cell) { contents.Dropped.Add($"quest {q.Id}: item room {itemRoom} full"); continue; }
                contents.QuestItems.Add(new QuestItemPlacement(q.Id, q.Params.ItemName ?? "Relic", cell));
            }
            else if (q.Template == QuestTemplate.Investigate)
            {
                var ordinal = 0;
                foreach (var room in q.Params.ClueRooms)
                {
                    if (Take(room) is not { } cell) { contents.Dropped.Add($"quest {q.Id}: clue room {room} full"); continue; }
                    contents.QuestItems.Add(new QuestItemPlacement(q.Id, q.Params.ClueName ?? "clue", cell, ordinal++));
                }
            }
        }

        foreach (var c in plan.Collectives)
        {
            var placed = memberCells[c.Id];
            var g = c.Goal;
            GridPos? objective = null;
            if (g.Objective is { } o)
                objective = o.Kind switch
                {
                    ObjectiveKind.Chest when int.TryParse(o.Ref, out var ci) => ci >= FloorContents.ExtraIndexBase
                        ? contents.ExtraChests.FirstOrDefault(x => x.SpawnIndex == ci)?.Cell
                        : ci >= 0 && ci < layout.ChestSpawns.Count ? layout.ChestSpawns[ci] : null,
                    ObjectiveKind.Lever when int.TryParse(o.Ref, out var vi) => layout.Vaults.FirstOrDefault(v => v.Index == vi)?.Lever,
                    ObjectiveKind.Door when int.TryParse(o.Ref, out var vi) => layout.Vaults.FirstOrDefault(v => v.Index == vi)?.Door,
                    ObjectiveKind.Stairs => o.Ref == "up" ? layout.UpStairs : layout.DownStairs,
                    ObjectiveKind.Npc => contents.Npcs.FirstOrDefault(n => n.Id == o.Ref)?.Cell,
                    ObjectiveKind.Gate when int.TryParse(o.Ref, out var gi) && gi >= 0 && gi < layout.Rooms.Count => layout.StructureAt(layout.Rooms[gi]) is { Kind: StructureKind.Gate } gate ? gate.Anchor : null,
                    _ => null,
                };
            var routeCells = g.Route.Select(r => Anchor(layout, r)).Where(p => p != null).Select(p => p!.Value).ToList();
            contents.Collectives.Add(new CollectiveContents(
                c.Id, c.Name, g.Kind, g.Room, placed.Select(p => p.cell).ToList(),
                g.Objective?.Kind, g.Objective?.Ref, objective,
                g.Route.ToList(), routeCells,
                g.AlarmTarget, g.RallyRoom, g.RallyRoom is { } rr ? Anchor(layout, rr) : null,
                placed.Select(p => p.spawn).ToList()));
        }
        return contents;
    }

    /// <summary>A walkable cell representing a room: its centre, or the first free cell.</summary>
    public static GridPos? Anchor(FloorLayout layout, int room)
    {
        if (room < 0 || room >= layout.Rooms.Count) return null;
        var r = layout.Rooms[room];
        if (layout[r.Center] == Cell.Floor && layout.LevelOf(r.Center) == layout.LevelOf(r.Center)) return r.Center;
        var free = RoomCells.Free(layout, r);
        return free.Count > 0 ? free[0] : null;
    }

    private static void Shuffle<T>(Random rng, List<T> list)
    {
        for (var i = list.Count - 1; i > 0; i--)
        {
            var j = rng.Next(i + 1);
            (list[i], list[j]) = (list[j], list[i]);
        }
    }
}
