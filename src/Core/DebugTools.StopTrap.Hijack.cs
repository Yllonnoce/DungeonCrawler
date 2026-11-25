using DungeonCrawler.Adventure;
using DungeonCrawler.Adventure.Objectives;
using DungeonCrawler.Adventure.Voice;
using DungeonCrawler.Dungeon;
using DungeonCrawler.Dungeon.Generation;
using DungeonCrawler.Dungeon.Rails;
using DungeonCrawler.Enemies;
using DungeonCrawler.Enemies.Ai;
using DungeonCrawler.Party;
using Godot;

namespace DungeonCrawler.Core;

// ---- --test-stop-trap=wonback (plan 16 Task 6) -------------------------------------------------------------------------------------
// The hijack, won back, at stop 2 with the real siege (portals on, NoHijack off). In order (any step out of its window fails):
//  setup: rc.Restore(2) and rc.DebugKillHolders(2) (the gate up, the guards out of the way); the lead stands by stop 2's cage (locked), the
//      companions set down by it holding fire (DebugHoldFire, Hold); every hijacker is held above 1 HP (DebugHoldHp) so only the grip ends it.
//      The flow's sweep kills every siege creature that is not a hijacker 1 s after it came through (the cap never stops a wave), and calls
//      DebugSiegeNow between waves. The siege restarts after the load's 10 s grace.
//  (a) tagging: wave 1 tags nobody; wave 2 tags exactly one, a cinder imp or an ember hound.
//  (b) the run: its distance to the cart falls over 3 s (or it climbs first); it climbs within 2 m, and 0.8 +- 0.1 s later it is Cart.Driver;
//      the BoarderThrow telegraph is told for it, and CrewCartTaken is said (or logged) once.
//  (c) the roll: Hijacked 3 +- 0.1 s after it became the driver, with BrakeOffLog ("A cinder imp lets the brake off! ...").
//  (d) won back on foot: the lead, teleported beside the cart's side, strikes the driver (the lead's own light swing) until it is off: the grip
//      reads 1, 2, then off on three hits; the driver stands beside the lane alive, not carried, aggro on; V is 0 within 0.8 s of the knock-off
//      and the train is Held at StopS[2] +- 0.05 with HijackOver at stop 2.
//  (e) nothing undone: the gate, the cage's lock and StopIndex as before; no crew counted lost.
//  (f) the 20 s wait: no tag within 20 s of HijackOver (the waves hurried through it); then the first cinder or hound through is tagged.
//  (g) won back from the seat: that hijacker becomes the driver and the roll starts; the lead boards the rolling cart (Board the cart at
//      V <= 4: seated in it), strikes the driver from seat 0 until it is off (1, 2, off), the brake, the return, Held. Then, seated, a fresh
//      cinder handed to rc.TakeDriver is refused (MayStart: a hero seated guards it), and the lead gets out.
//  (h) a Restore under way: a test-tagged runner (DebugTagNext) climbs the cart; rc.Restore(2) mid-climb leaves Cart.Driver null, Hijack
//      None and the runner in ChaseState, no longer a runner and untagged.
//  (i) a wagon boarder (Ruling 25): a test-tagged driver rolls the train back; at the Hijacked a second test-tagged runner catches the train
//      and climbs a wagon's boarder spot (a spared leap) while it rolls; a third is tagged and, while it is still on foot, the lead knocks the
//      driver off from foot: at the Braking every runner is released to ChaseState (still a hijacker); the wagon's boarder is still aboard after
//      HijackOver; the lead boards, the cage is waived and the train departs: the boarder still aboard after the Departed.
//  Throughout (Ruling 32): no "straggler ... freed" line names a runner, the driver or a wagon's hijacker.
public partial class DebugTools
{
    private const int HjStop = 2;
    private StopTrapController? _hjTrap;
    private RailController? _hjRc;
    private bool _hjListening;
    private readonly List<(double At, CartEventKind Kind, int Stop)> _hjEvents = new();
    /// <summary>Each siege creature through: when, its kind, its wave, and the creature.</summary>
    private readonly List<(double At, string Id, int Stop, int Wave, Enemy E)> _hjSpawns = new();
    private readonly List<(double At, Enemy E, string State)> _hjTelegraphs = new();
    private readonly List<(double At, string Text)> _hjSaidOrLogged = new();
    private readonly List<string> _hjCullFaults = new();
    /// <summary>Each counted hit on the cart's driver: when, the driver, the grip it read after the hit (−1: the driver was off), the striker's car.</summary>
    private readonly List<(double At, Enemy E, int Grip, int By)> _hjDriverHits = new();
    private int _hjSub;
    private double _hjMark, _hjDriverAt, _hjClimbAt, _hjKnockAt, _hjOverAt, _hjRollAt;
    private Enemy? _hjRunner, _hjBoarder, _hjLast;
    /// <summary>The driver whose hits are counted (the grip's readings).</summary>
    private Enemy? _hjStruck;
    private float _hjRunD0;
    private bool _hjRunFell;
    private int _hjSwings;
    private double _hjBlowAt;
    private float _hjWaveAt;
    private string _hjGateWas = "";
    private bool _hjCageWas;
    private int _hjLostWas;
    private readonly List<string> _hjSummary = new();
    /// <summary>Creatures the flow still checks after they lost their tag (the sweep spares them).</summary>
    private readonly HashSet<Enemy> _hjKeep = new();

