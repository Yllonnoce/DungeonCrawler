namespace DungeonCrawler.Dungeon.Generation;

/// <summary>
/// The rooms, doors and cells each floor objective needs. Every pass here is gated on the kind and draws from its own
/// salted rng, so a floor without an objective generates exactly as it always did. Rooms are only ever appended.
/// </summary>
public static partial class FloorGenerator
{
    /// <summary>After the heights are known and before structures, water and spawns: move or seal the exit, carve the jail, mark the shrine.</summary>
    private static void PlaceObjective(FloorSpec spec, Random rng, FloorLayout layout)
    {
        if (layout.Objective is not { } objective) return;
        switch (objective.Kind)
        {
            case FloorObjectiveKind.Toxic: PlaceToxic(rng, layout, objective); break;
            case FloorObjectiveKind.ShiftingVault: PlaceShiftingVault(rng, layout, objective); break;
            case FloorObjectiveKind.Ritual: PlaceRitual(rng, layout, objective); break;
            case FloorObjectiveKind.Jailbreak: PlaceJail(rng, layout, objective); break;
            default: break;
        }
    }

    /// <summary>
    /// Jailbreak: the jail is a dead end sealed by a key door with no key of its own (the jailers carry them, and a
    /// spare or two lie about); an existing treasure vault becomes the reward that opens when every prisoner is free.
    /// </summary>
    private static void PlaceJail(Random rng, FloorLayout layout, ObjectiveLayout objective)
    {
        var rooms = layout.Rooms;
        var upRoom = layout.RoomAt(layout.UpStairs);
        var downRoom = layout.RoomAt(layout.DownStairs);
        var hops = RoomHops(layout, upRoom);
        Room? jail = null;
        GridPos door = default;
        bool Usable(Room r) => r != upRoom && r != downRoom && r != layout.BossArena && layout.Vaults.All(v => v.Room != r) && layout.Cellars.All(c => c.Room != r) && layout.Ledges.All(l => l.Room != r);
        foreach (var r in rooms.Where(Usable).OrderBy(r => r.W * r.H).ThenByDescending(r => hops[rooms.IndexOf(r)]).ThenBy(rooms.IndexOf))
            if (IsSealable(layout, r, out door)) { jail = r; break; }
        if (jail == null)
            for (var attempt = 0; attempt < 60 && jail == null; attempt++)
                if (TryCarveVaultRoom(rng, layout) is { } side) { jail = side.room; door = side.entrance; }
        if (jail == null) { layout.Objective = null; return; }
        var index = layout.Vaults.Count;
        layout[door] = Cell.Door;
        layout.Vaults.Add(new Vault(index, jail, door, VaultKind.KeyDoor, null, null, Revealed: true));
        objective.JailRoom = jail;
        objective.JailVault = index;
        objective.Reserved.Add(door);
        // the reward: the first treasure vault the floor already has
        var reward = layout.Vaults.FirstOrDefault(v => v.Index != index && v.Room != jail);
        objective.RewardVault = reward?.Index ?? -1;
    }

