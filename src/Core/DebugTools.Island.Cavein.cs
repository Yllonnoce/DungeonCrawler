using DungeonCrawler.Dungeon;
using DungeonCrawler.Dungeon.Generation;
using DungeonCrawler.Party;
using Godot;

namespace DungeonCrawler.Core;

// ---- --test-island=cavein (plan 15, Task 3; the default) ------------------------------------------------------------------------------
// At stop 0, on foot, after the build (DebugTools.Island.cs). The west hall: the lead, walked from room 1 toward the down stairs (46,54)
// (ForcedMoveInput), never passes world x 152.0 (the cave-in's face) in 8 s, coming within 2 m of it; a companion sent for the down
// stairs (SeekSpot) has no path there (GridPathfinder) and stays out of room 9. The north hall: the same from the fork (46,45) toward
// (46,50), the face at world z 184.0. Into the lava (pre-flight F10: both walkers are companions, the lead stays up): one companion put in
// room 9 at (42,54) and walked west at walking speed falls into the trench within 2 s; another put on the party lane's cell 310 and walked
// along it onto 311, the exit bridge's first cell, falls within 2 s. Both come back (up, on cells that may take a body) when the lead,
// walked along the lane from outside room 1, steps onto stop 0's platform (plan 10's return).
public partial class DebugTools
{
    /// <summary>A walker's farthest reach toward its cave-in face (world x or z), and the companion sent for the stairs.</summary>
    private float _isReach;
    private Adventurer? _isSeeker, _isLavaA, _isLavaB;
    private bool _isSeekerInRoom9;
    private readonly List<Vector3> _isWay = new();
    private int _isWayAt;