    private void HijackListen(Main main)
    {
        if (_hjListening) return;
        _hjListening = true;
        var bus = EventBus.Instance;
        Action<Enemy, string> telegraph = (e, state) => _hjTelegraphs.Add((StNow, e, state));
        Action<string> message = text => { if (text == StopTrapLines.CrewCartTaken || text.Contains("lets the brake off") || text == StopTrapLines.ReturnLog || text.Contains("off the cart")) _hjSaidOrLogged.Add((StNow, text)); };
        Action<Enemy, float, Combat.HitFlavor, int> hurt = (e, _, _, by) =>
        {
            if (_hjRc is not { } rc || !IsInstanceValid(rc) || by < 0 || e != _hjStruck) return;
            var grip = rc.Cart.Driver == e ? rc.DriverGrip : -1; // heard after the controller's own grip handler (it hooked first)
            _hjDriverHits.Add((StNow, e, grip, by));
        };
        Action<Npc, Line> said = (npc, line) => { if (line.Text == StopTrapLines.CrewCartTaken) _hjSaidOrLogged.Add((StNow, $"{npc.NpcName}: {line.Text}")); };
        Action<Enemy, float, float> freed = (e, _, _) =>
        {
            var trap = _hjTrap;
            if (trap != null && IsInstanceValid(trap) && (trap.IsHijacker(e) || trap.Runners.Contains(e))) _hjCullFaults.Add($"the straggler cull freed {e.Name}, a hijacker");
        };
        bus.EnemyTelegraph += telegraph;
        bus.Message += message;
        bus.EnemyHurt += hurt;
        Npc.Said += said;
        PortalSpawner.DebugStragglerFreed += freed;
        _staticUnhooks.Add(() => { bus.EnemyTelegraph -= telegraph; bus.Message -= message; bus.EnemyHurt -= hurt; Npc.Said -= said; PortalSpawner.DebugStragglerFreed -= freed; });
    }

    private void HijackWatch(RailController rc, PortalSpawner portals)
    {
        if (rc != _hjRc)
        {
            _hjRc = rc;
            rc.CartEvents += e =>
            {
                _hjEvents.Add((StNow, e.Kind, e.Stop));
                if (e.Kind is not (CartEventKind.Seated or CartEventKind.Unseated or CartEventKind.Hit)) GD.Print($"[stoptrap] heard: {e.Kind} (stop {e.Stop}) at S={rc.State.S:0.00} V={rc.State.V:0.00}");
            };
        }
        if (portals != _stSpawnsOf)
        {
            _stSpawnsOf = portals;
            portals.SiegeSpawned += (e, stop, wave) => _hjSpawns.Add((StNow, e.Profile.Id, stop, wave, e));
        }
    }

    private double HjEventAt(CartEventKind kind, double since) { foreach (var e in _hjEvents) if (e.Kind == kind && e.At >= since) return e.At; return -1; }

    /// <summary>The trap's log entries of <paramref name="what"/> (a prefix) for <paramref name="who"/> (null: anyone) since <paramref name="since"/>.</summary>
    private List<(double At, string What, int Stop, int Wave, Enemy Who)> HjLog(StopTrapController trap, string what, Enemy? who, double since)
        => trap.HijackLog.Where(x => x.At >= since && x.What.StartsWith(what) && (who == null || x.Who == who)).ToList();

    /// <summary>How far <paramref name="at"/> stands from <paramref name="body"/>'s tub, flat (the controller's own measure).</summary>
    private static float HjFlat(Node3D body, Vector3 at)
    {
        var local = body.GlobalTransform.AffineInverse() * at;
        var dx = Mathf.Max(0f, Mathf.Abs(local.X) - MineCart.TubWidth / 2f);
        var dz = Mathf.Max(0f, Mathf.Abs(local.Z) - CartRules.HalfLength);
        return Mathf.Sqrt(dx * dx + dz * dz);
    }

    /// <summary>A point on foot beside the cart's front quarter, on its walkable side (where the driver is struck from).</summary>
    private static Vector3 HjBesideCart(RailController rc)
    {
        var cart = rc.Cart;
        var right = cart.GlobalTransform.Basis.X with { Y = 0f };
        right = right.Normalized();
        var beside = rc.StandBesideLane(cart.GlobalPosition) - cart.GlobalPosition;
        var side = beside.Dot(right) >= 0f ? 1f : -1f;
        var front = -cart.GlobalTransform.Basis.Z with { Y = 0f };
        return cart.GlobalPosition + right * side * (MineCart.TubWidth / 2f + 0.6f) + front.Normalized() * (MineCart.TubLength / 2f - 0.5f) + Vector3.Up * 0.3f;
    }

    /// <summary>The lead's light swing at <paramref name="at"/> (the knock flow's: its basic ability, aimed).</summary>
    private static void HjSwing(PartyController party, Adventurer me, Enemy at)
        => BdLeadBlow(party, me, at, (me.State.KnownAbilities.IndexOf(Combat.AbilityProfile.BasicId) is var b and >= 0 ? b : 0, false, false, "a light swing"));

    private int HjLost(CrewRescueController ctrl) => ctrl.Crew.Count(n => !IsInstanceValid(n) || n.Dead || ctrl.StateOf(n) == CrewState.Lost);

    /// <summary>The flow's sweep: every siege creature that is not a hijacker dies 1 s after it came through (no wave waits on the cap).</summary>
    private void HjSweep(StopTrapController trap)
    {
        var now = StNow;
        foreach (var s in _hjSpawns)
        {
            var e = s.E;
            if (now - s.At < 1.0 || !IsInstanceValid(e) || !e.IsAlive || trap.IsHijacker(e) || _hjKeep.Contains(e)) continue;
            e.DebugHoldHp = false;
            e.Kill();
        }
    }

