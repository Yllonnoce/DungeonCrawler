using DungeonCrawler.Adventure;
using DungeonCrawler.Adventure.Objectives;
using DungeonCrawler.Dungeon;
using DungeonCrawler.Dungeon.Generation;
using DungeonCrawler.Dungeon.Rails;
using DungeonCrawler.Party;
using Godot;

namespace DungeonCrawler.Core;

// ---- --test-chase ----------------------------------------------------------------------------------------------
// The four rides of the built-in campaign's floor 12 at lethality 3, scripted end to end (the condemned branch: the cages
// open with the orc's keys). The stop guards die by the test's hand on arrival and each cage is opened as the cart is
// held there, so it is a ride test. Section 1 rides the line: ride 1's lone cinder run over and the beam passed, the slag
// traffic (a hero on the enemy lane at stop 1 warned, then mangled; one in a pocket untouched), ride 2's three slag carts
// (a brake shoe broken by a debug hit derails one, a hound leaps), ride 3's switch thrown by a debug hook (the slag cart
// ahead rammed, the cart back on its own lane), ride 4's golem diverted at the spur by a debug-thrown lever, the jump made
// with two pumping and the buffer smashed into the last stop; the crew home and the stairs open. Section 2 builds the floor
// again: ride 3 with the switch left straight (the heap halts the cart, a hero breaks it with real swings, the ride
// resumes), then ride 4 crawled under a hammer and the pour so each hits, the golem killed, the valve broken, a save and a
// load mid-ride, and ride 4 again from stop 3 (no golem, the valve still broken). Throughout: no boss on the floor, the
// stairs stay sealed until the crew are home, and every hazard's warning comes at least 1.5 s before its hit (physics
// frames logged). Prints [chase] lines and [chase] RESULT.
public partial class DebugTools
{
    private bool _testChase;
    private int _chStep;
    private float _chClock;
    private bool _chSection2;
    private RailController? _chRails;
    private RailController? _chHeard;
    private RailEncounterController? _chEnc;
    private bool _chListening, _chSwept0;
    private int _chSwept = -1;
    private string? _chFail;
    private const string ChaseSlot = "chasetest";
    private readonly Dictionary<Enemies.Enemy, int> _chSpawnRide = new();
    // ride 1
    private Enemies.Enemy? _chLone;
    private bool _chLoneByCart, _chLoneOther;
    private string _chLoneSource = "";
    private readonly Dictionary<int, float> _chHp = new();
    private bool _chBeamDone;
    // ride 2
    private readonly HashSet<string> _chSlagSeen = new(), _chSlagBeside = new();
    private SlagCart? _chShoeCart;
    private readonly List<Enemies.Enemy> _chShoeRiders = new();
    private bool _chDerailed;
    private int _chLeaps;
    // ride 3
    private bool _chThrown, _chAcross, _chBack;
    private int _chRams0, _chRamsSeen;
    // ride 4
    private bool _chDiverted, _chOnSpur, _chJumped, _chBuffer;
    private int _chPumpers = -1, _chSlams;
    private float _chJumpV;
    // traffic
    private Adventurer? _chLaneHero, _chPocketHero;
    private Vector3 _chLaneAt, _chPocketAt;
    private float _chLaneS, _chPocketS, _chPocketHp;
    private bool _chTrafficOn, _chLaneHit;
    // section 2
    private bool _chHalted, _chResumed;
    private float _chPlatingBefore;
    private int _chSwings, _chHammer0, _chPour0;
    private bool _chGolemKilled;
    // warnings and hits, in physics frames
    private readonly Dictionary<string, ulong> _chWarn = new();
    private readonly List<(string Hazard, ulong Warn, ulong Hit, string Note)> _chHits = new();
    private readonly Dictionary<int, ulong> _chGlowAt = new();
    private readonly HashSet<int> _chGlowOpen = new();
    private bool _chPourOpen;

    private static readonly string[] ChaseHazards = { "bell", "heap", "hammer", "pour", "golem", "jump" };

    private static ulong ChaseMinGap => (ulong)Mathf.CeilToInt(1.5f * Engine.PhysicsTicksPerSecond);
    private static double ChaseSeconds(ulong frames) => frames / (double)Engine.PhysicsTicksPerSecond;

