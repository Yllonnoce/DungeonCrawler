using DungeonCrawler.Dungeon.Rails;
using DungeonCrawler.Party;
using Godot;

namespace DungeonCrawler.Core;

// ---- --test-cart-watchdog ----------------------------------------------------------------------------------
// The party cart's watchdog (plan 5 task 8) on the built-in campaign's floor 12 (no enemies): the human boards at the landing; on
// ride 1 the cart is caught (a test hook: it rolls and goes nowhere) while the human holds the brake for 15 s, and nothing frees it;
// the brake let go, it grinds free to stop 1 as an arrival 10 s later (the brake closed the window: a fresh one opens at the let-go).
// The window re-anchors at each half-metre of progress: a caught cart is freed 10 s after it last moved. Then from the kiln's stop (the
// test's shortcut, rc.Restore(LastRideStop): stop 4 on the shipped floor) on the kiln's ride it is caught with its front 25 m before the
// lava pit switch's lever (plan 15 Task 4): the watchdog sets it at the switch's seated window's start, still rolling, never carried past
// the pit; the lead throws the switch from the seat (ThrowPitOnTheWay), and the train goes round the pit, comes home past its far edge
// and arrives at the last stop with no fall.
// Prints [watchdog] lines and a RESULT.
public partial class DebugTools
{
    private bool _testCartWatchdog;
    private int _wdStep;
    private float _wdClock;
    private int _wdFreedBefore, _wdFallsBefore;
    private bool _wdAcross, _wdHome;

    /// <summary>The cart's brake is held this long while it is caught: a player who stops the cart on purpose is not stuck.</summary>
    const float WatchdogBrakeSeconds = 15f;

