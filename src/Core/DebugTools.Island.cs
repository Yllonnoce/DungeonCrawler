using DungeonCrawler.Adventure.Objectives;
using DungeonCrawler.Dungeon;
using DungeonCrawler.Dungeon.Generation;
using DungeonCrawler.Dungeon.Rails;
using DungeonCrawler.Party;
using Godot;

namespace DungeonCrawler.Core;

// ---- --test-island[=cavein|bridge|wreck|parked] (plan 15, Tasks 3 and 5) ---------------------------------------------------------------------------
// The built-in campaign's floor 12, the exit island's floor, at lethality 3, solo, gathering on; every enemy is culled as it appears, ride
// 4's runaway held. Every stop, ride and piece id reads Shape(rc) (B.10: every check reads LineShape). Prints [island] lines and
// "[island] RESULT ok ({variant}): ..." or "RESULT FAIL ({variant}): ..."; plan 13's stall watchdog fails a flow 10 s short of its budget.
// =cavein (the default; DebugTools.Island.Cavein.cs): at stop 0, on foot: the island's build, the two cave-ins never passed, a companion
// sent for the down stairs finding no way, and two companions walked into the lava, back when the lead steps onto stop 0's platform.
// =bridge and =wreck (DebugTools.Island.Bridge.cs): ride 5 ridden from its stop: no Get out and no move car to car over the exit bridge,
// braked on it too, a save there; and a wreck past its start that sets everyone down inside, spends no spare and takes the last stop.
// =parked (DebugTools.Island.Parked.cs, Task 5): ride 5's parked train in the way, called, smashed through and standing again as a lift.
public partial class DebugTools
{
    private bool _testIsland;
    private string _isVariant = "cavein";
    private int _isStep;
    private float _isClock, _isTime;
    private bool _isBuilt;

    private void RunIslandTest()
    {
        var main = Main.Instance;
        if (main?.Builder.Layout is not { } layout || main.Builder.FloorRoot is not { } root || !main.InGame || Game.Instance.CurrentFloor != 12) return;
        var dt = (float)GetProcessDeltaTime();
        _isClock += dt;
        _isTime += dt;
        var party = main.Party;
        var v = _isVariant;
        void Fail(string why) { GD.Print($"[island] RESULT FAIL ({v}): {why}"); IslandRelease(party); _done = true; GetTree().Quit(1); }
        void Pass(string what) { GD.Print($"[island] RESULT ok ({v}): {what}"); IslandRelease(party); _done = true; GetTree().Quit(0); }
        if (main.Rebuilding) return; // a load builds the floor again: the variant waits for the new one
        if (RailController.Instance is not { } rc || RailEncounterController.Instance is not { } enc || layout.Rail?.Island is not { } island)
        {
            if (_isClock > 3f) Fail("floor 12 has no line or no exit island");
            return;
        }
        foreach (var e in root.GetChildren().OfType<Enemies.Enemy>())
            if (e.IsAlive) e.Kill(); // the flow owns the fights
        RideWaiveCages(main, rc, "island");
        HoldTheRunaway("island");
        enc.RunawayClock = 0f; // no traffic runaway on the party line
        if (party.Local is not { } me) { Fail("no human"); return; }
        if (!_isBuilt && _isClock >= 1.5f)
        {
            if (IslandBuild(main, layout, island, rc, enc) is { } bad) { Fail(bad); return; }
            _isBuilt = true;
            _isClock = 0f;
            return;
        }
        if (!_isBuilt) return;
        switch (v)
        {
            case "cavein": IslandCavein(main, layout, island, party, me, Fail, Pass); break;
            case "bridge": IslandBridge(main, layout, island, rc, enc, party, me, Fail, Pass); break;
            case "wreck": IslandWreck(main, layout, island, rc, enc, party, Fail, Pass); break;
            case "parked": IslandParked(main, layout, island, rc, enc, party, me, root, Fail, Pass); break;
            default: Fail($"no such variant {v}"); break;
        }
    }

    /// <summary>The flow lets go of everything it holds: the stick, the pumpers' cap, the companions' errands.</summary>
    private void IslandRelease(PartyController party)
    {
        party.ForcedMoveInput = null;
        PumperCap = null;
        foreach (var a in party.Adventurers) if (IsInstanceValid(a)) a.SeekSpot = null;
    }

