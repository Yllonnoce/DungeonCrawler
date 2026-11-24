using DungeonCrawler.Dungeon;
using DungeonCrawler.Dungeon.Generation;
using DungeonCrawler.Dungeon.Rails;
using DungeonCrawler.Party;
using Godot;

namespace DungeonCrawler.Core;

// ---- --test-pit[=thrown|wrong|lone|stall|late] (plan 15 Task 4, spec B.4, Ruling 41) ---------------------------------------------------
// The built-in campaign's floor 12 at lethality 3, solo, every enemy culled as it appears; every stop and id read from the line's shape
// (Shape(rc): the pit's ride is the last, its pieces r{LastRide}_switch_out and r{LastRide}_switch_back). The build prints "[pit] floor 12: …"
// with the lip, the far edge, the out lever's points and lever ((deadly …)), the back's points ((fixed)) and whether the fall is armed, and fails
// unless the party track is bare over exactly [lip, far edge] and nowhere else on the ride (but a trestle's own span), the pit cell and its two
// inward cells are chasm and the enemy twin is walkable, and the fall is armed. Then rc.Restore(LastRideStop), everyone boards and the ride departs.
// Every ride: no hand on the lever before the lead's press (no companion ever moves it); the early call with the front 25-30 m before the lever;
// in the seated window MovesRefused holds and MoveOfferFor is null for every hero; at the lever's S a layer-1 ray from the seated lead's eye to
// the pit's near lamp is clear.
// =thrown (the default): the lead presses on the window's first frame (ThrowPitOnTheWay); every car passes the lip with LaneBlendAt >= 0.99 there;
// the train is home (blend <= 0.01) by the back's points + 8.1 m with no fall, and arrives at the last stop.
// =wrong: nobody presses (LeavePitStraight); the last call 5-6.5 m before the points; the train passes the points straight and falls with its front
// within 0.6 m of the lip ("The cart runs into the lava pit!"); every rider fallen within 1 s; the wipe rises at LastRideStop with every hero up
// within 10 m of a whole cart.
// =lone: pass 1, the lead (the only human, possession off) downed in the seat before the window and held down until the points: the train takes
// the safe line with the lever straight (SafeLineLog), comes home and arrives with no fall; pass 2, restored again, the lead seated and up in W2
// (rc.DebugSeat): not able (AbleToThrow reads the cart), the same safe line.
// =stall: as =wrong with the train caught (DebugStall) 25 m before the lever (set at the window's start, rolling), 8 m before it (set at the last
// call's mark, the last call heard after it), 3 m short of the points (set at them, taken straight) and 5 m past them (ground into the pit, where it
// falls); never carried past the pit.
// =late: the lead's press arrives as the server takes one (its window, then its late check: RailEncounterController.LateFor) on the tick the front
// reaches the points and again 1 m past them; each is "Too late to throw it." (LatePress.TooLate), a local press there too moves nothing, the lever
// stays straight and the train falls; the wipe rises as =wrong. Prints [pit] lines and a RESULT.
public partial class DebugTools
{
    private bool _testPit;
    private string _ptVariant = "thrown";
    private int _ptStep, _ptPass;
    private float _ptClock;
    private bool _ptListening, _ptFallLogged, _ptSafeLogged, _ptRoundLogged, _ptWiped;
    private RailController? _ptHooked, _ptWipeRc;
    private float _ptEarlyFront = float.NaN, _ptLastFront = float.NaN, _ptWindowFront = float.NaN, _ptPressFront = float.NaN, _ptFallFront = float.NaN;
    private string? _ptSight;
    private bool _ptTaken, _ptHome, _ptDowned, _ptLipChecked;
    private float _ptLipBlendMin = float.PositiveInfinity;
    private int _ptFallsBefore, _ptGroundBefore, _ptGroundAtWreck = -1, _ptFallsAtWreck, _ptFallenAtWreck, _ptStalls, _ptLate, _ptWindowFrames;
    private readonly List<Adventurer> _ptRiders = new();
    private readonly HashSet<string> _ptMayFall = new();
    private bool PtFalls => _ptVariant is "wrong" or "stall" or "late";

    private const int PtRide = 3, PtFall = 10, PtFallChecks = 11, PtWipe = 12, PtRise = 13;

    private void PitListen()
    {
        if (_ptListening) return;
        _ptListening = true;
        EventBus.Instance.Message += text =>
        {
            if (_done || !_testPit) return;
            if (text == PitRules.FallLog) _ptFallLogged = true;
            if (text == SwitchBackRules.SafeLineLog) _ptSafeLogged = true;
            if (text == PitRules.RoundLog) _ptRoundLogged = true;
        };
        EventBus.Instance.PartyWiped += () =>
        {
            if (_done || !_testPit) return;
            _ptWiped = true;
            _ptWipeRc = RailController.Instance;
        };
        Action<Adventure.BarkTrigger, string?> called = (t, by) =>
        {
            if (_done || !_testPit || RailController.Instance is not { } r) return;
            if (t == Adventure.BarkTrigger.PitSwitch && float.IsNaN(_ptEarlyFront)) _ptEarlyFront = r.Party.FrontS;
            if (t == Adventure.BarkTrigger.PitSwitchNow && float.IsNaN(_ptLastFront)) _ptLastFront = r.Party.FrontS;
            if (t is Adventure.BarkTrigger.PitSwitch or Adventure.BarkTrigger.PitSwitchNow)
                GD.Print($"[pit] heard the {(t == Adventure.BarkTrigger.PitSwitch ? "early" : "last")} call (by {by ?? "the log"}) at front S={r.Party.FrontS:0.0}");
        };
        RailCalls.Called += called;
        _staticUnhooks.Add(() => RailCalls.Called -= called);
    }