    private void RunCartWatchdogTest()
    {
        var main = Main.Instance;
        if (main?.Builder.Layout is not { } layout || main.Builder.FloorRoot is not { } root || !main.InGame || Game.Instance.CurrentFloor != 12) return;
        _wdClock += (float)GetProcessDeltaTime();
        void Fail(string why) { GD.Print($"[watchdog] RESULT FAIL: {why}"); if (main.Party != null) main.Party.ForcedMoveInput = null; _done = true; GetTree().Quit(1); }
        var party = main.Party;
        foreach (var e in root.GetChildren().OfType<Enemies.Enemy>())
            if (e.IsAlive) e.Kill(); // no enemies: the stop mobs, ambushers and riders are culled as they appear
        var rc = RailController.Instance;
        if (rc == null) { if (_wdClock > 3f) Fail("no RailController on floor 12"); return; }
        RideWaiveCages(main, rc, "watchdog");

        switch (_wdStep)
        {
            case 0 when _wdClock >= 1.5f:
                rc.RestoreGates(Enumerable.Range(1, Shape(rc).Intermediate)); // the test's shortcut: no holders to kill
                if (!RideBoard(main, layout, rc, out var why)) { Fail(why); return; }
                _wdStep++; _wdClock = 0f;
                break;
            case 1:
                if (rc.State.Mode == CartMode.Rolling && rc.StopIndex == 0 && rc.State.S >= rc.StopS[0] + 8f)
                {
                    rc.DebugStall = true;
                    party.ForcedMoveInput = new Vector2(0, 1); // the stick back: the brake
                    _wdFreedBefore = rc.GroundFree;
                    GD.Print($"[watchdog] 1 caught on ride 1 at S={rc.State.S:0.0} with the brake held");
                    _wdStep++; _wdClock = 0f;
                }
                else if (_wdClock > 20f) Fail($"the cart did not roll out of the landing (mode {rc.State.Mode}, S={rc.State.S:0.0})");
                break;
            case 2:
                if (rc.GroundFree != _wdFreedBefore) { Fail($"the watchdog freed a cart held by the brake after {_wdClock:0.0} s"); return; }
                if (rc.State.Mode != CartMode.Rolling) { Fail($"the caught cart stopped rolling while braked (mode {rc.State.Mode})"); return; }
                if (_wdClock < WatchdogBrakeSeconds) break;
                party.ForcedMoveInput = null;
                GD.Print($"[watchdog] 2 braked {_wdClock:0.0} s at V={rc.State.V:0.00} m/s: not stuck; the brake let go, still caught");
                _wdStep++; _wdClock = 0f;
                break;
            case 3:
                if (rc.GroundFree == _wdFreedBefore)
                {
                    if (_wdClock > CartWatchdog.WindowSeconds + 2f) Fail($"the caught cart was not freed {_wdClock:0.0} s after the brake was let go (S={rc.State.S:0.0})");
                    break;
                }
                GD.Print($"[watchdog] 3 ground free {_wdClock:0.0} s after the brake was let go: {rc.State.Mode} at stop {rc.StopIndex} (S={rc.State.S:0.0}, stop 1 at {rc.StopS[1]:0.0})");
                if (_wdClock < CartWatchdog.WindowSeconds - 0.5f) { Fail($"freed after only {_wdClock:0.0} s"); return; }
                if (rc.State.Mode != CartMode.Held || rc.StopIndex != 1 || Mathf.Abs(rc.State.S - rc.StopS[1]) > 0.01f) { Fail("the freed cart did not arrive at stop 1"); return; }
                _wdStep++; _wdClock = 0f;
                break;
            case 4 when _wdClock >= 1f:
                // the kiln's ride: from its stop (the test's shortcut over the rides between), caught short of the pit switch's window
                if (party.Local is { } me && me.Seated) me.Teleport(me.GlobalPosition);
                rc.Restore(Shape(rc).LastRideStop);
                _wdStep++; _wdClock = 0f;
                break;
            case 5 when _wdClock >= 1f:
                if (!RideBoard(main, layout, rc, out var why4)) { Fail(why4); return; }
                _wdStep++; _wdClock = 0f;
                break;
            case 6:
            {
                // the kiln's ride: caught with the front 25 m before the pit switch's lever (plan 15 Task 4, B.10)
                var shape = Shape(rc);
                if (RailEncounterController.Instance is not { PitSwitch: { } lever } enc || enc.PitRide != shape.LastRide) { Fail($"ride {shape.LastRide} has no pit switch"); return; }
                if (rc.State.Mode == CartMode.Rolling && rc.StopIndex == shape.LastRideStop && rc.Party.FrontS >= lever.LeverS - 25f)
                {
                    rc.DebugStall = true;
                    _wdFreedBefore = rc.GroundFree;
                    _wdFallsBefore = rc.Falls;
                    GD.Print($"[watchdog] 4 caught on ride {shape.LastRide} at front S={rc.Party.FrontS:0.0}, {lever.LeverS - rc.Party.FrontS:0.0} m before the pit switch's lever ({lever.LeverS:0.0}; its window opens at {lever.LeverS - SeatedSwitchRules.WindowAhead:0.0}, the lip at {enc.PitS:0.0}), stop {shape.Rides} at {rc.StopS[shape.Rides]:0.0}");
                    _wdStep++; _wdClock = 0f;
                }
                else if (_wdClock > 20f) Fail($"the cart did not leave stop {shape.LastRideStop} (mode {rc.State.Mode}, stop {rc.StopIndex}, S={rc.State.S:0.0})");
                break;
            }
            case 7:
            {
                if (rc.GroundFree == _wdFreedBefore)
                {
                    if (_wdClock > CartWatchdog.WindowSeconds + 2f) Fail($"the caught cart was not freed on ride {Shape(rc).LastRide} after {_wdClock:0.0} s (mode {rc.State.Mode}, S={rc.State.S:0.0}, V={rc.State.V:0.00}, halted {rc.Halted}, stop {rc.StopIndex}, seated [{string.Join(", ", party.Adventurers.Where(a => IsInstanceValid(a) && a.Seated).Select(a => $"{a.State.Name}:{a.Control}"))}])");
                    break;
                }
                if (RailEncounterController.Instance is not { PitSwitch: { } lever }) { Fail("no pit switch"); return; }
                var windowStart = lever.LeverS - SeatedSwitchRules.WindowAhead;
                GD.Print($"[watchdog] 5 ground free {_wdClock:0.0} s after it was caught: front S={rc.Party.FrontS:0.0} (the pit switch's window starts at {windowStart:0.0}), {rc.State.Mode}, stop {rc.StopIndex}; the lever {(lever.Thrown ? "thrown" : "straight")}, taken {lever.Taken}");
                if (rc.State.Mode != CartMode.Rolling || rc.StopIndex != Shape(rc).LastRideStop) { Fail("the freed train is not still rolling on the kiln's ride"); return; }
                if (rc.Party.FrontS < windowStart - 0.05f || rc.Party.FrontS > windowStart + 0.5f || lever.Taken || lever.Thrown) { Fail($"the freed train was not set at the pit switch's window start {windowStart:0.0} with its lever straight"); return; }
                _wdStep++; _wdClock = 0f;
                break;
            }
            case 8:
            {
                if (RailEncounterController.Instance is not { PitSwitch: { } lever } enc || enc.SetPieceNode(Kiln(rc, "switch_back")) is not TrackSwitch back) { Fail("no pit switch"); return; }
                ThrowPitOnTheWay(main, rc, "watchdog"); // the lead throws it from the seat as its window offers it
                if (rc.State.Mode == CartMode.Wrecked || rc.Falls != _wdFallsBefore) { Fail($"the train was wrecked or fell after the watchdog set it at the window (falls {rc.Falls - _wdFallsBefore})"); return; }
                if (rc.Crossing) _wdAcross = true;
                if (rc.Crossing && !_wdHome && rc.Party.FrontS >= back.PointsS + RailController.SwitchStretch + CartRules.HalfLength + 1f)
                {
                    if (rc.LaneBlend > 0.01f) { Fail($"the train is not home on its own lane past the far edge (blend {rc.LaneBlend:0.000} at front S={rc.Party.FrontS:0.0})"); return; }
                    _wdHome = true;
                    GD.Print($"[watchdog] 6 home on its own lane at front S={rc.Party.FrontS:0.0}, past the pit's far edge {enc.PitFarS:0.0} (across from {rc.CrossOutS:0.0}, back from {rc.CrossBackS:0.0})");
                }
                var shape = Shape(rc);
                if (rc.State.Mode != CartMode.Held || rc.StopIndex != shape.Rides) { if (_wdClock > 90f) Fail($"the train did not arrive at stop {shape.Rides} (mode {rc.State.Mode}, stop {rc.StopIndex}, front S={rc.Party.FrontS:0.0})"); break; }
                if (!_wdAcross || !_wdHome || float.IsNaN(_pitThrownFront)) { Fail($"the train arrived without the lead's throw ({_pitThrownFront:0.0}), the crossing ({_wdAcross}) or the home check ({_wdHome})"); return; }
                GD.Print($"[watchdog] RESULT ok: braked 15 s while caught and nothing freed it; let go, it ground free to stop 1 as an arrival; caught on ride {shape.LastRide} 25 m before the pit switch's lever it was set at the window's start still rolling, the lead threw the pit switch from the seat (front S={_pitThrownFront:0.0}), the train went round the pit and came home past its far edge ({enc.PitFarS:0.0}) with no fall and arrived at stop {shape.Rides}; ground free {rc.GroundFree} time(s)");
                _done = true;
                GetTree().Quit(0);
                break;
            }
        }
    }
}