    /// <summary>Where the prisoners start: one per room, a couple of hops in, and one or two already behind bars.</summary>
    private static void PlaceJailPrisoners(Random rng, FloorLayout layout, ObjectiveLayout objective)
    {
        if (objective.JailRoom is not { } jail) return;
        var rooms = layout.Rooms;
        var upRoom = layout.RoomAt(layout.UpStairs);
        var hops = RoomHops(layout, upRoom);
        var reach = layout.Reachable(layout.UpStairs);
        bool Free(GridPos p) => layout[p] == Cell.Floor && !layout.IsUnder(p) && !layout.IsReserved(p) && !layout.IsWater(p) && reach.Contains(p)
            && p != layout.UpStairs && p != layout.DownStairs && !layout.ChestSpawns.Contains(p) && layout.Vaults.All(v => v.Key != p && v.Lever != p && v.Door != p)
            && !layout.Ledges.Any(l => l.Cells.Contains(p) || l.RampBottom == p || l.RampTop == p) && !layout.Structures.Any(s => s.Cells.Contains(p) || s.Openings.Contains(p) || s.Anchor == p);
        var count = JailbreakCount(rng);
        var jailed = Math.Min(count - 1, 1 + rng.Next(2));
        objective.JailedAtStart = jailed;
        // behind bars first
        var cells = jail.Cells().Where(p => layout[p] == Cell.Floor && layout.LevelOf(p) == layout.LevelOf(jail.Center) && !layout.IsReserved(p) && p != layout.DownStairs).ToList();
        Shuffle(rng, cells);
        for (var i = 0; i < jailed && i < cells.Count; i++) { objective.PrisonerCells.Add(cells[i]); objective.Reserved.Add(cells[i]); }
        // the rest out on the floor, each in a different room, none at the start or in a vault
        var hosts = rooms.Where(r => r != jail && r != upRoom && r != layout.BossArena && layout.Vaults.All(v => v.Room != r) && layout.Cellars.All(c => c.Room != r) && hops[rooms.IndexOf(r)] >= 1)
            .OrderByDescending(r => hops[rooms.IndexOf(r)] >= 2 ? 1 : 0).ThenBy(_ => rng.Next()).ToList();
        foreach (var r in hosts)
        {
            if (objective.PrisonerCells.Count >= count) break;
            var free = r.Cells().Where(p => Free(p) && layout.LevelOf(p) == layout.LevelOf(r.Center)).ToList();
            if (free.Count == 0) continue;
            var cell = free[rng.Next(free.Count)];
            objective.PrisonerCells.Add(cell);
            objective.Reserved.Add(cell);
        }
        if (objective.PrisonerCells.Count <= objective.JailedAtStart) layout.Objective = null; // nobody to rescue out there: an ordinary floor after all
    }

    private static int JailbreakCount(Random rng) => 3 + rng.Next(3);

    /// <summary>Spare keys: inside the floor's treasure vaults (behind their own locks) or beside locked chests; failing both, anywhere far from the jail.</summary>
    private static void PlaceJailKeys(Random rng, FloorLayout layout, ObjectiveLayout objective)
    {
        if (objective.JailRoom is not { } jail) return;
        var want = 1 + rng.Next(2);
        bool Free(GridPos p) => layout[p] == Cell.Floor && !layout.IsUnder(p) && !layout.IsReserved(p) && !layout.IsWater(p) && p != layout.UpStairs && p != layout.DownStairs
            && !layout.ChestSpawns.Contains(p) && !layout.EnemySpawns.Contains(p) && !layout.TrapSpawns.Contains(p) && !layout.BreakableSpawns.Contains(p) && !layout.KeySpawns.Contains(p)
            && layout.Vaults.All(v => v.Key != p && v.Lever != p && v.Door != p) && !layout.Ledges.Any(l => l.Cells.Contains(p) || l.RampBottom == p || l.RampTop == p);
        var spots = new List<GridPos>();
        foreach (var v in layout.Vaults.Where(v => v.Room != jail))
            foreach (var p in v.Room.Cells().Where(Free).OrderBy(_ => rng.Next()).Take(1)) spots.Add(p);
        foreach (var i in layout.LockedChests)
        {
            if (i < 0 || i >= layout.ChestSpawns.Count) continue;
            var c = layout.ChestSpawns[i];
            foreach (var d in Dirs4) { var q = c + d; if (Free(q) && layout.RoomAt(q) != null) { spots.Add(q); break; } }
        }
        if (spots.Count < want)
        {
            var upRoom = layout.RoomAt(layout.UpStairs);
            var hops = RoomHops(layout, upRoom);
            foreach (var r in layout.Rooms.Where(r => r != jail && r != upRoom && layout.Vaults.All(v => v.Room != r)).OrderByDescending(r => hops[layout.Rooms.IndexOf(r)]))
            {
                var free = r.Cells().Where(Free).ToList();
                if (free.Count > 0) spots.Add(free[rng.Next(free.Count)]);
                if (spots.Count >= want) break;
            }
        }
        foreach (var p in spots.Take(want))
        {
            objective.ExtraKeyCells.Add(p);
            objective.Reserved.Add(p);
            layout.KeySpawns.Add(p);
        }
    }

