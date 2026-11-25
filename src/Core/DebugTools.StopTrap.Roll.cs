using DungeonCrawler.Adventure;
using DungeonCrawler.Dungeon;
using DungeonCrawler.Dungeon.Generation;
using DungeonCrawler.Dungeon.Rails;
using DungeonCrawler.Party;
using Godot;

namespace DungeonCrawler.Core;

// ---- --test-stop-trap=roll (plan 16 Task 5) ---------------------------------------------------------------------------------------
// The reverse roll, driverless (RailController.DebugRollBack), at stop 2. In order (any step out of its window fails):
//  setup: rc.Restore(2) and rc.DebugKillHolders(2); once the gate is up the lead boards and gets out (its companions are set down). One
//      companion stands on the party lane 15 m behind the last wagon's back face, an enemy.horde_cinder (DebugHoldHp, stunned) on the rails
//      25 m behind that face. Every CartEvent is counted.
//  (1) DebugRollBack() is true and the Hijacked event comes (the controller prints "rolls back from stop 2").
//  (2) V at 2 s is 1.0 +- 0.3 and never above 4.05.
//  (3) every frame of every roll: the cart's forward . the line's +S tangent > 0.9, each Wk stands at the cart's S - 2.6k +- 0.05 (measured off
//      the bodies) and faces the cart's way (+S, read off the line where it stands: a bend turns the line between them).
//  (4) the companion's "Cart coming!" comes while the leading face is within its 3 s look of it (from the face, never the cart's own back);
//      the flow then steps it off the rails.
//  (5) nothing confused: no Departed, Arrived or Resumed, StopIndex 2, ArmedRide -1, no grind (GroundFree), no gate change, spare:2 prompts "".
//  (6) DebugEndRollBack() at 8 s: V 0 within 0.8 s, the return at 2 +- 0.1 m/s, Held at StopS[2] +- 0.05 with HijackOver.
//  (7) a second roll left alone reaches HijackLimitS +- 0.1, brakes and returns (Ruling 11); the cinder is hit with the cart's S within 0.4 of
//      its S + 11.5 (the last wagon's back face).
//  (8) a third roll saved at 4 s and loaded: Held at StopS[2], Hijack None, Facing +1, every Wk at S - 2.6k, Cart.Driver null.
//  (9) a real departure: the lead boards, the countdown starts, and the Departed from stop 2 arms ride 3.
public partial class DebugTools
{
    private const int RlStop = 2;
    private const float RlCompanionBehind = 15f, RlCinderBehind = 25f;
    private RailController? _rlRc;
    private readonly List<(double At, CartEventKind Kind, int Stop, ulong Target, float S, float V)> _rlEvents = new();
    private readonly List<(double At, float S, float V)> _rlWarned = new();
    private bool _rlListening;
    private Adventurer? _rlMate;
    private Enemies.Enemy? _rlCinder;
    /// <summary>The cinder's instance id (it may be freed once run over).</summary>
    private ulong _rlCinderId;
    private float _rlMateS, _rlCinderS, _rlLimit, _rlMinS, _rlMaxV, _rlReturnMaxV;
    private double _rlRollAt, _rlBrakeAt = -1, _rlStoppedAt = -1, _rlReturnAt = -1;
    private float _rlV2 = float.NaN;
    private bool _rlMateMoved;
    private int _rlGroundFree, _rlReturnSamples, _rlReturnInBand;
    private string _rlGates = "";
    private string _rlSummary = "";

    private void RollListen()
    {
        if (_rlListening) return;
        _rlListening = true;
        Action<BarkTrigger, string?> called = (trigger, _) =>
        {
            if (trigger != BarkTrigger.CartComing || _rlRc is not { } rc || !IsInstanceValid(rc) || !rc.HijackLive) return;
            _rlWarned.Add((StNow, rc.State.S, rc.State.V));
        };
        RailCalls.Called += called;
        _staticUnhooks.Add(() => RailCalls.Called -= called);
    }

    /// <summary>Each frame: the current line's events are heard (a load builds a new one).</summary>
    private void RollWatchRails(RailController rc)
    {
        if (rc == _rlRc) return;
        _rlRc = rc;
        rc.CartEvents += e =>
        {
            _rlEvents.Add((StNow, e.Kind, e.Stop, e.Target, rc.State.S, rc.State.V));
            if (e.Kind is not (CartEventKind.Seated or CartEventKind.Unseated or CartEventKind.Hit)) GD.Print($"[stoptrap] heard: {e.Kind} (stop {e.Stop}) at S={rc.State.S:0.00}");
        };
    }

