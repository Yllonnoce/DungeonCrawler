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
        foreach (var n in root.GetChildren().OfType<Npc>())
        {
            var cell = DungeonBuilder.WorldToCell(n.GlobalPosition);
            GD.Print($"[dump] npc {n.Name} \"{n.NpcName}\" cell {cell} walkable={layout.IsWalkable(cell)} jailed={n.Jailed} captured={n.Captured} target={(n.MoveTarget is { } t ? DungeonBuilder.WorldToCell(t).ToString() : "-")} room={(layout.RoomAt(cell) is { } r ? layout.Rooms.IndexOf(r) : -1)}");
        }
    }
}