    /// <summary>
    /// Ritual: the shrine is a roomy room a few hops in with more than one way in, its altar in the middle. The nests
    /// (placed later) favour it; the breach levers come once the mouths are known.
    /// </summary>
    private static void PlaceRitual(Random rng, FloorLayout layout, ObjectiveLayout objective)
    {
        var rooms = layout.Rooms;
        var upRoom = layout.RoomAt(layout.UpStairs);
        var downRoom = layout.RoomAt(layout.DownStairs);
        var hops = RoomHops(layout, upRoom);
        bool Usable(Room r) => r != upRoom && r != downRoom && r != layout.BossArena && layout.Vaults.All(v => v.Room != r)
            && layout.Cellars.All(c => c.Room != r) && layout.Ledges.All(l => l.Room != r) && r.W >= 5 && r.H >= 5 && Entrances(layout, r).Count >= 2
            && layout[r.Center] == Cell.Floor && !layout.IsUnder(r.Center);
        var pool = rooms.Where(Usable).ToList();
        if (pool.Count == 0) pool = rooms.Where(r => r != upRoom && r != downRoom && r != layout.BossArena && layout.Vaults.All(v => v.Room != r) && r.W >= 4 && r.H >= 4 && layout[r.Center] == Cell.Floor).ToList();
        if (pool.Count == 0) { layout.Objective = null; return; }
        int Score(Room r) { var h = hops[rooms.IndexOf(r)]; return h is >= 2 and <= 4 ? 0 : h < 2 ? 2 : 1; }
        var shrine = pool.OrderBy(Score).ThenByDescending(r => r.W * r.H).ThenBy(rooms.IndexOf).First();
        objective.ShrineRoom = shrine;
        objective.Altar = shrine.Center;
        objective.Reserved.Add(shrine.Center);
    }

    /// <summary>One lever per breach mouth, two cells into the shrine from the mouth and a step to the side, where a body can reach it without standing in the stream.</summary>
    private static void PlaceRitualLevers(Random rng, FloorLayout layout, ObjectiveLayout objective)
    {
        if (objective.ShrineRoom is not { } shrine || layout.NestAt(shrine) is not { } nest) return;
        bool Free(GridPos p) => shrine.Contains(p) && layout[p] == Cell.Floor && layout.LevelOf(p) == layout.LevelOf(shrine.Center) && !layout.IsReserved(p) && !layout.IsWater(p)
            && p != layout.UpStairs && p != layout.DownStairs && !layout.ChestSpawns.Contains(p) && !layout.EnemySpawns.Contains(p) && !layout.TrapSpawns.Contains(p)
            && !layout.BreakableSpawns.Contains(p) && !layout.KeySpawns.Contains(p) && !layout.Ledges.Any(l => l.Cells.Contains(p) || l.RampBottom == p || l.RampTop == p)
            && layout.Vaults.All(v => v.Key != p && v.Lever != p) && !layout.Structures.Any(s => s.Cells.Contains(p) || s.Openings.Contains(p) || s.Anchor == p);
        for (var i = 0; i < nest.Mouths.Count; i++)
        {
            var mouth = nest.Mouths[i];
            var inward = nest.Outward[i];
            var perp = new GridPos(-inward.Y, inward.X);
            var front = mouth + inward;
            var options = new[] { front + inward + perp, front + inward - perp, front + inward + inward + perp, front + inward + inward - perp, front + inward, front + inward + inward, front + perp, front - perp };
            var spot = options.FirstOrDefault(Free);
            if (spot == default && !Free(spot)) spot = shrine.Cells().Where(Free).OrderBy(p => p.ManhattanDistance(front)).FirstOrDefault();
            if (spot == default && !Free(spot)) continue;
            objective.BreachLevers.Add((spot, mouth));
            objective.Reserved.Add(spot);
        }
    }

    /// <summary>
    /// Shifting Vault: the stairs move into a sealable room (the exit as it is, another dead end, or a side room carved
    /// for them), sealed by a lever gate with no lever of its own; then the lever-and-gate puzzle that opens it.
    /// </summary>
    private static void PlaceShiftingVault(Random rng, FloorLayout layout, ObjectiveLayout objective)
    {
        var rooms = layout.Rooms;
        var upRoom = layout.RoomAt(layout.UpStairs);
        var hops = RoomHops(layout, upRoom);
        Room? vaultRoom = null;
        GridPos door = default;
        bool Usable(Room r) => r != upRoom && r != layout.BossArena && layout.Vaults.All(v => v.Room != r) && StairsCell(layout, r) != null;
        var exit = layout.RoomAt(layout.DownStairs);
        if (exit != null && Usable(exit) && IsSealable(layout, exit, out door)) vaultRoom = exit;
        else
        {
            foreach (var r in rooms.Where(Usable).OrderByDescending(r => hops[rooms.IndexOf(r)]).ThenBy(rooms.IndexOf))
                if (IsSealable(layout, r, out door)) { vaultRoom = r; break; }
        }
        if (vaultRoom == null)
        {
            for (var attempt = 0; attempt < 60 && vaultRoom == null; attempt++)
                if (TryCarveVaultRoom(rng, layout) is { } side) { vaultRoom = side.room; door = side.entrance; }
        }
        if (vaultRoom == null || StairsCell(layout, vaultRoom) is not { } stairs) { layout.Objective = null; return; } // nowhere to seal the stairs: an ordinary floor after all
        layout.DownStairs = stairs;
        var index = layout.Vaults.Count;
        layout[door] = Cell.Door;
        layout.Vaults.Add(new Vault(index, vaultRoom, door, VaultKind.LeverGate, null, null, Revealed: true));
        objective.StairsVault = index;
        objective.Reserved.Add(door);
        // the puzzle itself waits for the late pass: pits, structures and spawns must be down before the proof is run
    }

