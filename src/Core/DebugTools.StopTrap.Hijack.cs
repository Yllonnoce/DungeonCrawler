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
//  setup: BeginRollBack asked straight (DebugBeginRollBack) at the landing and the last stop rolls nothing (its own guard); then rc.Restore(2) and rc.DebugKillHolders(2) (the gate up, the guards out of the way); the lead stands by stop 2's cage (locked), the
//      companions set down by it holding fire (DebugHoldFire, Hold); every hijacker is held above 1 HP (DebugHoldHp) so only the grip ends it.
//      The flow's sweep kills every siege creature that is not a hijacker 1 s after it came through (the cap never stops a wave), and calls
//      DebugSiegeNow between waves. The siege restarts after the load's 10 s grace.
//  (a) tagging: wave 1 tags nobody; wave 2 tags exactly one, a cinder imp or an ember hound.
//  (b) the run: its distance to the cart falls over 3 s (or it climbs first); it climbs within 2 m, and 0.8 +- 0.1 s later it is Cart.Driver;
//      the BoarderThrow telegraph is told for it, and CrewCartTaken is said (or logged) once.
//  (c) the roll: Hijacked 3 +- 0.1 s after it became the driver, with BrakeOffLog ("A cinder imp lets the brake off! ..."); the glow's telegraph
//      told at 0 and 1.5 s (a client's glow lasts 1.5 s: the 3 s covered on every peer).
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
//      and climbs a wagon's boarder spot (a spared leap, on the attack, no crew target) while it rolls; a third is tagged and, while it is still
//      on foot, the lead knocks the driver off from foot: at the Braking every runner is released to ChaseState (still a hijacker); the wagon's
//      boarder is still aboard after HijackOver; with stop 1's crew seated in its wagon and the lead beside it, for 4 s it attacks and never
//      targets or hurts the crew; the lead boards, the cage is waived and the train departs: 1 s after the Departed the boarder is aboard as the
//      ride's ordinary boarder (not spared, its grip) going for its wagon's crew.
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
    private bool _hjDriverDown, _hjZeroSeen, _hjAttackSeen;
    private List<Npc> _hjCrew = new();
    private List<float> _hjCrewHp = new();
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
    private static Vector3 HjBesideCart(RailController rc) => HjBeside(rc, rc.Cart, MineCart.TubLength / 2f - 0.5f);

    /// <summary>A point on foot beside <paramref name="body"/>'s tub, <paramref name="forward"/> m toward its front, on its walkable side.</summary>
    private static Vector3 HjBeside(RailController rc, Node3D body, float forward)
    {
        var right = body.GlobalTransform.Basis.X with { Y = 0f };
        right = right.Normalized();
        var beside = rc.StandBesideLane(body.GlobalPosition) - body.GlobalPosition;
        var side = beside.Dot(right) >= 0f ? 1f : -1f;
        var front = -body.GlobalTransform.Basis.Z with { Y = 0f };
        return body.GlobalPosition + right * side * (MineCart.TubWidth / 2f + 0.6f) + front.Normalized() * forward + Vector3.Up * 0.3f;
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
                // BeginRollBack's own guard (the T5 review's M3): asked straight at the landing and at the last stop, it rolls nothing
                foreach (var k in new[] { 0, rc.StopS.Length - 1 })
                {
                    rc.Restore(k);
                    var rolled = rc.DebugBeginRollBack();
                    GD.Print($"[stoptrap] wonback: BeginRollBack at stop {k} {(rolled ? "ROLLED" : "refused")} (hijack {rc.Hijack}, {rc.State.Mode})");
                    if (rolled || rc.HijackLive || rc.State.Mode != CartMode.Held) { Fail($"BeginRollBack rolled the train back from stop {k}"); return; }
                }
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
                var tells = _hjTelegraphs.Where(t => t.E == r && t.State == TelegraphRules.BoarderThrow && t.At >= _hjDriverAt - 0.02 && t.At <= rolled).Select(t => t.At - _hjDriverAt).ToList();
                var glowTold = tells.Count;
                var barks = _hjSaidOrLogged.Where(x => x.At >= _hjDriverAt - 0.02 && x.At <= _hjDriverAt + 1.0 && x.Text.EndsWith(StopTrapLines.CrewCartTaken)).ToList();
                var brake = string.Format(StopTrapLines.BrakeOffLog, StopTrapLines.AName(r.DisplayName));
                var braked = _hjSaidOrLogged.Count(x => x.Text == brake && Math.Abs(x.At - rolled) < 0.1);
                var after = rolled - _hjDriverAt;
                GD.Print($"[stoptrap] wonback: (c) the roll {after:0.00} s after the driver; the glow told at [{string.Join(", ", tells.Select(x => x.ToString("0.00")))}] s; the bark [{string.Join(" | ", barks.Select(b => b.Text))}]; \"{brake}\" {braked}x");
                // a client glows 1.5 s a telling: told at 0 and 1.5 s, the 3 s are covered on every peer
                if (glowTold != HijackRules.GlowTells || tells.Where((x, i) => Math.Abs(x - i * HijackRules.GlowRetell) > 0.05).Any()) { Fail($"the glow's telegraph was told at [{string.Join(", ", tells.Select(x => x.ToString("0.00")))}] s, not every {HijackRules.GlowRetell} s from 0 ({HijackRules.GlowTells} tellings)"); return; }
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
                // the driver set down (checked 0.2 s on, whatever the train does meanwhile: the I2 fix) and the stop at V 0 (the Returning), each once
                if (!_hjDriverDown && StNow - _hjKnockAt >= 0.2)
                {
                    var cell = DungeonBuilder.WorldToCell(r.GlobalPosition);
                    var log = string.Format(StopTrapLines.KnockedOffCartLog, me!.State.Name, StopTrapLines.Lower(r.DisplayName));
                    var logged = _hjSaidOrLogged.Count(x => x.At >= _hjKnockAt - 0.05 && x.Text == log);
                    GD.Print($"[stoptrap] wonback: (d) {r.Name} knocked off stands at {cell} (rail {layout.IsRail(cell)}, {HjFlat(rc.Cart, r.GlobalPosition):0.0} m from the tub), alive {r.IsAlive}, carried {r.Carried}, {r.State.Name}, unaware {r.State.Unaware}; \"{log}\" {logged}x");
                    if (!r.IsAlive || r.Carried || r.State.Unaware || r.State is RunForCartState || layout.IsRail(cell)) { Fail("the knocked-off driver is not standing beside the lane alive with its aggro on"); return; }
                    if (logged != 1) { Fail($"the knock-off log line was logged {logged} time(s), not once"); return; }
                    _hjDriverDown = true;
                }
                if (rc.Hijack == HijackPhase.Returning && !_hjZeroSeen) { _hjZeroSeen = true; var t = StNow - _hjKnockAt; GD.Print($"[stoptrap] wonback: (d) V 0 {t:0.00} s after the knock-off"); if (t > 0.8) { Fail($"V reached 0 {t:0.00} s after the knock-off, not within 0.8"); return; } }
                var stopped = HjEventAt(CartEventKind.HijackOver, _hjKnockAt);
                if (stopped < 0) { if (StNow - _hjKnockAt > 30) Fail($"the train never came back to stop {HjStop} ({rc.Hijack}, S={rc.State.S:0.00})"); break; }
                if (!_hjZeroSeen) { Fail("the return came without a stop at V 0 first"); return; }
                if (!_hjDriverDown) { Fail("the train was back before the knocked-off driver was checked"); return; }
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
                if (_hjBoarder.State is RunForCartState || _hjBoarder.State.Unaware || _hjBoarder.BoardTarget != null) { Fail($"the wagon's hijacker is {_hjBoarder.State.Name} (board target {_hjBoarder.BoardTarget?.NpcName ?? "none"}), not on the attack against the heroes (the I1 fix)"); return; }
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
                var b = _hjBoarder!;
                if (_hjSub == 1)
                {
                    var over = HjEventAt(CartEventKind.HijackOver, _hjKnockAt);
                    if (over < 0) { if (_stClock > 30f) Fail($"the third hijack never came back ({rc.Hijack})"); break; }
                    GD.Print($"[stoptrap] wonback: (i) after HijackOver {b.Name} on {(b.Carrier as Node)?.Name ?? "foot"}, spared {enc!.IsSparedBoarder(b)}");
                    if (!b.IsAlive || b.Carrier is not TrainWagon bw || !enc.IsSparedBoarder(b)) { Fail("the wagon's hijacker is not aboard after HijackOver"); return; }
                    // at the stop it goes for the heroes, never the crew: stop 1's crew seated in its wagon, the lead beside it
                    _hjCrew = ctrl.DebugSeatCrew(1, bw.Number)?.Select(c => c.Npc).ToList() ?? new();
                    if (_hjCrew.Count == 0) { Fail($"stop 1's crew could not be seated in wagon {bw.Number}"); return; }
                    _hjCrewHp = _hjCrew.Select(n => n.Hp).ToList();
                    me!.Teleport(HjBeside(rc, bw, 0f), bw.GlobalPosition);
                    _hjAttackSeen = false;
                    _hjMark = StNow;
                    _hjSub = 2;
                    break;
                }
                if (b.BoardTarget != null) { Fail($"at the stop the wagon's hijacker went for {b.BoardTarget.NpcName}, a crew member"); return; }
                if (b.State is RunForCartState || b.State.Unaware) { Fail($"at the stop the wagon's hijacker is {b.State.Name}, not on the attack (the I1 fix)"); return; }
                if (b.State is AttackState) _hjAttackSeen = true;
                if (StNow - _hjMark < 4.0) break;
                var hurt = _hjCrew.Where((n, i) => !IsInstanceValid(n) || n.Dead || n.Downed || n.Hp < _hjCrewHp[i]).Select(n => IsInstanceValid(n) ? n.NpcName : "?").ToList();
                GD.Print($"[stoptrap] wonback: (i) 4 s at the stop with [{string.Join(", ", _hjCrew.Select(n => n.NpcName))}] in its wagon and the lead beside it: {b.Name} on the attack all through (now {b.State.Name}, {(_hjAttackSeen ? "it swung" : "nobody in its reach")}), its target {(b.HasTarget ? b.Target!.State.Name : "none")}, board target none, crew hurt [{string.Join(", ", hurt)}]");
                if (!b.HasTarget) { Fail("the wagon's hijacker has no hero for its target at the stop"); return; }
                if (hurt.Count > 0) { Fail($"the wagon's hijacker hurt crew at the stop: {string.Join(", ", hurt)}"); return; }
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
                GD.Print($"[stoptrap] wonback: (i) 1 s after the Departed {b.Name} on {(b.Carrier as Node)?.Name ?? "foot"}, spared {enc!.IsSparedBoarder(b)}, grip {enc.GripOf(b)}, alive {b.IsAlive}, board target {b.BoardTarget?.NpcName ?? "none"}");
                if (!b.IsAlive || b.Carrier is not TrainWagon dw) { Fail("the wagon's hijacker was cleared with the departure"); return; }
                // once the train departs normally it is the ride's ordinary boarder: crew first, the grip
                if (enc.IsSparedBoarder(b) || enc.GripOf(b) < 0) { Fail("after the departure the wagon's hijacker is not an ordinary wagon boarder"); return; }
                if (b.BoardTarget is not { } bitten || !_hjCrew.Contains(bitten) || bitten.Cart != dw) { Fail($"after the departure the wagon's hijacker goes for {b.BoardTarget?.NpcName ?? "nobody"}, not the crew in its wagon"); return; }
                _hjSummary.Add("a wagon's hijacker fought the heroes, never the crew, at the stop, rode through HijackOver and the departure, and then went for its wagon's crew");
                StopTrapOk($"{string.Join("; ", _hjSummary)}; no hijacker freed by the cull");
                break;
            }
        }
    }

    // ---- --test-stop-trap=gotaway (plan 16 Task 7) ------------------------------------------------------------------------------------
    // The hijack, got away, at stop 3 with the real siege (portals on, NoHijack off). Three passes; in each (any step out of its window fails):
    //  setup: rc.Restore(3) and rc.DebugKillHolders(3); stop 1's and stop 2's crew seated in W1 and W2 (DebugSeatCrew; passes 2 and 3 seat stop 3's
    //      crew in W3 too, so that the roll's 12.7 s leave two aboard at the limit); the party stepped out and held 30 m up the line past the stop
    //      (ForcedMoveInput null, teleported back when it drifts), the companions holding fire. The hijack comes through DebugTagNext and the real
    //      run; the flow's sweep kills every siege creature that is not a hijacker 1 s after it came through, and hurries the waves until the roll.
    //  (a) the throws: the crew are put off at 3, 6, 9 and 12 s of RollingBack +- 0.2 in the order the last wagon's seat 5, its seat 4, then the
    //      wagon before (W2 s5, W2 s4, W1 s5, W1 s4 in pass 1), as many as the roll lasts; each alive, beside the lane by its wagon, not on a rail
    //      cell, within 2.5 cells (10 m) of its wagon, and following (or down).
    //  (b) gone: at HijackLimitS +- 0.1 the train is Gone: the driver and every wagon boarder (a later wave's tagged runner) beside the lane
    //      alive, not carried, on the attack (then the boarders die by the test's hand and the driver is stunned there); the cart and every wagon
    //      hidden and not solid, the train uncoupled; the crew still aboard set down there alive (PutOffAtLimit); GoneLog once; no crew lost.
    //  (c) the relief: 12 +- 0.3 s later the cart is shown at the limit with W1..W4 coupled fresh and empty, rolls forward at 4 +- 0.2 m/s and is
    //      held at StopS[3] +- 0.05: ReliefLog, CrewRelief (said or logged), HijackOver; no Arrived, StopIndex 3, the spares unchanged; ThrownIds
    //      the living put-off crew. Pass 1: the lead stands > 12 m behind the train's rear as the relief is held, and is not left behind (the
    //      relief's meeting kept through the hijack: no relief cart for a train that came back).
    //  (d) the crew come back: the lead boards and the countdown starts; the departure waits until every living thrown crew member is seated, or
    //      30 s after the relief's hold; every one within 60 m books and sits before the train leaves; the Departed arms ride 4.
    //  Pass 2 (a death on the way): the first crew member put off is killed 0.3 s later: it is lost, and the departure waits only for the living.
    //      One set down at the limit is moved 40-55 m from the stop's W1 back along the line and waits there (the test's walk) until the relief is
    //      held, so it books from beyond 25 m (the 60 m reach) and the countdown holds at 0 while it walks back.
    //  Pass 3 (a save mid-roll, Review Focus 2): saved at 5 s of RollingBack and loaded: Held at stop 3, Hijack None, no driver, the train whole and
    //      facing forward; the crew put off before the save on foot, the rest seated; nobody thrown; the siege's first wave 10 +- 0.5 s after its restart.
    //  Pass 4 (won back after the throws: the T7 review's Important 1, the ruled cap): the crew seated as in pass 2; after the third throw the lead
    //      knocks the driver off from foot; the second crew member put off is moved 40-55 m and the third 70-90 m back from the stop's W1 and held
    //      there (test walks); the first one put off is set on the line 8 m ahead of the returning cart. The brake and the return (warned as a
    //      party-lane chaser) run nobody down; held again with the lead > 12 m behind the
    //      rear, it is not left behind and no relief cart comes; the near one books from beyond 25 m and, still held out, sits in at once at the
    //      30 s cap; the far one is no longer thrown after the cap and stays on foot; the departure waits 30 s from HijackOver and arms ride 4.
    //  Throughout (Ruling 32): no "straggler ... freed" line names a runner, the driver or a wagon's hijacker (the driver lives at the limit, up to
    //  52 m from the stop and unseen, until the relief is held).
    private const int GaStop = 3;
    private const float GaHoldAhead = 30f;
    private int _gaPass = 1;
    private readonly List<(Npc Npc, int Wagon, int Seat)> _gaSeats = new();
    private readonly Dictionary<Npc, (double At, int Wagon, int Seat, HijackPhase Phase, GridPos Cell, float FromWagon)> _gaOff = new();
    private readonly Dictionary<Npc, double> _gaSat = new();
    private readonly HashSet<Npc> _gaBooked = new();
    private readonly List<(double At, string Text)> _gaLines = new();
    private readonly List<(double At, int Stop, float Delay)> _gaStarts = new();
    private readonly List<(double At, int Stop, int Wave)> _gaWaves = new();
    private readonly List<string> _gaFaults = new();
    private readonly Dictionary<Npc, float> _gaFarAtHold = new();
    private readonly HashSet<Enemy> _gaBoarders = new();
    // pass 4 (won back after the throws; the T7 review's Important 1 and the ruled cap)
    private Npc? _gaNear, _gaFar, _gaOnLine;
    private Vector3 _gaNearPin, _gaFarPin;
    private double _gaKnockAt;
    private float _gaNearBookD = float.NaN;
    private bool _gaMovedBehind, _gaFarChecked;
    private int _gaReliefs;
    private readonly List<string> _gaRunOver = new();
    private bool _gaListening, _gaFreeParty, _gaHoldEngaged;
    private double _gaMark, _gaRollAt, _gaGoneAt, _gaReliefAt, _gaHoldAt, _gaBoardAt, _gaLoadAt;
    private float _gaLimit, _gaMaxV, _gaHoldClock;
    private Vector3 _gaHold;
    private Enemy? _gaDriver;
    private Npc? _gaKilled;
    private int _gaSpares;
    private RailController? _gaRcBeforeLoad;
    private List<(string Id, string Name)> _gaOffBeforeSave = new();
    private List<string> _gaSeatedIds = new();
    private readonly List<string> _gaSummary = new();

    private void GotAwayListen()
    {
        if (_gaListening) return;
        _gaListening = true;
        var bus = EventBus.Instance;
        Action<string> message = text =>
        {
            if (text == StopTrapLines.GoneLog || text == StopTrapLines.ReliefLog || text == StopTrapLines.CrewRelief) _gaLines.Add((StNow, text));
            if (_gaPass == 4 && text.Contains(" runs over ") && _gaSeats.Any(c => IsInstanceValid(c.Npc) && text.Contains(c.Npc.NpcName))) _gaRunOver.Add(text);
        };
        Action<Npc, Line> said = (npc, line) => { if (line.Text == StopTrapLines.CrewRelief) _gaLines.Add((StNow, $"{npc.NpcName}: {line.Text}")); };
        Action<int, float, int> started = (stop, delay, _) => _gaStarts.Add((StNow, stop, delay));
        Action<int, int, IReadOnlyList<GridPos>, IReadOnlyList<int>, int, int> opened = (stop, wave, _, _, _, _) => _gaWaves.Add((StNow, stop, wave));
        bus.Message += message;
        Npc.Said += said;
        PortalSpawner.DebugSiegeStarted += started;
        PortalSpawner.DebugWaveOpened += opened;
        _staticUnhooks.Add(() => { bus.Message -= message; Npc.Said -= said; PortalSpawner.DebugSiegeStarted -= started; PortalSpawner.DebugWaveOpened -= opened; });
    }

    /// <summary>The order the crew are put off (Ruling 13): the last coupled wagon first, its seat 5 then 4.</summary>
    private List<(int Wagon, int Seat)> GaThrowOrder() => _gaSeats.Select(c => (c.Wagon, c.Seat)).OrderByDescending(c => c.Wagon).ThenByDescending(c => c.Seat).ToList();

    private List<Npc> GaLiving() => _gaSeats.Select(c => c.Npc).Where(n => IsInstanceValid(n) && !n.Dead).ToList();

    /// <summary>Each frame from the roll on: a seated crew member out of its seat during the hijack is put off (when, where, and its checks); bookings and sittings after.</summary>
    private void GaWatchCrew(RailController rc, CrewRescueController ctrl, FloorLayout layout)
    {
        foreach (var (npc, w, seat) in _gaSeats)
        {
            if (!IsInstanceValid(npc)) continue;
            if (!_gaOff.ContainsKey(npc) && !npc.Seated && rc.HijackLive)
            {
                var cell = DungeonBuilder.WorldToCell(npc.GlobalPosition);
                var wagon = rc.WagonAt(w);
                var from = wagon != null ? new Vector2(npc.GlobalPosition.X - wagon.GlobalPosition.X, npc.GlobalPosition.Z - wagon.GlobalPosition.Z).Length() : float.NaN;
                _gaOff[npc] = (StNow - _gaRollAt, w, seat, rc.Hijack, cell, from);
                GD.Print($"[stoptrap] gotaway: {npc.NpcName} off W{w} seat {seat} {StNow - _gaRollAt:0.00} s into the roll ({rc.Hijack}) at {cell} (rail {layout.IsRail(cell)}, {from:0.0} m from its wagon), alive {!npc.Dead}, following {npc.Escorting}, down {npc.Downed}");
                if (npc.Dead || layout.IsRail(cell) || !(from <= 2.5f * DungeonBuilder.CellSize) || !(npc.Escorting || npc.Downed)) _gaFaults.Add($"{npc.NpcName} was not put off alive beside its wagon, off the rails and following");
            }
            if (_gaOff.ContainsKey(npc) && !_gaSat.ContainsKey(npc) && npc.Seated && !rc.HijackLive) _gaSat[npc] = StNow;
            if (_gaOff.ContainsKey(npc) && ctrl.BookingOf(npc) != null) _gaBooked.Add(npc);
        }
    }

    /// <summary>The party held where the flow put it (<see cref="_gaHold"/>), until the lead boards: teleported back once a second when it drifted.</summary>
    private void GaHoldParty(PartyController party, float dt)
    {
        if ((_gaHoldClock += dt) < 1f) return;
        _gaHoldClock = 0f;
        party.ForcedMoveInput = null;
        if (party.Local is { } me && !me.Seated && me.GlobalPosition.DistanceTo(_gaHold) > 4f) party.Teleport(_gaHold + Vector3.Up * 0.3f);
    }

    private void RunStopTrapGotAway(Main main, FloorLayout layout, Node3D root, RailController rc, PortalSpawner portals)
    {
        var party = main.Party;
        var dt = (float)GetProcessDeltaTime();
        void Fail(string why) => StopTrapFail($"(pass {_gaPass}) {why}");
        void Next() { _stStep++; _stClock = 0f; _hjSub = 0; }
        HijackWatch(rc, portals);
        if (StopTrapController.Instance is not { } trap || !IsInstanceValid(trap) || trap.GetParent() != root) { if (_stClock > 5f) Fail("floor 12 has no StopTrapController"); return; }
        _hjTrap = trap;
        if (ObjectiveController.Instance is not CrewRescueController ctrl || !IsInstanceValid(ctrl) || ctrl.GetParent() != root) { if (_stClock > 5f) Fail("floor 12 has no CrewRescueController"); return; }
        var enc = RailEncounterController.Instance is { } en && IsInstanceValid(en) && en.GetParent() == root ? en : null;
        var me = party.Local;
        foreach (var a in party.Adventurers) if (IsInstanceValid(a) && a.Control == ControlKind.Companion) { a.DebugHoldFire = true; a.Stance = Characters.Stance.Hold; }
        if (_hjCullFaults.Count > 0) { Fail(string.Join("; ", _hjCullFaults)); return; }
        if (_gaFaults.Count > 0) { Fail(string.Join("; ", _gaFaults)); return; }
        if (_stStep >= 1) { HjSweep(trap); HjHold(trap); }
        if (_stStep == 1 && portals.SiegeStop == GaStop && (_hjWaveAt += dt) >= 2.5f) { _hjWaveAt = 0f; portals.DebugSiegeNow(); } // hurried until the roll (never after pass 3's load)
        if (_stStep is >= 1 and <= 3 && !_gaFreeParty) GaHoldParty(party, dt);
        if (_stStep is (>= 2 and <= 4) or (>= 20 and <= 22)) GaWatchCrew(rc, ctrl, layout);

        switch (_stStep)
        {
            case 0 when _stClock >= 1.5f: // setup
            {
                if (rc.StopS.Length != Floor12Stops) { Fail($"floor 12's line has {rc.StopS.Length} stops, not {Floor12Stops}"); return; }
                if (me == null || enc == null) { Fail("no lead or no encounter controller"); return; }
                HijackListen(main);
                GotAwayListen();
                rc.Restore(GaStop);
                var killed = rc.DebugKillHolders(GaStop);
                foreach (var n in ctrl.Crew) if (IsInstanceValid(n) && !n.Dead && n.Cart is TrainWagon) n.GetOut(n.GlobalPosition); // seated afresh below
                _gaSeats.Clear();
                foreach (var (stop, wagon) in _gaPass == 1 ? new[] { (1, 1), (2, 2) } : new[] { (1, 1), (2, 2), (3, 3) })
                    _gaSeats.AddRange(ctrl.DebugSeatCrew(stop, wagon) ?? new());
                if (_gaSeats.Count < 4 || _gaSeats.Any(c => c.Seat is not (4 or 5))) { Fail($"the crew were not seated in the wagons' benches ([{string.Join(", ", _gaSeats.Select(c => $"{c.Npc.NpcName} W{c.Wagon} s{c.Seat}"))}])"); return; }
                _gaHold = rc.StandBesideLane(rc.Party.Track.Point(rc.StopS[GaStop] + GaHoldAhead));
                _gaFreeParty = false;
                party.ForcedMoveInput = null;
                party.Teleport(_gaHold + Vector3.Up * 0.3f, rc.Cart.GlobalPosition);
                _gaOff.Clear(); _gaSat.Clear(); _gaBooked.Clear(); _gaFarAtHold.Clear(); _gaBoarders.Clear();
                _gaKilled = null; _gaDriver = null; _gaMaxV = 0f; _gaHoldEngaged = false;
                _gaSpares = rc.SparesLeft;
                _gaReliefs = rc.ReliefsSent;
                _gaMark = StNow;
                var tagged = trap.DebugTagNext();
                GD.Print($"[stoptrap] gotaway: pass {_gaPass}: rc.Restore({GaStop}) at S={rc.State.S:0.0}; {killed} holder(s) die by the test's hand; crew [{string.Join(", ", _gaSeats.Select(c => $"{c.Npc.NpcName} W{c.Wagon} s{c.Seat}"))}]; the party held at {DungeonBuilder.WorldToCell(_gaHold)} ({GaHoldAhead:0} m up the line); limit S={rc.RollBackLimitAt(GaStop, out _):0.0}; tagged {(tagged != null ? tagged.Name : "the next spawn")}; spares left {_gaSpares}");
                Next();
                break;
            }
            case 1: // the driver takes the cart, the glow, the roll
            {
                var rolled = HjEventAt(CartEventKind.Hijacked, _gaMark);
                if (rolled < 0) { if (_stClock > 60f) Fail($"no hijack rolled the train in 60 s ({rc.Hijack}, runners [{string.Join(", ", trap.Runners.Select(e => $"{e.Name} {e.State.Name}"))}])"); break; }
                _gaRollAt = rolled;
                _gaLimit = rc.HijackLimitS;
                _gaDriver = rc.Cart.Driver;
                if (_gaDriver == null) { Fail("the roll has no driver"); return; }
                _hjKeep.Add(_gaDriver);
                GD.Print($"[stoptrap] gotaway: {_gaDriver.Name} rolls the train back from stop {GaStop} toward S={_gaLimit:0.000} ({rc.StopS[GaStop] - _gaLimit:0.0} m)");
                if (Mathf.Abs(_gaLimit - HijackRules.ShippedLimits[GaStop].LimitS) > 0.1f) { Fail($"the roll's limit {_gaLimit:0.00} is not stop {GaStop}'s pinned {HijackRules.ShippedLimits[GaStop].LimitS:0.00}"); return; }
                Next();
                break;
            }
            case 2: // (a) the throws; (b) gone (pass 3: the save at 5 s; pass 4: won back, from step 20)
            {
                if (_gaPass == 4) { _stStep = 20; _stClock = 0f; _hjSub = 0; _hjSwings = 0; _hjBlowAt = 0; break; }
                if (_gaPass == 2 && _gaKilled == null && _gaOff.Count > 0)
                {
                    var first = _gaOff.OrderBy(o => o.Value.At).First();
                    if (StNow - _gaRollAt - first.Value.At >= 0.3)
                    {
                        _gaKilled = first.Key;
                        _gaKilled.Die();
                        GD.Print($"[stoptrap] gotaway: {_gaKilled.NpcName} killed on the way ({StNow - _gaRollAt:0.00} s into the roll)");
                    }
                }
                if (_gaPass == 3 && StNow - _gaRollAt >= 5.0)
                {
                    if (rc.Hijack != HijackPhase.RollingBack) { Fail($"the roll is {rc.Hijack} at 5 s"); return; }
                    _gaOffBeforeSave = _gaOff.Keys.Select(n => (n.Id, n.NpcName)).ToList();
                    _gaSeatedIds = _gaSeats.Select(c => c.Npc).Where(n => !_gaOff.ContainsKey(n) && IsInstanceValid(n) && !n.Dead).Select(n => n.Id).ToList();
                    GD.Print($"[stoptrap] gotaway: saved at {StNow - _gaRollAt:0.00} s of the roll at S={rc.State.S:0.00} (V {rc.State.V:0.00}); put off so far [{string.Join(", ", _gaOffBeforeSave.Select(o => o.Name))}]");
                    if (_gaOffBeforeSave.Count != 1) { Fail($"{_gaOffBeforeSave.Count} crew put off by 5 s of the roll, not one"); return; }
                    main.SaveGame(StSlot);
                    _gaRcBeforeLoad = rc;
                    _gaLoadAt = StNow;
                    if (!main.LoadGame(StSlot)) { Fail("the save mid-roll would not load"); return; }
                    _stStep = 10; _stClock = 0f; _hjSub = 0;
                    break;
                }
                if (rc.Hijack is not (HijackPhase.RollingBack or HijackPhase.Gone)) { Fail($"the roll became {rc.Hijack} before it got away (driver {rc.Cart.Driver?.Name ?? "none"})"); return; }
                if (rc.Hijack != HijackPhase.Gone)
                {
                    foreach (var w in rc.Train) foreach (var b in w.Boarders) if (b != null && IsInstanceValid(b) && b.IsAlive && b.Carrier == w) _gaBoarders.Add(b); // the wagons' hijackers (a later wave's tag)
                    if (StNow - _gaRollAt > 40) Fail($"the train never got away in 40 s (S={rc.State.S:0.00})");
                    break;
                }
                _gaGoneAt = StNow;
                var rollFor = _gaGoneAt - _gaRollAt;
                var order = GaThrowOrder();
                var off = _gaOff.OrderBy(o => o.Value.At).ToList();
                var onWay = off.Where(o => o.Value.Phase == HijackPhase.RollingBack).ToList();
                var atLimit = off.Where(o => o.Value.Phase == HijackPhase.Gone).ToList();
                GD.Print($"[stoptrap] gotaway: (a) put off on the way [{string.Join(", ", onWay.Select(o => $"W{o.Value.Wagon} s{o.Value.Seat} at {o.Value.At:0.00} s"))}], at the limit [{string.Join(", ", atLimit.Select(o => $"W{o.Value.Wagon} s{o.Value.Seat}"))}] (the controller's {rc.PutOffOnWay} and {rc.PutOffAtLimit}); the roll lasted {rollFor:0.00} s");
                var due = Math.Min(order.Count, (int)Math.Floor((rollFor + 0.2) / HijackRules.ThrowEvery));
                if (onWay.Count != rc.PutOffOnWay || atLimit.Count != rc.PutOffAtLimit || onWay.Count + atLimit.Count != order.Count) { Fail("the crew put off do not add up to the crew aboard"); return; }
                if (onWay.Count < Math.Min(order.Count, (int)Math.Floor((rollFor - 0.2) / HijackRules.ThrowEvery)) || onWay.Count > due) { Fail($"{onWay.Count} put off on a {rollFor:0.00} s roll"); return; }
                for (var i = 0; i < onWay.Count; i++)
                {
                    var o = onWay[i].Value;
                    if ((o.Wagon, o.Seat) != order[i]) { Fail($"the {i + 1}th put off was W{o.Wagon} s{o.Seat}, not W{order[i].Wagon} s{order[i].Seat}"); return; }
                    if (Math.Abs(o.At - HijackRules.ThrowEvery * (i + 1)) > 0.2) { Fail($"the {i + 1}th throw came at {o.At:0.00} s of the roll, not {HijackRules.ThrowEvery * (i + 1):0} +- 0.2"); return; }
                }
                if (!atLimit.Select(o => (o.Value.Wagon, o.Value.Seat)).ToHashSet().SetEquals(order.Skip(onWay.Count))) { Fail($"the crew set down at the limit [{string.Join(", ", atLimit.Select(o => $"W{o.Value.Wagon} s{o.Value.Seat}"))}] are not the rest of the train"); return; } // all at once: their order is one tick's
                // (b) gone
                var d = _gaDriver!;
                var dCell = DungeonBuilder.WorldToCell(d.GlobalPosition);
                GD.Print($"[stoptrap] gotaway: (b) gone at S={rc.State.S:0.000} (limit {_gaLimit:0.000}); {d.Name} at {dCell} (rail {layout.IsRail(dCell)}), alive {d.IsAlive}, carried {d.Carried}, {d.State.Name}, unaware {d.State.Unaware}; the cart shown {rc.Cart.Visible} layer {rc.Cart.CollisionLayer}; train [{string.Join(",", rc.Train.Select(w => w.Number))}]; wagons [{string.Join(", ", rc.Wagons.Select(w => $"W{w.Number} coupled {w.Coupled} shown {w.Visible} layer {w.CollisionLayer}"))}]; \"{StopTrapLines.GoneLog}\" {_gaLines.Count(x => x.At >= _gaRollAt && x.Text == StopTrapLines.GoneLog)}x");
                if (Mathf.Abs(rc.State.S - _gaLimit) > 0.1f) { Fail($"the train was gone at S={rc.State.S:0.00}, not its limit {_gaLimit:0.00}"); return; }
                if (!IsInstanceValid(d) || !d.IsAlive || d.Carried || rc.Cart.Driver != null || layout.IsRail(dCell) || d.State.Unaware || d.State is RunForCartState) { Fail("the driver is not standing beside the lane alive and on the attack"); return; }
                if (rc.Cart.Visible || rc.Cart.CollisionLayer != 0) { Fail("the gone cart is shown or solid"); return; }
                if (rc.Train.Count != 0 || rc.Wagons.Any(w => w.Coupled || w.Visible || w.CollisionLayer != 0)) { Fail("a gone wagon is coupled, shown or solid"); return; }
                if (_gaLines.Count(x => x.At >= _gaRollAt && x.Text == StopTrapLines.GoneLog) != 1) { Fail($"\"{StopTrapLines.GoneLog}\" was not logged once"); return; }
                var lostNow = _gaSeats.Where(c => (!IsInstanceValid(c.Npc) || c.Npc.Dead || ctrl.StateOf(c.Npc) == CrewState.Lost) && c.Npc != _gaKilled).Select(c => c.Npc.NpcName).ToList();
                if (lostNow.Count > 0) { Fail($"crew lost to the hijack: {string.Join(", ", lostNow)}"); return; }
                if (GaLiving().Any(n => n.Seated)) { Fail("crew still seated in the gone train"); return; }
                foreach (var b in _gaBoarders)
                {
                    var bc = IsInstanceValid(b) ? DungeonBuilder.WorldToCell(b.GlobalPosition) : default;
                    GD.Print($"[stoptrap] gotaway: (b) the wagons' boarder {(IsInstanceValid(b) ? b.Name : "?")} leapt off: at {bc} (rail {layout.IsRail(bc)}), alive {IsInstanceValid(b) && b.IsAlive}, carried {IsInstanceValid(b) && b.Carried}, {(IsInstanceValid(b) ? b.State.Name : "-")}");
                    if (!IsInstanceValid(b) || !b.IsAlive || b.Carried || layout.IsRail(bc) || b.State.Unaware) { Fail("a wagon's boarder did not leap off beside the lane alive and on the attack"); return; }
                    HjKill(b); // done with: the crew walk back past it
                }
                d.Effects.Apply(new Combat.StatusApplication(Combat.StatusKind.Stun, 30f, 0f)); // kept alive and still at the limit for the cull's check (Ruling 32), harming nobody
                if (_gaPass == 2 && atLimit.Count > 0) // one set down at the limit waits farther back along the line (the test's walk) until the relief is held: it books from beyond the usual 25 m
                {
                    var far = atLimit[0].Key;
                    var w1At = rc.Party.Track.Point(rc.StopS[GaStop] - TrainRules.Spacing); // where W1 is held at the stop
                    Vector3? wait = null;
                    for (var sb = _gaLimit; sb > _gaLimit - 60f && wait == null; sb -= 2f)
                    {
                        var p = rc.StandBesideLane(rc.Party.Track.Point(sb));
                        var fd = new Vector2(p.X - w1At.X, p.Z - w1At.Z).Length();
                        if (fd >= 40f && fd <= 55f) wait = p;
                    }
                    if (wait is not { } waitAt) { Fail("no place beside the line 40-55 m from the stop's W1 behind the limit"); return; }
                    ctrl.DebugPutOnFoot(far.Id, waitAt);
                    far.MoveTarget = waitAt;
                    far.MoveOverride = true;
                    GD.Print($"[stoptrap] gotaway: (b) {far.NpcName} waits at {DungeonBuilder.WorldToCell(waitAt)}, {new Vector2(waitAt.X - w1At.X, waitAt.Z - w1At.Z).Length():0.0} m from the stop's W1, until the relief is held");
                }
                _gaSummary.Add($"pass {_gaPass}: {onWay.Count} put off on the way at [{string.Join(", ", onWay.Select(o => o.Value.At.ToString("0.00")))}] s, {atLimit.Count} at the limit; gone at {rc.State.S:0.0}");
                Next();
                break;
            }
            case 3: // (c) the relief
            {
                if (_hjSub == 0)
                {
                    if (rc.Hijack == HijackPhase.Gone) { if (StNow - _gaGoneAt > 13.0) Fail("no relief train 13 s after the train went"); break; }
                    if (rc.Hijack != HijackPhase.ReliefIn) { Fail($"after the getaway the train is {rc.Hijack}, not a relief rolling in"); return; }
                    _gaReliefAt = StNow;
                    var after = _gaReliefAt - _gaGoneAt;
                    var empty = rc.Train.All(w => w.Riders.Count == 0 && w.Boarders.All(b => b == null || !IsInstanceValid(b) || b.Carrier != w));
                    GD.Print($"[stoptrap] gotaway: (c) the relief {after:0.00} s after the train went, at S={rc.State.S:0.000} (limit {_gaLimit:0.000}); shown {rc.Cart.Visible}; train [{string.Join(",", rc.Train.Select(w => w.Number))}], empty {empty}");
                    if (Math.Abs(after - HijackRules.ReliefAfter) > 0.3) { Fail($"the relief came {after:0.00} s after the train went, not 12 +- 0.3"); return; }
                    if (!rc.Cart.Visible || Mathf.Abs(rc.State.S - _gaLimit) > 0.15f) { Fail("the relief cart is not shown at the limit"); return; }
                    if (string.Join(",", rc.Train.Select(w => w.Number)) != "1,2,3,4" || !empty || rc.Train.Any(w => !w.Coupled || !w.Visible)) { Fail("the relief's wagons are not W1..W4 coupled fresh and empty"); return; }
                    _hjSub = 1;
                    break;
                }
                if (_hjSub == 1)
                {
                    if (rc.Hijack == HijackPhase.ReliefIn) _gaMaxV = Mathf.Max(_gaMaxV, rc.State.V);
                    // pass 1: the lead stands > 12 m behind the train's rear as it is held (it met them before the hijack and still does); moved there
                    // in the relief's last 3 m, so no thrown crew following it is on the line ahead of the relief
                    if (_gaPass == 1 && !_gaFreeParty && rc.Hijack == HijackPhase.ReliefIn && rc.State.S >= rc.StopS[GaStop] - 3f)
                    {
                        _gaFreeParty = true;
                        _gaHold = rc.StandBesideLane(rc.Party.Track.Point(rc.StopS[GaStop] - TrainRules.Length(4) + CartRules.HalfLength - 20f));
                        party.Teleport(_gaHold + Vector3.Up * 0.3f, rc.Cart.GlobalPosition);
                    }
                    var over = HjEventAt(CartEventKind.HijackOver, _gaReliefAt);
                    if (over < 0) { if (StNow - _gaReliefAt > 25) Fail($"the relief never reached stop {GaStop} ({rc.Hijack}, S={rc.State.S:0.00})"); break; }
                    if (rc.LeftBehindNow) { Fail("the lead behind the held relief train is left behind (a relief cart would come for a train that came back)"); return; }
                    if (StNow - over < 0.3) break; // the crew's bark is said a beat after its call (AdventureDirector.SayAs)
                    _gaHoldAt = over;
                    var arrived = _hjEvents.Count(x => x.At >= _gaRollAt && x.Kind == CartEventKind.Arrived);
                    var reliefLog = _gaLines.Count(x => x.At >= _gaReliefAt && x.Text == StopTrapLines.ReliefLog);
                    var bark = _gaLines.Where(x => x.At >= _gaReliefAt && x.Text.EndsWith(StopTrapLines.CrewRelief)).Select(x => x.Text).ToList();
                    var living = _gaOff.Keys.Where(n => IsInstanceValid(n) && !n.Dead).ToList();
                    var thrownIds = ctrl.ThrownIds.ToHashSet();
                    GD.Print($"[stoptrap] gotaway: (c) held at S={rc.State.S:0.000} (stop {rc.StopIndex} at {rc.StopS[GaStop]:0.000}) {over - _gaReliefAt:0.00} s after it came; max V {_gaMaxV:0.00}; \"{StopTrapLines.ReliefLog}\" {reliefLog}x; the bark [{string.Join(" | ", bark)}]; Arrived {arrived}; spares left {rc.SparesLeft} (were {_gaSpares}); thrown [{string.Join(", ", thrownIds)}]; the driver {(_gaDriver != null && IsInstanceValid(_gaDriver) && _gaDriver.IsAlive ? $"alive at {DungeonBuilder.WorldToCell(_gaDriver.GlobalPosition)}" : "gone")}");
                    if (rc.State.Mode != CartMode.Held || Mathf.Abs(rc.State.S - rc.StopS[GaStop]) > 0.05f || rc.Hijack != HijackPhase.None) { Fail($"the relief ended {rc.State.Mode} at S={rc.State.S:0.00} ({rc.Hijack}), not held at stop {GaStop}"); return; }
                    if (Math.Abs(_gaMaxV - HijackRules.RollBackMaxSpeed) > 0.2f) { Fail($"the relief rolled in at {_gaMaxV:0.00} m/s at most, not 4 +- 0.2"); return; }
                    if (reliefLog != 1 || bark.Count != 1) { Fail($"the relief's log ({reliefLog}x) or the crew's bark ({bark.Count}x) was not told once"); return; }
                    if (arrived != 0 || rc.StopIndex != GaStop || rc.SparesLeft != _gaSpares) { Fail($"the relief was an arrival ({arrived}), moved the stop ({rc.StopIndex}) or spent a spare ({rc.SparesLeft} left, were {_gaSpares})"); return; }
                    if (!thrownIds.SetEquals(living.Select(n => n.Id))) { Fail($"ThrownIds [{string.Join(", ", thrownIds)}] are not the living put-off crew [{string.Join(", ", living.Select(n => n.Id))}]"); return; }
                    if (_gaDriver == null || !IsInstanceValid(_gaDriver) || !_gaDriver.IsAlive) { Fail("the driver did not live through the getaway (freed?)"); return; }
                    var w1 = rc.WagonAt(1)!;
                    foreach (var n in living) _gaFarAtHold[n] = new Vector2(n.GlobalPosition.X - w1.GlobalPosition.X, n.GlobalPosition.Z - w1.GlobalPosition.Z).Length();
                    foreach (var n in living) if (n.MoveTarget is { } pin && ctrl.BookingOf(n) == null && pin.DistanceTo(n.GlobalPosition) < 1.5f) { n.MoveTarget = null; n.MoveOverride = false; } // the test's wait at the limit is over
                    _hjKeep.Remove(_gaDriver);
                    HjKill(_gaDriver); // done with: the cull never had it
                    _gaSummary.Add($"pass {_gaPass}: the relief {_gaReliefAt - _gaGoneAt:0.00} s after, in at {_gaMaxV:0.00} m/s, held at stop {GaStop}");
                    _hjSub = _gaPass == 1 ? 2 : 3;
                    _stClock = 0f;
                    break;
                }
                if (_hjSub == 2) // pass 1: the relief's meeting kept (the carried point from the T6 review)
                {
                    var rear = rc.State.S - TrainRules.Length(rc.Train.Count) + CartRules.HalfLength;
                    var behind = rear - rc.PlayerLineS;
                    if (rc.LeftBehindNow) { Fail($"the lead {behind:0.0} m behind the held relief train is left behind (a relief cart would come for a train that came back)"); return; }
                    if (_stClock < 1f) break;
                    GD.Print($"[stoptrap] gotaway: (c) the lead {behind:0.0} m behind the train's rear for 1 s: not left behind (met kept), reliefs sent {rc.ReliefsSent}");
                    if (behind <= ReliefRules.LeftBehindRange) { Fail($"the lead stood only {behind:0.0} m behind the train's rear: the check needs over 12"); return; }
                    _hjSub = 3;
                    break;
                }
                _gaFreeParty = true;
                if (!RideBoard(main, layout, rc, out var why)) { Fail(why); return; }
                _gaBoardAt = StNow;
                Next();
                break;
            }
            case 4: // (d) the crew come back; the departure waits for them
            {
                RideWaiveCages(main, rc, "stoptrap");
                if (rc.Boarding.Running && rc.Boarding.Left <= 0f) _gaHoldEngaged = true;
                if ((_gaHoldClock += dt) >= 2f)
                {
                    _gaHoldClock = 0f;
                    GD.Print($"[stoptrap] gotaway: (d) +{StNow - _gaHoldAt:0.0} s: [{string.Join(", ", _gaOff.Keys.Where(IsInstanceValid).Select(n => $"{n.NpcName} at {DungeonBuilder.WorldToCell(n.GlobalPosition)} {(n.Seated ? "seated" : n.Downed ? "down" : $"to {(n.MoveTarget is { } g ? DungeonBuilder.WorldToCell(g).ToString() : "-")} ({n.MoveOverride}) v {n.Velocity.Length():0.0}")} booking {ctrl.BookingOf(n)?.ToString() ?? "-"}"))}]");
                }
                var dep = HjEventAt(CartEventKind.Departed, _gaBoardAt);
                if (dep < 0) { if (StNow - _gaHoldAt > 45) Fail($"no departure from stop {GaStop} 45 s after the relief's hold (boarding {rc.Boarding.Running}, lead seated {me?.Seated}, thrown on the way {ctrl.ThrownOnTheWay(GaStop)})"); break; }
                if (StNow - dep < 0.5) break;
                var living = _gaOff.Keys.Where(n => IsInstanceValid(n) && !n.Dead).ToList();
                var lastSat = living.Select(n => _gaSat.TryGetValue(n, out var t) ? t : double.PositiveInfinity).DefaultIfEmpty(0).Max();
                var waited = dep - _gaHoldAt;
                GD.Print($"[stoptrap] gotaway: (d) departed {waited:0.00} s after the relief's hold (the lead boarded at +{_gaBoardAt - _gaHoldAt:0.00}; the countdown held at 0: {_gaHoldEngaged}); the thrown [{string.Join(", ", living.Select(n => $"{n.NpcName} {_gaFarAtHold.GetValueOrDefault(n):0.0} m at the hold, booked {_gaBooked.Contains(n) || _gaSat.ContainsKey(n)}, {(_gaSat.TryGetValue(n, out var t) ? $"sat at +{t - _gaHoldAt:0.00}" : "on foot")}"))}]; armed ride {enc?.ArmedRide}");
                foreach (var n in living)
                {
                    if (_gaFarAtHold.GetValueOrDefault(n) > HijackRules.ThrownBoardReach) continue; // beyond the reach: it stays on foot
                    if (!_gaBooked.Contains(n) && !_gaSat.ContainsKey(n)) { Fail($"{n.NpcName}, put off and within 60 m, never booked a seat"); return; }
                    if (!_gaSat.TryGetValue(n, out var at) || at > dep + 0.05) { Fail($"{n.NpcName}, put off and within 60 m, was not seated before the train left"); return; }
                }
                if (double.IsPositiveInfinity(lastSat) ? waited < HijackRules.ThrownWaitMax - 0.15 : dep < lastSat - 0.05) { Fail($"the train left {waited:0.00} s after the hold with thrown crew still on the way"); return; }
                if (_gaPass == 2 && waited > HijackRules.ThrownWaitMax - 1) { Fail($"the departure waited {waited:0.00} s: it waited for the dead"); return; }
                if (enc?.ArmedRide != GaStop + 1) { Fail($"the departure armed ride {enc?.ArmedRide}, not ride {GaStop + 1}"); return; }
                if (_gaPass == 2)
                {
                    var far = living.Where(n => _gaFarAtHold.GetValueOrDefault(n) > TrainRules.BoardReach).ToList();
                    if (far.Count == 0) { Fail("no thrown crew member stood beyond 25 m at the hold: the 60 m reach was not tried"); return; }
                    if (!_gaHoldEngaged) { Fail("the countdown never held at 0 for the thrown crew walking back"); return; }
                    var k = _gaKilled;
                    if (k == null || !IsInstanceValid(k) || !k.Dead || ctrl.StateOf(k) != CrewState.Lost || ctrl.ThrownIds.Contains(k.Id)) { Fail($"the crew member killed on the way ({k?.NpcName}) is not counted lost"); return; }
                    _gaSummary.Add($"pass 2: {k.NpcName} killed on the way is lost; [{string.Join(", ", far.Select(n => $"{n.NpcName} from {_gaFarAtHold[n]:0.0} m"))}] booked beyond 25 m and sat; the departure held at 0 and left {waited:0.00} s after the hold, for the living only");
                }
                else _gaSummary.Add($"pass 1: every thrown crew member booked from up to {living.Select(n => _gaFarAtHold.GetValueOrDefault(n)).DefaultIfEmpty(0).Max():0.0} m and sat; departed {waited:0.00} s after the hold (held at 0: {_gaHoldEngaged}), arming ride 4; the lead behind the returned train not left behind");
                _gaPass++;
                _stStep = 0; _stClock = 0f; _hjSub = 0;
                break;
            }
            case 10: // pass 3: after the load
            {
                if (rc == _gaRcBeforeLoad) { if (_stClock > 20f) Fail("the load never built the floor again"); break; }
                if (_hjSub == 0) // the first frame of the loaded floor: the crew put off before the save stand on foot, none of them thrown (ordinary freed crew, 25 m)
                {
                    foreach (var (id, name) in _gaOffBeforeSave)
                        if (ctrl.Crew.FirstOrDefault(n => IsInstanceValid(n) && n.Id == id) is not { Dead: false, Seated: false } n) { Fail($"{name}, put off before the save, is not on foot after the load"); return; }
                    if (ctrl.ThrownIds.Count != 0) { Fail("crew are still thrown after the load (they are ordinary freed crew)"); return; }
                    GD.Print($"[stoptrap] gotaway: after the load [{string.Join(", ", _gaOffBeforeSave.Select(o => o.Name))}] on foot, nobody thrown");
                    _hjSub = 1;
                    _stClock = 0f;
                    break;
                }
                if (_stClock < 1.5f) break;
                var wrong = RollPoseWrong(rc);
                GD.Print($"[stoptrap] gotaway: after the load {rc.State.Mode} at S={rc.State.S:0.000} stop {rc.StopIndex}, hijack {rc.Hijack}, driver {rc.Cart.Driver?.Name ?? "none"}, facing {rc.Party.Facing}, train [{string.Join(",", rc.Train.Select(w => w.Number))}], pose {wrong ?? "ok"}; thrown [{string.Join(", ", ctrl.ThrownIds)}]");
                if (rc.State.Mode != CartMode.Held || rc.StopIndex != GaStop || Mathf.Abs(rc.State.S - rc.StopS[GaStop]) > 0.05f) { Fail($"after the load the train is {rc.State.Mode} at S={rc.State.S:0.00} (stop {rc.StopIndex})"); return; }
                if (rc.Hijack != HijackPhase.None || rc.Cart.Driver != null || rc.Party.Facing != 1 || rc.Party.Dir != 1 || rc.Train.Count != 4 || !rc.Cart.Visible || wrong != null) { Fail($"the load kept something of the hijack ({wrong ?? "the phase, the driver, the facing or the train"})"); return; }
                foreach (var id in _gaSeatedIds)
                    if (ctrl.Crew.FirstOrDefault(n => IsInstanceValid(n) && n.Id == id) is not { Dead: false, Seated: true } n || n.Cart is not TrainWagon) { Fail($"{id}, seated at the save, is not seated in the train after the load"); return; }
                if (ctrl.ThrownIds.Count != 0) { Fail("crew are still thrown after the load (they are ordinary freed crew)"); return; }
                Next();
                break;
            }
            case 11: // pass 3: the siege restarts from wave 1 after the load's grace
            {
                var start = _gaStarts.LastOrDefault(x => x.At >= _gaLoadAt - 0.1 && x.Stop == GaStop);
                if (start.Stop != GaStop) { Fail($"the load did not restart stop {GaStop}'s siege"); return; }
                var w1 = _gaWaves.FirstOrDefault(x => x.At >= start.At && x.Stop == GaStop);
                if (w1.Stop != GaStop) { if (StNow - start.At > 13) Fail("no siege wave 13 s after the load's restart"); break; }
                var t = w1.At - start.At;
                GD.Print($"[stoptrap] gotaway: after the load the siege restarted {start.At - _gaLoadAt:0.00} s after the load call with a {start.Delay:0.0} s grace; wave {w1.Wave} {t:0.00} s after");
                if (w1.Wave != 1 || Mathf.Abs(start.Delay - StopSiegeRules.LoadGrace) > 1e-3f || Math.Abs(t - StopSiegeRules.LoadGrace) > 0.5) { Fail($"the load's first wave was wave {w1.Wave} at {t:0.00} s (grace {start.Delay:0.0}), not wave 1 at 10 +- 0.5"); return; }
                _gaSummary.Add($"pass 3: saved at 5 s of the roll, loaded whole at stop {GaStop} with {_gaOffBeforeSave.Count} put off on foot; wave 1 {t:0.00} s after");
                _gaPass = 4;
                _stStep = 0; _stClock = 0f; _hjSub = 0;
                break;
            }
            case 20: // pass 4 (won back after the throws): the near and far crew placed; the lead knocks the driver off from foot after the third throw
            {
                GaNoCrewDowned(Fail);
                var off = _gaOff.OrderBy(o => o.Value.At).Select(o => o.Key).ToList();
                if (off.Count >= 2 && _gaNear == null) { _gaNear = off[1]; _gaNearPin = GaPlaceBack(rc, ctrl, _gaNear, 40f, 55f, Fail); }
                if (off.Count >= 3 && _gaFar == null) { _gaFar = off[2]; _gaFarPin = GaPlaceBack(rc, ctrl, _gaFar, 70f, 90f, Fail); }
                if (_gaNear != null) GaPin(_gaNear, _gaNearPin);
                if (_gaFar != null) GaPin(_gaFar, _gaFarPin);
                var d = _gaDriver!;
                if (rc.Cart.Driver != d)
                {
                    if (rc.Hijack is not (HijackPhase.Braking or HijackPhase.Returning)) { Fail($"the driver went and the train is {rc.Hijack}, not braking or returning"); return; }
                    _gaKnockAt = StNow;
                    var hits = _hjDriverHits.Count(h => h.E == d);
                    GD.Print($"[stoptrap] gotaway: (pass 4) {d.Name} knocked off {_gaKnockAt - _gaRollAt:0.00} s into the roll after {hits} hit(s) in {_hjSwings} swing(s); put off [{string.Join(", ", off.Select(n => n.NpcName))}], aboard [{string.Join(", ", GaLiving().Where(n => n.Seated).Select(n => n.NpcName))}]");
                    if (off.Count < 3 || _gaNear == null || _gaFar == null) { Fail($"only {off.Count} crew were put off before the knock-off"); return; }
                    HjKill(d);
                    foreach (var w in rc.Train) foreach (var b in w.Boarders) if (b != null && IsInstanceValid(b) && b.Carrier == w) HjKill(b); // a later wave's wagon hijacker: not this pass's business
                    Next();
                    break;
                }
                if (rc.Hijack != HijackPhase.RollingBack) { Fail($"the roll became {rc.Hijack} before the driver was knocked off"); return; }
                if (StNow - _gaRollAt < 3 * HijackRules.ThrowEvery + 0.05) break; // after the third throw
                if (_hjSwings >= 14) { Fail($"{_hjSwings} swings did not knock the driver off"); return; }
                if (me == null || me.BladeSwinging || StNow - _hjBlowAt < 0.4) break;
                _gaFreeParty = true;
                _hjStruck = d;
                me.Teleport(HjBesideCart(rc), d.GlobalPosition);
                HjSwing(party, me, d);
                _hjSwings++;
                _hjBlowAt = StNow;
                break;
            }
            case 21: // pass 4: the brake and the return (warned as a party-lane chaser), held again; nobody downed; the relief's meeting kept
            {
                GaNoCrewDowned(Fail);
                GaPin(_gaNear!, _gaNearPin);
                GaPin(_gaFar!, _gaFarPin);
                if (_hjSub == 0)
                {
                    if (rc.Hijack == HijackPhase.Returning && _gaOnLine == null && _gaOff.Keys.FirstOrDefault(n => n != _gaNear && n != _gaFar && IsInstanceValid(n) && !n.Dead && !n.Seated) is { } walker)
                    {
                        // the first one put off stands on the line 8 m ahead of the returning cart's front: the return's warning must send it to a refuge
                        _gaOnLine = walker;
                        var at = rc.Party.Track.Point(rc.Party.FrontS + 8f) + Vector3.Up * 0.1f;
                        ctrl.DebugPutOnFoot(walker.Id, at);
                        var cell = DungeonBuilder.WorldToCell(walker.GlobalPosition);
                        GD.Print($"[stoptrap] gotaway: (pass 4) {walker.NpcName} stands on the line at {cell} (rail {layout.IsRail(cell)}) 8 m ahead of the returning cart's front (S={rc.Party.FrontS:0.0}, V {rc.State.V:0.00})");
                        if (!layout.IsRail(cell)) { Fail("the walker was not set on the line"); return; }
                    }
                    if (rc.Hijack == HijackPhase.Returning && rc.State.S >= rc.StopS[GaStop] - 3f && !_gaMovedBehind) // the lead > 12 m behind the train's rear as it is held
                    {
                        _gaMovedBehind = true;
                        _gaHold = rc.StandBesideLane(rc.Party.Track.Point(rc.StopS[GaStop] - TrainRules.Length(4) + CartRules.HalfLength - 20f));
                        party.Teleport(_gaHold + Vector3.Up * 0.3f, rc.Cart.GlobalPosition);
                    }
                    var over = HjEventAt(CartEventKind.HijackOver, _gaKnockAt);
                    if (over < 0) { if (StNow - _gaKnockAt > 40) Fail($"the train never came back after the knock-off ({rc.Hijack}, S={rc.State.S:0.00})"); break; }
                    if (rc.LeftBehindNow) { Fail("the lead behind the returned train is left behind (a relief cart would come for a train that came back)"); return; }
                    if (StNow - over < 1.0) break;
                    _gaHoldAt = over;
                    var thrown = ctrl.ThrownIds.ToHashSet();
                    var rear = rc.State.S - TrainRules.Length(rc.Train.Count) + CartRules.HalfLength;
                    var behind = rear - rc.PlayerLineS;
                    var w1 = rc.WagonAt(1)!;
                    foreach (var n in GaLiving()) _gaFarAtHold[n] = new Vector2(n.GlobalPosition.X - w1.GlobalPosition.X, n.GlobalPosition.Z - w1.GlobalPosition.Z).Length();
                    GD.Print($"[stoptrap] gotaway: (pass 4) held again at S={rc.State.S:0.000} {_gaHoldAt - _gaKnockAt:0.00} s after the knock-off; thrown [{string.Join(", ", thrown)}]; {_gaNear!.NpcName} {_gaFarAtHold[_gaNear]:0.0} m, {_gaFar!.NpcName} {_gaFarAtHold[_gaFar]:0.0} m from W1; the lead {behind:0.0} m behind the rear for 1 s, left behind {rc.LeftBehindNow}, reliefs sent {rc.ReliefsSent} (were {_gaReliefs}); the relief train {rc.Hijack}");
                    if (rc.State.Mode != CartMode.Held || Mathf.Abs(rc.State.S - rc.StopS[GaStop]) > 0.05f || rc.Hijack != HijackPhase.None) { Fail("the won-back train is not held at its stop"); return; }
                    if (_gaOnLine == null) { Fail("no crew member was set on the line ahead of the return"); return; }
                    if (!thrown.SetEquals(_gaOff.Keys.Where(n => IsInstanceValid(n) && !n.Dead && !n.Seated).Select(n => n.Id))) { Fail("ThrownIds are not the crew put off still on foot"); return; }
                    if (behind <= ReliefRules.LeftBehindRange) { Fail($"the lead stood only {behind:0.0} m behind the train's rear: the check needs over 12"); return; }
                    if (rc.ReliefsSent != _gaReliefs) { Fail("a relief cart was sent for the train that came back"); return; }
                    if (!(_gaFarAtHold[_gaFar] > HijackRules.ThrownBoardReach)) { Fail($"the far crew member is only {_gaFarAtHold[_gaFar]:0.0} m away"); return; }
                    _hjSub = 1;
                    _gaFreeParty = true;
                    if (!RideBoard(main, layout, rc, out var why)) { Fail(why); return; }
                    _gaBoardAt = StNow;
                    Next();
                }
                break;
            }
            case 22: // pass 4: the departure waits from HijackOver; the cap seats the near one at once and ends the far one's status
            {
                GaNoCrewDowned(Fail);
                RideWaiveCages(main, rc, "stoptrap");
                var near = _gaNear!;
                var far = _gaFar!;
                if (ctrl.BookingOf(near) != null && float.IsNaN(_gaNearBookD)) _gaNearBookD = _gaFarAtHold.GetValueOrDefault(near); // its booking came at the hold, from where it stood
                if (ctrl.BookingOf(far) != null) { Fail($"{far.NpcName}, beyond 60 m, booked a seat"); return; }
                if (!near.Seated) GaPin(near, _gaNearPin); // a test's walk: it stays out until the cap
                if (!far.Seated) GaPin(far, _gaFarPin);
                if (StNow - _gaHoldAt >= HijackRules.ThrownWaitMax + 0.5 && !_gaFarChecked)
                {
                    _gaFarChecked = true;
                    GD.Print($"[stoptrap] gotaway: (pass 4) at the cap +0.5 s: {near.NpcName} seated {near.Seated}, {far.NpcName} seated {far.Seated}, thrown [{string.Join(", ", ctrl.ThrownIds)}]");
                    if (ctrl.ThrownIds.Contains(far.Id)) { Fail($"{far.NpcName} is still thrown after the 30 s cap"); return; }
                }
                var dep = HjEventAt(CartEventKind.Departed, _gaBoardAt);
                if (dep < 0) { if (StNow - _gaHoldAt > 45) Fail($"no departure 45 s after the hold (boarding {rc.Boarding.Running}, thrown on the way {ctrl.ThrownOnTheWay(GaStop)})"); break; }
                if (StNow - dep < 1.0) break;
                var waited = dep - _gaHoldAt;
                var nearAt = _gaSat.TryGetValue(near, out var na) ? na - _gaHoldAt : double.NaN;
                var others = _gaOff.Keys.Where(n => n != near && n != far && IsInstanceValid(n) && !n.Dead).ToList();
                GD.Print($"[stoptrap] gotaway: (pass 4) departed {waited:0.00} s after HijackOver; {near.NpcName} booked from {_gaNearBookD:0.0} m and sat at +{nearAt:0.00}; {far.NpcName} on foot {!far.Seated}, thrown {ctrl.ThrownIds.Contains(far.Id)}, at the cap checked {_gaFarChecked}; [{string.Join(", ", others.Select(n => $"{n.NpcName} sat at +{(_gaSat.TryGetValue(n, out var t) ? t - _gaHoldAt : double.NaN):0.00}"))}]; armed ride {enc?.ArmedRide}; reliefs sent {rc.ReliefsSent}");
                if (!(_gaNearBookD > TrainRules.BoardReach && _gaNearBookD <= HijackRules.ThrownBoardReach)) { Fail($"{near.NpcName} did not book from beyond 25 m within 60 ({_gaNearBookD:0.0} m)"); return; }
                if (!(Math.Abs(nearAt - HijackRules.ThrownWaitMax) <= 0.2)) { Fail($"{near.NpcName} sat at +{nearAt:0.00}, not at once at the 30 s cap"); return; }
                if (waited < HijackRules.ThrownWaitMax - 0.15) { Fail($"the train left {waited:0.00} s after HijackOver, before the thrown crew's cap"); return; }
                if (far.Seated || far.Dead || !_gaFarChecked || ctrl.ThrownIds.Contains(far.Id)) { Fail($"{far.NpcName} (beyond 60 m) did not stay on foot as an ordinary freed crew member"); return; }
                if (others.Any(n => !_gaSat.TryGetValue(n, out var t) || t > dep + 0.05)) { Fail("a thrown crew member within reach was not seated before the train left"); return; }
                if (enc?.ArmedRide != GaStop + 1 || rc.ReliefsSent != _gaReliefs) { Fail($"the departure armed ride {enc?.ArmedRide} (want {GaStop + 1}), or a relief cart came ({rc.ReliefsSent} sent, were {_gaReliefs})"); return; }
                _gaSummary.Add($"pass 4: won back after {_gaOff.Count} throws; the return ran nobody down ({_gaOnLine?.NpcName} on the line 8 m ahead stepped clear); {near.NpcName} booked from {_gaNearBookD:0.0} m and sat at once at the cap (+{nearAt:0.00}); {far.NpcName} beyond 60 m stayed on foot, no longer thrown; the departure waited {waited:0.00} s from HijackOver; no relief cart");
                StopTrapOk($"{string.Join("; ", _gaSummary)}; no hijacker freed by the cull");
                break;
            }
        }
    }

    /// <summary>Pass 4: no crew member of the train downed or dead (the returning train warns them off the line), and no run-over of one logged.</summary>
    private void GaNoCrewDowned(Action<string> fail)
    {
        if (_gaRunOver.Count > 0) { fail($"the train ran over crew: {string.Join("; ", _gaRunOver)}"); return; }
        if (GaLiving().Count != _gaSeats.Count || _gaSeats.Any(c => c.Npc.Downed)) fail($"crew downed or dead in the won-back hijack: [{string.Join(", ", _gaSeats.Where(c => !IsInstanceValid(c.Npc) || c.Npc.Dead || c.Npc.Downed).Select(c => c.Npc.NpcName))}]");
    }

    /// <summary>A test's walk: <paramref name="npc"/> holds at <paramref name="at"/> (a booking's walk yields to it, a refuge's too).</summary>
    private static void GaPin(Npc npc, Vector3 at)
    {
        if (!IsInstanceValid(npc) || npc.Seated) return;
        npc.MoveTarget = at;
        npc.MoveOverride = true;
    }

    /// <summary>Pass 4: <paramref name="npc"/> set down beside the line behind the limit, <paramref name="min"/>-<paramref name="max"/> m from the stop's W1, and held there.</summary>
    private Vector3 GaPlaceBack(RailController rc, CrewRescueController ctrl, Npc npc, float min, float max, Action<string> fail)
    {
        var w1At = rc.Party.Track.Point(rc.StopS[GaStop] - TrainRules.Spacing);
        for (var sb = rc.StopS[GaStop] - 10f; sb > rc.StopS[GaStop] - 120f; sb -= 2f)
        {
            var p = rc.StandBesideLane(rc.Party.Track.Point(sb));
            var fd = new Vector2(p.X - w1At.X, p.Z - w1At.Z).Length();
            if (fd < min || fd > max) continue;
            ctrl.DebugPutOnFoot(npc.Id, p);
            GaPin(npc, p);
            GD.Print($"[stoptrap] gotaway: (pass 4) {npc.NpcName} waits at {DungeonBuilder.WorldToCell(p)}, {fd:0.0} m from the stop's W1");
            return p;
        }
        fail($"no place beside the line {min:0}-{max:0} m from the stop's W1");
        return npc.GlobalPosition;
    }

    // ---- --test-stop-trap=limits (plan 16 Task 7, Ruling 26; the T5 review's M15) ------------------------------------------------------
    // (1) For stops 1-4, each read just after rc.Restore(k) with four wagons (ride 4's bridge counts standing or gone: Ruling 10; no runaway
    //     lingers after a restore): the live roll-back limit from CollectRollBackHazards on the built floor, printed as
    //     [hijack] stop {k}: limit S={l} ({m} m of roll; {the 40 m cap|cut by {from}..{to}}). It fails unless each equals its
    //     HijackRules.ShippedLimits row to 0.1 with RollRoom matching: a change to the hazards or the layout turns it red.
    // (2) Rolled to it: a driverless roll back (DebugRollBack) from stop 3, from stop 4 with the bridge standing, and from stop 4 with every bridge
    //     cell gone (DebugBridgeGone: the limit read again is the same) each reaches its limit +- 0.1 with the leading face (S - 11.5) at least 5 m
    //     past every hazard behind the stop, then brakes and comes back to its stop (Ruling 11).
    private int _lmRoll;
    private float _lmMinS, _lmLimit;
    private bool _lmRolled;
    private readonly List<string> _lmSummary = new();

    private void RunStopTrapLimits(RailController rc)
    {
        var rolls = new[] { (Stop: 3, Gone: false), (Stop: 4, Gone: false), (Stop: 4, Gone: true) };
        if (_stStep == 0)
        {
            if (_stClock < 1.5f) return;
            if (rc.StopS.Length != Floor12Stops) { StopTrapFail($"floor 12's line has {rc.StopS.Length} stops, not {Floor12Stops}"); return; }
            var faults = new List<string>();
            for (var k = 1; k <= rc.StopS.Length - 2; k++)
            {
                rc.Restore(k);
                if (rc.Train.Count != 4) { StopTrapFail($"stop {k}: the restored train has {rc.Train.Count} wagons, not 4"); return; }
                var limit = rc.RollBackLimitAt(k, out _, out var cutBy);
                var stopS = rc.StopS[k];
                var room = HijackRules.RollRoom(stopS, limit);
                var why = cutBy is { } c ? $"cut by {c.From:0.0}..{c.To:0.0}" : "the 40 m cap";
                GD.Print($"[hijack] stop {k}: limit S={limit:0.0} ({stopS - limit:0.0} m of roll; {why})");
                GD.Print($"[stoptrap] limits: stop {k} at S={stopS:0.000}, limit {limit:0.000}, room {room}{(cutBy is { } d ? $", span {d.From:0.000}..{d.To:0.000}" : "")}");
                if (!HijackRules.ShippedLimits.TryGetValue(k, out var row)) { faults.Add($"stop {k} has no ShippedLimits row"); continue; }
                if (Mathf.Abs(row.LimitS - limit) > 0.1f) faults.Add($"stop {k}'s limit is {limit:0.00}, not its row's {row.LimitS:0.00}");
                if (row.RollRoom != room) faults.Add($"stop {k}'s roll room is {room}, not its row's {row.RollRoom}");
            }
            if (faults.Count > 0) { StopTrapFail(string.Join("; ", faults)); return; }
            _lmSummary.Add("four limits as pinned");
            _stStep = 1; _stClock = 0f;
            return;
        }
        if (_lmRoll >= rolls.Length) { StopTrapOk(string.Join("; ", _lmSummary)); return; }
        var (stop, gone) = rolls[_lmRoll];
        var enc = RailEncounterController.Instance is { } e && IsInstanceValid(e) ? e : null;
        if (_stStep == 1) // the roll starts
        {
            if (_stClock < 1f) return;
            rc.Restore(stop);
            var standing = rc.RollBackLimitAt(stop, out _);
            if (gone)
            {
                var cells = enc?.DebugBridgeGone() ?? 0;
                if (cells == 0 || enc!.BridgeGone != enc.BridgeCells) { StopTrapFail($"ride 4's bridge did not go ({cells} cell(s) gone)"); return; }
                var after = rc.RollBackLimitAt(stop, out _);
                GD.Print($"[stoptrap] limits: stop {stop} with the bridge gone ({enc.BridgeGone}/{enc.BridgeCells} cells): limit {after:0.000} (standing {standing:0.000})");
                if (Mathf.Abs(after - standing) > 1e-3f) { StopTrapFail($"stop {stop}'s limit moved from {standing:0.00} to {after:0.00} with the bridge gone"); return; }
            }
            _lmLimit = standing;
            _lmMinS = float.MaxValue;
            _lmRolled = false;
            if (!rc.DebugRollBack()) { StopTrapFail($"no driverless roll back from stop {stop}"); return; }
            _stStep = 2; _stClock = 0f;
            return;
        }
        // _stStep 2: the roll under way, then back
        if (rc.HijackLive)
        {
            if (rc.Hijack == HijackPhase.RollingBack) { _lmRolled = true; _lmMinS = Mathf.Min(_lmMinS, rc.State.S); }
            if (_stClock > 60f) StopTrapFail($"the roll back from stop {stop} never came back ({rc.Hijack}, S={rc.State.S:0.00})");
            return;
        }
        if (!_lmRolled) { StopTrapFail($"the roll back from stop {stop} never rolled"); return; }
        var hazards = new List<(float From, float To)>();
        enc?.CollectRollBackHazards(hazards);
        var face = _lmMinS - (rc.Train.Count * TrainRules.Spacing + CartRules.HalfLength);
        var nearest = hazards.Where(h => h.From < rc.StopS[stop]).Select(h => face - h.To).DefaultIfEmpty(float.PositiveInfinity).Min();
        GD.Print($"[stoptrap] limits: stop {stop}{(gone ? " (the bridge gone)" : "")} rolled to S={_lmMinS:0.000} (limit {_lmLimit:0.000}), the leading face at {face:0.000}, {nearest:0.0} m past the nearest hazard behind; back {rc.State.Mode} at S={rc.State.S:0.000}");
        if (Mathf.Abs(_lmMinS - _lmLimit) > 0.1f) { StopTrapFail($"the roll from stop {stop} stopped at S={_lmMinS:0.00}, not its limit {_lmLimit:0.00}"); return; }
        if (nearest < HijackRules.HoleClear - 0.05f) { StopTrapFail($"the roll from stop {stop} left its leading face {nearest:0.00} m past a hazard, under 5"); return; }
        if (rc.State.Mode != CartMode.Held || Mathf.Abs(rc.State.S - rc.StopS[stop]) > 0.05f) { StopTrapFail($"after the roll from stop {stop} the train is {rc.State.Mode} at S={rc.State.S:0.00}"); return; }
        _lmSummary.Add($"stop {stop}{(gone ? " (bridge gone)" : "")} rolled to {_lmMinS:0.0}");
        _lmRoll++;
        _stStep = 1; _stClock = 0f;
    }
}