    private void IslandCavein(Main main, FloorLayout layout, RailIsland island, PartyController party, Adventurer me, Action<string> Fail, Action<string> Pass)
    {
        var builder = main.Builder;
        var rail = layout.Rail!;
        var companions = party.Active.Where(a => a.Control == ControlKind.Companion).ToList();
        var exitRoom = rail.Stops[^1].Room;
        if (companions.Count < 2) { Fail($"{companions.Count} companion(s): the flow wants two at least"); return; }
        if (party.Active.FirstOrDefault(a => a.State.Fallen && a != _isLavaA && a != _isLavaB) is { } stray) { Fail($"{stray.State.Name} fell at step {_isStep} at {DungeonBuilder.WorldToCell(stray.GlobalPosition)}: nobody falls but the lava's two walkers"); return; }
        if (me.State.Fallen || me.Downed) { Fail($"the lead is fallen={me.State.Fallen} downed={me.Downed} at step {_isStep}"); return; }
        var dt = (float)GetProcessDeltaTime();

        switch (_isStep)
        {
            case 0: // the west hall, from room 1
            case 2: // the north hall, from the fork
            {
                var west = _isStep == 0;
                var start = west ? new GridPos(36, 54) : new GridPos(46, 45);
                var toward = west ? layout.DownStairs : new GridPos(46, 50);
                if (!layout.IsWalkable(start)) { Fail($"the walk's start {start} is no floor"); return; }
                me.Teleport(builder.Surface(start) + Vector3.Up * 0.1f, builder.Surface(toward));
                _isSeeker = companions[0];
                var beside = start.Neighbours4().Where(n => layout.IsWalkable(n) && !layout.IsRail(n) && !island.CaveIns.Contains(n) && n.X <= start.X && n.Y <= start.Y).DefaultIfEmpty(start).First();
                _isSeeker.Stance = Characters.Stance.Hold;
                _isSeeker.Teleport(builder.Surface(beside) + Vector3.Up * 0.1f);
                _isSeeker.SeekSpot = builder.Surface(layout.DownStairs);
                var path = GridPathfinder.FindPath(layout, beside, layout.DownStairs, maxExpansions: 20000);
                GD.Print($"[island] {(west ? "west" : "north")} hall: the lead walks from {start} toward {toward}; {_isSeeker.State.Name} at {beside} is sent for the down stairs {layout.DownStairs}: a path {(path == null ? "none" : $"of {path.Count} cells")}");
                if (path != null) { Fail($"a path from {beside} to the down stairs: {string.Join(" ", path)}"); return; }
                _isReach = float.NegativeInfinity;
                _isSeekerInRoom9 = false;
                _isStep++; _isClock = 0f;
                break;
            }
            case 1:
            case 3:
            {
                var west = _isStep == 1;
                var face = west ? 152.0f : 184.0f;
                var toward = west ? layout.DownStairs : new GridPos(46, 50);
                me.FaceToward(builder.Surface(toward));
                party.ForcedMoveInput = new Vector2(0f, -0.5f); // forward, a walk
                _isReach = Mathf.Max(_isReach, west ? me.GlobalPosition.X : me.GlobalPosition.Z);
                if (_isSeeker is { } s && InRoom(layout, exitRoom, s.GlobalPosition)) _isSeekerInRoom9 = true;
                if (_isReach >= face) { Fail($"the lead passed the {(west ? "west" : "north")} cave-in's face {face:0.0} (at {_isReach:0.00})"); return; }
                if (_isSeekerInRoom9) { Fail($"{_isSeeker!.State.Name}, sent for the down stairs, came into room {exitRoom}"); return; }
                if (_isClock < 8f) break;
                party.ForcedMoveInput = null;
                _isSeeker!.SeekSpot = null;
                GD.Print($"[island] {(west ? "west" : "north")} hall: in 8 s the lead came to {_isReach:0.00} (the face at {face:0.0}), never past it; {_isSeeker.State.Name} stayed out of room {exitRoom} at {DungeonBuilder.WorldToCell(_isSeeker.GlobalPosition)}, fallen={_isSeeker.State.Fallen}");
                if (_isReach < face - 2f) { Fail($"the lead never came within 2 m of the {(west ? "west" : "north")} face (reached {_isReach:0.00}): the control"); return; }
                if (_isSeeker.State.Fallen) { Fail($"{_isSeeker.State.Name} fell going for the stairs"); return; }
                _isStep++; _isClock = 0f;
                break;
            }
            case 4: // into the lava: a companion put in room 9 and walked west into the trench
            case 6: // and one walked along the lane from cell 310 onto the bridge's first cell 311
            {
                var trench = _isStep == 4;
                var walker = trench ? companions[1] : companions.Count > 2 ? companions[2] : companions[0];
                var from = trench ? new GridPos(42, 54) : rail.Party[island.Bridge.From - 1];
                var onto = trench ? new GridPos(41, 54) : rail.Party[island.Bridge.From];
                if (trench && !layout.Rooms[exitRoom].Contains(from)) { Fail($"{from} is not in room {exitRoom}"); return; }
                if (!CrevasseRules.SetDownOk(layout, from) || !layout.IsChasm(onto)) { Fail($"the walk {from} -> {onto}: the start may not take a body or the end is no drop"); return; }
                walker.Stance = Characters.Stance.Hold;
                walker.Teleport(builder.Surface(from) + Vector3.Up * 0.05f, builder.Surface(onto));
                if (trench) _isLavaA = walker; else _isLavaB = walker;
                GD.Print($"[island] into the lava: {walker.State.Name} put at {from}{(trench ? $" in room {exitRoom}" : $" (party lane cell {island.Bridge.From - 1})")}, walked toward {onto}");
                _isStep++; _isClock = 0f;
                break;
            }
            case 5:
            case 7:
            {
                var walker = (_isStep == 5 ? _isLavaA : _isLavaB)!;
                var onto = _isStep == 5 ? new GridPos(41, 54) : rail.Party[island.Bridge.From];
                if (walker.State.Fallen)
                {
                    GD.Print($"[island] into the lava: {walker.State.Name} fell into {(_isStep == 5 ? "the trench" : "the lava under the exit bridge")} at {onto} after {_isClock:0.00} s");
                    _isStep++; _isClock = 0f;
                    break;
                }
                if (_isClock > 2f) { Fail($"{walker.State.Name} walked toward {onto} did not fall within 2 s (at {DungeonBuilder.WorldToCell(walker.GlobalPosition)}, y {walker.GlobalPosition.Y:0.00})"); return; }
                // a walk at walking speed, step by step (a companion's own steering keeps it from any lip: R7), until it drops
                // until its middle is a metre over the drop: from there the fall is its own
                var way = builder.Surface(onto) - walker.GlobalPosition;
                way.Y = 0f;
                if (way.Length() > DungeonBuilder.CellSize / 2f - 1f && walker.GlobalPosition.Y > builder.Surface(onto).Y - 0.3f)
                    walker.Teleport(walker.GlobalPosition + way.Normalized() * Adventurer.WalkSpeed * dt, builder.Surface(onto));
                break;
            }
            case 8: // the lead steps onto stop 0's platform from the lane outside room 1
            {
                var platform = rail.Stops[0].Platform[0];
                var room = rail.Stops[0].Room;
                var at = rail.Party.ToList().IndexOf(platform);
                var j = at;
                while (j + 1 < rail.Party.Count && layout.Rooms[room].Contains(rail.Party[j])) j++;
                if (at < 0 || layout.Rooms[room].Contains(rail.Party[j])) { Fail($"no lane cell outside room {room} past stop 0's platform {platform}"); return; }
                _isWay.Clear();
                for (var k = j; k >= at; k--) _isWay.Add(builder.Surface(rail.Party[k]));
                _isWayAt = 0;
                me.Teleport(builder.Surface(rail.Party[j]) + Vector3.Up * 0.1f, _isWay[Math.Min(1, _isWay.Count - 1)]);
                GD.Print($"[island] the lead walks along the lane from {rail.Party[j]} (outside room {room}) onto stop 0's platform {platform}");
                _isStep++; _isClock = 0f;
                break;
            }
            case 9:
            {
                var a = _isLavaA!;
                var b = _isLavaB!;
                if (!a.State.Fallen && !b.State.Fallen)
                {
                    party.ForcedMoveInput = null;
                    var cells = $"{a.State.Name} at {DungeonBuilder.WorldToCell(a.GlobalPosition)}, {b.State.Name} at {DungeonBuilder.WorldToCell(b.GlobalPosition)}";
                    GD.Print($"[island] both lava walkers are back as the lead steps onto stop 0's platform ({cells}), {_isClock:0.0} s into the walk");
                    if (!StandsOk(layout, a) || !StandsOk(layout, b)) { Fail($"a lava walker came back on a cell that may take no body ({cells})"); return; }
                    if (!InRoom(layout, rail.Stops[0].Room, me.GlobalPosition)) { Fail($"the walkers came back with the lead at {DungeonBuilder.WorldToCell(me.GlobalPosition)}, off stop 0's platform"); return; }
                    Pass($"rooms 9+12 sealed by two trenches and six cave-in cells (four dressed faces), the exit bridge's 4 cells and the trenches and moat with no footing, the pit's twin solid; neither cave-in passed in 8 s, no path for a companion to the stairs; two companions walked into the lava fell within 2 s and came back at the lead's step onto stop 0's platform");
                    return;
                }
                if (_isWayAt < _isWay.Count)
                {
                    var p = _isWay[_isWayAt];
                    if (new Vector2(p.X - me.GlobalPosition.X, p.Z - me.GlobalPosition.Z).Length() < 0.6f) _isWayAt++;
                    if (_isWayAt < _isWay.Count) { me.FaceToward(_isWay[_isWayAt]); party.ForcedMoveInput = new Vector2(0f, -0.5f); }
                    else party.ForcedMoveInput = null;
                }
                if (_isClock > 20f) Fail($"the lava walkers were not back 20 s into the lead's walk onto stop 0's platform (the lead at {DungeonBuilder.WorldToCell(me.GlobalPosition)}, way point {_isWayAt} of {_isWay.Count}; fallen {a.State.Fallen}/{b.State.Fallen})");
                break;
            }
        }
    }
}