    private void RunPitTest()
    {
        var main = Main.Instance;
        if (main?.Builder.Layout is not { } layout || main.Builder.FloorRoot is not { } root || !main.InGame || Game.Instance.CurrentFloor != 12) return;
        _ptClock += (float)GetProcessDeltaTime();
        HoldTheRunaway("pit"); // not this flow's business
        var party = main.Party;
        void Fail(string why) { GD.Print($"[pit] RESULT FAIL ({_ptVariant}): {why}"); party.AllowPossession = true; LeavePitStraight = false; _done = true; GetTree().Quit(1); }
        if (RailController.Instance is not { } rc || RailEncounterController.Instance is not { } enc || layout.Rail is not { } rail) { if (_ptClock > 3f) Fail("floor 12 has no line"); return; }
        PitListen();
        if (_ptHooked != rc)
        {
            _ptHooked = rc; // each floor build's controller
            rc.CartEvents += e =>
            {
                if (e.Kind != CartEventKind.Wrecked || _done || _ptStep != PtFall || !float.IsNaN(_ptFallFront) || RailController.Instance is not { } r) return;
                _ptFallFront = r.Party.FrontS;
                _ptFallsAtWreck = r.Falls;
                _ptGroundAtWreck = r.GroundFree;
                _ptFallenAtWreck = _ptRiders.Count(a => a.State.Fallen); // told after the train is taken: the wipe may rebuild the floor before the next frame
                foreach (var a in _ptRiders) _ptMayFall.Add(a.State.Name);
            };
        }
        foreach (var e in root.GetChildren().OfType<Enemies.Enemy>()) if (e.IsAlive) e.Kill(); // the flow owns the fights
        RideWaiveCages(main, rc, "pit");
        if (party.Local is not { } me) { Fail("no human"); return; }
        if (party.Active.FirstOrDefault(a => a.State.Fallen && !_ptMayFall.Contains(a.State.Name)) is { } stray) { Fail($"{stray.State.Name} fell at step {_ptStep} (cell {DungeonBuilder.WorldToCell(stray.GlobalPosition)}): nobody fell but the train's"); return; }
        var shape = Shape(rc);

        switch (_ptStep)
        {
            case 0 when _ptClock >= 1.5f:
            {
                if (PitBuild(main, root, layout, rail, rc, enc, shape) is { } bad) { Fail(bad); return; }
                LeavePitStraight = PtFalls; // Ruling 21: the fall tests leave it straight
                rc.RestoreGates(Enumerable.Range(1, shape.Intermediate)); // the test's shortcut: no holders to kill
                rc.Restore(shape.LastRideStop);
                _ptStep++; _ptClock = 0f;
                break;
            }
            case 1 when _ptClock >= 1f:
            {
                if (!RideBoard(main, layout, rc, out var why)) { Fail(why); return; }
                _ptStep++; _ptClock = 0f;
                break;
            }
            case 2:
            {
                if (_ptVariant == "lone" && _ptPass == 2 && me.Cart == rc.Cart && rc.State.Mode == CartMode.Held && rc.Boarding.Running)
                {
                    // pass 2: the lead seated and up in W2, the cart's seats the companions' (AbleToThrow reads the cart)
                    if (!rc.DebugSeat(me, 2)) { Fail("lone pass 2: the lead could not be seated in W2"); return; }
                    GD.Print($"[pit] lone pass 2: the lead {me.State.Name} seated in W2 (car {rc.CarOf(me)}), up={!me.Downed}");
                }
                if (rc.State.Mode != CartMode.Rolling || enc.ArmedRide != shape.LastRide) { if (_ptClock > 20f) Fail($"ride {shape.LastRide} did not depart (mode {rc.State.Mode}, stop {rc.StopIndex}, armed {enc.ArmedRide})"); break; }
                _ptRiders.Clear();
                _ptRiders.AddRange(party.Active.Where(a => a.Seated && rc.CarOf(a) >= 0));
                if (_ptRiders.Count != party.Active.Count()) { Fail($"{_ptRiders.Count} of {party.Active.Count()} heroes aboard at the departure"); return; }
                if (_ptVariant == "lone" && _ptPass == 2 ? rc.CarOf(me) != 2 : me.Cart != rc.Cart) { Fail($"the lead rides in car {rc.CarOf(me)} at the departure"); return; }
                _ptFallsBefore = rc.Falls;
                _ptGroundBefore = rc.GroundFree;
                GD.Print($"[pit] {(_ptPass > 0 ? $"pass {_ptPass}: " : "")}departed stop {shape.LastRideStop} with [{string.Join(", ", _ptRiders.Select(a => $"{a.State.Name} (car {rc.CarOf(a)})"))}] aboard, train=[{string.Join(",", rc.Train.Select(w => w.Number))}]; plating {rc.State.Plating:0}");
                _ptStep = PtRide; _ptClock = 0f;
                break;
            }
            case PtRide:
            {
                if (enc.PitSwitch is not { } lever || enc.SetPieceNode(Kiln(rc, "switch_back")) is not TrackSwitch back) { Fail("no pit switch on the last ride"); return; }
                if (PitRideTick(main, rc, enc, lever, back, me, Fail)) return;
                if (_ptVariant is "thrown" or "lone")
                {
                    if (rc.State.Mode == CartMode.Wrecked || rc.Falls != _ptFallsBefore || enc.PitFalls > 0) { Fail($"the train was wrecked or fell (falls {rc.Falls - _ptFallsBefore}, pit falls {enc.PitFalls})"); return; }
                    if (rc.State.Mode != CartMode.Held || rc.StopIndex != shape.Rides) { if (_ptClock > 120f) Fail($"the train did not arrive at stop {shape.Rides} (mode {rc.State.Mode}, stop {rc.StopIndex}, front S={rc.Party.FrontS:0.0})"); break; }
                    if (!_ptTaken || !_ptHome || !_ptLipChecked || _ptSight != "clear") { Fail($"arrived without the take ({_ptTaken}), the home check ({_ptHome}), the lip check ({_ptLipChecked}) or the sight ({_ptSight ?? "never cast"})"); return; }
                    GD.Print($"[pit] {(_ptPass > 0 ? $"pass {_ptPass}: " : "")}arrived at stop {rc.StopIndex} (S={rc.State.S:0.0}) with no fall: plating {rc.State.Plating:0}, riders [{string.Join(", ", _ptRiders.Select(a => $"{a.State.Name}{(a.State.Fallen ? " FALLEN" : "")}"))}]");
                    if (_ptVariant == "lone" && _ptPass == 1)
                    {
                        me.State.Mangled = false;
                        me.Revive();
                        party.AllowPossession = true;
                        PitPassReset();
                        _ptPass = 2;
                        _ptStep = 0; _ptClock = 1.5f; // pass 2: built again from the restore
                        break;
                    }
                    var early = lever.LeverS - _ptEarlyFront;
                    GD.Print(_ptVariant == "thrown"
                        ? $"[pit] RESULT ok (thrown): the early call {early:0.0} m before the lever, the lever straight with no hand on it until the lead threw it from the seat on the window's first frame ({lever.LeverS - _ptPressFront:0.0} m before the lever), moves refused in the window ({_ptWindowFrames} frames); every car past the lip across (blend {_ptLipBlendMin:0.000} at it); home by the back's points + 8.1 m, no fall, \"{PitRules.RoundLog}\" logged; arrived at stop {shape.Rides}; sight from the lever to the pit's lamp clear"
                        : $"[pit] RESULT ok (lone): pass 1 the lead, the only human, downed in the seat before the window; pass 2 the lead up in W2; each time at the points the train took the safe line with the lever straight (\"{SwitchBackRules.SafeLineLog}\"), every car past the lip across (blend {_ptLipBlendMin:0.000}), home, no fall, arrived at stop {shape.Rides}");
                    _done = true;
                    GetTree().Quit(0);
                    return;
                }
                _ptStep = PtFall; // the fall branches
                break;
            }
            case PtFall:
            {
                if (!float.IsNaN(_ptFallFront)) { _ptStep = PtFallChecks; _ptClock = 0f; break; }
                if (enc.PitSwitch is not { } lever || enc.SetPieceNode(Kiln(rc, "switch_back")) is not TrackSwitch back) { Fail("no pit switch on the last ride"); return; }
                if (PitRideTick(main, rc, enc, lever, back, me, Fail)) return;
                if (_ptVariant == "stall" && PitStalls(rc, enc, lever, Fail)) return;
                if (_ptVariant == "late" && PitLatePresses(main, rc, enc, lever, me, Fail)) return;
                if (rc.State.Mode == CartMode.Held && rc.StopIndex > shape.LastRideStop) { Fail($"the cart reached stop {rc.StopIndex}: it did not fall"); return; }
                if (rc.State.Mode == CartMode.Wrecked) { Fail("the cart was wrecked, not fallen"); return; }
                if (rc.State.Mode == CartMode.Rolling && rc.Party.FrontS > enc.PitFarS) { Fail($"the front passed the pit (S={rc.Party.FrontS:0.0}, blend at the lip {rc.LaneBlendAt(enc.PitS):0.00}) and the train did not fall"); return; }
                if (_ptClock > 150f) Fail($"the train did not fall within 150 s (mode {rc.State.Mode}, front S={rc.Party.FrontS:0.0})");
                break;
            }
            case PtFallChecks:
            {
                if (_ptVariant == "stall" && (_ptStalls != 7 || _ptGroundAtWreck - _ptGroundBefore != 4)) { Fail($"stall: the train fell after {_ptStalls} step(s) and {_ptGroundAtWreck - _ptGroundBefore} grind(s) free, not 7 and 4 (the window's start, the last call's mark, the points, then into the pit)"); return; }
                if (_ptVariant == "late" && _ptLate != 2) { Fail($"late: {_ptLate} late press(es) made, not 2"); return; }
                if (!_ptTaken || _ptSight != "clear") { Fail($"the train fell before the points were judged ({_ptTaken}) or the sight from the lever was cast ({_ptSight ?? "never"})"); return; }
                var off = Mathf.Abs(_ptFallFront - enc.PitS);
                if (_ptFallsAtWreck < 1) { Fail("a wreck, not a fall"); return; }
                if (off > 0.6f) { Fail($"the train fell with its front at S={_ptFallFront:0.00}, {off:0.00} m from the lip at {enc.PitS:0.00}"); return; }
                if (!_ptFallLogged) { Fail($"\"{PitRules.FallLog}\" was not logged"); return; }
                var standing = _ptFallenAtWreck == _ptRiders.Count || _ptWiped ? new List<Adventurer>() : _ptRiders.Where(a => !a.State.Fallen).ToList();
                if (_ptWiped && _ptFallenAtWreck < _ptRiders.Count) { Fail($"{_ptRiders.Count - _ptFallenAtWreck} of {_ptRiders.Count} riders not fallen at the fall, and the party wiped"); return; }
                if (standing.Count > 0) { if (_ptClock > 1f) Fail($"within 1 s of the fall {string.Join(", ", standing.Select(a => a.State.Name))} not fallen"); break; }
                GD.Print($"[pit] the train fell with its front at S={_ptFallFront:0.00} ({off:0.00} m from the lip at {enc.PitS:0.00}), \"{PitRules.FallLog}\" logged; every rider fallen ({_ptFallenAtWreck} of {_ptRiders.Count} at the fall itself, {_ptClock:0.00} s on)");
                _ptStep = PtWipe; _ptClock = 0f;
                break;
            }
            case PtWipe:
            {
                if (!_ptWiped) { if (_ptClock > 5f) Fail("the party did not wipe within 5 s of the fall"); break; }
                GD.Print("[pit] the party has fallen: the wipe");
                _ptStep = PtRise; _ptClock = 0f;
                break;
            }
            case PtRise:
            {
                if (rc == _ptWipeRc || main.Rebuilding) { if (_ptClock > 10f) Fail("the floor was not built again within 10 s of the wipe"); break; }
                _ptMayFall.Clear();
                var problems = new List<string>();
                if (rc.StopIndex != shape.LastRideStop || rc.State.Mode != CartMode.Held) problems.Add($"the cart {rc.State.Mode} at stop {rc.StopIndex}, not held at stop {shape.LastRideStop}");
                if (rc.State.Plating < CartRules.MaxPlating || !rc.Cart.Visible) problems.Add($"plating {rc.State.Plating:0}/{CartRules.MaxPlating:0}, visible={rc.Cart.Visible}");
                foreach (var a in party.Active)
                    if (a.State.Fallen || a.Downed || a.GlobalPosition.DistanceTo(rc.Cart.GlobalPosition) > 10f || !CrevasseRules.SetDownOk(layout, DungeonBuilder.WorldToCell(a.GlobalPosition)))
                        problems.Add($"{a.State.Name} fallen={a.State.Fallen} downed={a.Downed} {a.GlobalPosition.DistanceTo(rc.Cart.GlobalPosition):0.0} m from the cart at {DungeonBuilder.WorldToCell(a.GlobalPosition)}");
                if (problems.Count > 0) { if (_ptClock > 10f) Fail($"the rise: {string.Join("; ", problems)}"); break; }
                GD.Print($"[pit] the rise at stop {rc.StopIndex} after {_ptClock:0.0} s: the cart {rc.State.Mode}, plating {rc.State.Plating:0}/{CartRules.MaxPlating:0}; heroes [{string.Join(", ", party.Active.Select(a => $"{a.State.Name} {a.GlobalPosition.DistanceTo(rc.Cart.GlobalPosition):0.0} m"))}]");
                var how = _ptVariant switch
                {
                    "stall" => "caught 25 m before the lever, 8 m before it, 3 m short of the points and 5 m past them, the watchdog set the train at the window's start, then at the last call's mark (the last call still heard), then at the points (taken straight), then ground it into the pit",
                    "late" => "the lead's press on the tick the front reached the points and again 1 m past them each judged too late by the server's late rule (LateFor: TooLate) and a local press there moving nothing, the lever left straight (no message is told in solo: \"Too late to throw it.\" is the server's to a remote presser)",
                    _ => "nobody pressed",
                };
                var earlyM = (enc.PitSwitch?.LeverS ?? float.NaN) - _ptEarlyFront;
                GD.Print($"[pit] RESULT ok ({_ptVariant}): the early call {earlyM:0.0} m before the lever and the last {PitPointsS(enc) - _ptLastFront:0.0} m before the points; {how}; the window offered it to the lead; the train on its own lane fell at the lip (front {_ptFallFront:0.0}, lip {enc.PitS:0.0}) and the wipe rose at stop {shape.LastRideStop}; sight from the lever to the pit's lamp clear; nobody fell but the train's");
                LeavePitStraight = false;
                _done = true;
                GetTree().Quit(0);
                return;
            }
        }
    }

