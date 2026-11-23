using DungeonCrawler.Adventure;
using DungeonCrawler.Dungeon.Rails;
using DungeonCrawler.Party;
using Godot;

namespace DungeonCrawler.Core;

// ---- --test-boarders=pause (plan 14, Task 5): see DebugTools.Boarders.cs for the flow as a whole -----------------------------------------
// As =throw (nobody answers, the companions hold fire, the hound never drops below 1 HP), restored at stop 2 with its two crew seated in W1,
// rolling on ride 3 with every hero in the cart. A hound is put on W1 when r3_switch_out's seated window (the front at the lever − 15 m) is
// 3.0 s away at the train's speed; nobody throws the lever (r3_heap, which blocks the straight line past it, is cleared by the test's hook).
//  - Inside the window: MovesRefused holds, MoveOfferFor is null for every hero, and W1's clock does not move (the same count every frame).
//  - After it: the throw comes at 6.0 ± 0.1 s of counted time, printed with its paused seconds, which are the window's length (its frames
//    while open, read here) within 0.1 s. The count before the window is printed (3.0 s meant; 2.5–3.5 s accepted, the speed varies).
//  - The reset (Review Focus 3): in the physics tick (RailEncounterController.DebugThrowTick) the lead's move to W1 is pressed with the grip
//    HopSeconds (36 ticks) before the next throw is due, so it lands on the throw's own tick: the flow prints both ticks, which must be equal, and
//    no crew member is thrown (watched to 4 s past the tick it was due).
public partial class DebugTools
{
    private ulong _bdOpenFrame, _bdCloseFrame, _bdPressFrame, _bdDueFrame, _bdLandedFrame;
    private float _bdOpenCount = -1f;