    /// <summary>The lever-and-gate puzzle, proven against the floor as it finally stands; failing that, an ordinary lever far from the vault.</summary>
    private static void PlaceShiftingVaultPuzzle(Random rng, FloorLayout layout, ObjectiveLayout objective)
    {
        if (objective.StairsVault < 0) return;
        var rooms = layout.Rooms;
        var upRoom = layout.RoomAt(layout.UpStairs);
        var hops = RoomHops(layout, upRoom);
        var vault = layout.Vaults[objective.StairsVault];
        var puzzle = VaultPuzzles.Generate(layout, rng, vault.Door, vault.Room);
        if (puzzle == null)
        {
            var hosts = rooms.Where(r => r != vault.Room && layout.Vaults.All(v => v.Room != r)).OrderByDescending(r => hops[rooms.IndexOf(r)]).ThenBy(rooms.IndexOf).ToList();
            if (hosts.Count > 0) layout.Vaults[objective.StairsVault] = vault with { Lever = RandomCell(rng, hosts[0], layout) };
            return;
        }
        objective.Puzzle = puzzle;
        foreach (var g in puzzle.Gates) objective.Reserved.Add(g);
        foreach (var l in puzzle.Levers) objective.Reserved.Add(l);
        for (var i = 0; i < puzzle.LeverCount; i++)
            if (Beside(layout, puzzle.Levers[i], objective) is { } spot) { objective.Plaques.Add((spot, i)); objective.Reserved.Add(spot); }
        foreach (var room in rooms.Where(r => r != vault.Room && layout.Vaults.All(v => v.Room != r)).OrderBy(r => hops[rooms.IndexOf(r)] < 0 ? 99 : hops[rooms.IndexOf(r)]).ThenBy(rooms.IndexOf))
            if (PlaqueSpot(layout, room, objective) is { } notice) { objective.Plaques.Add((notice, -1)); objective.Reserved.Add(notice); break; }
    }

    /// <summary>
    /// Carve a side room for the stairs, after the heights and ledges are in: the corridor must meet its host on plain
    /// floor (not a ramp, not a ledge strip), and the room takes that cell's height. A carve that lands badly is undone.
    /// </summary>
    private static (Room room, GridPos entrance)? TryCarveVaultRoom(Random rng, FloorLayout layout)
    {
        var cells = (Cell[])layout.Cells.Clone();
        var rooms = layout.Rooms.Count;
        var connections = layout.Connections.Count;
        if (TryCarveSideRoom(rng, layout) is not { } side) return null;
        if (LevelCarved(layout, side.room, side.entrance)) return side;
        // undo: the cells as they were, the room and its connection gone, the walls raised again
        Array.Copy(cells, layout.Cells, cells.Length);
        while (layout.Rooms.Count > rooms) layout.Rooms.RemoveAt(layout.Rooms.Count - 1);
        while (layout.Connections.Count > connections) layout.Connections.RemoveAt(layout.Connections.Count - 1);
        RaiseWalls(layout);
        return null;
    }