    /// <summary>The pit's deadly switch_out's points (the encounter's), or NaN.</summary>
    private static float PitPointsS(RailEncounterController enc) => enc.PitSwitch?.PointsS ?? float.NaN;

    /// <summary>Lone's pass 2 starts clean: every per-ride mark the ride sets.</summary>
    private void PitPassReset()
    {
        _ptEarlyFront = _ptLastFront = _ptWindowFront = _ptPressFront = _ptFallFront = float.NaN;
        _ptSight = null;
        _ptTaken = _ptHome = _ptDowned = _ptLipChecked = _ptSafeLogged = _ptRoundLogged = false;
        _ptLipBlendMin = float.PositiveInfinity;
        _ptWindowFrames = 0;
    }

    /// <summary>
    /// Each rolling tick of the ride, every variant: no hand on the lever until the lead's press; the early call 25-30 m before the lever; in the
    /// seated window moves refused and none offered; the sight from the lever; the press (=thrown) or the lead downed (=lone pass 1); at the points
    /// the take as the lever stands; every car across at the lip; home past the far edge. True when it failed.
    /// </summary>
    private bool PitRideTick(Main main, RailController rc, RailEncounterController enc, TrackSwitch lever, TrackSwitch back, Adventurer me, Action<string> fail)
    {
        if (rc.State.Mode != CartMode.Rolling) return false;
        var party = main.Party;
        var front = rc.Party.FrontS;
        var windowStart = lever.LeverS - SeatedSwitchRules.WindowAhead;
        // =lone pass 1: the lead, the only human, downed in the seat just before the window and held down until the points
        if (_ptVariant == "lone" && _ptPass <= 1 && !_ptDowned && front >= windowStart - 3f)
        {
            party.AllowPossession = false; // else solo hands the lead to a seated companion, who would then be the human able to throw it
            me.State.Mangled = true;       // held down until the points (calm auto-revive never stands a Mangled member up)
            me.State.Hp = 0;
            party.OnMemberDowned(me.Index);
            _ptDowned = true;
            _ptPass = 1;
            var able = _ptRiders.Count(a => SwitchBackRules.AbleToThrow(a.Control, a.Downed, a.Seated && a.Cart == rc.Cart));
            GD.Print($"[pit] lone pass 1: the lead {me.State.Name} downed in the seat at front S={front:0.0} ({windowStart - front:0.0} m short of the window's start {windowStart:0.0}): downed={me.Downed} seated={me.Cart == rc.Cart}, {able} rider(s) able to throw it");
            if (!me.Downed || me.Cart != rc.Cart || able != 0) { fail("lone: the lead is not downed in the seat, or someone aboard could still throw it"); return true; }
        }
        if (_ptVariant == "lone" && _ptPass == 2 && !_ptDowned)
        {
            _ptDowned = true; // pass 2's "downed" mark: the lead is up in W2, never in the cart
            var able = _ptRiders.Count(a => SwitchBackRules.AbleToThrow(a.Control, a.Downed, a.Seated && a.Cart == rc.Cart));
            GD.Print($"[pit] lone pass 2: the lead {me.State.Name} up in car {rc.CarOf(me)}: {able} rider(s) able to throw it");
            if (me.Downed || rc.CarOf(me) != 2 || able != 0) { fail("lone pass 2: the lead is not up in W2, or someone in the cart could throw it"); return true; }
        }
        // the window: its first frame, and nobody moves car to car in it
        var offered = party.LookingAt == lever; // the seat looks at it whatever the aim: the window offers it to the lead (ThrowPitOnTheWay presses on this)
        if (offered && float.IsNaN(_ptWindowFront)) { _ptWindowFront = front; GD.Print($"[pit] the seated window offers {lever.Name} to the lead at front S={front:0.0} ({lever.LeverS - front:0.0} m before the lever, prompt \"{lever.Prompt}\")"); }
        if (enc.AnyLeverWindow() && !lever.Taken)
        {
            _ptWindowFrames++;
            if (!rc.MovesRefused) { fail($"in the pit switch's window (front S={front:0.0}) moves car to car are not refused"); return true; }
            if (party.Active.FirstOrDefault(a => rc.MoveOfferFor(a) != null) is { } mover) { fail($"in the pit switch's window {mover.State.Name} is offered a move car to car"); return true; }
        }
        // =thrown: the lead presses on the window's first frame
        if (_ptVariant == "thrown" && !lever.Thrown && ThrowPitOnTheWay(main, rc, "pit")) _ptPressFront = front;
        if (_ptVariant == "thrown" && lever.Thrown && (float.IsNaN(_ptPressFront) || lever.LastToggledBy != me.State.Name)) { fail($"the lever was thrown before the lead's press, or by {lever.LastToggledBy ?? "nobody"}"); return true; }
        if (_ptVariant == "thrown" && !float.IsNaN(_ptPressFront) && (float.IsNaN(_ptWindowFront) || _ptPressFront - _ptWindowFront > 0.01f || _ptPressFront < windowStart - 0.01f)) { fail($"the lead's press at front S={_ptPressFront:0.0} was not on the window's first frame (it opened at {_ptWindowFront:0.0}, its start {windowStart:0.0})"); return true; }
        if (_ptVariant != "thrown" && (lever.Thrown || lever.LastToggledBy != null)) { fail($"the pit switch moved with nobody's press (thrown={lever.Thrown}, by {lever.LastToggledBy ?? "nobody"})"); return true; }
        if (!float.IsNaN(_ptEarlyFront) && (lever.LeverS - _ptEarlyFront < 25f || lever.LeverS - _ptEarlyFront > 30f)) { fail($"the early call came {lever.LeverS - _ptEarlyFront:0.0} m before the lever, not 25-30 m"); return true; }
        // the sight from the lever's S: a layer-1 ray from the seated lead's eye to the pit's near lamp (the lead sits in the cart: not =lone pass 2)
        if (front >= lever.LeverS && _ptSight == null && me.Cart == rc.Cart)
        {
            if (enc.PitLook is not { } look) { fail("no LavaPit dressing"); return true; }
            var eye = me.Camera.GlobalPosition;
            var lamp = look.LampPoint;
            var hit = me.GetWorld3D().DirectSpaceState.IntersectRay(PhysicsRayQueryParameters3D.Create(eye, lamp, 1));
            _ptSight = hit.Count == 0 ? "clear" : $"blocked by {(hit["collider"].AsGodotObject() as Node)?.Name} at {hit["position"].AsVector3().DistanceTo(eye):0.0} m";
            GD.Print($"[pit] sight from the lever: the front at S={front:0.0} (the lever at {lever.LeverS:0.0}), a layer-1 ray from the seated lead's eye to the pit's near lamp ({eye.DistanceTo(lamp):0.0} m, the lip {enc.PitS - front:0.0} m ahead): {_ptSight}");
            if (_ptSight != "clear") { fail($"the pit's lamp is not in sight from the lever: {_ptSight}"); return true; }
        }
        if (_ptVariant == "lone" && _ptPass == 2) _ptSight ??= "clear"; // the lead in W2 has no cart seat's view to cast from
        // at the points: taken as the lever stands
        if (!_ptTaken && front >= lever.PointsS)
        {
            if (!lever.Taken) { if (front > lever.PointsS + 2f) { fail("the front passed the pit switch's points and it is not taken"); return true; } return false; }
            _ptTaken = true;
            if (float.IsNaN(_ptEarlyFront)) { fail("the early call was never heard before the points"); return true; }
            var across = rc.Crossing;
            GD.Print($"[pit] the front at S={front:0.0} reached {lever.Name}'s points ({lever.PointsS:0.0}): the lever {(lever.Thrown ? "thrown" : "straight")}, {(across ? $"taken: across from {rc.CrossOutS:0.0}, back from {rc.CrossBackS:0.0}" : "passed straight: the train stays on its own lane")}; calls early {lever.LeverS - _ptEarlyFront:0.0} m before the lever, last {(float.IsNaN(_ptLastFront) ? "none" : $"{lever.PointsS - _ptLastFront:0.0} m before the points")}");
            if (_ptVariant == "thrown" && (!across || !_ptRoundLogged)) { fail($"the thrown lever's points: across {across}, \"{PitRules.RoundLog}\" logged {_ptRoundLogged}"); return true; }
            if (_ptVariant == "lone")
            {
                if (lever.Thrown || !across || !_ptSafeLogged) { fail($"lone: thrown {lever.Thrown}, across {across}, \"{SwitchBackRules.SafeLineLog}\" logged {_ptSafeLogged}"); return true; }
                if (_ptPass == 1) { me.State.Mangled = false; party.AllowPossession = true; } // taken: calm auto-revive may stand the lead up again
            }
            if (!(Mathf.Abs(rc.CrossBackS - back.PointsS) < 0.01f) && across) { fail($"the cart crossed with its return at {rc.CrossBackS:0.0}, not the back's points {back.PointsS:0.0}"); return true; }
            if (PtFalls)
            {
                if (across) { fail("nobody pressed with an able human aboard, but the train took the switch round the pit"); return true; }
                if (float.IsNaN(_ptWindowFront)) { fail("the seated window never offered the pit switch to the lead before its points"); return true; }
                if (float.IsNaN(_ptLastFront) || lever.PointsS - _ptLastFront < 5f || lever.PointsS - _ptLastFront > 6.5f) { fail($"the last call came {(float.IsNaN(_ptLastFront) ? "never" : $"{lever.PointsS - _ptLastFront:0.0} m before the points")}, not 5-6.5 m"); return true; }
            }
        }
        // every car past the lip across: the blend at the lip while any car's body is over the pit, and the cart's front at it
        if (rc.Crossing && _ptTaken)
        {
            var rear = RunawayRules.TrainRearS(rc.Party.BackS, rc.Train.Count, RailController.WagonSpacing);
            if (front >= enc.PitS && rear <= enc.PitFarS) _ptLipBlendMin = Mathf.Min(_ptLipBlendMin, rc.LaneBlendAt(enc.PitS));
            if (!_ptLipChecked && rear > enc.PitFarS)
            {
                _ptLipChecked = true;
                GD.Print($"[pit] every car past the pit across: the rear at S={rear:0.0} past the far edge {enc.PitFarS:0.0}; the lane blend at the lip {_ptLipBlendMin:0.000} at least while any car was over it ({rc.Train.Count} wagon(s))");
                if (!(_ptLipBlendMin >= 0.99f)) { fail($"a car passed the lip with the lane blend {_ptLipBlendMin:0.000} there, under 0.99"); return true; }
            }
        }
        // home on its own lane by the back's points + 8.1 m (the 6 m blend read at the cart's centre, the front 1.1 m ahead, 1 m of slack)
        var home = back.PointsS + RailController.SwitchStretch + CartRules.HalfLength + 1f;
        if (rc.Crossing && !_ptHome && front >= home)
        {
            GD.Print($"[pit] the front at S={front:0.0} (the back's points + {front - back.PointsS:0.0} m): lane blend {rc.LaneBlend:0.000}");
            if (rc.LaneBlend > 0.01f) { fail($"the cart is not home on its own lane by the back's points + {home - back.PointsS:0.0} m (blend {rc.LaneBlend:0.000})"); return true; }
            _ptHome = true;
        }
        return false;
    }