    /// <summary>
    /// The build (every variant): rooms 9 and 12 the island, its two trenches, its six cave-in cells and their four dressed faces, the exit
    /// bridge's four cells and span, and the lava regions; a ray down at each trench, moat and bridge cell finds no footing, and one at the
    /// pit's enemy twin finds the floor. Null, or why not.
    /// </summary>
    private static string? IslandBuild(Main main, FloorLayout layout, RailIsland island, RailController rc, RailEncounterController enc)
    {
        var rail = layout.Rail!;
        var builder = main.Builder;
        string Cells(IEnumerable<GridPos> cells) => string.Join(", ", cells);
        var lava = ChasmDressing.Instance?.Lava ?? -1;
        GD.Print($"[island] floor 12: rooms {string.Join("+", island.Rooms)}, trenches [{Cells(island.Trenches)}], {island.CaveIns.Count} cave-in cells (faces {builder.CaveInFaces}), exit bridge S={enc.ExitFromS:0.0}..{enc.ExitToS:0.0} ({enc.ExitCells} cells), lava regions {lava}");
        if (!island.Rooms.SequenceEqual(new[] { 9, 12 })) return $"the island's rooms are [{string.Join(",", island.Rooms)}], not 9 and 12";
        if (!island.Trenches.SequenceEqual(new[] { new GridPos(46, 49), new GridPos(41, 54) })) return $"the trenches are [{Cells(island.Trenches)}]";
        if (island.CaveIns.Count != 6 || builder.CaveInFaces != 4) return $"{island.CaveIns.Count} cave-in cells with {builder.CaveInFaces} faces, not 6 with 4";
        if (enc.ExitCells != 4 || float.IsNaN(enc.ExitFromS) || !(enc.ExitToS > enc.ExitFromS)) return $"the exit bridge has {enc.ExitCells} cells over S={enc.ExitFromS:0.0}..{enc.ExitToS:0.0}";
        if (lava < 1) return "no lava region was dressed";
        if (rail.Pit is not { } pit) return "no pit on the line";
        var space = main.GetViewport().World3D.DirectSpaceState;
        float? FloorUnder(GridPos c)
        {
            var top = builder.Surface(c);
            var hit = space.IntersectRay(PhysicsRayQueryParameters3D.Create(top + Vector3.Up * 1f, top + Vector3.Down * (ExitBridgeRules.LavaDepth + 1f), 1));
            return hit.Count > 0 ? ((Vector3)hit["position"]).Y : null;
        }
        var drops = island.Trenches.Concat(island.Moat).Concat(Enumerable.Range(island.Bridge.From, island.Bridge.To - island.Bridge.From + 1).Select(i => rail.Party[i])).ToList();
        var footed = drops.Where(c => FloorUnder(c) != null).ToList();
        if (footed.Count > 0) return $"a ray down finds footing at {Cells(footed)}";
        var twin = rail.Party[pit.Cell].Neighbours4().FirstOrDefault(n => layout.IsRail(n) && !layout.IsPartyRail(n));
        var twinFloor = FloorUnder(twin);
        GD.Print($"[island] rays down: no footing at {drops.Count} trench, moat and bridge cells; the pit {rail.Party[pit.Cell]}'s enemy twin {twin} stands at {twinFloor:0.00} m");
        if (twinFloor is not { } y || Mathf.Abs(y - builder.Surface(twin).Y) > 0.3f) return $"the pit's enemy twin {twin} has no floor under it (ray found {twinFloor})";
        return null;
    }

    /// <summary>The cells of <paramref name="room"/> (its index in the layout).</summary>
    private static bool InRoom(FloorLayout layout, int room, Vector3 at) => layout.Rooms[room].Contains(DungeonBuilder.WorldToCell(at));

    /// <summary>A crew member, or a hero, stands somewhere a body may be set down (<see cref="CrevasseRules.SetDownOk"/>) and has not fallen.</summary>
    private static bool StandsOk(FloorLayout layout, Node3D body) => CrevasseRules.SetDownOk(layout, DungeonBuilder.WorldToCell(body.GlobalPosition));

    /// <summary>The floor's crew (living), or none without a crew controller.</summary>
    private static IEnumerable<Adventure.Npc> IslandCrew()
        => ObjectiveController.Instance is CrewRescueController ctrl ? ctrl.Crew.Where(n => IsInstanceValid(n) && !n.Dead) : Enumerable.Empty<Adventure.Npc>();
}