    private int RollEventsSince(double since, params CartEventKind[] kinds) => _rlEvents.Count(e => e.At >= since && kinds.Contains(e.Kind));

    /// <summary>A body's S on the party line near <paramref name="hint"/> (NaN when it is not near the line).</summary>
    private static float RollS(RailController rc, Vector3 pos, float hint) => rc.Party.Track.Measure(pos, hint, 6f, out var b) ? b.S : float.NaN;

    /// <summary>Check (3): the cart faces +S and each wagon stands 2.6k behind it facing its way. Null when all is well.</summary>
    private static string? RollPoseWrong(RailController rc)
    {
        var track = rc.Party.Track;
        var cartS = RollS(rc, rc.Cart.GlobalPosition, rc.State.S);
        if (float.IsNaN(cartS)) return "the cart is off its line";
        var forward = -rc.Cart.GlobalTransform.Basis.Z;
        var tangent = -track.Frame(cartS).Basis.Z;
        if (forward.Dot(tangent) <= 0.9f) return $"the cart faces {forward.Dot(tangent):0.00} along the line at S={cartS:0.00}";
        for (var k = 0; k < rc.Train.Count; k++)
        {
            var w = rc.Train[k];
            var want = cartS - TrainRules.Spacing * (k + 1);
            var at = RollS(rc, w.GlobalPosition, want);
            if (float.IsNaN(at) || Mathf.Abs(at - want) > 0.05f) return $"W{w.Number} stands at S={at:0.00}, not the cart's {cartS:0.00} - {TrainRules.Spacing * (k + 1):0.0}";
            var dot = (-w.GlobalTransform.Basis.Z).Dot(-track.Frame(at).Basis.Z); // the cart's way (+S) where the wagon stands: a bend turns the line between them
            if (dot <= 0.9f) return $"W{w.Number} faces {dot:0.00} along the line at its S={at:0.00}, not the cart's way";
        }
        return null;
    }

