using DungeonCrawler.Dungeon.Rails;
using DungeonCrawler.Party;
using Godot;

namespace DungeonCrawler.Core;

// ---- --test-cart-watchdog ----------------------------------------------------------------------------------
// The party cart's watchdog (plan 5 task 8) on the built-in campaign's floor 12 (no enemies): the human boards at the landing; on
// ride 1 the cart is caught (a test hook: it rolls and goes nowhere) while the human holds the brake for 15 s, and nothing frees it;
// the brake let go, it grinds free to stop 1 as an arrival 10 s later (the brake closed the window: a fresh one opens at the let-go).
// The window re-anchors at each half-metre of progress: a caught cart is freed 10 s after it last moved. Then from stop 3 on ride 4 it is caught short of the jump gap:
// the watchdog sets it at the gap's near lip at the jump's speed, and the jump is judged (the cart leaps it), never carried across.
// Prints [watchdog] lines and a RESULT.
public partial class DebugTools
{
    private bool _testCartWatchdog;
    private int _wdStep;
    private float _wdClock;
    private int _wdFreedBefore;
    private bool _wdLeapt;

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

        switch (_wdStep)
        {
            case 0 when _wdClock >= 1.5f:
                rc.RestoreGates(new[] { 1, 2, 3 }); // the test's shortcut: no holders to kill
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
                // ride 4: from stop 3 (the test's shortcut over rides 2 and 3), caught short of the jump gap
                if (party.Local is { } me && me.Seated) me.Teleport(me.GlobalPosition);
                rc.Restore(3);
                _wdStep++; _wdClock = 0f;
                break;
            case 5 when _wdClock >= 1f:
                if (!RideBoard(main, layout, rc, out var why4)) { Fail(why4); return; }
                _wdStep++; _wdClock = 0f;
                break;
            case 6:
                if (rc.State.Mode == CartMode.Rolling && rc.StopIndex == 3 && rc.State.S >= rc.StopS[3] + 5f)
                {
                    if (float.IsNaN(rc.GapFrom) || rc.GapFrom > rc.StopS[4]) { Fail($"ride 4 has no gap before stop 4 (gap from {rc.GapFrom:0.0})"); return; }
                    rc.DebugStall = true;
                    _wdFreedBefore = rc.GroundFree;
                    GD.Print($"[watchdog] 4 caught on ride 4 at S={rc.State.S:0.0}, the gap's lip at {rc.GapFrom:0.0}, stop 4 at {rc.StopS[4]:0.0}");
                    _wdStep++; _wdClock = 0f;
                }
                else if (_wdClock > 20f) Fail($"the cart did not leave stop 3 (mode {rc.State.Mode}, stop {rc.StopIndex}, S={rc.State.S:0.0})");
                break;
            case 7:
                if (rc.GroundFree == _wdFreedBefore)
                {
                    if (_wdClock > CartWatchdog.WindowSeconds + 2f) Fail($"the caught cart was not freed on ride 4 after {_wdClock:0.0} s (mode {rc.State.Mode}, S={rc.State.S:0.0}, V={rc.State.V:0.00}, halted {rc.Halted}, stop {rc.StopIndex}, seated [{string.Join(", ", party.Adventurers.Where(a => IsInstanceValid(a) && a.Seated).Select(a => $"{a.State.Name}:{a.Control}"))}])");
                    break;
                }
                _wdLeapt = rc.Jumped;
                GD.Print($"[watchdog] 5 ground free {_wdClock:0.0} s after it was caught: S={rc.State.S:0.0} (front {rc.Party.FrontS:0.0}, lip {rc.GapFrom:0.0}), {rc.State.Mode}, the jump judged: leapt={rc.Jumped} at {rc.JumpSpeed:0.00} m/s, stop {rc.StopIndex}");
                if (!_wdLeapt || rc.State.Mode != CartMode.Rolling || rc.StopIndex != 3) { Fail("the freed cart did not leap the gap from its lip"); return; }
                if (rc.Party.FrontS < rc.GapFrom || rc.Party.FrontS > rc.GapFrom + 0.5f) { Fail("the freed cart was not set at the gap's near lip"); return; }
                _wdStep++; _wdClock = 0f;
                break;
            case 8:
                if (rc.State.Mode == CartMode.Wrecked) { Fail("the cart was wrecked after the watchdog's jump"); return; }
                if (rc.State.S < rc.GapTo + CartRules.HalfLength) { if (_wdClock > 6f) Fail($"the cart did not clear the gap (S={rc.State.S:0.0}, far lip {rc.GapTo:0.0})"); break; }
                GD.Print($"[watchdog] RESULT ok: braked 15 s while caught and nothing freed it; let go, it ground free to stop 1 as an arrival; caught on ride 4 it was set at the gap's lip at {SetPieceRules.JumpSpeed:0.0} m/s and leapt it (S={rc.State.S:0.0} past the far lip {rc.GapTo:0.0}); ground free {rc.GroundFree} time(s)");
                _done = true;
                GetTree().Quit(0);
                break;
        }
    }
}