    private void BoardersPause(RailController rc, RailEncounterController enc, PartyController party, Adventurer me, Action<string> Fail)
    {
        var t = _bdClock - _bdPhaseT;
        void Next(int phase) { _bdPhase = phase; _bdPhaseT = _bdClock; }
        TrainWagon W(int k) => rc.WagonAt(k)!;
        if (rc.State.Mode != CartMode.Rolling) { Fail($"the train stopped ({rc.State.Mode} at stop {rc.StopIndex}) at phase {_bdPhase}, before the script was done"); return; }
        if (enc.SetPieceNode("r3_switch_out") is not TrackSwitch sw) { Fail("floor 12 lacks r3_switch_out"); return; }
        var windowAt = sw.SeatedView.LeverS - SeatedSwitchRules.WindowAhead;
        var open = enc.AnyLeverWindow();
        switch (_bdPhase)
        {
            case 0:
                enc.DebugHoldAnswers = true;
                foreach (var a in party.Adventurers) if (IsInstanceValid(a) && a.Control == ControlKind.Companion) a.DebugHoldFire = true;
                me.SeatYaw = BdBack; // the second pass's press: W1 is offered looking back from the cart
                if (enc.SetPieceNode("r3_heap") is OreHeap { IsAlive: true } heap) { heap.DebugClear(); GD.Print("[boarders] pause: r3_heap cleared by the test (the lever is left straight: the train rides on past it)"); }
                GD.Print($"[boarders] pause: r3_switch_out's window opens with the front at S={windowAt:0.0} (lever S={sw.SeatedView.LeverS:0.0}, points S={sw.SeatedView.PointsS:0.0}); the front at S={rc.Party.FrontS:0.0}, v {rc.State.V:0.00}; W1 crew [{string.Join(", ", W(1).Riders.Values.OfType<Npc>().Select(n => $"{n.NpcName} seat {n.Seat}"))}]");
                if (rc.Party.FrontS >= windowAt) { Fail("the train is already past r3_switch_out's window"); return; }
                Next(1);
                break;
            case 1:
            {
                // the hound lands with 3.0 s of the alone stretch left before the window
                var gap = windowAt - rc.Party.FrontS;
                if (open || gap <= 0f) { Fail("the window opened before the hound was put on W1"); return; }
                if (gap > 3f * rc.State.V || rc.State.V < 1f) break;
                _bdLogMark = enc.ThrowLog.Count;
                _bdThrowsBase = enc.Throws;
                _bdOpenFrame = _bdCloseFrame = _bdPressFrame = _bdDueFrame = _bdLandedFrame = 0;
                _bdOpenCount = -1f;
                _bdHound = enc.DebugBoard(1, holdHp: true);
                if (_bdHound == null) { Fail("no hound could be put on W1"); return; }
                _bdLandFrame = Engine.GetPhysicsFrames();
                GD.Print($"[boarders] pause: a hound lands on W1 at tick {_bdLandFrame}, the front {gap:0.0} m short of the window at v {rc.State.V:0.00} ({gap / rc.State.V:0.00} s)");
                Next(2);
                break;
            }
            case 2:
            {
                // the window: refused, nothing offered, the clock still
                var clock = enc.ThrowClockOf(1);
                if (open && _bdCloseFrame == 0) // r3_switch_out's (a later lever's window, if any, is not this check's)
                {
                    if (_bdOpenFrame == 0)
                    {
                        _bdOpenFrame = Engine.GetPhysicsFrames();
                        _bdOpenCount = clock?.Running ?? -1f;
                        GD.Print($"[boarders] pause: the window opens at tick {_bdOpenFrame} ({BdSince(_bdOpenFrame):0.00} s after the landing, the front at S={rc.Party.FrontS:0.0}) with {_bdOpenCount:0.000} s counted alone");
                        if (clock is not { Alone: true }) { Fail("W1 is not alone at the window"); return; }
                    }
                    var offered = party.Adventurers.Where(a => IsInstanceValid(a) && rc.MoveOfferFor(a) != null).Select(a => a.State.Name).ToList();
                    if (!rc.MovesRefused || offered.Count > 0) { Fail($"in the window moves refused {rc.MovesRefused}, offered to [{string.Join(", ", offered)}]"); return; }
                    if (clock == null || Mathf.Abs(clock.Running - _bdOpenCount) > 1e-5f) { Fail($"W1's clock moved in the window ({_bdOpenCount:0.000} -> {clock?.Running:0.000} s)"); return; }
                    break;
                }
                if (_bdOpenFrame == 0) { if (BdSince(Engine.GetPhysicsFrames()) > 8f) Fail("r3_switch_out's window never opened"); break; }
                if (_bdCloseFrame == 0)
                {
                    _bdCloseFrame = Engine.GetPhysicsFrames();
                    GD.Print($"[boarders] pause: the window closes at tick {_bdCloseFrame} after {(_bdCloseFrame - _bdOpenFrame) / BdTps:0.00} s (the front at S={rc.Party.FrontS:0.0}); W1's clock {clock?.Running:0.000} s, paused {clock?.Paused:0.00} s");
                    if (_bdOpenCount < 2.5f || _bdOpenCount > 3.5f) { Fail($"{_bdOpenCount:0.00} s of the alone stretch passed before the window, not about 3.0"); return; }
                }
                var thrown = BdThrowEntries(enc, 1, _bdLogMark).FirstOrDefault(e => e.Step == ThrowStep.Throw);
                if (thrown.Step != ThrowStep.Throw) { if (BdSince(Engine.GetPhysicsFrames()) > 15f) Fail("W1's throw never came after the window"); break; }
                var window = (_bdCloseFrame - _bdOpenFrame) / BdTps;
                GD.Print($"[boarders] pause: {thrown.Who} thrown off W1 at {thrown.Counted:0.000} s counted ({thrown.Paused:0.000} s paused; the window {window:0.000} s), {BdSince(thrown.Frame):0.00} s after the landing");
                if (Mathf.Abs(thrown.Counted - BoarderRules.ThrowFirst) > 0.1f || Mathf.Abs(thrown.Paused - window) > 0.1f) { Fail($"the throw came at {thrown.Counted:0.000} s counted with {thrown.Paused:0.000} s paused; wanted {BoarderRules.ThrowFirst} ± 0.1 and the window's {window:0.000} ± 0.1"); return; }
                if (BdSince(thrown.Frame) < BoarderRules.ThrowFirst + window - 0.1f) { Fail("the throw came before the window's pause was added"); return; }
                // the second pass: the press made in the physics tick 36 ticks before the next throw is due
                var hop = Mathf.RoundToInt(TrainMoveRules.HopSeconds * BdTps);
                enc.DebugThrowTick = (k, c) =>
                {
                    if (k != 1 || _done) return;
                    var now = Engine.GetPhysicsFrames();
                    if (_bdPressFrame != 0)
                    {
                        // the landing's own tick: its log line's frame (the rail controller's tick), not this hook's first sight of it
                        if (_bdLandedFrame == 0 && rc.CarOf(me) == 1 && !rc.Hopping(me) && _bdClimbFrame > _bdPressFrame) _bdLandedFrame = _bdClimbFrame;
                        return;
                    }
                    if (!c.Alone) return;
                    var dt = 1f / BdTps;
                    var x = c.Running;
                    var n = 0;
                    while (x < c.NextThrowAt) { x += dt; n++; } // the tick the clock will cross it, stepped as it steps
                    if (n != hop) return;
                    if (rc.MovesRefused || rc.MoveOfferFor(me) != W(1) || !ReferenceEquals(me.LookingAt, W(1))) { Fail($"no move to W1 offered {hop} ticks before the throw (refused {rc.MovesRefused})"); return; }
                    party.VrUse(true);
                    party.VrUse(false);
                    if (!rc.Hopping(me)) { Fail($"the press {hop} ticks before the throw took no move (refused: {rc.LastMoveRefused ?? "-"})"); return; }
                    _bdPressFrame = now;
                    _bdDueFrame = now + (ulong)n;
                    GD.Print($"[boarders] pause: the lead presses to W1 at tick {now}, {n} ticks before the throw due at tick {_bdDueFrame} ({c.Running:0.000} of {c.NextThrowAt:0.0} s counted)");
                };
                Next(3);
                break;
            }
            case 3:
            {
                if (_bdPressFrame == 0) { if (t > 8f) Fail("the second pass's press never came"); break; }
                if (_bdLandedFrame == 0) { if (t > 8f) Fail("the lead never landed in W1"); break; }
                if (Engine.GetPhysicsFrames() < _bdDueFrame + (ulong)(4f * BdTps)) break;
                enc.DebugThrowTick = null;
                var log = BdThrowEntries(enc, 1, _bdLogMark);
                var crew = W(1).Riders.Values.OfType<Npc>().Count(n => n.Crew && !n.Dead);
                var clock = enc.ThrowClockOf(1);
                GD.Print($"[boarders] pause: the reset: the lead landed in W1 at tick {_bdLandedFrame}, the throw's own tick {_bdDueFrame}; W1's log [{string.Join(", ", log.Select(e => e.Step))}], throws {enc.Throws - _bdThrowsBase}, {crew} crew left, its clock alone {clock?.Alone} at {clock?.Running:0.00} s");
                if (_bdLandedFrame != _bdDueFrame) { Fail($"the landing came at tick {_bdLandedFrame}, not the throw's own tick {_bdDueFrame}"); return; }
                if (enc.Throws - _bdThrowsBase != 1 || log.Count(e => e.Step == ThrowStep.Throw) != 1 || crew != 1 || clock is not { Alone: false, Running: 0f }) { Fail("a crew member was thrown on the landing's tick, or the clock did not reset"); return; }
                GD.Print($"[boarders] RESULT ok ({_bdVariant}): r3_switch_out's window ({(_bdCloseFrame - _bdOpenFrame) / BdTps:0.00} s) refused every move, offered none and held W1's clock at {_bdOpenCount:0.00} s; the throw came at 6.0 s counted with the window's seconds paused; a move landing on the next throw's own tick reset the clock and nobody more was thrown");
                _done = true;
                GetTree().Quit(0);
                break;
            }
        }
    }
}