    /// <summary>A carved side room takes the height of the host cell its corridor meets, corridor included; false when that cell is a ramp or a ledge.</summary>
    private static bool LevelCarved(FloorLayout layout, Room room, GridPos entrance)
    {
        // the corridor runs straight from the entrance away from the room until it meets another room
        var step = entrance.X < room.X ? new GridPos(-1, 0) : entrance.X > room.Right ? new GridPos(1, 0) : entrance.Y < room.Y ? new GridPos(0, -1) : new GridPos(0, 1);
        var p = entrance;
        var corridor = new List<GridPos>();
        Room? host = null;
        var met = p;
        for (var n = 0; n < 12 && host == null; n++)
        {
            if (!layout.InBounds(p.X, p.Y) || !layout.IsWalkable(p)) break;
            host = layout.RoomAt(p);
            if (host == null) corridor.Add(p); else met = p;
            p += step;
        }
        if (host == null) return false;
        if (layout[met] != Cell.Floor || layout.Ramps.ContainsKey(met) || layout.Ledges.Any(l => l.Cells.Contains(met) || l.RampBottom == met || l.RampTop == met)) return false;
        if (layout.LevelOf(met) != layout.LevelOf(host.Center)) return false; // a strip a level above the room: the way in would not be a way in
        if (layout.LevelOf(met) != 0) return false; // a stairs well in an upper room stands too close to the wall tops; keep the vault on the ground
        var level = layout.LevelOf(met);
        foreach (var c in corridor) layout.SetLevel(c, level);
        foreach (var c in room.Cells()) layout.SetLevel(c, level);
        return true;
    }

    /// <summary>A free cell orthogonally beside <paramref name="cell"/> inside its room, for a plaque.</summary>
    private static GridPos? Beside(FloorLayout layout, GridPos cell, ObjectiveLayout objective)
    {
        var room = layout.RoomAt(cell);
        if (room == null) return null;
        for (var radius = 1; radius <= 2; radius++)
            foreach (var d in new[] { new GridPos(1, 0), new GridPos(-1, 0), new GridPos(0, 1), new GridPos(0, -1), new GridPos(1, 1), new GridPos(-1, 1), new GridPos(1, -1), new GridPos(-1, -1) })
            {
                var q = new GridPos(cell.X + d.X * radius, cell.Y + d.Y * radius);
                if (room.Contains(q) && PlaqueOk(layout, room, q, objective) && q.X != room.X && q.X != room.Right && q.Y != room.Y && q.Y != room.Bottom) return q;
            }
        return null;
    }

    /// <summary>A wall-side cell of the room that blocks nothing, for the vault's notice.</summary>
    private static GridPos? PlaqueSpot(FloorLayout layout, Room room, ObjectiveLayout objective)
    {
        var entrances = Entrances(layout, room);
        var ok = room.Cells().Where(p => PlaqueOk(layout, room, p, objective) && Math.Abs(p.X - layout.UpStairs.X) + Math.Abs(p.Y - layout.UpStairs.Y) >= 2).ToList();
        var wall = ok.Where(p => (p.X == room.X + 1 || p.X == room.Right - 1 || p.Y == room.Y + 1 || p.Y == room.Bottom - 1) && entrances.All(e => Math.Abs(e.X - p.X) + Math.Abs(e.Y - p.Y) > 2)).ToList();
        var pool = wall.Count > 0 ? wall : ok;
        return pool.OrderBy(p => p.Y).ThenBy(p => p.X).Cast<GridPos?>().FirstOrDefault();
    }

    private static bool PlaqueOk(FloorLayout layout, Room room, GridPos p, ObjectiveLayout objective)
        => layout[p] == Cell.Floor && layout.LevelOf(p) == layout.LevelOf(room.Center) && !layout.IsUnder(p) && !layout.IsReserved(p) && !objective.Reserved.Contains(p)
           && p != layout.UpStairs && p != layout.DownStairs && !layout.ChestSpawns.Contains(p) && !layout.IsWater(p)
           && !layout.EnemySpawns.Contains(p) && !layout.TrapSpawns.Contains(p) && !layout.BreakableSpawns.Contains(p) && !layout.KeySpawns.Contains(p)
           && !layout.Ledges.Any(l => l.Cells.Contains(p) || l.RampBottom == p || l.RampTop == p)
           && layout.Vaults.All(v => v.Key != p && v.Lever != p && v.Door != p)
           && !layout.Structures.Any(s => s.Cells.Contains(p) || s.Openings.Contains(p) || s.Anchor == p);