    /// <summary>
    /// =stall: the train caught four times, each freed by the watchdog to where it must be: the window's start (rolling, short of it), the last call's
    /// mark (inside the window; the last call heard after it), the points (taken straight), and into the pit (it falls). Marks 1..7 count the catches
    /// and their checks (the fourth grind is the fall's own, read at the wreck). True when it failed.
    /// </summary>
    private bool PitStalls(RailController rc, RailEncounterController enc, TrackSwitch lever, Action<string> fail)
    {
        if (rc.State.Mode != CartMode.Rolling) return false;
        var at = rc.Party.FrontS;
        var windowStart = lever.LeverS - SeatedSwitchRules.WindowAhead;
        var mark = lever.PointsS - PitRules.CallLast;
        var ground = rc.GroundFree - _ptGroundBefore;
        switch (_ptStalls)
        {
            case 0 when at >= lever.LeverS - 25f:
                _ptStalls = 1;
                rc.DebugStall = true;
                GD.Print($"[pit] stall: the train caught at front S={at:0.0}, {windowStart - at:0.0} m short of the pit switch's window (it opens at {windowStart:0.0}; the lever {(lever.Thrown ? "thrown" : "straight")})");
                break;
            case 1 when ground >= 1:
                if (at < windowStart - 0.05f || at > windowStart + 0.5f || lever.Taken) { fail($"stall: the watchdog freed the train short of the window to front S={at:0.0}, not the window's start {windowStart:0.0} (taken {lever.Taken})"); return true; }
                _ptStalls = 2;
                GD.Print($"[pit] stall: the watchdog freed the train to front S={at:0.0}, the window's start ({windowStart:0.0}), rolling at v {rc.State.V:0.00}; the lever untaken, straight");
                break;
            case 2 when at >= lever.LeverS - 8f:
                if (!float.IsNaN(_ptLastFront) || lever.Taken) { fail($"stall: the last call came (at {_ptLastFront:0.0}) or the lever was taken before front S={at:0.0}"); return true; }
                _ptStalls = 3;
                rc.DebugStall = true;
                GD.Print($"[pit] stall: the train caught at front S={at:0.0} inside the pit switch's window, {lever.PointsS - at:0.0} m short of its points (the last call not yet heard)");
                break;
            case 3 when ground >= 2:
                if (at < mark - 0.05f || at > mark + 0.5f || lever.Taken) { fail($"stall: the watchdog freed the train inside the window to front S={at:0.0}, not the last call's mark {mark:0.0} (taken {lever.Taken})"); return true; }
                _ptStalls = 4;
                GD.Print($"[pit] stall: the watchdog freed the train to front S={at:0.0}, the last call's mark ({mark:0.0}, {lever.PointsS - at:0.0} m short of the points); the lever untaken, straight");
                break;
            case 4 when !lever.Taken && at >= lever.PointsS - 3f:
                if (float.IsNaN(_ptLastFront)) { fail($"stall: freed to the last call's mark, the train reached front S={at:0.0} with no last call heard"); return true; }
                _ptStalls = 5;
                rc.DebugStall = true;
                GD.Print($"[pit] stall: the train caught at front S={at:0.0}, {lever.PointsS - at:0.0} m short of the pit switch's points (the lever straight, untaken; the last call heard at {lever.PointsS - _ptLastFront:0.0} m)");
                break;
            case 5 when ground >= 3:
                if (at < lever.PointsS - 0.05f || at > lever.PointsS + 1.5f) { fail($"stall: the watchdog set the front at S={at:0.0}, not the pit switch's points {lever.PointsS:0.0}"); return true; }
                _ptStalls = 6;
                GD.Print($"[pit] stall: the watchdog set the train with its front at S={at:0.0}, the points ({lever.PointsS:0.0}): taken as the lever stands (taken {lever.Taken}, across {rc.Crossing})");
                break;
            case 6 when lever.Taken && at >= lever.PointsS + 5f:
                if (rc.Crossing) { fail("stall: the straight lever took the train round the pit"); return true; }
                _ptStalls = 7;
                rc.DebugStall = true;
                GD.Print($"[pit] stall: the train caught again at front S={at:0.0} on its own lane past the points, {enc.PitS - at:0.0} m short of the lip");
                break;
        }
        if (at > enc.PitS + 0.6f) { fail($"stall: the train was carried past the pit's lip (front S={at:0.0}, lip {enc.PitS:0.0})"); return true; }
        return false;
    }

