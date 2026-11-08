using DungeonCrawler.Adventure;
using DungeonCrawler.Adventure.Objectives;
using DungeonCrawler.Dungeon;
using DungeonCrawler.Dungeon.Props;
using DungeonCrawler.Enemies;
using Godot;

namespace DungeonCrawler.Core;

/// <summary>
/// --test-jailer (with --builtin --floor=2 --no-ai --no-ambush --no-portals --no-gathering --lethality=1): a creature is set beside a wandering
/// prisoner; it must seize them, unlock the jail door, march them in, and take its post outside with the door locked again. Before the fix a
/// jailer stood in the corridor with its face in the wall: the closed key door had no path through it for anyone.
/// </summary>
public partial class DebugTools
{
    private bool _testJailer;
    private int _jailerStep;
    private float _jailerClock;
    private Npc? _jailerPrisoner;
    private Enemy? _jailerEnemy;
    private bool _jailerDoorOpened;

    private void TickJailerTest()
    {
        if (!_testJailer || Main.Instance is not { InGame: true } main || main.Builder.Layout is not { } layout || main.Builder.FloorRoot is not { } root) return;
        if (ObjectiveController.Instance is not JailbreakController jail || layout.Objective is not { JailRoom: { } jailRoom } objective) { if (_elapsed > 8) JailerFail("floor 2 is not a jailbreak floor"); return; }
        _jailerClock += (float)GetProcessDeltaTime();
        var door = root.GetChildren().OfType<Door>().FirstOrDefault(d => d.VaultIndex == objective.JailVault);
        switch (_jailerStep)
        {
            case 0 when _elapsed >= 3.0:
            {
                _jailerStep++;
                _jailerPrisoner = jail.Prisoners.FirstOrDefault(p => !p.Jailed && !p.Captured && !p.Dead);
                _jailerEnemy = root.GetChildren().OfType<Enemy>().Where(e => e.IsAlive && !e.IsBoss && e.Captive == null).OrderBy(e => _jailerPrisoner != null ? e.GlobalPosition.DistanceTo(_jailerPrisoner.GlobalPosition) : 0).FirstOrDefault();
                if (_jailerPrisoner == null || _jailerEnemy == null || door == null) { JailerFail($"no wandering prisoner ({_jailerPrisoner?.NpcName}), creature ({_jailerEnemy?.Name}) or jail door"); return; }
                _jailerEnemy.GlobalPosition = _jailerPrisoner.GlobalPosition + new Vector3(1.0f, 0, 0);
                GD.Print($"[jailer] {_jailerEnemy.DisplayName} set beside {_jailerPrisoner.NpcName} at {DungeonBuilder.WorldToCell(_jailerPrisoner.GlobalPosition)}; jail room {layout.Rooms.IndexOf(jailRoom)} door {door.Cell} open={door.IsOpen}");
                for (var y = door.Cell.Y - 5; y <= door.Cell.Y + 4; y++)
                {
                    var row = "";
                    for (var x = door.Cell.X - 6; x <= door.Cell.X + 7; x++) { var c = new Dungeon.Generation.GridPos(x, y); row += c == door.Cell ? "D" : !layout.IsWalkable(c) ? "#" : main.Builder.IsCellBlockedForEnemies(c) ? "x" : layout.RoomAt(c) == jailRoom ? "j" : "."; }
                    GD.Print($"[jailer] map y{y,3}: {row}");
                }
                _jailerClock = 0f;
                break;
            }
            case 1 when _jailerPrisoner is { Captured: true }:
                _jailerStep++; _jailerClock = 0f;
                GD.Print($"[jailer] captured after {_elapsed:0.0} s by {_jailerEnemy?.DisplayName} ({_jailerEnemy?.State.Name})");
                break;
            case 1 when _jailerClock > 15f:
                JailerFail($"never captured: creature {_jailerEnemy?.State.Name} at {(_jailerEnemy != null ? DungeonBuilder.WorldToCell(_jailerEnemy.GlobalPosition).ToString() : "-")}, prisoner captured={_jailerPrisoner?.Captured}");
                break;
            case 2:
            {
                if (door is { IsOpen: true }) _jailerDoorOpened = true;
                var npc = _jailerPrisoner!; var e = _jailerEnemy!;
                var inJail = layout.RoomAt(DungeonBuilder.WorldToCell(npc.GlobalPosition)) == jailRoom;
                if ((int)_jailerClock % 5 == 0 && _jailerClock - Math.Floor(_jailerClock) < 0.02)
                    GD.Print($"[jailer] t+{_jailerClock:0} jailer {e.State.Name} at {DungeonBuilder.WorldToCell(e.GlobalPosition)} pos=({e.GlobalPosition.X:0.0},{e.GlobalPosition.Z:0.0}) doorDist={new Vector2(e.GlobalPosition.X - door!.GlobalPosition.X, e.GlobalPosition.Z - door.GlobalPosition.Z).Length():0.0} captive={e.Captive != null} order={e.Order} post={DungeonBuilder.WorldToCell(e.PostPosition)} home={DungeonBuilder.WorldToCell(e.HomePosition)} group={e.Group?.Id}; prisoner at {DungeonBuilder.WorldToCell(npc.GlobalPosition)} jailed={npc.Jailed} inJail={inJail}; door open={door?.IsOpen} openedOnce={_jailerDoorOpened}");
                if (npc.Jailed && inJail && door is { IsOpen: false } && e.IsAlive && e.State is not Enemies.Ai.EscortCaptiveState)
                {
                    var post = DungeonBuilder.WorldToCell(e.GlobalPosition);
                    var outside = layout.RoomAt(post) != jailRoom && post != door!.Cell;
                    GD.Print($"[jailer] delivered after {_jailerClock:0.0} s: prisoner jailed in room {layout.Rooms.IndexOf(jailRoom)}, door unlocked once={_jailerDoorOpened} and locked again, jailer {e.State.Name} at {post} outside={outside}");
                    if (!_jailerDoorOpened) { JailerFail("the prisoner got in without the door ever opening"); return; }
                    if (!outside) { JailerFail("the jailer is inside the jail or in its doorway"); return; }
                    GD.Print("[jailer] RESULT ok"); _done = true; GetTree().Quit(0); return;
                }
                if (e.State is Enemies.Ai.ReturnState && (int)_jailerClock % 10 == 0 && _jailerClock - Math.Floor(_jailerClock) < 0.02)
                {
                    var from = DungeonBuilder.WorldToCell(e.GlobalPosition); var to = DungeonBuilder.WorldToCell(e.PostPosition);
                    var blocked = JailbreakRules.BlockedFor(e.CarriesJailKey, main.Builder.JailDoorCell, main.Builder.IsCellBlockedForEnemies);
                    var path = Dungeon.Generation.GridPathfinder.FindPath(layout, from, to, blocked, passable: main.Builder.IsSecretOpen);
                    var pathHome = Dungeon.Generation.GridPathfinder.FindPath(layout, from, DungeonBuilder.WorldToCell(e.HomePosition), blocked, passable: main.Builder.IsSecretOpen);
                    GD.Print($"[jailer] path {from}->{to}: {(path == null ? "NONE" : path.Count + " cells")}; to home {DungeonBuilder.WorldToCell(e.HomePosition)}: {(pathHome == null ? "NONE" : pathHome.Count + " cells")}; door {door!.Cell} walkable={layout.IsWalkable(door.Cell)} blocked={main.Builder.IsCellBlockedForEnemies(door.Cell)} secretOpen={main.Builder.IsSecretOpen(door.Cell)} open={door.IsOpen}; key={e.CarriesJailKey}");
                }
                if (_jailerClock > 130f)
                {
                    var fwd = -e.GlobalTransform.Basis.Z; fwd.Y = 0;
                    var ahead = DungeonBuilder.WorldToCell(e.GlobalPosition + fwd.Normalized());
                    JailerFail($"not delivered in 130 s: jailer {e.State.Name} at {DungeonBuilder.WorldToCell(e.GlobalPosition)} facing a {(layout.IsWalkable(ahead) ? "walkable" : "WALL")} cell, prisoner at {DungeonBuilder.WorldToCell(npc.GlobalPosition)} jailed={npc.Jailed} captured={npc.Captured}, door open={door?.IsOpen} openedOnce={_jailerDoorOpened}");
                }
                break;
            }
        }
    }

    private void JailerFail(string why) { GD.Print($"[jailer] RESULT FAIL: {why}"); _done = true; GetTree().Quit(1); }
}
