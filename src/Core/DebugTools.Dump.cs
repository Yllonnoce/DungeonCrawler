using DungeonCrawler.Adventure;
using DungeonCrawler.Dungeon;
using DungeonCrawler.Dungeon.Generation;
using DungeonCrawler.Dungeon.Props;
using DungeonCrawler.Enemies;
using Godot;

namespace DungeonCrawler.Core;

/// <summary>--dump-enemies=T1,T2,...: at each time, print every creature's cell, state, facing and post, and every NPC's place, for anomaly hunts.</summary>
public partial class DebugTools
{
    private readonly List<float> _dumpAt = new();

    private void TickDumpEnemies()
    {
        if (_dumpAt.Count == 0 || Main.Instance?.InGame != true || Main.Instance.Builder.Layout is not { } layout || Main.Instance.Builder.FloorRoot is not { } root || _elapsed < _dumpAt[0]) return;
        _dumpAt.RemoveAt(0);
        var b = Main.Instance.Builder;
        GD.Print($"[dump] t={_elapsed:0.0} floor {b.CurrentFloor} objective={layout.Objective?.Kind}");
        if (layout.Objective is { JailRoom: { } jail } obj)
        {
            var door = root.GetChildren().OfType<Door>().FirstOrDefault(d => d.VaultIndex == obj.JailVault);
            var step = door != null ? VaultPuzzles.Doorstep(layout, door.Cell, jail) : null;
            GD.Print($"[dump] jail room {layout.Rooms.IndexOf(jail)} x{jail.X} y{jail.Y} w{jail.W} h{jail.H}; door {(door != null ? door.Cell.ToString() : "none")} open={door?.IsOpen}; doorstep {step}; prisoner cells {string.Join(" ", obj.PrisonerCells)}");
        }
        foreach (var e in root.GetChildren().OfType<Enemy>().Where(e => e.IsAlive))
        {
            var cell = DungeonBuilder.WorldToCell(e.GlobalPosition);
            var fwd = -e.GlobalTransform.Basis.Z; fwd.Y = 0;
            var ahead = DungeonBuilder.WorldToCell(e.GlobalPosition + fwd.Normalized() * 1.0f);
            var home = DungeonBuilder.WorldToCell(e.HomePosition);
            GD.Print($"[dump] enemy {e.Name} {e.Profile.Id} cell {cell} walkable={layout.IsWalkable(cell)} state={e.State.GetType().Name} unaware={e.IsUnaware} facing=({fwd.X:0.00},{fwd.Z:0.00}) ahead {ahead} aheadWalkable={layout.IsWalkable(ahead)} home {home} group={e.Group?.Id ?? "-"} captive={(e.Captive != null)} room={(layout.RoomAt(cell) is { } r ? layout.Rooms.IndexOf(r) : -1)}");
        }
        foreach (var e in root.GetChildren().OfType<Enemy>().Where(e => e.IsAlive && e.State is Enemies.Ai.PatrolState && e.Group is { Route.Count: >= 2 } g))
        {
            var g = e.Group!; var wp = g.Route[e.PatrolIndex % g.Route.Count]; var wpCell = DungeonBuilder.WorldToCell(wp); var me = DungeonBuilder.WorldToCell(e.GlobalPosition);
            var path = GridPathfinder.FindPath(layout, me, wpCell, Adventure.Objectives.JailbreakRules.BlockedFor(e.CarriesJailKey, b.JailDoorCell, b.IsCellBlockedForEnemies), passable: b.IsSecretOpen);
            GD.Print($"[dump] patrol {e.Name} stop {e.PatrolIndex % g.Route.Count}/{g.Route.Count} at {wpCell} (walkable={layout.IsWalkable(wpCell)} blocked={b.IsCellBlockedForEnemies(wpCell)} room={(layout.RoomAt(wpCell) is { } rr ? layout.Rooms.IndexOf(rr) : -1)} level={layout.LevelOf(wpCell)} myLevel={layout.LevelOf(me)}) dist={e.GlobalPosition.DistanceTo(wp):0.0} path={(path == null ? "NONE" : path.Count + " cells")}; route cells {string.Join(" ", g.Route.Select(r => DungeonBuilder.WorldToCell(r)))}");
            var plain = GridPathfinder.FindPath(layout, me, wpCell, null, passable: c => layout[c] == Cell.Secret || b.IsSecretOpen(c));
            if (plain != null)
            {
                var closed = plain.Where(c => b.IsCellBlockedForEnemies(c) || (!layout.IsWalkable(c))).Select(c => $"{c}:{(b.IsCellBlocked(c) ? "door" : b.IsCellBlockedForEnemies(c) ? "enemy-only" : "secret")}{(layout.RoomAt(c) is { } cr ? " room" + layout.Rooms.IndexOf(cr) : "")}");
                GD.Print($"[dump] patrol {e.Name} path ignoring doors/secrets: {plain.Count} cells; closed to creatures on it: {string.Join(" ", closed)}; safe room={layout.SafeRoom} door {layout.SafeRoomDoor.ToString()}");
                foreach (var c in plain.Where(c => b.IsCellBlocked(c)))
                {
                    var what = root.GetChildren().Select(n => n switch { Door d when d.Cell == c => $"Door#{d.VaultIndex} {d.Kind} open={d.IsOpen}", Chest ch when DungeonBuilder.WorldToCell(ch.GlobalPosition) == c => $"Chest#{ch.SpawnIndex} {ch.TableId}", Node3D n3 when n3 is not Enemy && DungeonBuilder.WorldToCell(n3.GlobalPosition) == c => n3.Name.ToString(), _ => null }).Where(x => x != null).ToList();
                    GD.Print($"[dump] blocked cell {c}: {string.Join(", ", what)}; layout cell={layout[c]}");
                }
            }
            else GD.Print($"[dump] patrol {e.Name} no path even ignoring doors and secrets: the stop is on another layer or an island");
            GD.Print($"[dump] vaults: {string.Join("; ", layout.Vaults.Select(v => $"#{v.Index} {v.Kind} door {v.Door} room x{v.Room.X} y{v.Room.Y} w{v.Room.W} h{v.Room.H} (layout room {(layout.RoomAt(new GridPos(v.Room.X, v.Room.Y)) is { } vr ? layout.Rooms.IndexOf(vr) : -1)})"))}");
            for (var y = me.Y - 4; y <= me.Y + 4; y++)
            {
                var row = "";
                for (var x = me.X - 7; x <= me.X + 7; x++) { var c = new GridPos(x, y); row += c == me ? "E" : c == wpCell ? "W" : !layout.IsWalkable(c) ? "#" : b.IsCellBlockedForEnemies(c) ? "x" : layout.LevelOf(c) != layout.LevelOf(me) ? "^" : "."; }
                GD.Print($"[dump] map y{y,3}: {row}");
            }
            break; // one probe is enough: the group shares the stop
        }
        foreach (var n in root.GetChildren().OfType<Npc>())
        {
            var cell = DungeonBuilder.WorldToCell(n.GlobalPosition);
            GD.Print($"[dump] npc {n.Name} \"{n.NpcName}\" cell {cell} walkable={layout.IsWalkable(cell)} jailed={n.Jailed} captured={n.Captured} target={(n.MoveTarget is { } t ? DungeonBuilder.WorldToCell(t).ToString() : "-")} room={(layout.RoomAt(cell) is { } r ? layout.Rooms.IndexOf(r) : -1)}");
        }
    }
}