    /// <summary>
    /// =late: the lead's press arrives as the server takes a seated press (its seated window, then its late check, <see cref="RailEncounterController.LateFor"/>):
    /// on the tick the front reaches the points, and again 1 m past them. Each must read too late by the server's late check (a remote presser would be
    /// told "Too late to throw it."; nobody is told in solo), a local press there moves nothing, and the lever stays straight. True when it failed.
    /// </summary>
    private bool PitLatePresses(Main main, RailController rc, RailEncounterController enc, TrackSwitch lever, Adventurer me, Action<string> fail)
    {
        if (rc.State.Mode != CartMode.Rolling || _ptLate >= 2) return false;
        var front = rc.Party.FrontS;
        var due = _ptLate == 0 ? lever.PointsS : lever.PointsS + 1f;
        if (front < due) return false;
        var window = enc.InSeatedWindow(me, lever);
        var late = enc.LateFor(lever);
        main.Party.VrUse(true); // and a local press there: the seat offers nothing past the points (the window's grace), so it moves nothing
        main.Party.VrUse(false);
        _ptLate++;
        var told = late == LatePress.TooLate ? SwitchBackRules.TooLateLine : late == LatePress.AlreadyThrown ? "(ignored quietly)" : "(taken)";
        GD.Print($"[pit] late press {_ptLate}: at front S={front:0.0} ({front - lever.PointsS:0.0} m past the points, taken {lever.Taken}): the seated window {(window ? "yes" : "no")}, the late check {late}: a remote presser would be told \"{told}\"; the lever {(lever.Thrown ? "thrown" : "straight")}");
        if (late != LatePress.TooLate || lever.Thrown) { fail($"late press {_ptLate} at front S={front:0.0}: {late}, thrown {lever.Thrown} (want TooLate, straight)"); return true; }
        return false;
    }