    /// <summary>
    /// Toxic: the exit moves to the low room farthest (by hops) from the start, so the party crosses the gas to reach it,
    /// and when that room can be sealed its door becomes a lever gate whose lever stands on the high ground.
    /// </summary>
    private static void PlaceToxic(Random rng, FloorLayout layout, ObjectiveLayout objective)
    {
        var rooms = layout.Rooms;
        var upRoom = layout.RoomAt(layout.UpStairs);
        var hops = RoomHops(layout, upRoom);
        bool Usable(Room r) => r != upRoom && r != layout.BossArena && layout.Vaults.All(v => v.Room != r) && StairsCell(layout, r) != null;
        var low = rooms.Where(r => Usable(r) && layout.LevelOf(r.Center) == 0).ToList();
        var pool = low.Count > 0 ? low : rooms.Where(Usable).ToList();
        if (pool.Count == 0) return;
        var exit = pool.OrderByDescending(r => hops[rooms.IndexOf(r)]).ThenByDescending(r => Dist2(r.Center, layout.UpStairs)).ThenBy(rooms.IndexOf).First();
        layout.DownStairs = StairsCell(layout, exit)!.Value;

        if (!IsSealable(layout, exit, out var door)) return;
        // the lever: on the high ground when there is any, as far from the exit as the floor allows
        var hosts = rooms.Where(r => r != exit && r != layout.BossArena && layout.Vaults.All(v => v.Room != r) && layout.LevelOf(r.Center) == 1).ToList();
        if (hosts.Count == 0) hosts = rooms.Where(r => r != exit && layout.Vaults.All(v => v.Room != r)).ToList();
        if (hosts.Count == 0) return;
        var host = hosts.OrderByDescending(r => hops[rooms.IndexOf(r)]).ThenBy(rooms.IndexOf).First();
        var lever = RandomCell(rng, host, layout);
        var index = layout.Vaults.Count;
        layout[door] = Cell.Door;
        layout.Vaults.Add(new Vault(index, exit, door, VaultKind.LeverGate, lever, null, Revealed: true));
        objective.ExitGateVault = index;
    }

    /// <summary>The room's centre, or the nearest plain floor cell to it that nothing else has claimed; null when the room has none.</summary>
    private static GridPos? StairsCell(FloorLayout layout, Room room)
    {
        var level = layout.LevelOf(room.Center);
        bool Ok(GridPos p) => layout[p] == Cell.Floor && layout.LevelOf(p) == level && !layout.IsUnder(p) && p != layout.UpStairs
            && !layout.Ledges.Any(l => l.Cells.Contains(p) || l.RampBottom == p || l.RampTop == p)
            && !layout.IsReserved(p) && !layout.ChestSpawns.Contains(p) && layout.Vaults.All(v => v.Key != p && v.Lever != p && v.Door != p);
        if (Ok(room.Center)) return room.Center;
        return room.Cells().Where(Ok).OrderBy(p => Math.Abs(p.X - room.Center.X) + Math.Abs(p.Y - room.Center.Y)).Cast<GridPos?>().FirstOrDefault();
    }

    /// <summary>After nests and before spawns: what needs the breaches known and the floor still empty.</summary>
    private static void PlaceObjectiveMid(FloorSpec spec, Random rng, FloorLayout layout)
    {
        if (layout.Objective is not { } objective) return;
        switch (objective.Kind)
        {
            case FloorObjectiveKind.Ritual: ConfirmShrine(layout, objective); break;
            case FloorObjectiveKind.Jailbreak: PlaceJailPrisoners(rng, layout, objective); break;
            default: break;
        }
    }

