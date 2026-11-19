using DungeonCrawler.Dungeon;
using DungeonCrawler.Dungeon.Generation;
using DungeonCrawler.Dungeon.Rails;
using DungeonCrawler.Party;
using Godot;

namespace DungeonCrawler.Core;

// ---- --test-ride-edges -------------------------------------------------------------------------------------
// The ride's edge cases on the built-in campaign's floor 12 (lethality 3, no enemies): a companion downed as the party
// boards is left behind, gets back up during the ride and never regroups into or ahead of the cart (it hops in at stop 1);
// a save between stops and a load put the cart back at the stop it left with every hero beside it, none on a rail; the
// human thrown from the moving cart at lethality 1 is hurt once, not downed and not hit again; and the companions riding
// on without a player are set down at the next stop, where the cart stays. The relief cart is off from the throw (NoRelief): this flow
// pins the party cart's own run-over and leaves the thrown human on the line; --test-relief covers the relief. Prints [ride-edges] lines and a RESULT.
public partial class DebugTools
{
    private bool _testRideEdges;
    private int _edgeStep;
    private float _edgeClock;
    private Adventurer? _edgeLeft;
    private bool _edgeLeftUp, _edgeLeftFar;
    private int _edgeRedowns;
    private float _edgeLeftNearest = float.MaxValue;
    private float _edgeSavedS;
    private float _edgeHpAfter, _edgeHpLow, _edgeMaxHp;
    /// <summary>The human jumps once the cart is going this fast: well above the throwing speed, so a landing ahead would be swept.</summary>
    const float ThrowAt = 4f;