    /// <summary>
    /// The pit as built (every variant's first step): its print, the party track bare over exactly [lip, far edge] on the ride (a trestle's own span
    /// aside), the pit cell and its two inward cells chasm with the enemy twin walkable, the out lever deadly and the back fixed, the fall armed. Null
    /// when all holds, else what does not.
    /// </summary>
    private string? PitBuild(Main main, Node3D root, FloorLayout layout, RailLine rail, RailController rc, RailEncounterController enc, LineShape shape)
    {
        if (rail.Pit is not { } pit || enc.PitRide != shape.LastRide || float.IsNaN(enc.PitS) || float.IsNaN(enc.PitFarS)) return $"floor 12 has no pit on ride {shape.LastRide} (pit ride {enc.PitRide}, lip {enc.PitS:0.0})";
        if (enc.PitSwitch is not { } lever || enc.PitSwitchId != Kiln(rc, "switch_out")) return $"the pit's deadly switch is {enc.PitSwitchId ?? "none"}, not {Kiln(rc, "switch_out")}";
        if (enc.SetPieceNode(Kiln(rc, "switch_back")) is not TrackSwitch back) return $"no {Kiln(rc, "switch_back")}";
        GD.Print($"[pit] floor 12 ({_ptVariant}): the pit on ride {enc.PitRide} at {rail.Party[pit.Cell]}, lip S={enc.PitS:0.0}, far edge {enc.PitFarS:0.0}; {Kiln(rc, "switch_out")} points S={lever.PointsS:0.0}, lever S={lever.LeverS:0.0} ({(lever.Deadly ? $"deadly: the pit {enc.PitS - lever.PointsS:0.0} m past" : "NOT deadly")}); {Kiln(rc, "switch_back")} points S={back.PointsS:0.0} ({(back.Fixed ? "fixed" : "NOT fixed")}, {back.PointsS - enc.PitFarS:0.0} m past the far edge); {(enc.PitArmed ? "fall armed" : "fall DISARMED")}; stops [{string.Join(",", rc.StopS.Select(s => s.ToString("0.0")))}]; lethality {TierRules.Lethality}");
        if (!lever.Deadly || lever.Fixed || !back.Fixed || !enc.PitArmed) return "the out lever is not deadly, the back not fixed, or the fall disarmed";
        // the party track's gaps on the ride: exactly the pit (a trestle's own span aside)
        if (main.Builder.PartyTrack is not { } partyTrack) return "no party track";
        float from = rc.StopS[shape.LastRide - 1], to = rc.StopS[shape.LastRide];
        var trestle = enc.SetPieceNode(Kiln(rc, "trestle")) as Trestle;
        var gaps = partyTrack.Gaps.Where(g => g.to > from && g.from < to && !(trestle != null && g.from >= trestle.From - 0.01f && g.to <= trestle.To + 0.01f)).ToList();
        GD.Print($"[pit] the party track bare on ride {shape.LastRide} over [{string.Join(", ", gaps.Select(g => $"{g.from:0.0}..{g.to:0.0}"))}]");
        if (gaps.Count != 1 || Mathf.Abs(gaps[0].from - enc.PitS) > 0.01f || Mathf.Abs(gaps[0].to - enc.PitFarS) > 0.01f) return $"the party track is not bare over exactly the pit [{enc.PitS:0.0}, {enc.PitFarS:0.0}] on ride {shape.LastRide}";
        // the cut: the pit cell and its two inward cells chasm; the enemy twin walkable
        var twin = rail.Party[pit.Cell].Neighbours4().Cast<GridPos?>().FirstOrDefault(n => layout.IsRail(n!.Value) && !layout.IsPartyRail(n.Value));
        GD.Print($"[pit] the cut: [{string.Join(", ", pit.Cut.Select(c => $"{c} {(layout.IsChasm(c) ? "chasm" : layout[c].ToString())}"))}]; the enemy twin {twin?.ToString() ?? "none"} {(twin is { } t ? layout[t].ToString() : "")}");
        if (pit.Cut.Count != 3 || pit.Cut[0] != rail.Party[pit.Cell] || pit.Cut.Any(c => !layout.IsChasm(c))) return "the pit cell and its two inward cells are not all chasm";
        if (twin is not { } tw || !layout.IsWalkable(tw) || layout.IsChasm(tw)) return $"the pit's enemy twin {twin?.ToString() ?? "none"} is not walkable";
        // the dressing: the lamps at both edges, the glow and the haze, no shadow
        if (enc.PitLook is not { } look) return "no LavaPit dressing";
        var near = look.GetNodeOrNull<OmniLight3D>("LampLight");
        var farLamp = look.GetNodeOrNull<OmniLight3D>("FarLampLight");
        var glow = look.GetNodeOrNull<OmniLight3D>("PitGlow");
        var haze = look.GetNodeOrNull<MeshInstance3D>("PitHaze");
        var leverLamp = lever.GetNodeOrNull<Node3D>("PitLamp")?.GetNodeOrNull<OmniLight3D>("PitLampLight");
        GD.Print($"[pit] the LavaPit dressing at {DungeonBuilder.WorldToCell(look.GlobalPosition)}: {look.GetChildCount()} parts, the near lamp {near?.OmniRange:0} m, the far lamp {farLamp?.OmniRange:0} m, the glow {glow?.OmniRange:0} m, its haze {(haze != null ? "standing" : "MISSING")}; the lever's red lamp {(leverLamp != null ? $"{leverLamp.OmniRange:0} m" : "MISSING")}");
        if (near == null || farLamp == null || near.ShadowEnabled || farLamp.ShadowEnabled || glow == null || haze == null || leverLamp == null) return "the pit's lamps, glow, haze or the lever's red lamp are missing (or cast shadows)";
        return null;
    }
}