    /// <summary>
    /// The shrine picked early may have taken no breach (its walls had no rock behind them) or lost its centre to
    /// a pit: settle on a room that has a nest, an open reachable middle and nothing built in it, or give the objective up.
    /// </summary>
    private static void ConfirmShrine(FloorLayout layout, ObjectiveLayout objective)
    {
        var rooms = layout.Rooms;
        var upRoom = layout.RoomAt(layout.UpStairs);
        var downRoom = layout.RoomAt(layout.DownStairs);
        var reach = layout.Reachable(layout.UpStairs);
        var hops = RoomHops(layout, upRoom);
        GridPos? AltarFor(Room r)
        {
            bool Ok(GridPos p) => r.Contains(p) && layout[p] == Cell.Floor && layout.LevelOf(p) == layout.LevelOf(r.Center) && !layout.IsUnder(p) && !layout.IsWater(p)
                && !layout.Rubble.Contains(p) && !layout.Fires.Contains(p) && !layout.IsNestMouth(p) && reach.Contains(p) && p != layout.UpStairs && p != layout.DownStairs
                && !layout.ChestSpawns.Contains(p) && layout.Vaults.All(v => v.Key != p && v.Lever != p) && !layout.Ledges.Any(l => l.Cells.Contains(p) || l.RampBottom == p || l.RampTop == p)
                && p.X > r.X && p.X < r.Right && p.Y > r.Y && p.Y < r.Bottom;
            if (Ok(r.Center)) return r.Center;
            return r.Cells().Where(Ok).OrderBy(p => p.ManhattanDistance(r.Center)).Cast<GridPos?>().FirstOrDefault();
        }
        bool Good(Room r) => r != upRoom && r != downRoom && r != layout.BossArena && layout.Vaults.All(v => v.Room != r) && layout.NestAt(r) != null
            && layout.StructureAt(r) == null && layout.Water.All(w => w.Room != r) && layout.Cellars.All(c => c.Room != r);
        if (objective.ShrineRoom is { } chosen && Good(chosen) && AltarFor(chosen) is { } keep)
        {
            if (objective.Altar is { } old) objective.Reserved.Remove(old);
            objective.Altar = keep;
            objective.Reserved.Add(keep);
            return;
        }
        int Score(Room r) { var h = hops[rooms.IndexOf(r)]; return h is >= 2 and <= 4 ? 0 : h < 2 ? 2 : 1; }
        foreach (var r in rooms.Where(Good).OrderBy(Score).ThenByDescending(r => layout.NestAt(r)!.Mouths.Count).ThenByDescending(r => r.W * r.H).ThenBy(rooms.IndexOf))
        {
            if (AltarFor(r) is not { } altar) continue;
            if (objective.Altar is { } old) objective.Reserved.Remove(old);
            objective.ShrineRoom = r;
            objective.Altar = altar;
            objective.Reserved.Add(altar);
            return;
        }
        layout.Objective = null; // no shrine to hold: an ordinary floor after all
    }

    /// <summary>After the spawns: the puzzle (proven against the finished floor), spare keys and anything else that lives among the loot.</summary>
    private static void PlaceObjectiveLate(FloorSpec spec, Random rng, FloorLayout layout)
    {
        if (layout.Objective is not { } objective) return;
        switch (objective.Kind)
        {
            case FloorObjectiveKind.ShiftingVault: PlaceShiftingVaultPuzzle(rng, layout, objective); break;
            case FloorObjectiveKind.Ritual: PlaceRitualLevers(rng, layout, objective); break;
            case FloorObjectiveKind.Jailbreak: PlaceJailKeys(rng, layout, objective); break;
            default: break;
        }
    }

    /// <summary>Rooms hops from the start room over the room graph (-1 = unreachable).</summary>
    internal static int[] RoomHops(FloorLayout layout, Room? from)
    {
        var rooms = layout.Rooms;
        var hops = Enumerable.Repeat(-1, rooms.Count).ToArray();
        var start = from == null ? -1 : rooms.IndexOf(from);
        if (start < 0) return hops;
        var neighbours = Enumerable.Range(0, rooms.Count).Select(_ => new List<int>()).ToArray();
        foreach (var (a, b) in layout.Connections)
        {
            if (a < 0 || b < 0 || a >= rooms.Count || b >= rooms.Count || a == b) continue;
            neighbours[a].Add(b); neighbours[b].Add(a);
        }
        var queue = new Queue<int>();
        hops[start] = 0; queue.Enqueue(start);
        while (queue.Count > 0)
        {
            var r = queue.Dequeue();
            foreach (var n in neighbours[r]) if (hops[n] < 0) { hops[n] = hops[r] + 1; queue.Enqueue(n); }
        }
        return hops;
    }

    /// <summary>A room with one entrance whose sealing keeps every other room and both stairs reachable: the vault test.</summary>
    internal static bool IsSealable(FloorLayout layout, Room room, out GridPos door)
    {
        door = default;
        var entrances = Entrances(layout, room);
        if (entrances.Count != 1) return false;
        door = entrances[0];
        if (layout.Ramps.ContainsKey(door) || layout.LevelOf(door) != layout.LevelOf(room.Center)) return false; // a door on a ramp would break the ramp
        var d = door;
        var reach = layout.Reachable(layout.UpStairs, p => p == d);
        if (!room.Contains(layout.DownStairs) && !reach.Contains(layout.DownStairs)) return false;
        return layout.Rooms.All(r => r == room || reach.Contains(r.Center));
    }
}