    private void RunRideEdgesTest()
    {
        var main = Main.Instance;
        if (main?.Builder.Layout is not { } layout || main.Builder.FloorRoot is not { } root || !main.InGame || Game.Instance.CurrentFloor != 12) return;
        _edgeClock += (float)GetProcessDeltaTime();
        void Fail(string why) { GD.Print($"[ride-edges] RESULT FAIL: {why}"); TierRules.Lethality = 3f; _done = true; GetTree().Quit(1); }
        var party = main.Party;
        var rc = RailController.Instance;
        // no enemies on the floor (as --clear-enemies): the stop mobs, ambushers and portals are culled as they appear, so the fallen get back up
        foreach (var e in root.GetChildren().OfType<Enemies.Enemy>())
            if (e.IsAlive) e.Kill();
        if (rc == null) { if (_edgeClock > 3f) Fail("no RailController on floor 12"); return; }
        RideWaiveCages(main, rc, "ride-edges");

        switch (_edgeStep)
        {
            case 0 when _edgeClock >= 1.5f:
            {
                _edgeLeft = party.Active.FirstOrDefault(a => a.Control == ControlKind.Companion);
                if (_edgeLeft == null) { Fail("no companion in the party"); return; }
                // downed (not mangled) at the moment the human boards: the boarding skips it
                _edgeLeft.ReceiveHazard(HazardDamage.RunOver, new HazardOutcome(false, 2f, 0f), "the test", -1);
                if (!_edgeLeft.Downed || _edgeLeft.State.Mangled) { Fail($"{_edgeLeft.State.Name} was not downed (downed={_edgeLeft.Downed} mangled={_edgeLeft.State.Mangled})"); return; }
                if (!RideBoard(main, layout, rc, out var why)) { Fail(why); return; }
                _edgeLeft.Stance = Characters.Stance.Defensive; // a companion that follows (Hold never regroups)
                var seated = party.Active.Count(a => a.Seated);
                GD.Print($"[ride-edges] 1 {_edgeLeft.State.Name} downed at boarding: seated={_edgeLeft.Seated}; {seated} seated, StopS=[{string.Join(", ", rc.StopS.Select(s => s.ToString("0.0")))}]");
                if (_edgeLeft.Seated || seated != 3) { Fail($"boarding seated {seated} (the downed companion seated={_edgeLeft.Seated})"); return; }
                _edgeStep++;
                _edgeClock = 0f;
                break;
            }
            case 1:
            {
                if (rc.State.Mode == CartMode.Rolling) { GD.Print($"[ride-edges] 2 departed without {_edgeLeft!.State.Name} after {_edgeClock:0.0} s"); _edgeStep++; _edgeClock = 0f; }
                else if (_edgeClock > 8f) Fail($"the cart did not depart (mode {rc.State.Mode})");
                break;
            }
            case 2:
            {
                var left = _edgeLeft!;
                // kept down until the cart is well past the regroup reach (25 m), so it gets up with its lead far off in the cart
                var gap = left.GlobalPosition.DistanceTo(rc.Cart.GlobalPosition);
                if (!_edgeLeftFar)
                {
                    if (left.Downed && gap >= 30f) _edgeLeftFar = true;
                    else if (!left.Downed && rc.State.Mode == CartMode.Rolling) { left.ReceiveHazard(HazardDamage.RunOver, new HazardOutcome(false, 2f, 0f), "the test", -1); _edgeRedowns++; }
                }
                if (_edgeLeftFar && !left.Downed && !_edgeLeftUp) { _edgeLeftUp = true; GD.Print($"[ride-edges] 2 {left.State.Name} gets up {gap:0.0} m behind the rolling cart (downed again {_edgeRedowns}x until then)"); }
                if (left.State.Mangled) { Fail($"{left.State.Name} was mangled during the ride"); return; }
                if (rc.State.Mode == CartMode.Rolling && !left.Seated && !left.Downed)
                {
                    var front = rc.Cart.GlobalPosition - rc.Cart.GlobalBasis.Z * CartRules.HalfLength;
                    var d = left.GlobalPosition.DistanceTo(front);
                    _edgeLeftNearest = Mathf.Min(_edgeLeftNearest, d);
                    if (d < 2f) { Fail($"{left.State.Name}, revived on foot, came {d:0.00} m from the rolling cart's front (cell {DungeonBuilder.WorldToCell(left.GlobalPosition)})"); return; }
                    var dc = left.GlobalPosition.DistanceTo(rc.Cart.GlobalPosition);
                    if (dc < 2f) { Fail($"{left.State.Name}, revived on foot, is {dc:0.00} m from the rolling cart's middle: inside the tub"); return; }
                }
                if (rc.State.Mode == CartMode.Held && rc.StopIndex == 1)
                {
                    GD.Print($"[ride-edges] 3 held at stop 1 after {_edgeClock:0.0} s: {left.State.Name} revived={_edgeLeftUp} seated={left.Seated} mangled={left.State.Mangled} nearest to the front while rolling={(_edgeLeftNearest == float.MaxValue ? "-" : _edgeLeftNearest.ToString("0.0"))} m");
                    if (!_edgeLeftUp) { Fail($"{left.State.Name} never got back up during the ride"); return; }
                    _edgeStep++;
                    _edgeClock = 0f;
                }
                else if (_edgeClock > 60f) Fail($"the cart was not held at stop 1 within 60 s (mode {rc.State.Mode}, S={rc.State.S:0.0})");
                break;
            }
            case 3:
            {
                // the companion left behind asks the held cart for a seat instead of regrouping onto the lead
                if (_edgeLeft!.Seated && _edgeLeft.Cart == rc.Cart)
                {
                    GD.Print($"[ride-edges] 4 {_edgeLeft.State.Name} hopped in at stop 1 after {_edgeClock:0.0} s (seat {_edgeLeft.Seat}); {party.Active.Count(a => a.Seated)} seated");
                    _edgeStep++;
                    _edgeClock = 0f;
                }
                else if (_edgeClock > 3f) Fail($"{_edgeLeft.State.Name} did not hop into the held cart at stop 1 (cell {DungeonBuilder.WorldToCell(_edgeLeft.GlobalPosition)}, {_edgeLeft.GlobalPosition.DistanceTo(rc.Cart.GlobalPosition):0.0} m away)");
                break;
            }
            case 4:
            {
                // (a) a save between stops 1 and 2, and a load
                if (rc.State.Mode == CartMode.Rolling && rc.State.S >= rc.StopS[1] + 0.4f * (rc.StopS[2] - rc.StopS[1]))
                {
                    _edgeSavedS = rc.State.S;
                    var seated = party.Active.Count(a => a.Seated);
                    main.SaveGame("rideedges");
                    GD.Print($"[ride-edges] 5 saved mid-ride at S={_edgeSavedS:0.0} (stop 1 {rc.StopS[1]:0.0}, stop 2 {rc.StopS[2]:0.0}) with {seated} seated");
                    if (!main.LoadGame("rideedges")) { Fail("the save would not load"); return; }
                    _edgeStep++;
                    _edgeClock = 0f;
                }
                else if (_edgeClock > 40f) Fail($"the cart never got halfway to stop 2 (mode {rc.State.Mode}, S={rc.State.S:0.0})");
                break;
            }
            case 5 when _edgeClock >= 0.5f:
            {
                var off = Mathf.Abs(rc.State.S - rc.StopS[1]);
                var fails = new List<string>();
                var lines = new List<string>();
                var beside = BesideTheCartCells(layout, rc);
                foreach (var a in party.Active)
                {
                    var cell = DungeonBuilder.WorldToCell(a.GlobalPosition);
                    var d = a.GlobalPosition.DistanceTo(rc.Cart.GlobalPosition);
                    lines.Add($"{a.State.Name} {cell} {d:0.0} m rail={layout.IsRail(cell)} beside={beside.Contains(cell)}");
                    if (a.Seated) fails.Add($"{a.State.Name} is seated");
                    if (layout.IsRail(cell)) fails.Add($"{a.State.Name} stands on a rail cell {cell}");
                    if (d < 1.2f) fails.Add($"{a.State.Name} is inside the parked cart");
                    if (!beside.Contains(cell)) fails.Add($"{a.State.Name} at {cell} is not beside the lane by the cart ({d:0.0} m away)");
                }
                GD.Print($"[ride-edges] 5 after the load: mode={rc.State.Mode} stop={rc.StopIndex} S={rc.State.S:0.000} (off {off:0.000}) plating={rc.State.Plating:0} seats booked={rc.Cart.Seats.Count}; {string.Join("; ", lines)}");
                if (rc.State.Mode != CartMode.Held || rc.StopIndex != 1 || off > 0.05f) fails.Insert(0, "the cart did not come back held at stop 1");
                if (rc.Cart.Seats.Count > 0) fails.Add("seats are still booked");
                if (fails.Count > 0) { Fail(string.Join("; ", fails)); return; }
                _edgeStep++;
                _edgeClock = 0f;
                if (!RideBoard(main, layout, rc, out var why)) { Fail(why); return; }
                break;
            }
            case 6:
            {
                if (rc.State.Mode != CartMode.Rolling) { if (_edgeClock > 8f) Fail($"re-boarded at stop 1, but the cart did not depart ({party.Active.Count(a => a.Seated)} seated)"); break; }
                if (rc.State.V < ThrowAt) { if (_edgeClock > 12f) Fail($"the cart never reached {ThrowAt} m/s (V={rc.State.V:0.00})"); break; }
                if (party.Local is not { } me || me.Cart != rc.Cart) { Fail("the human is not seated in the rolling cart"); return; }
                // (c) the human thrown at lethality 1
                TierRules.Lethality = 1f;
                _edgeMaxHp = me.State.Stats[Characters.Stat.MaxHp];
                me.State.Hp = _edgeMaxHp;
                var v = rc.State.V;
                NoRelief = true; // "hit once, not again": a relief sent to the thrown human would run them over (--test-relief covers it)
                rc.Cart.Interact(party, me);
                _edgeHpAfter = _edgeHpLow = me.State.Hp;
                var lost = (_edgeMaxHp - me.State.Hp) / _edgeMaxHp;
                var sMe = main.Builder.PartyTrack!.Curve.GetClosestOffset(main.Builder.PartyTrack.ToLocal(me.GlobalPosition));
                GD.Print($"[ride-edges] 6 {me.State.Name} thrown at V={v:0.00} m/s, lethality 1: seated={me.Seated} downed={me.Downed} mangled={me.State.Mangled} lost {lost * 100f:0.0}% (expected {HazardRules.DamageFraction(1f) * 100f:0.0}%); lands at S={sMe:0.00}, the cart's back at {rc.State.S - CartRules.HalfLength:0.00}");
                if (me.Seated || me.Downed || me.State.Mangled) { Fail($"{me.State.Name} thrown at lethality 1 was not just hurt"); return; }
                if (Mathf.Abs(lost - HazardRules.DamageFraction(1f)) > 0.02f) { Fail($"{me.State.Name} lost {lost * 100f:0.0}% of max HP"); return; }
                if (sMe > rc.State.S - CartRules.HalfLength) { Fail($"{me.State.Name} landed at S={sMe:0.00}, not behind the cart"); return; }
                _edgeStep++;
                _edgeClock = 0f;
                break;
            }
            case 7:
            {
                var me = party.Local!;
                _edgeHpLow = Mathf.Min(_edgeHpLow, me.State.Hp);
                if (me.Downed || _edgeHpLow < _edgeHpAfter - 0.01f * _edgeMaxHp) { Fail($"{me.State.Name} was hit again after the throw (hp {_edgeHpAfter:0} -> {me.State.Hp:0}, downed={me.Downed})"); return; }
                if (_edgeClock < 2f) break;
                GD.Print($"[ride-edges] 6 two seconds later: {me.State.Name} hp {_edgeHpAfter:0} (lowest {_edgeHpLow:0}) downed={me.Downed}; the cart {rc.State.Mode} at S={rc.State.S:0.0} with {party.Active.Count(a => a.Seated)} seated");
                _edgeStep++;
                _edgeClock = 0f;
                break;
            }
            case 8:
            {
                // (d) the companions ride on without a player: set down at stop 2, and the cart stays
                if (rc.State.Mode == CartMode.Rolling) { if (_edgeClock > 60f) Fail($"the cart did not reach stop 2 within 60 s (S={rc.State.S:0.0})"); break; }
                var seated = party.Adventurers.Count(a => IsInstanceValid(a) && a.Seated);
                var cells = party.Active.Where(a => a.Control == ControlKind.Companion).Select(a => DungeonBuilder.WorldToCell(a.GlobalPosition)).ToList();
                GD.Print($"[ride-edges] 7 the cart {rc.State.Mode} at stop {rc.StopIndex} after {_edgeClock:0.0} s: seated={seated} boarding={rc.Boarding.Running} companions at [{string.Join(" ", cells.Select(c => $"{c}{(layout.IsRail(c) ? "(rail)" : "")}"))}]");
                if (rc.State.Mode != CartMode.Held || rc.StopIndex != 2) { Fail($"the cart is {rc.State.Mode} at stop {rc.StopIndex}, not held at stop 2"); return; }
                if (seated > 0) { Fail($"{seated} still seated at stop 2 without a player"); return; }
                if (rc.Boarding.Running) { Fail("the boarding countdown started without a player aboard"); return; }
                if (cells.Any(layout.IsRail)) { Fail("a companion was set down on a rail cell"); return; }
                _edgeStep++;
                _edgeClock = 0f;
                break;
            }
            case 9:
            {
                if (rc.State.Mode != CartMode.Held || rc.StopIndex != 2 || party.Adventurers.Any(a => IsInstanceValid(a) && a.Seated))
                {
                    Fail($"the cart left stop 2 without a player (mode {rc.State.Mode}, stop {rc.StopIndex}, seated {party.Adventurers.Count(a => IsInstanceValid(a) && a.Seated)})");
                    return;
                }
                if (_edgeClock < Boarding.Countdown + 1f) break;
                GD.Print($"[ride-edges] 7 still held at stop 2 {_edgeClock:0.0} s later, nobody aboard");
                TierRules.Lethality = 3f;
                NoRelief = false;
                GD.Print("[ride-edges] RESULT ok");
                _done = true;
                GetTree().Quit();
                break;
            }
        }
    }

    /// <summary>The cells a rider steps out onto at the cart's stop: off the lanes, 4-adjacent to a party lane cell within two of the one under the cart.</summary>
    private static HashSet<GridPos> BesideTheCartCells(FloorLayout layout, RailController rc)
    {
        var lane = layout.Rail!.Party;
        var here = DungeonBuilder.WorldToCell(rc.Cart.GlobalPosition);
        int best = 0, bestD = int.MaxValue;
        for (var i = 0; i < lane.Count; i++)
        {
            int dx = lane[i].X - here.X, dy = lane[i].Y - here.Y, d = dx * dx + dy * dy;
            if (d < bestD) { bestD = d; best = i; }
        }
        var cells = new HashSet<GridPos>();
        for (var i = Math.Max(0, best - 2); i <= Math.Min(lane.Count - 1, best + 2); i++)
            foreach (var n in new[] { new GridPos(lane[i].X, lane[i].Y - 1), new GridPos(lane[i].X + 1, lane[i].Y), new GridPos(lane[i].X, lane[i].Y + 1), new GridPos(lane[i].X - 1, lane[i].Y) })
                if (!layout.IsRail(n) && layout.IsWalkable(n)) cells.Add(n);
        return cells;
    }
}