    private void RunChaseTest()
    {
        var main = Main.Instance;
        if (main?.Builder.Layout is not { } layout || main.Builder.FloorRoot is not { } root || !main.InGame || Game.Instance.CurrentFloor != 12) return;
        _chClock += (float)GetProcessDeltaTime();
        void Fail(string why) { GD.Print($"[chase] RESULT FAIL: {why}"); _done = true; GetTree().Quit(1); }
        var party = main.Party;
        var rc = RailController.Instance;
        var enc = root.GetNodeOrNull<RailEncounterController>("RailEncounterController");
        var ctrl = ObjectiveController.Instance as CrewRescueController;
        if (rc == null || enc == null || ctrl == null) { if (_chClock > 3f) Fail($"floor 12 lacks {(rc == null ? "a RailController " : "")}{(enc == null ? "a RailEncounterController " : "")}{(ctrl == null ? "a CrewRescueController" : "")}"); return; }
        if (_chSection2 && rc == _chRails) return; // the floor is being built again: wait for the new one
        ChaseListen(rc, enc);
        if (_chFail != null) { Fail(_chFail); return; }

        // throughout: nothing on the floor is a boss, and the stairs stay sealed until the crew are home
        var stairs = root.GetNodeOrNull<Stairs>("DownStairs");
        if (stairs == null) { Fail("floor 12 has no down stairs"); return; }
        if (!stairs.Locked && !ctrl.Completed) { Fail($"the down stairs opened with the crew not home ({CrewStates(ctrl)})"); return; }
        if (!_chSwept0) { _chSwept0 = true; if (!ChaseSweep(main, root, "the floor as built (before any cull)", Fail)) return; }
        foreach (var e in root.GetChildren().OfType<Enemies.Enemy>())
            if (e.IsBoss) { Fail($"{e.Name} ({e.Profile.Id}, {e.DisplayName}) on floor 12 is a boss"); return; }
        if (enc.ArmedRide >= 1 && enc.ArmedRide != _chSwept)
        {
            _chSwept = enc.ArmedRide;
            if (!ChaseSweep(main, root, $"ride {enc.ArmedRide} starts{(_chSection2 ? " (section 2)" : "")}", Fail)) return;
        }

        // the test owns the floor's fights: everything is culled as it appears except the stop guards (section 1: they die
        // by the test's hand at their stop) and the armed ride's own enemies; section 2 culls all but ride 4's golem
        foreach (var e in root.GetChildren().OfType<Enemies.Enemy>())
        {
            if (!e.IsAlive) continue;
            var keep = _chSection2
                ? e == enc.GolemBody && !_chGolemKilled
                : HoldsAStop(main, layout, e) || enc.ArmedRide >= 1 && (e == enc.GolemBody || e.IsReinforcement && _chSpawnRide.TryGetValue(e, out var r) && r == enc.ArmedRide);
            if (!keep) e.Kill();
        }
        ChaseTrackRides(rc, enc);
        if (_chTrafficOn) ChaseHoldTraffic(enc);

        if (_chSection2) { RunChaseSection2(main, layout, root, rc, enc, ctrl, Fail); return; }
        var lead = party.Local;
        switch (_chStep)
        {
            case 0 when _chClock >= 1.5f:
            {
                var keyring = root.GetNodeOrNull<OrcKeyring>("OrcKeyring");
                GD.Print($"[chase] 0 floor 12: stops={rc.StopS.Length} StopS=[{string.Join(", ", rc.StopS.Select(s => s.ToString("0.0")))}] lethality={TierRules.Lethality} set pieces={enc.SetPieceCount} wagons={ctrl.Wagons.Count} caged={ctrl.Crew.Count(c => ctrl.StateOf(c) == CrewState.Caged)} stairs locked={stairs.Locked} keyring={(keyring == null ? "none" : $"\"{keyring.Prompt}\"")}");
                if (rc.StopS.Length != 5 || ctrl.Wagons.Count != 3 || keyring == null || !stairs.Locked) { Fail("floor 12 is not the shipped line (5 stops, 3 wagons, the orc's keys, sealed stairs)"); return; }
                if (Mathf.Abs(TierRules.Lethality - 3f) > 0.01f) { Fail($"lethality {TierRules.Lethality}, not 3"); return; }
                _chStep++;
                _chClock = 0f;
                if (!RideBoard(main, layout, rc, out var why)) { Fail(why); return; }
                break;
            }
            case 1:
            {
                if (rc.State.Mode != CartMode.Rolling) { if (_chClock > 8f) Fail($"the cart did not leave the landing (mode {rc.State.Mode})"); break; }
                GD.Print($"[chase] 1 departed the landing: ride {enc.ArmedRide} armed, {party.Active.Count(a => a.Seated)} heroes seated");
                _chStep++;
                _chClock = 0f;
                break;
            }
            case 2:
            {
                // 1. ride 1: the lone cinder on the rails, the beam
                if (_chLone == null && enc.ArmedRide == 1)
                {
                    var at = rc.Party.Track.Point(rc.StopS[0] + 0.45f * (rc.StopS[1] - rc.StopS[0]));
                    _chLone = root.GetChildren().OfType<Enemies.Enemy>().FirstOrDefault(e => e.IsAlive && e.IsReinforcement && e.Profile.Id == "enemy.horde_cinder" && ChaseFlat(e.GlobalPosition, at) < 2f);
                    if (_chLone != null) GD.Print($"[chase] 1 the lone cinder {_chLone.Name} stands on the party lane at cell {DungeonBuilder.WorldToCell(_chLone.GlobalPosition)}");
                }
                if (!_chBeamDone)
                {
                    if (enc.BeamPasses == 0)
                    {
                        foreach (var a in party.Active) if (a.Seated) _chHp[a.Index] = a.State.Hp; // the frame before the beam
                    }
                    else
                    {
                        _chBeamDone = true;
                        var lost = party.Active.Where(a => _chHp.ContainsKey(a.Index) && (a.State.Hp < _chHp[a.Index] - 0.01f || !a.Seated)).Select(a => $"{a.State.Name} {_chHp[a.Index]:0}->{a.State.Hp:0} seated={a.Seated}").ToList();
                        GD.Print($"[chase] 1 the beam passed at S={rc.State.S:0.0}: {_chHp.Count} seated riders, hp [{string.Join(" ", party.Active.Where(a => _chHp.ContainsKey(a.Index)).Select(a => $"{a.State.Name}:{_chHp[a.Index]:0}->{a.State.Hp:0}"))}]{(lost.Count == 0 ? ", no damage" : $", HURT [{string.Join("; ", lost)}]")}");
                        if (_chHp.Count == 0) { Fail("nobody was seated under the beam"); return; }
                        if (lost.Count > 0) { Fail($"the beam hurt or unseated seated riders: {string.Join("; ", lost)}"); return; }
                    }
                }
                if (_chLoneOther) { Fail("the lone cinder died, but not under the cart"); return; }
                if (!ChaseHeldAt(rc, 1, Fail)) break;
                GD.Print($"[chase] 1 ride 1 done, held at stop 1 after {_chClock:0.0} s: lone cinder {(_chLone == null ? "never seen" : $"killed by the cart={_chLoneByCart} (\"{_chLoneSource}\")")}, beam passes={enc.BeamPasses}, plating={rc.State.Plating:0}");
                if (_chLone == null || !_chLoneByCart) { Fail("the lone cinder was not run over by the cart"); return; }
                if (!_chBeamDone || enc.BeamPasses != 1) { Fail($"the beam passed {enc.BeamPasses} times"); return; }
                if (!ChaseKillGuards(main, layout, rc, 1, Fail)) return;
                _chStep++;
                _chClock = 0f;
                break;
            }
            case 3:
            {
                if (!rc.GateUp(1)) { if (_chClock > 2f) Fail("stop 1's gate did not lift with its guards dead"); break; }
                // the party gets out (the cart stays: nobody aboard), takes the orc's keys and opens cage 1
                rc.Cart.Interact(party, lead!);
                var ring = root.GetNode<OrcKeyring>("OrcKeyring");
                ring.Interact(party, lead!);
                GD.Print($"[chase] 6 stop 1's gate up; {lead!.State.Name} gets out (seated={lead.Seated}); the orc's keys taken={ctrl.OrcKeys}");
                if (!ctrl.OrcKeys) { Fail("taking the keyring did not give the party the orc's keys"); return; }
                if (!ChaseOpenCage(main, ctrl, 1, Fail)) return;
                _chStep++;
                _chClock = 0f;
                break;
            }
            case 4 when _chClock >= 1f:
            {
                // 6. traffic: the cart held at stop 1 with nobody aboard and no ride armed; a hero on the enemy lane, one in a pocket
                var aboard = party.Adventurers.Count(a => IsInstanceValid(a) && a.Seated);
                if (aboard > 0 || rc.Cart.Riders.Count > 0 || rc.State.Mode != CartMode.Held || enc.ArmedRide >= 1) { Fail($"not a quiet stop for the traffic: {aboard} seated, {rc.Cart.Riders.Count} riders, mode {rc.State.Mode}, armed ride {enc.ArmedRide}"); return; }
                if (!ChasePlaceTraffic(main, layout, out var why)) { Fail(why); return; }
                RailEncounterController.TraceTraffic = true;
                enc.TrafficClock = SetPieceRules.SlagTrafficEvery - 0.25f; // the test's shortcut: the next traffic cart in a quarter second, not 25 s
                enc.RunawayClock = 0f;
                _chTrafficOn = true;
                _chStep++;
                _chClock = 0f;
                break;
            }
            case 5:
            {
                var traffic = enc.TrafficCart;
                var past = Mathf.Max(_chLaneS, _chPocketS) + 3f;
                if (traffic == null || traffic.Run.FrontS < past) { if (_chClock > 40f) Fail($"no traffic cart came past the heroes within 40 s (traffic {(traffic == null ? "none" : $"front S={traffic.Run.FrontS:0.0}")}, want {past:0.0})"); break; }
                _chTrafficOn = false;
                RailEncounterController.TraceTraffic = false;
                var lane = _chLaneHero!;
                var pocket = _chPocketHero!;
                var warned = _chHits.Any(h => h.Hazard == "bell");
                GD.Print($"[chase] 6 the traffic cart passed (front S={traffic.Run.FrontS:0.0}): {lane.State.Name} on the enemy lane at S={_chLaneS:0.0} hit={_chLaneHit} warned before it={warned} downed={lane.Downed} mangled={lane.State.Mangled}; {pocket.State.Name} in the pocket at S={_chPocketS:0.0} hp {_chPocketHp:0}->{pocket.State.Hp:0} downed={pocket.Downed} mangled={pocket.State.Mangled}");
                if (!_chLaneHit || !lane.Downed || !lane.State.Mangled) { Fail($"{lane.State.Name} on the enemy lane was not mangled by the traffic cart"); return; }
                if (!warned) { Fail($"{lane.State.Name} was hit with no \"Cart coming!\" before it"); return; }
                if (pocket.Downed || pocket.State.Mangled || pocket.State.Hp < _chPocketHp - 0.01f) { Fail($"{pocket.State.Name} in the pocket was hurt by the traffic cart"); return; }
                ChaseMend(main, root);
                _chStep++;
                _chClock = 0f;
                break;
            }
            case 6 when _chClock >= 0.5f:
            {
                enc.RunawayClock = 0f;
                _chStep++;
                _chClock = 0f;
                if (!RideBoard(main, layout, rc, out var why)) { Fail(why); return; }
                break;
            }
            case 7: if (ChaseDepart(rc, enc, ctrl, 1, Fail)) { _chStep++; _chClock = 0f; } break;
            case 8:
            {
                // 2. ride 2: the three slag carts, a brake shoe, a hound's leap
                if (_chShoeCart == null && enc.SlagCarts.FirstOrDefault(c => c.EncounterId == "r2_slag1" && !c.Derailed) is { } slag1 && _chSlagBeside.Contains("r2_slag1"))
                {
                    _chShoeCart = slag1;
                    _chShoeRiders.AddRange(slag1.Aboard);
                    _chDerailed = false;
                    var r = slag1.Shoe.ReceiveAttack(lead!.State.Stats, new Combat.AttackProfile { MinDamage = 1, MaxDamage = 2 }, new Combat.HitContext(), party.Rng, lead.State.Name);
                    GD.Print($"[chase] 2 debug hit on {slag1.Name}'s brake shoe at party S={rc.State.S:0.0}: hit={r.Hit} shoe alive={slag1.Shoe.IsAlive}; riders [{string.Join(", ", _chShoeRiders.Select(e => e.Profile.Id))}]");
                    _chClock = 0f;
                }
                if (!ChaseHeldAt(rc, 2, Fail)) break;
                var ridersDead = _chShoeRiders.All(e => !IsInstanceValid(e) || !e.IsAlive);
                GD.Print($"[chase] 2 ride 2 done, held at stop 2: slag carts seen [{string.Join(",", _chSlagSeen.OrderBy(x => x))}] alongside [{string.Join(",", _chSlagBeside.OrderBy(x => x))}]; {(_chShoeCart == null ? "no shoe hit" : $"{_chShoeCart.EncounterId} derailed={_chDerailed} riders dead={ridersDead}")}; hound leaps={_chLeaps}; plating={rc.State.Plating:0}");
                var ride2 = new[] { "r2_slag1", "r2_slag2", "r2_slag3" };
                if (!ride2.All(_chSlagSeen.Contains) || !ride2.All(_chSlagBeside.Contains)) { Fail($"ride 2's three slag carts: seen [{string.Join(",", _chSlagSeen)}], alongside [{string.Join(",", _chSlagBeside)}]"); return; }
                if (_chShoeCart == null || !_chDerailed || !ridersDead) { Fail("the slag cart whose brake shoe was broken did not derail with its riders"); return; }
                if (_chLeaps < 1) { Fail("no hound leapt from a slag cart"); return; }
                if (!ChaseKillGuards(main, layout, rc, 2, Fail)) return;
                _chStep++;
                _chClock = 0f;
                break;
            }
            case 9:
            {
                if (!rc.GateUp(2)) { if (_chClock > 2f) Fail("stop 2's gate did not lift with its guards dead"); break; }
                ChaseMend(main, root);
                if (!ChaseOpenCage(main, ctrl, 2, Fail)) return;
                _chStep++;
                _chClock = 0f;
                break;
            }
            case 10:
                if (!ChaseDepart(rc, enc, ctrl, 2, Fail)) break;
                _chRams0 = enc.Rams;
                _chStep++;
                _chClock = 0f;
                break;
            case 11:
            {
                // 3. ride 3: the switch thrown (the debug hook), the ram, the way back
                if (!_chThrown && enc.ArmedRide == 3 && rc.State.Mode == CartMode.Rolling && enc.DebugThrowSwitches(3) > 0)
                {
                    _chThrown = true;
                    GD.Print($"[chase] 3 ride 3 armed at S={rc.State.S:0.0}: the switch_out lever thrown by the debug hook");
                }
                if (!ChaseHeldAt(rc, 3, Fail)) break;
                GD.Print($"[chase] 3 ride 3 done, held at stop 3: thrown={_chThrown} across={_chAcross} rams={enc.Rams - _chRams0} back on the party lane={_chBack}; plating={rc.State.Plating:0}");
                if (!_chThrown || !_chAcross) { Fail("the cart did not take the switch onto the enemy lane"); return; }
                if (enc.Rams - _chRams0 < 1) { Fail("the cart did not ram the slag cart ahead"); return; }
                if (!_chBack) { Fail("the cart did not cross back to its own lane"); return; }
                if (!ChaseKillGuards(main, layout, rc, 3, Fail)) return;
                _chStep++;
                _chClock = 0f;
                break;
            }
            case 12:
            {
                if (!rc.GateUp(3)) { if (_chClock > 2f) Fail("stop 3's gate did not lift with its guards dead"); break; }
                ChaseMend(main, root);
                if (!ChaseOpenCage(main, ctrl, 3, Fail)) return;
                _chStep++;
                _chClock = 0f;
                break;
            }
            case 13:
            {
                if (!ChaseDepart(rc, enc, ctrl, 3, Fail)) break;
                // 4. ride 4: the golem out of the kiln wall; the spur lever thrown by the debug hook
                if (!ChaseGolemOut(enc, Fail)) return;
                if (enc.SetPieceNode("r4_spur") is not SpurSwitch spur) { Fail("no spur on ride 4"); return; }
                spur.Lever.DebugThrow();
                GD.Print($"[chase] 4 the spur lever thrown by the debug hook at S={rc.State.S:0.0}: thrown={spur.Lever.Thrown}");
                if (!spur.Lever.Thrown) { Fail("the spur lever would not throw"); return; }
                _chStep++;
                _chClock = 0f;
                break;
            }
            case 14:
            {
                if (!ChaseHeldAt(rc, 4, Fail)) break;
                var buffer = enc.SetPieceNode("r4_buffer") as BufferStop;
                var golem = enc.GolemBody;
                GD.Print($"[chase] 4 ride 4 done, held at stop 4 (the end of the line): golem took the spur={_chOnSpur} off the spur={_chDiverted} ({(golem == null ? "gone" : $"alive={golem.IsAlive} hp {golem.Hp:0}/{golem.MaxHp:0}")}), slams={_chSlams}; jump made={_chJumped} at {_chJumpV:0.00} m/s with {_chPumpers} pumping; buffer smashed={buffer?.Smashed} (cue={_chBuffer}); plating={rc.State.Plating:0}");
                if (!_chDiverted) { Fail("the golem was not diverted at the spur"); return; }
                if (!_chJumped || _chJumpV < SetPieceRules.JumpSpeed || _chPumpers != 2) { Fail($"the jump: made={_chJumped} at {_chJumpV:0.00} m/s with {_chPumpers} pumping (want made, >= {SetPieceRules.JumpSpeed}, 2)"); return; }
                if (buffer is not { Smashed: true } || !_chBuffer) { Fail("the cart did not come through the buffer"); return; }
                _chStep++;
                _chClock = 0f;
                break;
            }
            case 15:
            {
                if (!ctrl.Completed) { if (_chClock > 3f) Fail($"held at the end of the line but the floor is not complete: {CrewStates(ctrl)}"); break; }
                var home = ctrl.Crew.Count(c => ctrl.StateOf(c) == CrewState.Home);
                GD.Print($"[chase] 5 the end of the line: completed={ctrl.Completed} home={home}/{ctrl.Crew.Count} stairs locked={stairs.Locked} (sealed until now) {CrewStates(ctrl)}");
                if (stairs.Locked) { Fail("the crew are home but the stairs are still sealed"); return; }
                _chSection2 = true;
                _chRails = rc;
                _chStep = 0;
                _chClock = 0f;
                _chSwept = -1;
                Game.Instance.Floors.Remove(12); // a fresh floor, not the one just won
                GD.Print("[chase] 7 floor 12 is built again for the heap and the save");
                Game.Instance.JumpToFloor(12);
                break;
            }
        }
    }

    // ---- section 2: the heap, the hazards hit, a save and a load mid-ride 4 ------------------------------------------

    private void RunChaseSection2(Main main, FloorLayout layout, Node3D root, RailController rc, RailEncounterController enc, CrewRescueController ctrl, Action<string> fail)
    {
        var party = main.Party;
        var lead = party.Local!;
        switch (_chStep)
        {
            case 0 when _chClock >= 1.5f:
            {
                ChaseMend(main, root);
                rc.RestoreGates(new[] { 1, 2, 3 }); // the test's shortcut over rides 1 and 2 (section 1 rode them): the cart stands at stop 2
                rc.Restore(2);
                GD.Print($"[chase] 7 rebuilt: the cart restored at stop {rc.StopIndex} (mode {rc.State.Mode}, plating {rc.State.Plating:0}); golem defeated={enc.GolemDefeated}; stairs locked={root.GetNode<Stairs>("DownStairs").Locked}");
                _chStep++;
                _chClock = 0f;
                break;
            }
            case 1 when _chClock >= 0.5f:
            {
                _chStep++;
                _chClock = 0f;
                if (!RideBoard(main, layout, rc, out var why)) { fail(why); return; }
                break;
            }
            case 2:
            {
                if (rc.State.Mode != CartMode.Rolling) { if (_chClock > 8f) fail($"the cart did not leave stop 2 (mode {rc.State.Mode})"); break; }
                _chPlatingBefore = rc.State.Plating;
                GD.Print($"[chase] 7 departed stop 2: ride {enc.ArmedRide} armed, the switch_out lever left straight, plating {_chPlatingBefore:0}");
                _chStep++;
                _chClock = 0f;
                break;
            }
            case 3:
            {
                // 7. the heap: the cart crashes into it and is halted on the line
                if (rc.State.Mode == CartMode.Rolling) { if (_chClock > 45f) fail($"the cart did not reach the heap within 45 s (S={rc.State.S:0.0})"); break; }
                var heap = enc.SetPieceNode("r3_heap") as OreHeap;
                var lever = enc.SetPieceNode("r3_switch_out") as TrackSwitch;
                GD.Print($"[chase] 7 the cart stopped at S={rc.State.S:0.0} front {rc.Party.FrontS:0.0}: halted={rc.Halted} (event {_chHalted}) stop={rc.StopIndex} plating {_chPlatingBefore:0}->{rc.State.Plating:0} heap alive={heap?.IsAlive} face S={heap?.FaceS:0.0} blocked at {rc.BlockedAt:0.0} lever thrown={lever?.Thrown} hud=\"{rc.HudText}\"");
                if (!rc.Halted || !_chHalted || rc.State.Mode != CartMode.Held) { fail($"the cart was not halted at the heap (mode {rc.State.Mode}, halted {rc.Halted}, stop {rc.StopIndex})"); return; }
                if (Mathf.Abs(_chPlatingBefore - rc.State.Plating - RailEncounterController.HeapDent) > 0.5f) { fail($"the heap took {_chPlatingBefore - rc.State.Plating:0} plating, not {RailEncounterController.HeapDent:0}"); return; }
                if (heap is not { IsAlive: true } || lever is not { Thrown: false }) { fail("the heap is not standing or the lever was thrown"); return; }
                _chStep++;
                _chClock = 0f;
                break;
            }
            case 4 when _chClock >= 1f:
            {
                rc.Cart.Interact(party, lead);
                GD.Print($"[chase] 7 {lead.State.Name} gets out to clear the heap: seated={lead.Seated}, holding {lead.State.Weapon?.DisplayName ?? "nothing"}");
                if (lead.Seated) { fail($"{lead.State.Name} could not get out of the halted cart"); return; }
                _chSwings = 0;
                _chStep++;
                _chClock = 1f;
                break;
            }
            case 5 when _chClock >= 1f:
            {
                var heap = (OreHeap)enc.SetPieceNode("r3_heap")!;
                if (!heap.IsAlive) { _chStep++; _chClock = 0f; break; }
                if (_chSwings >= 8) { fail($"8 real swings did not break the heap (left {heap.Left})"); return; }
                ChaseSwingAt(main, lead, heap);
                _chClock = 0f;
                break;
            }
            case 6 when _chClock >= 0.5f:
            {
                var heap = (OreHeap)enc.SetPieceNode("r3_heap")!;
                GD.Print($"[chase] 7 the heap broken by {_chSwings} real swings (PartyController.Attack): alive={heap.IsAlive} line blocked at {rc.BlockedAt}");
                if (!float.IsPositiveInfinity(rc.BlockedAt)) { fail("the line is still blocked with the heap gone"); return; }
                _chStep++;
                _chClock = 0f;
                if (!RideBoard(main, layout, rc, out var why)) { fail(why); return; }
                break;
            }
            case 7:
            {
                if (rc.State.Mode != CartMode.Rolling) { if (_chClock > 10f) fail($"re-boarded at the heap, but the cart did not go on (mode {rc.State.Mode}, halted {rc.Halted}, hud \"{rc.HudText}\")"); break; }
                GD.Print($"[chase] 7 resumed after {_chClock:0.0} s: event Resumed={_chResumed} armed ride {enc.ArmedRide} halted={rc.Halted} S={rc.State.S:0.0}");
                if (!_chResumed || enc.ArmedRide != 3) { fail("the cart left the heap without a Resumed event, or ride 3 was no longer armed"); return; }
                _chStep++;
                _chClock = 0f;
                break;
            }
            case 8:
            {
                if (!ChaseHeldAt(rc, 3, fail)) break;
                GD.Print($"[chase] 7 the heap path reached stop 3: plating {rc.State.Plating:0}; the test mends it to {CartRules.MaxPlating:0} for ride 4");
                rc.DebugSetPlating(CartRules.MaxPlating);
                ChaseMend(main, root);
                _chHammer0 = enc.HammerHits;
                _chPour0 = enc.PourHits;
                _chGolemKilled = false;
                _chSlams = 0;
                _chStep++;
                _chClock = 0f;
                break;
            }
            case 9:
            {
                if (rc.State.Mode != CartMode.Rolling) { if (_chClock > 10f) fail($"the cart did not leave stop 3 (mode {rc.State.Mode}, gate up {rc.GateUp(3)})"); break; }
                if (!ChaseGolemOut(enc, fail)) return;
                _chStep++;
                _chClock = 0f;
                break;
            }
            case 10:
            {
                // 8. ride 4, first try: the cart crawls under a hammer and the pour until each hits; the golem dies after a slam
                if (rc.State.Mode != CartMode.Rolling) { fail($"the cart stopped ({rc.State.Mode} at stop {rc.StopIndex}) before the save"); return; }
                var s = rc.State.S;
                if (!_chGolemKilled && enc.GolemBody is { IsAlive: true } golem && enc.GolemSlams >= 1)
                {
                    _chGolemKilled = true;
                    golem.Kill();
                    GD.Print($"[chase] 8 the golem killed by the test after {enc.GolemSlams} slam(s) at S={s:0.0}");
                }
                var mid = (enc.SetPieceNode("r4_hammers") as Node3D)?.GetNodeOrNull<DropHammer>("Hammer1");
                var pour = enc.SetPieceNode("r4_pour") as MoltenPour;
                if (mid == null || pour == null) { fail("ride 4 has no hammers or no pour"); return; }
                var crawl = (enc.HammerHits == _chHammer0 && s >= mid.S - 0.3f) || (enc.PourHits == _chPour0 && s >= pour.S - 1.5f);
                if (crawl) rc.Party.State = rc.State with { V = Mathf.Min(rc.State.V, 0.15f) }; // the test holds the cart under it until it hits
                if (enc.HammerHits > _chHammer0 && enc.PourHits > _chPour0 && enc.GolemDefeated)
                {
                    var r = pour.Valve.ReceiveAttack(lead.State.Stats, new Combat.AttackProfile { MinDamage = 1, MaxDamage = 2 }, new Combat.HitContext(), party.Rng, lead.State.Name);
                    GD.Print($"[chase] 8 hammer hits={enc.HammerHits - _chHammer0} pour hits={enc.PourHits - _chPour0} slams={_chSlams} golem defeated={enc.GolemDefeated} (stairs locked={root.GetNode<Stairs>("DownStairs").Locked} after its death); debug hit on the quench valve at S={s:0.0}: hit={r.Hit}");
                    _chStep++;
                    _chClock = 0f;
                }
                else if (_chClock > 50f) fail($"ride 4's first try: hammer hits {enc.HammerHits - _chHammer0}, pour hits {enc.PourHits - _chPour0}, slams {_chSlams}, golem defeated {enc.GolemDefeated} after 50 s (S={s:0.0})");
                break;
            }
            case 11 when _chClock >= 0.1f:
            {
                var pour = (MoltenPour)enc.SetPieceNode("r4_pour")!;
                if (!pour.Quenched) { fail("the valve's hit did not quench the pour"); return; }
                if (rc.State.Mode != CartMode.Rolling) { fail("the cart is not mid-ride for the save"); return; }
                GD.Print($"[chase] 8 saved mid-ride 4 at S={rc.State.S:0.0} (stop 3 {rc.StopS[3]:0.0}, stop 4 {rc.StopS[4]:0.0}): golem defeated={enc.GolemDefeated} valve broken={pour.Quenched}");
                main.SaveGame(ChaseSlot);
                if (!main.LoadGame(ChaseSlot)) { fail("the save would not load"); return; }
                _chSwept = -1;
                _chStep++;
                _chClock = 0f;
                break;
            }
            case 12 when _chClock >= 1f:
            {
                var pour = enc.SetPieceNode("r4_pour") as MoltenPour;
                GD.Print($"[chase] 8 after the load: mode={rc.State.Mode} stop={rc.StopIndex} armed ride={enc.ArmedRide} golem defeated={enc.GolemDefeated} valve broken={pour?.Quenched} slag carts={enc.SlagCarts.Count} golem={(enc.GolemBody == null ? "none" : enc.GolemBody.Name)}");
                if (rc.State.Mode != CartMode.Held || rc.StopIndex != 3 || enc.ArmedRide >= 1) { fail($"the cart came back {rc.State.Mode} at stop {rc.StopIndex} (armed ride {enc.ArmedRide}), not held at stop 3"); return; }
                if (!enc.GolemDefeated || pour is not { Quenched: true }) { fail("the load lost the golem's death or the broken valve"); return; }
                ChaseMend(main, root);
                _chStep++;
                _chClock = 0f;
                if (!RideBoard(main, layout, rc, out var why)) { fail(why); return; }
                break;
            }
            case 13:
            {
                if (rc.State.Mode != CartMode.Rolling) { if (_chClock > 8f) fail($"re-boarded at stop 3 after the load, but the cart did not depart (mode {rc.State.Mode})"); break; }
                _chStep++;
                _chClock = 0f;
                break;
            }
            case 14 when _chClock >= 0.3f:
            {
                var golems = _chSpawnRide.Count(kv => kv.Value == 4 && IsInstanceValid(kv.Key) && kv.Key.Profile.Id == "enemy.forge_golem"); // spawned since the load (the map starts again with the loaded floor)
                var pour = enc.SetPieceNode("r4_pour") as MoltenPour;
                GD.Print($"[chase] 8 departed stop 3 after the load: armed ride {enc.ArmedRide}, golem={(enc.GolemBody == null ? "none" : enc.GolemBody.Name)}, forge golems spawned with the ride={golems}, valve broken={pour?.Quenched}");
                if (enc.ArmedRide != 4 || enc.GolemBody != null || golems > 0) { fail("a golem came again after it was killed and the game loaded"); return; }
                if (pour is not { Quenched: true }) { fail("the valve was mended by the departure"); return; }
                _chStep++;
                _chClock = 0f;
                break;
            }
            case 15:
            {
                if (!ChaseHeldAt(rc, 4, fail)) break;
                var buffer = enc.SetPieceNode("r4_buffer") as BufferStop;
                var pour = enc.SetPieceNode("r4_pour") as MoltenPour;
                GD.Print($"[chase] 8 ride 4 again, held at stop 4: no golem={enc.GolemBody == null}, pour hits={enc.PourHits} (valve broken={pour?.Quenched}), hammer hits={enc.HammerHits}, jump made={_chJumped} at {_chJumpV:0.00} m/s with {_chPumpers} pumping, buffer smashed={buffer?.Smashed}");
                if (enc.GolemBody != null || enc.PourHits > 0 || pour is not { Quenched: true }) { fail("after the load the golem came or the pour ran"); return; }
                if (!_chJumped || _chPumpers != 2 || buffer is not { Smashed: true }) { fail("after the load the jump or the buffer went wrong"); return; }
                if (!ChaseTimings(fail)) return;
                GD.Print("[chase] RESULT ok");
                _done = true;
                GetTree().Quit();
                break;
            }
        }
    }

    // ---- helpers -------------------------------------------------------------------------------------------------

    /// <summary>The events the test hears: the log, the sfx cues, deaths and spawns once; the cart's events per controller.</summary>
    private void ChaseListen(RailController rc, RailEncounterController enc)
    {
        if (!_chListening)
        {
            _chListening = true;
            var bus = EventBus.Instance;
            bus.Message += OnChaseMessage;
            bus.Sfx += OnChaseSfx;
            bus.EnemySpawned += e => { if (_chEnc != null && IsInstanceValid(_chEnc)) _chSpawnRide[e] = _chEnc.ArmedRide; };
            bus.EnemyKilledByHazard += (e, source) => { if (e == _chLone) { _chLoneByCart = true; _chLoneSource = source; } };
            bus.EnemyDied += e => { if (e == _chLone && !_chLoneByCart) _chLoneOther = true; };
            bus.MemberKilledByHazard += (index, _) =>
            {
                if (!_chTrafficOn || _chLaneHero == null || index != _chLaneHero.Index || _chLaneHit) return;
                _chLaneHit = true;
                ChaseHit("bell", Engine.GetPhysicsFrames(), $"the traffic cart runs over {_chLaneHero.State.Name}");
            };
            bus.BossDefeated += floor => _chFail ??= $"a boss died on floor {floor}";
        }
        if (_chHeard != rc)
        {
            _chHeard = rc;
            rc.CartEvents += OnChaseCart;
        }
        if (_chEnc != enc)
        {
            _chEnc = enc; // a new floor (a rebuild, a load): its pieces' clocks start again
            _chGlowAt.Clear();
            _chGlowOpen.Clear();
            _chPourOpen = false;
            _chWarn.Clear();
            _chSpawnRide.Clear();
        }
    }

    private void OnChaseCart(CartEvent e)
    {
        if (_done || !_testChase || RailController.Instance is not { } rc) return;
        switch (e.Kind)
        {
            case CartEventKind.Halted: _chHalted = true; break;
            case CartEventKind.Resumed: _chResumed = true; break;
            case CartEventKind.Departed: _chJumped = false; _chPumpers = -1; _chJumpV = 0f; _chOnSpur = _chDiverted = _chBuffer = false; _chAcross = _chBack = false; break;
            case CartEventKind.Wrecked: _chFail ??= $"the cart was wrecked at S={rc.State.S:0.0} (plating {rc.State.Plating:0})"; break;
            case CartEventKind.OnFoot: _chFail ??= "the party ended up on foot"; break;
        }
        if (e.Kind is CartEventKind.Halted or CartEventKind.Resumed or CartEventKind.Departed or CartEventKind.Arrived or CartEventKind.Restored or CartEventKind.Wrecked)
            GD.Print($"[chase] . t={ChaseSeconds(Engine.GetPhysicsFrames()):0.000} cart {e.Kind} (stop {e.Stop}) at S={rc.State.S:0.0} plating {rc.State.Plating:0}");
    }

    private void OnChaseMessage(string text)
    {
        if (_done || !_testChase) return;
        var t = Engine.GetPhysicsFrames();
        if (text.Contains("Cart coming!")) { if (_chTrafficOn) _chWarn.TryAdd("bell", t); }
        else if (text.Contains("Line's blocked")) _chWarn["heap"] = t;
        else if (text == "The cart crashes into the ore heap!") ChaseHit("heap", t, "the cart into the heap");
        else if (text == "A drop hammer slams into the cart!")
        {
            var rc = RailController.Instance;
            DropHammer? hit = null;
            if (_chEnc != null && rc != null)
                foreach (var h in ChaseHammers(_chEnc))
                    if (h.Judged == HammerPhase.Fall && (hit == null || Mathf.Abs(h.S - rc.State.S) < Mathf.Abs(hit.S - rc.State.S))) hit = h;
            if (hit != null && _chGlowAt.TryGetValue(hit.Index, out var glow)) _chWarn["hammer"] = glow;
            else _chWarn.Remove("hammer");
            ChaseHit("hammer", t, $"hammer {hit?.Index.ToString() ?? "?"} on the cart");
        }
        else if (text == "Molten slag pours over the cart!") ChaseHit("pour", t, "the pour on the cart");
        else if (text == "The golem slams the cart!") { _chSlams++; ChaseHit("golem", t, "the golem's slam"); }
        else if (text.Contains("Pump it")) _chWarn["jump"] = t;
        else if (text is "The cart leaps the gap!" or "The cart drops into the gap!")
        {
            var rc = RailController.Instance;
            _chJumped = text == "The cart leaps the gap!";
            _chJumpV = rc?.JumpSpeed ?? 0f;
            _chPumpers = rc == null ? -1 : Main.Instance.Party.Adventurers.Count(a => IsInstanceValid(a) && a.Cart == rc.Cart && a.Pumping); // set this tick, before the lip was judged
            ChaseHit("jump", t, $"the lip at {(rc?.State.V ?? 0f):0.00} m/s, {_chPumpers} pumping");
        }
        else if (text == "The trestle groans under the cart.") _chWarn["trestle"] = t;
        else if (text is "The trestle collapses behind the cart." or "The trestle gives way under the cart!") ChaseHit("trestle", t, text == "The trestle collapses behind the cart." ? "it falls behind the cart" : "it falls under the cart");
        else if (text.StartsWith("An ember hound lands") || text.StartsWith("An ember hound misses")) _chLeaps++;
        else if (text == "The golem goes off the spur!") _chDiverted = true;
        else if (text == "The cart smashes through the buffer!") _chBuffer = true;
        else if (text == "The slag cart derails!") _chDerailed = true;
    }

    private void OnChaseSfx(string cue)
    {
        if (_done || !_testChase) return;
        var t = Engine.GetPhysicsFrames();
        switch (cue)
        {
            case "golem_windup": _chWarn["golem"] = t; break;
            case "pour_hiss": _chWarn["pour"] = t; _chPourOpen = true; break;
            case "hammer_wind":
                if (_chEnc != null) foreach (var h in ChaseHammers(_chEnc)) if (h.Judged == HammerPhase.Glow && _chGlowOpen.Add(h.Index)) _chGlowAt[h.Index] = t; // the hammer that has just begun to glow
                break;
        }
    }

    /// <summary>
    /// Every physics tick: when each hammer began to glow and when the pour began its warning, for a piece whose cue did not
    /// play (the cart was far). Seen here at most a tick late, which only makes a measured warning shorter.
    /// </summary>
    private void ChasePoll()
    {
        if (_done || _chEnc == null || !IsInstanceValid(_chEnc)) return;
        var t = Engine.GetPhysicsFrames();
        foreach (var h in ChaseHammers(_chEnc))
        {
            if (h.Judged != HammerPhase.Glow) { _chGlowOpen.Remove(h.Index); continue; }
            if (_chGlowOpen.Add(h.Index)) _chGlowAt[h.Index] = t;
        }
        if (_chEnc.SetPieceNode("r4_pour") is MoltenPour pour)
        {
            if (pour.Phase == PourPhase.Dry) _chPourOpen = false;
            else if (pour.Phase == PourPhase.Warn && !_chPourOpen) { _chPourOpen = true; _chWarn["pour"] = t; }
        }
    }

    private static IEnumerable<DropHammer> ChaseHammers(RailEncounterController enc) =>
        enc.SetPieceNode("r4_hammers") is Node3D holder ? holder.GetChildren().OfType<DropHammer>() : Enumerable.Empty<DropHammer>();

    /// <summary>A hazard hit: it must have warned at least 1.5 s before (the gap is printed, in physics frames' seconds).</summary>
    private void ChaseHit(string hazard, ulong t, string note)
    {
        if (!_chWarn.TryGetValue(hazard, out var w))
        {
            GD.Print($"[chase] 5 {hazard}: hit at t={ChaseSeconds(t):0.000} s ({note}) with NO warning before it");
            _chFail ??= $"{hazard} hit ({note}) with no warning before it";
            return;
        }
        var gap = t - w;
        _chHits.Add((hazard, w, t, note));
        GD.Print($"[chase] 5 {hazard}: warned at t={ChaseSeconds(w):0.000} s, hit at t={ChaseSeconds(t):0.000} s ({note}): {ChaseSeconds(gap):0.000} s{(gap < ChaseMinGap ? " UNDER 1.5 s" : "")}");
        if (gap < ChaseMinGap) _chFail ??= $"{hazard} hit {ChaseSeconds(gap):0.000} s after its warning ({note}), under 1.5 s";
        if (hazard is "bell" or "heap" or "jump" or "trestle") _chWarn.Remove(hazard); // one warning, one hit
    }

    /// <summary>The end: every hazard kind hit at least once, each a full 1.5 s after its warning (the table of first hits).</summary>
    private bool ChaseTimings(Action<string> fail)
    {
        GD.Print($"[chase] 5 warning -> first hit (physics frames at {Engine.PhysicsTicksPerSecond} Hz; {_chHits.Count} hits in all):");
        foreach (var kind in ChaseHazards.Append("trestle"))
        {
            var all = _chHits.Where(h => h.Hazard == kind).ToList();
            if (all.Count == 0) { GD.Print($"[chase] 5   {kind,-8} no hit"); continue; }
            var first = all[0];
            var shortest = all.Min(h => h.Hit - h.Warn);
            GD.Print($"[chase] 5   {kind,-8} warned {ChaseSeconds(first.Warn),9:0.000} s  hit {ChaseSeconds(first.Hit),9:0.000} s  gap {ChaseSeconds(first.Hit - first.Warn):0.000} s  ({all.Count} hits, shortest gap {ChaseSeconds(shortest):0.000} s; {first.Note})");
        }
        var missing = ChaseHazards.Where(k => _chHits.All(h => h.Hazard != k)).ToList();
        if (missing.Count > 0) { fail($"no hit recorded for [{string.Join(", ", missing)}]: their warning timing is unproven"); return false; }
        if (_chHits.Any(h => h.Hit - h.Warn < ChaseMinGap)) { fail("a hazard hit under 1.5 s after its warning"); return false; }
        return true;
    }

    /// <summary>Every enemy on the floor root (living or dead, plan collectives included) and every collective's members: none is a boss.</summary>
    private static bool ChaseSweep(Main main, Node3D root, string when, Action<string> fail)
    {
        var all = root.GetChildren().OfType<Enemies.Enemy>().ToList();
        var groups = main.Builder.Collectives?.Collectives ?? new List<Enemies.Collective>();
        var members = groups.SelectMany(c => c.Members).Where(m => IsInstanceValid(m)).ToList();
        var bosses = all.Concat(members).Where(e => e.IsBoss).Distinct().ToList();
        var stairs = root.GetNodeOrNull<Stairs>("DownStairs");
        var kinds = string.Join(", ", all.GroupBy(e => e.Profile.Id.Replace("enemy.", "")).OrderBy(g => g.Key).Select(g => $"{g.Key}x{g.Count()}"));
        GD.Print($"[chase] 5 boss sweep, {when}: {all.Count} enemies on the floor ({all.Count(e => e.IsAlive)} alive) [{kinds}], {groups.Count} collectives with {members.Count} members, elites [{string.Join(", ", all.Where(e => e.IsElite).Select(e => e.DisplayName))}]: bosses={bosses.Count}; stairs locked={stairs?.Locked}");
        if (bosses.Count == 0) return true;
        fail($"boss on floor 12 ({when}): {string.Join(", ", bosses.Select(e => $"{e.Name} {e.Profile.Id}"))}");
        return false;
    }

    /// <summary>Per frame: the slag carts that came and pulled alongside, the lane blend of the cart, the golem on the spur.</summary>
    private void ChaseTrackRides(RailController rc, RailEncounterController enc)
    {
        foreach (var c in enc.SlagCarts)
        {
            if (!IsInstanceValid(c) || c.Derailed || c.EncounterId == "traffic") continue;
            if (_chSlagSeen.Add(c.EncounterId)) GD.Print($"[chase] {enc.ArmedRide} slag cart {c.EncounterId} (offset {c.Offset:+0;-0}) on the enemy line at S={c.Run.State.S:0.0} (party S={rc.State.S:0.0}) riders [{string.Join(", ", c.Aboard.Select(r => r.Profile.Id.Replace("enemy.", "")))}]");
            if (rc.State.Mode == CartMode.Rolling && PaceRules.Alongside(c.Run.State.S, PaceRules.TargetS(enc.Lanes.EnemyS(rc.State.S), c.Offset)) && _chSlagBeside.Add(c.EncounterId))
                GD.Print($"[chase] {enc.ArmedRide} slag cart {c.EncounterId} pulls alongside at S={c.Run.State.S:0.0} (pace point {PaceRules.TargetS(enc.Lanes.EnemyS(rc.State.S), c.Offset):0.0}), v {c.Run.State.V:0.0} vs the party's {rc.State.V:0.0}");
        }
        if (rc.State.Mode == CartMode.Rolling && enc.ArmedRide == 3)
        {
            if (!_chAcross && rc.FrontOnEnemyLane) { _chAcross = true; GD.Print($"[chase] 3 the cart is across on the enemy lane at S={rc.State.S:0.0} (blend {rc.LaneBlend:0.00})"); }
            if (enc.Rams > _chRamsSeen) { _chRamsSeen = enc.Rams; GD.Print($"[chase] 3 the cart rams a slag cart at S={rc.State.S:0.0} on the enemy lane={rc.FrontOnEnemyLane}: plating {rc.State.Plating:0}"); }
            if (_chAcross && !_chBack && rc.Crossing && rc.LaneBlend <= 0.001f) { _chBack = true; GD.Print($"[chase] 3 the cart is back on its own lane at S={rc.State.S:0.0}"); }
        }
        if (enc.ArmedRide == 4 && !_chOnSpur && enc.SetPieceNode("r4_spur") is SpurSwitch spur && enc.GolemRun is { } run && run.Track == spur.Sampler)
        {
            _chOnSpur = true;
            GD.Print($"[chase] 4 the golem's runner takes the spur (party S={rc.State.S:0.0})");
        }
    }

    /// <summary>The cart rolled in and is held at <paramref name="stop"/> (not halted on the line). False while it rolls.</summary>
    private bool ChaseHeldAt(RailController rc, int stop, Action<string> fail)
    {
        if (rc.State.Mode == CartMode.Rolling) { if (_chClock > 70f) fail($"the cart was not held at stop {stop} within 70 s (S={rc.State.S:0.0})"); return false; }
        if (rc.State.Mode != CartMode.Held || rc.StopIndex != stop || rc.Halted) { fail($"the cart is {rc.State.Mode} at stop {rc.StopIndex} (halted {rc.Halted}), not held at stop {stop}"); return false; }
        return true;
    }

    /// <summary>The stop's gate is down with its guards alive; they die by the test's hand.</summary>
    private static bool ChaseKillGuards(Main main, FloorLayout layout, RailController rc, int stop, Action<string> fail)
    {
        var guards = StopGuards(main, layout.Rail!.Stops[stop].Room).ToList();
        GD.Print($"[chase] . stop {stop}: gate up={rc.GateUp(stop)}, {guards.Count} guards [{string.Join(", ", guards.Select(e => e.Profile.Id.Replace("enemy.", "")))}] die by the test's hand; plating {rc.State.Plating:0}, hp [{string.Join(" ", main.Party.Active.Select(a => $"{a.State.Name}:{a.State.Hp:0}"))}]");
        if (rc.GateUp(stop) || guards.Count == 0) { fail($"stop {stop}'s gate is not held down by living guards"); return false; }
        foreach (var e in guards) e.Kill();
        return true;
    }

    /// <summary>Cage <paramref name="stop"/> opens with the orc's keys.</summary>
    private static bool ChaseOpenCage(Main main, CrewRescueController ctrl, int stop, Action<string> fail)
    {
        var wagon = ctrl.Wagons.First(w => w.Stop == stop);
        var prompt = wagon.Prompt;
        wagon.Interact(main.Party, main.Party.Local!);
        GD.Print($"[chase] . cage {stop} by \"{prompt}\": open={wagon.Open}");
        if (wagon.Open) return true;
        fail($"cage {stop} did not open by \"{prompt}\"");
        return false;
    }

    /// <summary>The cart has left stop <paramref name="stop"/> with the stop's wagon coupled on behind.</summary>
    private bool ChaseDepart(RailController rc, RailEncounterController enc, CrewRescueController ctrl, int stop, Action<string> fail)
    {
        if (rc.State.Mode != CartMode.Rolling) { if (_chClock > 10f) fail($"the cart did not leave stop {stop} (mode {rc.State.Mode}, boarding {rc.Boarding.Running})"); return false; }
        var wagon = ctrl.Wagons.First(w => w.Stop == stop);
        GD.Print($"[chase] . departed stop {stop}: ride {enc.ArmedRide} armed, wagon {stop} coupled={wagon.Coupled}, train=[{string.Join(",", rc.Train.Select(w => w.Stop))}], {Main.Instance.Party.Active.Count(a => a.Seated)} heroes seated");
        if (wagon.Coupled && rc.Train.Count == stop) return true;
        fail($"wagon {stop} did not couple on at the departure (train {rc.Train.Count})");
        return false;
    }

    /// <summary>Ride 4's golem is out on the enemy line: alive, carried, an elite and no boss.</summary>
    private static bool ChaseGolemOut(RailEncounterController enc, Action<string> fail)
    {
        var g = enc.GolemBody;
        GD.Print($"[chase] 4 ride 4 armed: golem {(g == null ? "none" : $"{g.Name} \"{g.DisplayName}\" ({g.Profile.Id}) alive={g.IsAlive} carried={g.Carried} on the runner={enc.GolemRun != null} elite={g.IsElite} affixes=[{string.Join(",", g.Profile.Affixes ?? Array.Empty<Enemies.EliteAffix>())}] boss={g.IsBoss} hp {g.Hp:0}/{g.MaxHp:0}")}");
        if (g is { IsAlive: true, IsBoss: false, IsElite: true, Carried: true } && enc.GolemRun != null) return true;
        fail("the golem did not come out on the enemy line as a carried elite (never a boss)");
        return false;
    }

    /// <summary>Heroes back to full (the downed got up), and crew who went down in their wagons revived: the test keeps the party able to ride.</summary>
    private static void ChaseMend(Main main, Node3D root)
    {
        foreach (var a in main.Party.Adventurers)
        {
            if (!IsInstanceValid(a) || a.Parked) continue;
            var down = a.Downed;
            a.State.Hp = a.State.Stats[Characters.Stat.MaxHp];
            if (down) a.Revive();
        }
        foreach (var n in root.GetChildren().OfType<Npc>())
            if (n.Crew && n.Downed && !n.Dead) n.TryRevive();
    }

    /// <summary>Two companions on foot: one on the enemy lane's centre line, one in an enemy-lane pocket near it. Both are held there.</summary>
    private bool ChasePlaceTraffic(Main main, FloorLayout layout, out string why)
    {
        why = "";
        var mates = main.Party.Adventurers.Where(a => IsInstanceValid(a) && !a.Parked && !a.Downed && !a.Seated && a.Control == ControlKind.Companion).ToList();
        if (mates.Count < 2 || main.Builder.EnemyTrack is not { } track) { why = $"{mates.Count} companions on foot (want 2) or no enemy track"; return false; }
        var enemy = new TrackSampler(track);
        _chLaneS = Mathf.Min(40f, enemy.Length - 20f);
        _chLaneAt = enemy.Point(_chLaneS) + Vector3.Up * 0.05f;
        var laneCell = DungeonBuilder.WorldToCell(_chLaneAt);
        Vector3? best = null;
        var bestD = float.MaxValue;
        foreach (var cell in layout.Rail!.Pockets)
        {
            var onParty = layout.IsPartyRail(cell + new GridPos(1, 0)) || layout.IsPartyRail(cell + new GridPos(-1, 0)) || layout.IsPartyRail(cell + new GridPos(0, 1)) || layout.IsPartyRail(cell + new GridPos(0, -1));
            if (onParty) continue;
            var p = main.Builder.Surface(cell);
            var s = track.Curve.GetClosestOffset(track.ToLocal(p));
            var d = Mathf.Abs(s - _chLaneS);
            if (d < 4f || d >= bestD || s < 8f) continue; // not the lane hero's own spot, not where the cart sets off
            bestD = d;
            best = p;
            _chPocketS = s;
        }
        if (best is not { } pocket) { why = "no enemy-lane pocket on the line"; return false; }
        _chPocketAt = pocket + Vector3.Up * 0.05f;
        _chLaneHero = mates[0];
        _chPocketHero = mates[1];
        _chLaneHit = false;
        _chLaneHero.Teleport(_chLaneAt, enemy.Point(_chLaneS - 5f));
        _chPocketHero.Teleport(_chPocketAt, _chLaneAt);
        _chPocketHp = _chPocketHero.State.Hp;
        GD.Print($"[chase] 6 traffic: {_chLaneHero.State.Name} stands on the enemy lane at S={_chLaneS:0.0} (cell {laneCell}, enemy lane cell={layout.Rail.Enemy.Contains(laneCell)}); {_chPocketHero.State.Name} in the enemy-lane pocket at {DungeonBuilder.WorldToCell(_chPocketAt)} (S={_chPocketS:0.0}, rail cell={layout.IsRail(DungeonBuilder.WorldToCell(_chPocketAt))}); the test holds both there and starts the traffic clock");
        return true;
    }

    /// <summary>The two traffic heroes keep their places (a companion on a warned lane would walk to a refuge; this one stays, as a player who ignores the bell).</summary>
    private void ChaseHoldTraffic(RailEncounterController enc)
    {
        enc.RunawayClock = 0f; // the runaway wagon is another test's: none on the party line now
        if (_chLaneHero is { } lane && IsInstanceValid(lane) && !lane.Downed) { lane.GlobalPosition = _chLaneAt; lane.Velocity = Vector3.Zero; }
        if (_chPocketHero is { } pocket && IsInstanceValid(pocket) && !pocket.Downed) { pocket.GlobalPosition = _chPocketAt; pocket.Velocity = Vector3.Zero; }
    }

    /// <summary>Stand beside the heap, look at it (the mouse-look path) and swing the held weapon (the attack input's path).</summary>
    private void ChaseSwingAt(Main main, Adventurer lead, OreHeap heap)
    {
        var frame = heap.GlobalTransform;
        var x = frame.Basis.X.Normalized();
        var aim = heap.GlobalPosition + Vector3.Up * 0.6f;
        var ground = main.Builder.Surface(DungeonBuilder.WorldToCell(frame.Origin + x * 1.9f));
        var stand = new Vector3(frame.Origin.X, ground.Y, frame.Origin.Z) + x * 1.9f + Vector3.Up * 0.1f;
        lead.Teleport(stand, aim);
        var eye = lead.Camera.GlobalPosition;
        var pitch = Mathf.Atan2(aim.Y - eye.Y, Mathf.Max(0.01f, new Vector2(aim.X - eye.X, aim.Z - eye.Z).Length()));
        var sens = 0.0022f * Settings.MouseSensitivity;
        lead.Look(new Vector2(0f, -pitch / sens * (Settings.InvertY ? -1f : 1f)));
        main.Party.Attack(Array.IndexOf(main.Party.Adventurers, lead));
        _chSwings++;
        GD.Print($"[chase] 7 swing {_chSwings} at the heap from {stand.DistanceTo(aim):0.00} m (pitch {Mathf.RadToDeg(pitch):0} deg): heap left {heap.Left}");
    }

    private static float ChaseFlat(Vector3 a, Vector3 b) => new Vector2(a.X - b.X, a.Z - b.Z).Length();
}