    /// <summary>Every hijacker held above 1 HP (only its grip or the flow ends it).</summary>
    private void HjHold(StopTrapController trap)
    {
        foreach (var x in trap.HijackLog) if (x.What == "tagged" && IsInstanceValid(x.Who) && x.Who.IsAlive && trap.IsHijacker(x.Who)) x.Who.DebugHoldHp = true;
    }

    /// <summary>
    /// <paramref name="e"/> was released to the chase since <paramref name="since"/> (the trap's "released" entry: it sets ChaseState) and fights on
    /// as a creature on the attack: no longer running for the train, never unaware (the chase may have turned to its attack by now).
    /// </summary>
    private static bool HjReleased(StopTrapController trap, Enemy e, double since)
        => trap.HijackLog.Any(x => x.At >= since && x.What == "released" && x.Who == e) && e.State is not RunForCartState && !e.State.Unaware;

    /// <summary>A hijacker done with: killed by the test's hand.</summary>
    private static void HjKill(Enemy? e) { if (e != null && IsInstanceValid(e) && e.IsAlive) { e.DebugHoldHp = false; e.Kill(); } }

    private void RunStopTrapWonback(Main main, FloorLayout layout, Node3D root, RailController rc, PortalSpawner portals)
    {
        var party = main.Party;
        void Fail(string why) => StopTrapFail(why);
        void Next() { _stStep++; _stClock = 0f; _hjSub = 0; }
        HijackWatch(rc, portals);
        if (StopTrapController.Instance is not { } trap || !IsInstanceValid(trap) || trap.GetParent() != root) { if (_stClock > 3f) Fail("floor 12 has no StopTrapController"); return; }
        _hjTrap = trap;
        if (ObjectiveController.Instance is not CrewRescueController ctrl) { Fail("floor 12 has no CrewRescueController"); return; }
        var enc = RailEncounterController.Instance is { } en && IsInstanceValid(en) ? en : null;
        var me = party.Local;
        foreach (var a in party.Adventurers) if (IsInstanceValid(a) && a.Control == ControlKind.Companion) { a.DebugHoldFire = true; a.Stance = Characters.Stance.Hold; }
        if (_hjCullFaults.Count > 0) { Fail(string.Join("; ", _hjCullFaults)); return; }
        if (_stStep >= 1) { HjSweep(trap); HjHold(trap); }
        // the waves are hurried (the next 2.5 s after the last try) where the flow waits on a spawn: up to wave 2, through the 20 s wait, and for
        // the test's tags; never while a runner it watches is on its way (a natural wave 3 then comes 12 s on, the hijack live by then)
        var hurry = _stStep is 1 or 2 && portals.SiegeWaves < 2 || _stStep == 7 || _stStep is >= 10 and <= 14;
        if (hurry && portals.SiegeStop == HjStop && (_hjWaveAt += (float)GetProcessDeltaTime()) >= 2.5f) { _hjWaveAt = 0f; portals.DebugSiegeNow(); }

        switch (_stStep)
        {
            case 0 when _stClock >= 1.5f: // setup
            {
                if (rc.StopS.Length != Floor12Stops) { Fail($"floor 12's line has {rc.StopS.Length} stops, not {Floor12Stops}"); return; }
                if (me == null || enc == null) { Fail("no lead or no encounter controller"); return; }
                HijackListen(main);
                rc.Restore(HjStop);
                var killed = rc.DebugKillHolders(HjStop);
                var cage = ctrl.Cages.FirstOrDefault(c => c.Stop == HjStop);
                if (cage == null || cage.Open) { Fail($"stop {HjStop} has no locked cage"); return; }
                if (PerfBeside(main, layout, cage.GlobalPosition) is not { } stand) { Fail("no walkable cell by stop 2's cage"); return; }
                party.Teleport(stand + Vector3.Up * 0.3f, cage.GlobalPosition);
                _hjMark = StNow;
                GD.Print($"[stoptrap] wonback: rc.Restore({HjStop}) at S={rc.State.S:0.0}; {killed} holder(s) die by the test's hand; the party by the cage at {DungeonBuilder.WorldToCell(stand)}; train [{string.Join(",", rc.Train.Select(w => w.Number))}]; limit S={rc.RollBackLimitAt(HjStop, out _):0.0}");
                Next();
                break;
            }
            case 1: // the gate is up; the state to keep (e)
            {
                if (!rc.GateUp(HjStop)) { if (_stClock > 5f) Fail($"stop {HjStop}'s gate did not lift with its holders dead"); break; }
                _hjGateWas = string.Join(",", rc.GatesUpList);
                _hjCageWas = ctrl.Cages.First(c => c.Stop == HjStop).Open;
                _hjLostWas = HjLost(ctrl);
                Next();
                break;
            }
            case 2: // (a) wave 1 tags nobody, wave 2 exactly one
            {
                var w2 = _hjSpawns.Where(s => s.Stop == HjStop && s.Wave == 2 && s.At >= _hjMark).ToList();
                if (w2.Count == 0 || StNow - w2[^1].At < 0.6) { if (_stClock > 40f) Fail($"no wave 2 at stop {HjStop} within 40 s (siege at {portals.SiegeStop}, waves {portals.SiegeWaves})"); break; }
                var tags = HjLog(trap, "tagged", null, _hjMark);
                var w1 = _hjSpawns.Count(s => s.Stop == HjStop && s.Wave == 1 && s.At >= _hjMark);
                GD.Print($"[stoptrap] wonback: (a) wave 1 sent {w1}, wave 2 [{string.Join(", ", w2.Select(s => s.Id))}]; tags [{string.Join(", ", tags.Select(t => $"{t.Who.Name} wave {t.Wave}"))}]");
                if (w1 == 0) { Fail("wave 1 sent nobody"); return; }
                if (tags.Count != 1 || tags[0].Wave != 2 || tags[0].Stop != HjStop) { Fail($"{tags.Count} tag(s) by wave 2, not exactly one in wave 2"); return; }
                _hjRunner = tags[0].Who;
                if (!HijackRules.MayTagKind(_hjRunner.Profile.Id)) { Fail($"{_hjRunner.Name} ({_hjRunner.Profile.Id}) is neither a cinder imp nor an ember hound"); return; }
                _hjRunD0 = HjFlat(rc.Cart, _hjRunner.GlobalPosition);
                _hjMark = StNow;
                _hjStruck = _hjRunner;
                Next();
                break;
            }
            case 3: // (b) the run, the climb, the driver
            {
                var r = _hjRunner!;
                if (!IsInstanceValid(r) || !r.IsAlive) { Fail("the runner died"); return; }
                var climbs = HjLog(trap, "climbs the cart", r, _hjMark);
                if (!_hjRunFell && (_stClock >= 3f || climbs.Count > 0))
                {
                    var d = HjFlat(rc.Cart, r.GlobalPosition);
                    GD.Print($"[stoptrap] wonback: (b) {r.Name} ran from {_hjRunD0:0.0} m to {d:0.0} m of the cart in {_stClock:0.00} s ({r.State.Name})");
                    if (!(d < _hjRunD0 - 1f) && climbs.Count == 0) { Fail($"the runner's distance to the cart did not fall over 3 s ({_hjRunD0:0.0} -> {d:0.0} m, {r.State.Name})"); return; }
                    _hjRunFell = true;
                }
                if (climbs.Count == 0) { if (_stClock > 25f) Fail($"{r.Name} never climbed into the cart ({r.State.Name}, {HjFlat(rc.Cart, r.GlobalPosition):0.0} m)"); break; }
                if (_hjClimbAt <= 0) { _hjClimbAt = climbs[0].At; GD.Print($"[stoptrap] wonback: (b) {r.Name} climbs at {HjFlat(rc.Cart, r.GlobalPosition):0.00} m"); if (HjFlat(rc.Cart, r.GlobalPosition) > HijackRules.ClimbReach + 0.3f) { Fail("it climbed from beyond 2 m"); return; } }
                var driver = HjLog(trap, "driver", r, _hjMark);
                if (driver.Count == 0) { if (StNow - _hjClimbAt > 1.5) Fail($"{r.Name} was not the driver 1.5 s after its climb"); break; }
                _hjDriverAt = driver[0].At;
                var took = _hjDriverAt - _hjClimbAt;
                if (rc.Cart.Driver != r || rc.Hijack != HijackPhase.Glow) { Fail($"the cart's driver is {rc.Cart.Driver?.Name ?? "nobody"} ({rc.Hijack}), not {r.Name} in the glow"); return; }
                if (Math.Abs(took - HijackRules.ClimbSeconds) > 0.1) { Fail($"the climb took {took:0.00} s, not 0.8 +- 0.1"); return; }
                _hjSummary.Add($"climbed in {took:0.00} s");
                Next();
                break;
            }
            case 4: // (b) the glow told, the bark; (c) the roll and its log
            {
                var r = _hjRunner!;
                var rolled = HjEventAt(CartEventKind.Hijacked, _hjDriverAt);
                if (rolled < 0) { if (StNow - _hjDriverAt > 4.0) Fail($"no roll 4 s after {r.Name} took the cart ({rc.Hijack})"); break; }
                if (StNow - rolled < 0.2) break; // its log line is heard in the same tick
                _hjRollAt = rolled;
                var glowTold = _hjTelegraphs.Count(t => t.E == r && t.State == TelegraphRules.BoarderThrow && t.At >= _hjDriverAt - 0.02 && t.At <= _hjDriverAt + 0.1);
                var barks = _hjSaidOrLogged.Where(x => x.At >= _hjDriverAt - 0.02 && x.At <= _hjDriverAt + 1.0 && x.Text.EndsWith(StopTrapLines.CrewCartTaken)).ToList();
                var brake = string.Format(StopTrapLines.BrakeOffLog, StopTrapLines.AName(r.DisplayName));
                var braked = _hjSaidOrLogged.Count(x => x.Text == brake && Math.Abs(x.At - rolled) < 0.1);
                var after = rolled - _hjDriverAt;
                GD.Print($"[stoptrap] wonback: (c) the roll {after:0.00} s after the driver; the glow told {glowTold}x; the bark [{string.Join(" | ", barks.Select(b => b.Text))}]; \"{brake}\" {braked}x");
                if (glowTold != 1) { Fail($"the glow's telegraph was told {glowTold} time(s) for the driver"); return; }
                if (barks.Count != 1) { Fail($"\"{StopTrapLines.CrewCartTaken}\" said or logged {barks.Count} time(s), not once"); return; }
                if (Math.Abs(after - HijackRules.BrakeGlow) > 0.1) { Fail($"the roll started {after:0.00} s after the driver took the cart, not 3 +- 0.1"); return; }
                if (braked != 1) { Fail($"\"{brake}\" was logged {braked} time(s)"); return; }
                _hjSummary.Add($"rolled {after:0.00} s after");
                Next();
                break;
            }
            case 5 when StNow - _hjRollAt >= 1.0: // (d) won back on foot
            {
                var r = _hjRunner!;
                var off = rc.Cart.Driver != r;
                if (off)
                {
                    var hits = _hjDriverHits.Where(h => h.E == r).ToList();
                    GD.Print($"[stoptrap] wonback: (d) the grip after each hit [{string.Join(", ", hits.Select(h => h.Grip < 0 ? "off" : h.Grip.ToString()))}] in {_hjSwings} swing(s), by [{string.Join(",", hits.Select(h => party.Members[h.By].Name))}]");
                    if (hits.Count != 3 || hits[0].Grip != 1 || hits[1].Grip != 2 || hits[2].Grip != -1 || hits.Any(h => h.By != me!.Index)) { Fail("the driver's grip did not go 1, 2, off on three of the lead's hits"); return; }
                    _hjKnockAt = hits[2].At;
                    Next();
                    break;
                }
                if (rc.Hijack != HijackPhase.RollingBack) { Fail($"the roll became {rc.Hijack} before the driver was knocked off"); return; }
                if (_hjSwings >= 12) { Fail($"{_hjSwings} swings landed {_hjDriverHits.Count(h => h.E == r)} hit(s); the driver still aboard"); return; }
                if (me!.BladeSwinging || StNow - _hjBlowAt < 0.4) break;
                me.Teleport(HjBesideCart(rc), r.GlobalPosition);
                HjSwing(party, me, r);
                _hjSwings++;
                _hjBlowAt = StNow;
                break;
            }
            case 6: // (d) the driver beside the lane; the brake, the return, Held
            {
                var r = _hjRunner!;
                if (_hjSub == 0 && StNow - _hjKnockAt >= 0.2)
                {
                    var cell = DungeonBuilder.WorldToCell(r.GlobalPosition);
                    GD.Print($"[stoptrap] wonback: (d) {r.Name} knocked off stands at {cell} (rail {layout.IsRail(cell)}), alive {r.IsAlive}, carried {r.Carried}, {r.State.Name}");
                    if (!r.IsAlive || r.Carried || r.State.Unaware || layout.IsRail(cell)) { Fail("the knocked-off driver is not standing beside the lane alive with its aggro on"); return; }
                    if (!_hjSaidOrLogged.Any(x => x.At >= _hjKnockAt - 0.05 && x.Text == string.Format(StopTrapLines.KnockedOffCartLog, me!.State.Name, StopTrapLines.Lower(r.DisplayName)))) { Fail("no knock-off log line"); return; }
                    _hjSub = 1;
                }
                var stopped = HjEventAt(CartEventKind.HijackOver, _hjKnockAt);
                if (rc.Hijack == HijackPhase.Returning && _hjSub < 2) { _hjSub = 2; var t = StNow - _hjKnockAt; GD.Print($"[stoptrap] wonback: (d) V 0 {t:0.00} s after the knock-off"); if (t > 0.8) { Fail($"V reached 0 {t:0.00} s after the knock-off, not within 0.8"); return; } }
                if (stopped < 0) { if (StNow - _hjKnockAt > 30) Fail($"the train never came back to stop {HjStop} ({rc.Hijack}, S={rc.State.S:0.00})"); break; }
                if (_hjSub < 2) { Fail("the return came without a stop at V 0 first"); return; }
                _hjOverAt = stopped;
                var over = _hjEvents.First(x => x.Kind == CartEventKind.HijackOver && x.At >= _hjKnockAt);
                GD.Print($"[stoptrap] wonback: (d) held again at S={rc.State.S:0.000} (stop {rc.StopIndex} at {rc.StopS[HjStop]:0.000}), HijackOver at stop {over.Stop}; the return log {_hjSaidOrLogged.Count(x => x.Text == StopTrapLines.ReturnLog && x.At >= _hjKnockAt)}x");
                if (rc.State.Mode != CartMode.Held || Mathf.Abs(rc.State.S - rc.StopS[HjStop]) > 0.05f || over.Stop != HjStop) { Fail($"the return ended {rc.State.Mode} at S={rc.State.S:0.00}, not held at stop {HjStop}"); return; }
                if (!_hjSaidOrLogged.Any(x => x.Text == StopTrapLines.ReturnLog && x.At >= _hjKnockAt)) { Fail("no return log line"); return; }
                // (e) nothing undone
                var gates = string.Join(",", rc.GatesUpList);
                var cageOpen = ctrl.Cages.First(c => c.Stop == HjStop).Open;
                var lost = HjLost(ctrl);
                GD.Print($"[stoptrap] wonback: (e) gates [{gates}] (were [{_hjGateWas}]), cage open {cageOpen} (was {_hjCageWas}), StopIndex {rc.StopIndex}, crew lost {lost} (was {_hjLostWas})");
                if (gates != _hjGateWas || cageOpen != _hjCageWas || rc.StopIndex != HjStop || lost != _hjLostWas) { Fail("the hijack undid something (the gate, the cage, the stop or the crew)"); return; }
                HjKill(r); // done with
                _hjSummary.Add("won back on foot (1, 2, off)");
                Next();
                break;
            }
            case 7: // (f) the 20 s wait, then the first cinder or hound is tagged
            {
                var early = HjLog(trap, "tagged", null, _hjOverAt).Where(t => t.At < _hjOverAt + HijackRules.NextTagAfter - 0.02).ToList();
                if (early.Count > 0) { Fail($"{early[0].Who.Name} tagged {early[0].At - _hjOverAt:0.00} s after HijackOver, within 20 s"); return; }
                if (StNow - _hjOverAt < HijackRules.NextTagAfter) break;
                var first = _hjSpawns.FirstOrDefault(s => s.At >= _hjOverAt + HijackRules.NextTagAfter && s.Stop == HjStop && HijackRules.MayTagKind(s.Id));
                if (first.E == null) { if (StNow - _hjOverAt > 50) Fail("no cinder or hound came through in 30 s after the wait"); break; }
                var tags = HjLog(trap, "tagged", null, _hjOverAt);
                var during = _hjSpawns.Count(s => s.At >= _hjOverAt && s.At < _hjOverAt + HijackRules.NextTagAfter);
                GD.Print($"[stoptrap] wonback: (f) {during} siege creature(s) came through in the 20 s untagged; then {first.E.Name} ({first.Id}, wave {first.Wave}) {first.At - _hjOverAt:0.00} s after; tags [{string.Join(", ", tags.Select(t => $"{t.Who.Name} at +{t.At - _hjOverAt:0.00}"))}]");
                if (during == 0) { Fail("no siege creature came through in the 20 s: the wait was not tried"); return; }
                if (tags.Count != 1 || tags[0].Who != first.E) { Fail("the first cinder or hound after the 20 s was not the one tagged"); return; }
                _hjRunner = first.E;
                _hjStruck = _hjRunner;
                _hjMark = StNow;
                _hjSwings = 0;
                Next();
                break;
            }
            case 8: // (g) the second hijacker takes the cart and the roll starts; the lead boards the rolling cart
            {
                var rolled = HjEventAt(CartEventKind.Hijacked, _hjMark);
                if (rolled < 0) { if (_stClock > 30f) Fail($"the second hijacker never rolled the train ({_hjRunner!.State.Name}, driver {rc.Cart.Driver?.Name ?? "none"}, {rc.Hijack})"); break; }
                // the driver is the first tagged after the wait, or one a later wave tagged while it still ran (spec Ruling 2 allows that: one
                // hijack live, not one runner), who then got there first; the other goes for a wagon
                if (rc.Cart.Driver is not { } r || !HjLog(trap, "tagged", r, _hjOverAt + HijackRules.NextTagAfter - 0.02).Any()) { Fail($"the roll's driver {rc.Cart.Driver?.Name ?? "nobody"} was not tagged after the 20 s wait"); return; }
                if (r != _hjRunner) GD.Print($"[stoptrap] wonback: (g) {r.Name}, tagged by a later wave, took the cart before {_hjRunner!.Name}");
                _hjRunner = r;
                _hjStruck = r;
                if (StNow - rolled < 1.5) break;
                if (rc.State.V > HijackRules.BoardMaxSpeed) { Fail($"the roll is at {rc.State.V:0.00} m/s already"); return; }
                me!.Teleport(HjBesideCart(rc), rc.Cart.GlobalPosition);
                var prompt = rc.Cart.Prompt;
                rc.Cart.Interact(party, me);
                GD.Print($"[stoptrap] wonback: (g) the lead boards the rolling cart at V={rc.State.V:0.00} ({rc.Hijack}): prompt \"{prompt}\", seated {me.Cart == rc.Cart} (car {rc.CarOf(me)} seat {me.Seat})");
                if (me.Cart != rc.Cart) { Fail($"the lead could not board the rolling cart (prompt \"{prompt}\", {rc.Hijack}, V {rc.State.V:0.00})"); return; }
                if (prompt != "Board the cart") { Fail($"the rolling cart offered \"{prompt}\", not \"Board the cart\""); return; }
                if (rc.Boarding.Running) { Fail("the countdown started under the hijack"); return; }
                _hjBlowAt = StNow;
                Next();
                break;
            }
            case 9: // (g) struck from seat 0
            {
                var r = _hjRunner!;
                if (rc.Cart.Driver != r)
                {
                    var hits = _hjDriverHits.Where(h => h.E == r).ToList();
                    GD.Print($"[stoptrap] wonback: (g) from the seat the grip went [{string.Join(", ", hits.Select(h => h.Grip < 0 ? "off" : h.Grip.ToString()))}] in {_hjSwings} swing(s)");
                    if (hits.Count != 3 || hits[0].Grip != 1 || hits[1].Grip != 2 || hits[2].Grip != -1 || hits.Any(h => h.By != me!.Index)) { Fail("from the seat the driver's grip did not go 1, 2, off on three of the lead's hits"); return; }
                    _hjKnockAt = hits[2].At;
                    Next();
                    break;
                }
                if (rc.Hijack != HijackPhase.RollingBack) { Fail($"the roll became {rc.Hijack} before the driver was knocked off from the seat"); return; }
                if (rc.CarOf(me!) != 0) { Fail($"the lead is in car {rc.CarOf(me!)}, not the cart"); return; }
                if (_hjSwings >= 12) { Fail($"{_hjSwings} swings from the seat landed {_hjDriverHits.Count(h => h.E == r)} hit(s)"); return; }
                if (me!.BladeSwinging || StNow - _hjBlowAt < 0.4) break;
                HjSwing(party, me, r);
                _hjSwings++;
                _hjBlowAt = StNow;
                break;
            }
            case 10: // (g) the brake, the return, Held; MayStart refuses with the lead seated; the lead gets out
            {
                var over = HjEventAt(CartEventKind.HijackOver, _hjKnockAt);
                if (over < 0) { if (StNow - _hjKnockAt > 30) Fail($"the train never came back after the knock-off from the seat ({rc.Hijack})"); break; }
                if (_hjSub == 0)
                {
                    if (rc.State.Mode != CartMode.Held || Mathf.Abs(rc.State.S - rc.StopS[HjStop]) > 0.05f) { Fail($"after the seat's knock-off the train is {rc.State.Mode} at S={rc.State.S:0.00}"); return; }
                    HjKill(_hjRunner);
                    var probe = main.Builder.SpawnEnemy("enemy.horde_cinder", rc.StandBesideLane(rc.Cart.GlobalPosition), main.Builder.CurrentFloor);
                    if (probe == null) { Fail("could not spawn the probe cinder"); return; }
                    var took = rc.TakeDriver(probe);
                    GD.Print($"[stoptrap] wonback: (g) held again with the lead seated; rc.TakeDriver(a fresh cinder) {(took ? "TOOK it" : "refused")} ({rc.HeroesSeatedInTrain()} hero(es) seated, hijack {rc.Hijack})");
                    HjKill(probe);
                    if (took || rc.Hijack != HijackPhase.None || rc.Cart.Driver != null) { Fail("a hijack started with a hero seated in the cart (MayStart)"); return; }
                    if (AmGetOut(main, rc) is { } why) { Fail($"(g) {why}"); return; }
                    _hjSummary.Add("won back from the seat; refused with a hero seated");
                    _hjSub = 1;
                    _stClock = 0f;
                    break;
                }
                if (_stClock < 1.5f) break;
                if (rc.HeroesSeatedInTrain() != 0) { Fail($"{rc.HeroesSeatedInTrain()} hero(es) still seated after the lead got out"); return; }
                _hjMark = StNow;
                _hjRunner = trap.DebugTagNext();
                Next();
                break;
            }
            case 11: // (h) a Restore under way
            {
                if (_hjSub == 0)
                {
                    var tagged = HjLog(trap, "tagged", null, _hjMark);
                    if (tagged.Count == 0) { if (_stClock > 20f) Fail("DebugTagNext tagged nobody in 20 s"); break; }
                    var climbs = HjLog(trap, "climbs the cart", null, _hjMark);
                    if (climbs.Count == 0) { if (_stClock > 30f) Fail($"the test's runner {tagged[0].Who.Name} never climbed ({tagged[0].Who.State.Name})"); break; }
                    _hjRunner = climbs[0].Who;
                    _hjKeep.Add(_hjRunner);
                    if (StNow - climbs[0].At < 0.3) break;
                    if (rc.Cart.Driver != null || rc.Hijack != HijackPhase.None) { Fail("the climb was over before the restore"); return; }
                    GD.Print($"[stoptrap] wonback: (h) {_hjRunner.Name} climbs; rc.Restore({HjStop}) {StNow - climbs[0].At:0.00} s into the climb");
                    rc.Restore(HjStop);
                    _hjSub = 1;
                    _stClock = 0f;
                    break;
                }
                if (_stClock < 1.2f) break; // past the climb's end
                var r = _hjRunner!;
                if (!IsInstanceValid(r) || !r.IsAlive) { Fail("the runner caught by the restore died"); return; }
                GD.Print($"[stoptrap] wonback: (h) after the restore: driver {rc.Cart.Driver?.Name ?? "none"}, hijack {rc.Hijack}, {r.Name} {r.State.Name}, a runner {trap.Runners.Contains(r)}, a hijacker {trap.IsHijacker(r)}");
                if (rc.Cart.Driver != null || rc.Hijack != HijackPhase.None) { Fail("a restore mid-climb left a driver or a hijack"); return; }
                if (!HjReleased(trap, r, _hjMark) || trap.Runners.Contains(r) || trap.IsHijacker(r)) { Fail($"after the restore {r.Name} is {r.State.Name} (a runner {trap.Runners.Contains(r)}, tagged {trap.IsHijacker(r)}), not released untagged to the chase"); return; }
                _hjKeep.Remove(r);
                HjKill(r);
                _hjSummary.Add("a restore mid-climb leaves no driver");
                _hjMark = StNow;
                _hjRunner = trap.DebugTagNext();
                Next();
                break;
            }
            case 12: // (i) a test-tagged driver rolls the train back; at the Hijacked a second runner is tagged
            {
                var tagged = HjLog(trap, "tagged", null, _hjMark);
                if (tagged.Count == 0) { if (_stClock > 25f) Fail("the third hijack's DebugTagNext tagged nobody"); break; }
                var rolled = HjEventAt(CartEventKind.Hijacked, _hjMark);
                if (rolled < 0) { if (_stClock > 40f) Fail($"the third hijacker never rolled the train ({tagged[0].Who.State.Name}, {rc.Hijack})"); break; }
                if (rc.Cart.Driver is not { } third) { Fail("the third roll has no driver"); return; }
                _hjRunner = third;
                _hjStruck = third;
                _hjLast = null;
                _hjRollAt = rolled;
                _hjMark = StNow;
                _hjBoarder = trap.DebugTagNext();
                Next();
                break;
            }
            case 13: // (i) the second runner catches the rolling train and boards a wagon
            {
                if (rc.Hijack != HijackPhase.RollingBack) { Fail($"the roll became {rc.Hijack} before a runner boarded a wagon"); return; }
                var tagged = HjLog(trap, "tagged", null, _hjMark);
                if (tagged.Count == 0) { if (_stClock > 8f) Fail("the second DebugTagNext tagged nobody in 8 s"); break; }
                var boarded = HjLog(trap, "boards wagon", null, _hjMark);
                if (boarded.Count == 0) { if (_stClock > 15f) Fail($"{tagged[0].Who.Name} never boarded a wagon of the rolling train ({tagged[0].Who.State.Name}, {rc.State.V:0.00} m/s, {HjFlat(rc.Cart, tagged[0].Who.GlobalPosition):0.0} m from the cart)"); break; }
                _hjBoarder = boarded[0].Who;
                _hjKeep.Add(_hjBoarder);
                var w = _hjBoarder.Carrier as TrainWagon;
                GD.Print($"[stoptrap] wonback: (i) {_hjBoarder.Name} boards {boarded[0].What[7..]} {boarded[0].At - _hjRollAt:0.00} s into the roll at V={rc.State.V:0.00}; spared {enc!.IsSparedBoarder(_hjBoarder)}, grip {enc.GripOf(_hjBoarder)}");
                if (w == null || !enc.IsSparedBoarder(_hjBoarder) || enc.GripOf(_hjBoarder) != 0) { Fail("the wagon's hijacker is not a spared boarder with its grip"); return; }
                _hjMark = StNow;
                trap.DebugTagNext();
                _hjSwings = 0;
                Next();
                break;
            }
            case 14: // (i) a third runner on foot; the lead knocks the driver off from foot
            {
                var r = _hjRunner!;
                if (rc.Cart.Driver != r)
                {
                    _hjKnockAt = _hjDriverHits.LastOrDefault(h => h.E == r).At;
                    Next();
                    break;
                }
                if (_hjLast == null) // the runner on foot to watch: the first running once the third tag is in
                {
                    if (HjLog(trap, "tagged", null, _hjMark).Count == 0 || trap.Runners.FirstOrDefault(e => IsInstanceValid(e) && e.IsAlive && !e.Carried) is not { } runner) { if (_stClock > 8f) Fail("the third DebugTagNext gave no runner on foot in 8 s"); break; }
                    _hjLast = runner;
                    _hjKeep.Add(runner);
                    GD.Print($"[stoptrap] wonback: (i) {runner.Name} runs on foot ({runner.State.Name}, {HjFlat(rc.Cart, runner.GlobalPosition):0.0} m from the cart); the lead strikes the driver");
                }
                var foot = _hjLast!;
                if (!trap.Runners.Contains(foot)) { Fail($"{foot.Name} was no runner on foot while the driver was struck ({foot.State.Name}, carried {foot.Carried})"); return; }
                if (rc.Hijack != HijackPhase.RollingBack) { Fail($"the roll became {rc.Hijack} before the driver was knocked off"); return; }
                if (_hjSwings >= 12) { Fail($"{_hjSwings} swings landed {_hjDriverHits.Count(h => h.E == r)} hit(s) on the third driver"); return; }
                if (me!.BladeSwinging || StNow - _hjBlowAt < 0.4) break;
                me.Teleport(HjBesideCart(rc), r.GlobalPosition);
                HjSwing(party, me, r);
                _hjSwings++;
                _hjBlowAt = StNow;
                break;
            }
            case 15: // (i) at the Braking every runner is released (still a hijacker); the boarder rides on through HijackOver
            {
                var foot = _hjLast!;
                if (_hjSub == 0)
                {
                    if (rc.Hijack is not (HijackPhase.Braking or HijackPhase.Returning)) { if (_stClock > 1f) Fail($"no brake after the third driver's knock-off ({rc.Hijack})"); break; }
                    if (StNow - _hjKnockAt < 0.05) break; // the trap releases at its next physics tick (the knock-off came inside the flow's own swing)
                    GD.Print($"[stoptrap] wonback: (i) at the {rc.Hijack}: runners [{string.Join(", ", trap.Runners.Select(e => e.Name))}]; {foot.Name} {foot.State.Name}, carried {foot.Carried}, a hijacker {trap.IsHijacker(foot)}");
                    if (trap.Runners.Count != 0) { Fail("runners still running at the brake"); return; }
                    if (!foot.IsAlive || foot.Carried || !HjReleased(trap, foot, _hjMark) || !trap.IsHijacker(foot)) { Fail($"the runner on foot was not released to the chase, still tagged ({foot.State.Name}, carried {foot.Carried})"); return; }
                    _hjSub = 1;
                }
                var over = HjEventAt(CartEventKind.HijackOver, _hjKnockAt);
                if (over < 0) { if (_stClock > 30f) Fail($"the third hijack never came back ({rc.Hijack})"); break; }
                var b = _hjBoarder!;
                GD.Print($"[stoptrap] wonback: (i) after HijackOver {b.Name} on {(b.Carrier as Node)?.Name ?? "foot"}, spared {enc!.IsSparedBoarder(b)}");
                if (!b.IsAlive || b.Carrier is not TrainWagon || !enc.IsSparedBoarder(b)) { Fail("the wagon's hijacker is not aboard after HijackOver"); return; }
                HjKill(_hjRunner);
                _hjKeep.Remove(foot);
                HjKill(foot);
                if (!RideBoard(main, layout, rc, out var why)) { Fail(why); return; }
                _hjMark = StNow;
                Next();
                break;
            }
            case 16: // (i) the departure: the boarder still aboard after the Departed
            {
                RideWaiveCages(main, rc, "stoptrap");
                var departed = HjEventAt(CartEventKind.Departed, _hjMark);
                if (departed < 0) { if (_stClock > 25f) Fail($"no departure from stop {HjStop} in 25 s (boarding {rc.Boarding.Running}, cage held {rc.CageHeld}, lead seated {me?.Seated})"); break; }
                if (StNow - departed < 1.0) break;
                var b = _hjBoarder!;
                GD.Print($"[stoptrap] wonback: (i) 1 s after the Departed {b.Name} on {(b.Carrier as Node)?.Name ?? "foot"}, spared {enc!.IsSparedBoarder(b)}, alive {b.IsAlive}");
                if (!b.IsAlive || b.Carrier is not TrainWagon || !enc.IsSparedBoarder(b)) { Fail("the wagon's hijacker was cleared with the departure"); return; }
                _hjSummary.Add($"a wagon's hijacker rode through HijackOver and the departure");
                StopTrapOk($"{string.Join("; ", _hjSummary)}; no hijacker freed by the cull");
                break;
            }
        }
    }
}