    private void RunStopTrapRoll(Main main, FloorLayout layout, Node3D root, RailController rc)
    {
        var party = main.Party;
        void Fail(string why) => StopTrapFail(why);
        RollWatchRails(rc);
        var me = party.Local;
        var enc = RailEncounterController.Instance is { } e && IsInstanceValid(e) ? e : null;

        // every frame of a roll: the facing, the wagons and the speed cap
        if (rc.HijackLive && rc.State.Mode == CartMode.Rolling)
        {
            if (RollPoseWrong(rc) is { } wrong) { Fail($"({_stStep}) {wrong} (V {rc.State.V:0.00}, {rc.Hijack})"); return; }
            if (rc.Party.Reversing) { _rlMaxV = Mathf.Max(_rlMaxV, rc.State.V); _rlMinS = Mathf.Min(_rlMinS, rc.State.S); }
            if (rc.State.V > 4.05f) { Fail($"the roll back reached {rc.State.V:0.00} m/s, over 4.05"); return; }
        }

        switch (_stStep)
        {
            case 0 when _stClock >= 1.5f:
            {
                if (rc.StopS.Length != Floor12Stops) { Fail($"floor 12's line has {rc.StopS.Length} stops, not {Floor12Stops}"); return; }
                RollListen();
                rc.Restore(RlStop);
                var killed = rc.DebugKillHolders(RlStop);
                foreach (var a in party.Adventurers) if (IsInstanceValid(a) && a.Control == ControlKind.Companion) a.Stance = Characters.Stance.Hold;
                GD.Print($"[stoptrap] roll: rc.Restore({RlStop}) at S={rc.State.S:0.0}; {killed} holder(s) die by the test's hand; train [{string.Join(",", rc.Train.Select(w => w.Number))}]");
                _stStep++; _stClock = 0f;
                break;
            }
            case 1:
            {
                if (!rc.GateUp(RlStop)) { if (_stClock > 5f) Fail($"stop {RlStop}'s gate did not lift with its holders dead"); break; }
                if (_stClock < 1f) break;
                if (!RideBoard(main, layout, rc, out var why)) { Fail(why); return; }
                _stStep++; _stClock = 0f;
                break;
            }
            case 2 when _stClock >= 1.5f:
            {
                if (AmGetOut(main, rc) is { } why) { Fail($"roll: {why}"); return; }
                _stStep++; _stClock = 0f;
                break;
            }
            case 3 when _stClock >= 1.5f:
            {
                if (rc.Cart.Riders.Count > 0 || party.Adventurers.Any(a => IsInstanceValid(a) && rc.CarOf(a) >= 0)) { Fail($"{party.Adventurers.Count(a => IsInstanceValid(a) && rc.CarOf(a) >= 0)} hero(es) still in the train after the lead got out"); return; }
                if (rc.State.Mode != CartMode.Held || rc.StopIndex != RlStop) { Fail($"the cart is {rc.State.Mode} at stop {rc.StopIndex}, not held at stop {RlStop}"); return; }
                var stopS = rc.StopS[RlStop];
                _rlLimit = rc.RollBackLimitAt(RlStop, out var cut);
                var face = stopS - CartRules.HalfLength - rc.Train.Count * TrainRules.Spacing;
                GD.Print($"[stoptrap] roll: stop {RlStop} at S={stopS:0.0}, the last wagon's back face at S={face:0.0}; limit S={_rlLimit:0.0} ({stopS - _rlLimit:0.0} m of roll{(cut != null ? $"; {cut}" : "")})");
                if (stopS - _rlLimit < RlCinderBehind + 1f) { Fail($"stop {RlStop}'s roll ({stopS - _rlLimit:0.0} m) is too short to reach the cinder {RlCinderBehind} m behind the face"); return; }
                _rlMate = party.Adventurers.FirstOrDefault(a => IsInstanceValid(a) && a.Control == ControlKind.Companion && !a.Downed && !a.Parked);
                if (_rlMate == null) { Fail("no companion to stand on the lane"); return; }
                _rlMate.Teleport(rc.LanePoint(face - RlCompanionBehind) + Vector3.Up * 0.3f, rc.Cart.GlobalPosition);
                _rlCinder = main.Builder.SpawnEnemy("enemy.horde_cinder", rc.Party.Track.Point(face - RlCinderBehind) + Vector3.Up * 0.05f, main.Builder.CurrentFloor);
                if (_rlCinder == null) { Fail("could not spawn the cinder"); return; }
                _rlCinder.DebugHoldHp = true;
                _rlCinderId = _rlCinder.GetInstanceId();
                _rlCinder.Effects.Apply(new Combat.StatusApplication(Combat.StatusKind.Stun, 90f, 0f));
                _stKeep.Add(_rlCinder);
                _stStep++; _stClock = 0f;
                break;
            }
            case 4 when _stClock >= 1f: // (1) the roll starts
            {
                var stopS = rc.StopS[RlStop];
                var face = stopS - CartRules.HalfLength - rc.Train.Count * TrainRules.Spacing;
                _rlMateS = RollS(rc, _rlMate!.GlobalPosition, face - RlCompanionBehind);
                _rlCinderS = _rlCinder is { } c && IsInstanceValid(c) ? RollS(rc, c.GlobalPosition, face - RlCinderBehind) : float.NaN;
                GD.Print($"[stoptrap] roll: {_rlMate.State.Name} on the lane at S={_rlMateS:0.00}, the cinder at S={_rlCinderS:0.00}");
                if (float.IsNaN(_rlMateS) || float.IsNaN(_rlCinderS)) { Fail("the companion or the cinder is not on the line"); return; }
                _rlGates = string.Join(",", rc.GatesUpList);
                _rlGroundFree = rc.GroundFree;
                _rlRollAt = StNow;
                _rlMinS = float.MaxValue; _rlMaxV = 0f;
                if (!rc.DebugRollBack()) { Fail($"DebugRollBack() refused at stop {rc.StopIndex} ({rc.State.Mode}, hijack {rc.Hijack})"); return; }
                if (RollEventsSince(_rlRollAt, CartEventKind.Hijacked) == 0 && rc.Hijack != HijackPhase.RollingBack) { Fail("no roll back started"); return; }
                _stStep++; _stClock = 0f;
                break;
            }
            case 5: // (2)-(4) the first roll
            {
                var t = StNow - _rlRollAt;
                if (float.IsNaN(_rlV2) && t >= 2.0) { _rlV2 = rc.State.V; GD.Print($"[stoptrap] roll: V {_rlV2:0.00} m/s at {t:0.00} s, S={rc.State.S:0.00}"); if (_rlV2 < 0.7f || _rlV2 > 1.3f) { Fail($"V at 2 s is {_rlV2:0.00}, not 1.0 +- 0.3"); return; } }
                if (!_rlMateMoved && _rlWarned.Count > 0)
                {
                    var w = _rlWarned[0];
                    var faceDist = w.S - CartRules.HalfLength - rc.Train.Count * TrainRules.Spacing - _rlMateS;
                    var look = Mathf.Max(w.V, RunOverRules.WarnMinSpeed) * RailWarn.Seconds;
                    GD.Print($"[stoptrap] roll: \"Cart coming!\" at {w.At - _rlRollAt:0.00} s: cart S={w.S:0.00} V={w.V:0.00}, the leading face {faceDist:0.00} m from {_rlMate!.State.Name} (look {look:0.00}), the cart's own back {faceDist + rc.Train.Count * TrainRules.Spacing:0.00} m");
                    if (faceDist > look + 0.3f || faceDist < look - 1.5f) { Fail($"the warning came with the leading face {faceDist:0.00} m off, not within its {look:0.00} m look (from the face, not the cart's back)"); return; }
                    _rlMate.Teleport(rc.StandBesideLane(_rlMate.GlobalPosition), rc.Cart.GlobalPosition); // stepped aside
                    _rlMateMoved = true;
                }
                if (rc.Hijack != HijackPhase.RollingBack) { Fail($"the roll back became {rc.Hijack} before the test braked it"); return; }
                if (rc.SpareAt(RlStop)?.Prompt is { Length: > 0 } offered) { Fail($"spare:{RlStop} prompts \"{offered}\" during the roll"); return; }
                if (RollConfused(rc, enc, "") is { } mixed) { Fail(mixed); return; }
                if (t < 8.0) break;
                if (!_rlMateMoved) { Fail($"no \"Cart coming!\" for {_rlMate?.State.Name} in 8 s of roll (face at S={rc.State.S - 11.5f:0.00}, it at {_rlMateS:0.00})"); return; }
                GD.Print($"[stoptrap] roll: at 8 s S={rc.State.S:0.00} V={rc.State.V:0.00} (max {_rlMaxV:0.00}); the test brakes");
                rc.DebugEndRollBack();
                _rlBrakeAt = StNow;
                _stStep++; _stClock = 0f;
                break;
            }
            case 6: // (5), (6) the brake and the return
            {
                if (_rlStoppedAt < 0 && rc.Hijack == HijackPhase.Returning) { _rlStoppedAt = StNow; _rlReturnAt = StNow; GD.Print($"[stoptrap] roll: stopped {_rlStoppedAt - _rlBrakeAt:0.00} s after the brake at S={rc.State.S:0.00}"); }
                if (_rlStoppedAt < 0 && StNow - _rlBrakeAt > 0.8) { Fail($"the brake did not stop the train within 0.8 s (V {rc.State.V:0.00}, {rc.Hijack})"); return; }
                if (rc.Hijack == HijackPhase.Returning && rc.State.Mode == CartMode.Rolling)
                {
                    _rlReturnMaxV = Mathf.Max(_rlReturnMaxV, rc.State.V);
                    if (StNow - _rlReturnAt > 2.0 && rc.StopS[RlStop] - rc.State.S > 1.5f) { _rlReturnSamples++; if (Mathf.Abs(rc.State.V - 2f) <= 0.1f) _rlReturnInBand++; }
                }
                if (rc.HijackLive) { if (_stClock > 30f) Fail($"the return never held at stop {RlStop} ({rc.Hijack}, S={rc.State.S:0.00})"); break; }
                var over = RollEventsSince(_rlRollAt, CartEventKind.HijackOver);
                var confused = RollEventsSince(_rlRollAt, CartEventKind.Departed, CartEventKind.Arrived, CartEventKind.Resumed);
                var spare = rc.SpareAt(RlStop)?.Prompt ?? "";
                GD.Print($"[stoptrap] roll: held again at S={rc.State.S:0.000} (stop {rc.StopIndex} at {rc.StopS[RlStop]:0.000}); return V max {_rlReturnMaxV:0.00}, {_rlReturnInBand}/{_rlReturnSamples} samples at 2 +- 0.1; HijackOver {over}, departed/arrived/resumed {confused}; armed ride {enc?.ArmedRide}; ground free {rc.GroundFree - _rlGroundFree}; gates [{string.Join(",", rc.GatesUpList)}] (were [{_rlGates}]); spare:{RlStop} prompt \"{spare}\"");
                if (rc.State.Mode != CartMode.Held || Mathf.Abs(rc.State.S - rc.StopS[RlStop]) > 0.05f) { Fail($"the return ended {rc.State.Mode} at S={rc.State.S:0.00}, not held at {rc.StopS[RlStop]:0.00}"); return; }
                if (over != 1) { Fail($"{over} HijackOver event(s), not 1"); return; }
                if (_rlReturnSamples < 10 || _rlReturnInBand < _rlReturnSamples || _rlReturnMaxV > 2.1f) { Fail($"the return ran at up to {_rlReturnMaxV:0.00} m/s ({_rlReturnInBand}/{_rlReturnSamples} samples within 2 +- 0.1)"); return; }
                if (RollConfused(rc, enc, spare) is { } why) { Fail(why); return; }
                _rlSummary = $"V {_rlV2:0.00} at 2 s, max {_rlMaxV:0.00}; stopped {_rlStoppedAt - _rlBrakeAt:0.00} s after the brake; returned at 2 m/s";
                _stStep++; _stClock = 0f;
                break;
            }
            case 7 when _stClock >= 1f: // (7) the limit
            {
                _rlRollAt = StNow;
                _rlMinS = float.MaxValue; _rlMaxV = 0f;
                if (!rc.DebugRollBack()) { Fail("the second DebugRollBack() refused"); return; }
                _rlLimit = rc.HijackLimitS;
                _stStep++; _stClock = 0f;
                break;
            }
            case 8:
            {
                if (rc.HijackLive) { if (_stClock > 60f) Fail($"the second roll never came back ({rc.Hijack}, S={rc.State.S:0.00})"); break; }
                var hit = _rlEvents.FirstOrDefault(ev => ev.At >= _rlRollAt && ev.Kind == CartEventKind.Hit && ev.Target == _rlCinderId);
                var face = _rlCinderS + CartRules.HalfLength + rc.Train.Count * TrainRules.Spacing;
                GD.Print($"[stoptrap] roll: the second roll reached S={_rlMinS:0.000} (limit {_rlLimit:0.000}), max V {_rlMaxV:0.00}; the cinder {(hit.Kind == CartEventKind.Hit ? $"hit at cart S={hit.S:0.00} (its S {_rlCinderS:0.00} + 11.5 = {face:0.00})" : "never hit")}; held again at S={rc.State.S:0.000}");
                if (Mathf.Abs(_rlMinS - _rlLimit) > 0.1f) { Fail($"the roll left alone stopped at S={_rlMinS:0.00}, not its limit {_rlLimit:0.00}"); return; }
                if (rc.State.Mode != CartMode.Held || Mathf.Abs(rc.State.S - rc.StopS[RlStop]) > 0.05f || RollEventsSince(_rlRollAt, CartEventKind.HijackOver) != 1) { Fail("the train did not come back from its limit to its stop"); return; }
                if (hit.Kind != CartEventKind.Hit) { Fail("the cinder on the rails behind the train was never hit"); return; }
                if (Mathf.Abs(hit.S - face) > 0.4f) { Fail($"the cinder was hit at cart S={hit.S:0.00}, not within 0.4 of {face:0.00} (from the last wagon's back)"); return; }
                if (RollConfused(rc, enc, rc.SpareAt(RlStop)?.Prompt ?? "") is { } why) { Fail(why); return; }
                if (_rlCinder != null && IsInstanceValid(_rlCinder) && _rlCinder.IsAlive) _rlCinder.Kill();
                _stStep++; _stClock = 0f;
                break;
            }
            case 9 when _stClock >= 1f: // (8) a load mid-roll
            {
                _rlRollAt = StNow;
                if (!rc.DebugRollBack()) { Fail("the third DebugRollBack() refused"); return; }
                _stStep++; _stClock = 0f;
                break;
            }
            case 10 when StNow - _rlRollAt >= 4.0:
            {
                GD.Print($"[stoptrap] roll: saved mid-roll at S={rc.State.S:0.00} ({rc.Hijack}, V {rc.State.V:0.00})");
                main.SaveGame(StSlot);
                if (!main.LoadGame(StSlot)) { Fail("the save mid-roll would not load"); return; }
                _stStep++; _stClock = 0f;
                break;
            }
            case 11 when _stClock >= 1.5f:
            {
                var wrong = RollPoseWrong(rc);
                GD.Print($"[stoptrap] roll: after the load {rc.State.Mode} at S={rc.State.S:0.000} stop {rc.StopIndex}, hijack {rc.Hijack}, facing {rc.Party.Facing}, dir {rc.Party.Dir}, driver {(rc.Cart.Driver == null ? "none" : rc.Cart.Driver.Name)}, pose {wrong ?? "ok"}");
                if (rc.State.Mode != CartMode.Held || rc.StopIndex != RlStop || Mathf.Abs(rc.State.S - rc.StopS[RlStop]) > 0.05f) { Fail($"after the load the cart is {rc.State.Mode} at S={rc.State.S:0.00} (stop {rc.StopIndex})"); return; }
                if (rc.Hijack != HijackPhase.None || rc.Party.Facing != 1 || rc.Party.Dir != 1 || rc.Party.Reversing || rc.Cart.Driver != null) { Fail("the load kept something of the hijack"); return; }
                if (wrong != null) { Fail($"after the load {wrong}"); return; }
                _rlRollAt = StNow;
                if (!RideBoard(main, layout, rc, out var why)) { Fail(why); return; }
                _stStep++; _stClock = 0f;
                break;
            }
            case 12: // (9) a real departure
            {
                RideWaiveCages(main, rc, "stoptrap");
                var at = _rlEvents.FindIndex(ev => ev.At >= _rlRollAt && ev.Kind == CartEventKind.Departed); // Departed is the enum's 0: a default tuple would pass for one
                if (at < 0) { if (_stClock > 20f) Fail($"no departure from stop {RlStop} in 20 s (boarding {rc.Boarding.Running}, cage held {rc.CageHeld}, gate up {rc.GateUp(RlStop)}, lead seated {me?.Seated})"); break; }
                var departed = _rlEvents[at];
                var boarding = RollEventsSince(_rlRollAt, CartEventKind.Boarding);
                GD.Print($"[stoptrap] roll: departed stop {departed.Stop} ({boarding} countdown(s)); armed ride {enc?.ArmedRide}");
                if (departed.Stop != RlStop || boarding == 0 || enc?.ArmedRide != RlStop + 1) { Fail($"the departure was from stop {departed.Stop} with {boarding} countdown(s), arming ride {enc?.ArmedRide}, not ride {RlStop + 1}"); return; }
                StopTrapOk($"{_rlSummary}; the limit roll stopped at {_rlLimit:0.0} and came back; the cinder hit from the last wagon's back; a load mid-roll stands the train whole; a real departure arms ride {RlStop + 1}");
                break;
            }
        }
    }

    /// <summary>Check (5): nothing of a ride happened since the roll started. Null when all is well.</summary>
    private string? RollConfused(RailController rc, RailEncounterController? enc, string spare)
    {
        var confused = RollEventsSince(_rlRollAt, CartEventKind.Departed, CartEventKind.Arrived, CartEventKind.Resumed);
        if (confused > 0) return $"{confused} Departed/Arrived/Resumed event(s) during the roll";
        if (rc.StopIndex != RlStop) return $"StopIndex is {rc.StopIndex}, not {RlStop}";
        if (enc != null && enc.ArmedRide != -1) return $"ride {enc.ArmedRide} armed during the roll";
        if (rc.GroundFree != _rlGroundFree) return "the watchdog ground the cart free during the roll";
        if (string.Join(",", rc.GatesUpList) != _rlGates) return $"the gates changed during the roll ([{_rlGates}] -> [{string.Join(",", rc.GatesUpList)}])";
        if (spare.Length > 0) return $"spare:{RlStop} prompts \"{spare}\"";
        return null;
    }
}
