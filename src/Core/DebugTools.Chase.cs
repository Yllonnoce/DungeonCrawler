using DungeonCrawler.Adventure;
using DungeonCrawler.Adventure.Objectives;
using DungeonCrawler.Dungeon;
using DungeonCrawler.Dungeon.Generation;
using DungeonCrawler.Dungeon.Rails;
using DungeonCrawler.Party;
using Godot;

namespace DungeonCrawler.Core;

// ---- --test-chase ----------------------------------------------------------------------------------------------
// The five rides of the built-in campaign's floor 12 at lethality 3, scripted end to end (the condemned branch: the cages
// open with the orc's keys). The stop guards die by the test's hand on arrival and each cage is opened as the cart is
// held there, so it is a ride test. Every number that names the kiln (the last ride, the stop it leaves from) or the end of
// the line reads LineShape, and the kiln's pieces are "r{LastRide}_*" (r5_* on the shipped floor). Section 1 rides the line:
// ride 1's lone cinder run over and the beam passed, the slag traffic (a hero on the enemy lane at stop 1 warned, then mangled;
// one in a pocket untouched), ride 2's three slag carts (a brake shoe broken by a debug hit derails one, a hound leaps), ride
// 3's switch thrown by a hit that a second hit leaves thrown (the slag cart ahead rammed; its live switch back missed by a hit, which
// leaves it straight, and thrown from the seat by the lead through the dispatch E uses; the cart back on its own lane), the
// rides after it up to the kiln's stop ridden for real (RideOnTo), then the kiln: its golem out of the wall as the cart
// reaches the golem's At (never short of it by more than 1 m), diverted at the spur by a lever debug-thrown ahead on ride 2
// (every arrival before the kiln leaves it thrown) that the seated lead sets back and throws again from the seat as the
// kiln's seated window offers it (no aim, two presses through the dispatch E uses, a mash between them ignored by the
// re-press guard), the jump made with two pumping and the buffer smashed into the last stop; the crew home and the stairs
// open. Section 2 builds the floor again: ride 1 departs (its thrower a full enemy, the floor's first), a save mid-ride 1
// is loaded and ride 1 departs again (the thrower given back a reinforcement: the save keeps that it was sent); ride 3 with
// the switch left straight (the heap halts the cart, a hero breaks it with real swings, the ride resumes), on to the kiln's
// stop, then the kiln crawled under a hammer and the pour so each hits, the golem killed, the valve broken, a save and a load
// mid-ride, a stunned cinder under a hammer hurt with no ride armed, and the kiln again from its stop (no golem, the valve
// still broken) with every cage open, W1..W4 coupled from stop 0 and the pumpers capped at one: the jump is still made (JumpSpeed
// with one pumper, the load counting three wagons at most, the lip's slope and all). Plan 14: the train's four open wagons are
// coupled from the landing (the floor check reads W1..W4 coupled and empty at stop 0); the kiln stop's crew are seated at the landing
// by the test's hook (DebugSeatCrew), so ride 1 runs with a crewed W1 and r1_beam must clear every seated head in the train by 0.05 m
// ([chase] 1 beam); every other cage the party opens at its stop lets its crew out, they walk to the first empty seats and each
// departure checks they sat there before it (the one-pumper set-up opens the far cages with the hook). A hound must board a crewed
// wagon on ride 2 (boarders pick the wagon with the most crew first).
// Throughout: no boss on the floor, the
// stairs stay sealed until the crew are home, every hazard's warning comes at least 1.5 s before its hit (physics
// frames logged; a barricade the cart smashes, each one its own call), and the cart is mended to full plating at every stop. At stop 1, before ride 2, the party is levelled to
// --party-level=N (default ChaseDefaultLevel = 12, the level a run of the shipped campaign has by floor 12), its points spent; on
// ride 2 the lead sits idle, who kills each boarder is logged, and at that level or higher at most one companion may be
// down by stop 2; a hound must board a wagon, and one that boards a crewed wagon with no hero up in it must be answered: struck by a
// companion within ChaseBoarderAnswer + 3 s (plan 14, Ruling 15: the answerer climbs to it car by car; its line prints the answerer, its
// moves and the first strike; the reach from each seat is printed), and no crewman may go down to a wagon boarder's bite. Ride 2 is ridden twice with those checks: in
// section 1 in the natural seating (the rear seats hold whoever hopped in there, often the ranged companions, who turn right
// round to shoot), and in section 2, after the thrower's save, from a restored stop 1 with a blade swapped into a rear seat.
// Prints [chase] lines and [chase] RESULT.
public partial class DebugTools
{
    private bool _testChase;
    /// <summary>
    /// The most companions that may pump the cart at once while --test-chase sets it (its one-pumper jump pass); null, always
    /// outside that pass, leaves the game's own rule (two). Debug only: nothing else sets it.
    /// </summary>
    public int? PumperCap { get; private set; }
    /// <summary>
    /// The party's level for --test-chase. 12 is what a run of the shipped campaign has on reaching floor 12: the XP curve is
    /// tuned for level 18–20 at the end of floor 20, and with XP per floor ∝ the tier's creature XP (crypt ~27, caves ~30, forge
    /// ~45, abyss ~58) × <c>TierRules.XpScale</c>, floors 1–11 give about 28% of the run's XP: 14.5k–19.8k, level 11.6–12.9.
    /// </summary>
    private const int ChaseDefaultLevel = 12;
    private int _chPartyLevel = ChaseDefaultLevel;
    private bool _chLevelled;
    private int _chStep;
    private float _chClock;
    private bool _chSection2;
    private RailController? _chRails;
    private RailController? _chHeard;
    private RailEncounterController? _chEnc;
    private bool _chListening, _chSwept0;
    private int _chSwept = -1;
    private string? _chFail;
    /// <summary>The ride music's pitch (plan 6 task 6): the pour heard at its pitch on the kiln's ride, and the first track in front after it, at 1.</summary>
    private string? _chPourPitch, _chAfterPour;
    private const string ChaseSlot = "chasetest";
    private readonly Dictionary<Enemies.Enemy, int> _chSpawnRide = new();
    // ride 1
    private Enemies.Enemy? _chLone;
    private bool _chLoneByCart, _chLoneOther;
    private string _chLoneSource = "";
    private readonly Dictionary<int, float> _chHp = new();
    private bool _chBeamDone;
    /// <summary>r1_beam's measure over the train (plan 14).</summary>
    private readonly BeamHeads _chHeads = new();
    // ride 2
    private readonly HashSet<string> _chSlagSeen = new(), _chSlagBeside = new();
    private SlagCart? _chShoeCart;
    private readonly List<Enemies.Enemy> _chShoeRiders = new();
    private bool _chDerailed;
    private int _chLeaps;
    private readonly Dictionary<Enemies.Enemy, string> _chBoarders = new(); // a hound that boarded -> what it boarded
    private readonly Dictionary<Enemies.Enemy, (int Striker, ulong Frame)> _chLastHit = new();
    private readonly Dictionary<Enemies.Enemy, string> _chKiller = new();
    private Enemies.Enemy? _chDiedNow;
    private ulong _chDiedFrame;
    private readonly HashSet<int> _chDownedRide2 = new();
    private readonly Dictionary<Enemies.Enemy, ulong> _chBoardedAt = new(); // a wagon boarder -> the physics frame it boarded
    private readonly Dictionary<Enemies.Enemy, (string Who, ulong Frame)> _chFirstMateHit = new(); // a wagon boarder -> the first companion to hit it
    private readonly Dictionary<Enemies.Enemy, int> _chNeedsAnswer = new(); // a wagon boarder that boarded a crewed wagon with no hero up -> the wagon
    private readonly HashSet<Enemies.Enemy> _chAnswerSaid = new(); // its answer line printed
    private readonly List<string> _chCrewBitten = new(); // crew who went down on ride 2 to a wagon boarder's bite
    private readonly List<string> _chCrewDownOther = new(); // crew who went down on ride 2 to something else
    /// <summary>Which ride-2 boarder pass is on: 0 none, 1 section 1's (natural seating), 2 section 2's (a blade swapped into a rear seat).</summary>
    private int _chBoarderPass;
    /// <summary>Ride 2's hounds that boarded a wagon with crew seated in it, this boarder pass.</summary>
    private int _chCrewedBoards;
    /// <summary>A hound that boards a wagon on ride 2 must be struck by a companion within this long.</summary>
    private const float ChaseBoarderAnswer = 6f;
    private bool _chSpurEarly;
    // section 2: the thrower sent once, a save mid-ride 1 and the thrower given back by the load (9); the one-pumper jump (10)
    private int _chThrowerPhase;
    private readonly List<(Enemies.Enemy Thrower, bool Reinforcement)> _chThrowersSeen = new();
    private int _chThrowersBefore;
    // section 2: a creature stood under a hammer with no ride armed
    private Enemies.Enemy? _chProbe;
    private float _chProbeHp;
    private string _chProbeBy = "";
    // ride 3
    private bool _chThrown, _chAcross, _chBack;
    /// <summary>The seated lead threw ride 3's live switch back this ride.</summary>
    private bool _chBackThrown;
    private int _chRams0, _chRamsSeen;
    // the kiln's ride
    private bool _chDiverted, _chOnSpur, _chJumped, _chBuffer;
    /// <summary>The party cart's S as the kiln's golem spawned (its burst), NaN before.</summary>
    private float _chGolemSpawnS = float.NaN;
    /// <summary>Section 1's seated presses on the kiln's spur lever (0: none, 1: set back, 2: thrown again), and the first mismatch they met.</summary>
    private int _chSeatSpur;
    private string? _chSeatSpurFail;
    /// <summary>When (this machine's clock) the first seated press went: the second waits out the re-press guard (<see cref="SeatedSwitchRules.RepressGuardSeconds"/>).</summary>
    private double _chSeatSpurAt;
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
    private bool _chGolemKilled, _chGolemSeen;
    // warnings and hits, in physics frames
    private readonly Dictionary<string, ulong> _chWarn = new();
    private readonly List<(string Hazard, ulong Warn, ulong Hit, string Note)> _chHits = new();
    private readonly Dictionary<int, ulong> _chGlowAt = new();
    private readonly HashSet<int> _chGlowOpen = new();
    private bool _chPourOpen;

    private static readonly string[] ChaseHazards = { "bell", "heap", "hammer", "pour", "golem", "jump", "barricade" };

    private static ulong ChaseMinGap => (ulong)Mathf.CeilToInt(1.5f * Engine.PhysicsTicksPerSecond);
    private static double ChaseSeconds(ulong frames) => frames / (double)Engine.PhysicsTicksPerSecond;

    private void RunChaseTest()
    {
        var main = Main.Instance;
        if (main?.Builder.Layout is not { } layout || main.Builder.FloorRoot is not { } root || !main.InGame || Game.Instance.CurrentFloor != 12) return;
        _chClock += (float)GetProcessDeltaTime();
        HoldTheRunaway("chase"); // Ruling 22: not this flow's business
        void Fail(string why) { GD.Print($"[chase] RESULT FAIL: {why}"); _done = true; GetTree().Quit(1); }
        var party = main.Party;
        var rc = RailController.Instance;
        var enc = root.GetNodeOrNull<RailEncounterController>("RailEncounterController");
        var ctrl = ObjectiveController.Instance as CrewRescueController;
        if (rc == null || enc == null || ctrl == null) { if (_chClock > 3f) Fail($"floor 12 lacks {(rc == null ? "a RailController " : "")}{(enc == null ? "a RailEncounterController " : "")}{(ctrl == null ? "a CrewRescueController" : "")}"); return; }
        if (_chSection2 && rc == _chRails) return; // the floor is being built again: wait for the new one
        RideWaiveCages(main, rc, "chase");
        // the flow's human is one hero throughout (its lead sits idle on ride 2 and acts by its own hand later): no possession when it goes
        // down (plan 14 Task 4: companions now climb off to answer boarded wagons, and on ride 4 the idle lead can fall to r4_slag's priest)
        party.AllowPossession = false;
        ChaseListen(rc, enc);
        ChaseMusicPoll();
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
        // by the test's hand at their stop) and the armed ride's own enemies; section 2 culls all but the kiln's golem
        foreach (var e in root.GetChildren().OfType<Enemies.Enemy>())
        {
            if (!e.IsAlive) continue;
            if (enc.IsThrower(e) && _chThrowersSeen.All(t => t.Thrower != e))
            {
                _chThrowersSeen.Add((e, e.IsReinforcement));
                GD.Print($"[chase] {(_chSection2 ? "9" : "1")} thrower {e.Name} ({e.Profile.Id}) stands for ride {enc.ArmedRide}: reinforcement={e.IsReinforcement}");
            }
            var keep = _chSection2 && _chBoarderPass != 2
                ? e == enc.GolemBody && !_chGolemKilled || e == _chProbe
                : HoldsAStop(main, layout, e) || enc.ArmedRide >= 1 && (e == enc.GolemBody || (e.IsReinforcement || enc.IsThrower(e)) && _chSpawnRide.TryGetValue(e, out var r) && r == enc.ArmedRide);
            if (!keep) e.Kill();
            // a stop's guards leave their post as the cart comes in sight and meet it at the platform, where the seated companions
            // (who shoot at any distance from a seat) can kill them before it is held; section 1 owns them (they die by the test's
            // hand at their stop), so the party's blows miss them, as the lone cinder's do (a hazard's blow ignores Untargetable)
            else if (!_chSection2 && !e.Untargetable && HoldsAStop(main, layout, e)) e.SetUntargetable(true);
        }
        ChaseTrackRides(rc, enc, party);
        if (_chTrafficOn) ChaseHoldTraffic(enc);

        if (_chSection2) { RunChaseSection2(main, layout, root, rc, enc, ctrl, Fail); return; }
        var lead = party.Local;
        switch (_chStep)
        {
            case 0 when _chClock >= 1.5f:
            {
                var keyring = root.GetNodeOrNull<OrcKeyring>("OrcKeyring");
                GD.Print($"[chase] 0 floor 12: stops={rc.StopS.Length} StopS=[{string.Join(", ", rc.StopS.Select(s => s.ToString("0.0")))}] lethality={TierRules.Lethality} set pieces={enc.SetPieceCount} cages={ctrl.Cages.Count} caged={ctrl.Crew.Count(c => ctrl.StateOf(c) == CrewState.Caged)} stairs locked={stairs.Locked} keyring={(keyring == null ? "none" : $"\"{keyring.Prompt}\"")}");
                if (rc.StopS.Length != Floor12Stops || ctrl.Cages.Count != Shape(rc).Intermediate || keyring == null || !stairs.Locked) { Fail($"floor 12 is not the shipped line ({Floor12Stops} stops, {Floor12Stops - 2} cages, the orc's keys, sealed stairs)"); return; }
                var trainAt0 = string.Join(",", rc.Train.Select(w => w.Number));
                var riders0 = rc.Wagons.Sum(w => w.Riders.Count);
                GD.Print($"[chase] 0 the train at stop {rc.StopIndex}: wagons {rc.Wagons.Count}, train=[{trainAt0}], {riders0} rider(s) in it, its rear {rc.State.S - TrainRules.Length(rc.Train.Count) + CartRules.HalfLength:0.0}");
                if (rc.StopIndex != 0 || rc.Wagons.Count != Shape(rc).Intermediate || trainAt0 != string.Join(",", Enumerable.Range(1, Shape(rc).Intermediate)) || riders0 != 0) { Fail($"W1..W{Shape(rc).Intermediate} are not coupled and empty from stop 0 (stop {rc.StopIndex}, wagons {rc.Wagons.Count}, train [{trainAt0}], {riders0} rider(s))"); return; }
                if (Mathf.Abs(TierRules.Lethality - 3f) > 0.01f) { Fail($"lethality {TierRules.Lethality}, not 3"); return; }
                // ride 1 must carry crew in the train (the beam's check over seated heads): the kiln stop's cage is opened by the test's hook
                // and its crew sat at once in the first empty seats (W1), so every later cage's crew walk to the seats after them
                if (!ChaseOpenCage(main, ctrl, Shape(rc).LastRideStop, Fail, hook: true)) return;
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
                    if (_chLone != null)
                    {
                        // the run-over is what this checks: the seated companions (who now shoot at any distance from a seat) may not
                        // kill it first; a hazard's blow ignores Untargetable, so only the cart can
                        _chLone.SetUntargetable(true);
                        GD.Print($"[chase] 1 the lone cinder {_chLone.Name} stands on the party lane at cell {DungeonBuilder.WorldToCell(_chLone.GlobalPosition)} (untargetable to the party's blows: only the cart may kill it)");
                    }
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
                if (ChaseBeamHeads(rc, enc, Fail)) return;
                if (!ChaseHeldAt(rc, 1, Fail)) break;
                GD.Print($"[chase] 1 ride 1 done, held at stop 1 after {_chClock:0.0} s: lone cinder {(_chLone == null ? "never seen" : $"killed by the cart={_chLoneByCart} (\"{_chLoneSource}\")")}, beam passes={enc.BeamPasses}, plating={rc.State.Plating:0}");
                if (_chLone == null || !_chLoneByCart) { Fail("the lone cinder was not run over by the cart"); return; }
                if (!_chBeamDone || enc.BeamPasses != 1) { Fail($"the beam passed {enc.BeamPasses} times"); return; }
                if (!_chHeads.Done) { Fail("r1_beam's seated heads were never judged (the train's rear never passed it)"); return; }
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
                if (!warned) { Fail($"{lane.State.Name} was hit with no cart warning before it"); return; }
                if (pocket.Downed || pocket.State.Mangled || pocket.State.Hp < _chPocketHp - 0.01f) { Fail($"{pocket.State.Name} in the pocket was hurt by the traffic cart"); return; }
                ChaseMend(main, root);
                _chStep++;
                _chClock = 0f;
                break;
            }
            case 6 when _chClock >= 0.5f:
            {
                ChaseLevelParty(main); // here, not at the landing: a levelled seated rogue can kill ride 1's lone cinder before the cart runs it over
                enc.RunawayClock = 0f;
                _chStep++;
                _chClock = 0f;
                if (!RideBoard(main, layout, rc, out var why)) { Fail(why); return; }
                break;
            }
            case 7:
                if (!ChaseDepart(rc, enc, ctrl, 1, Fail)) break;
                ChaseBeginBoarderPass(party, rc, 1); // the natural seating: whoever hopped in where
                _chStep++;
                _chClock = 0f;
                break;
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
                if (!_chSpurEarly && enc.ArmedRide == 2 && enc.SetPieceNode(Kiln(rc, "spur")) is SpurSwitch early)
                {
                    _chSpurEarly = true;
                    early.Lever.DebugThrow(); // a walker threw the kiln's lever ahead: every arrival before the kiln must leave it thrown
                    GD.Print($"[chase] 2 ride {Shape(rc).LastRide}'s spur lever thrown ahead during ride 2 (S={rc.State.S:0.0}): thrown={early.Lever.Thrown}");
                }
                foreach (var a in party.Adventurers)
                    if (IsInstanceValid(a) && !a.Parked && a.Control == ControlKind.Companion && a.Downed && _chDownedRide2.Add(a.Index))
                        GD.Print($"[chase] 2 {a.State.Name} is down at S={rc.State.S:0.0} (seated={a.Seated}); plating {rc.State.Plating:0}");
                if (!ChaseTrackBoarders(party, root, rc, Fail)) return;
                if (!ChaseHeldAt(rc, 2, Fail)) break;
                if (!ChaseBoarderReport(main, rc, Fail)) return;
                if (enc.SetPieceNode(Kiln(rc, "spur")) is not SpurSwitch { Lever.Thrown: true }) { Fail($"arriving at stop 2 set back ride {Shape(rc).LastRide}'s spur lever, thrown ahead"); return; }
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
                // 3. ride 3: the switch thrown by a hit (a second hit leaves it thrown), the ram, the way back
                if (!_chThrown && enc.ArmedRide == 3 && rc.State.Mode == CartMode.Rolling && enc.SetPieceNode("r3_switch_out") is TrackSwitch sw)
                {
                    _chThrown = true;
                    var hit = new Combat.AttackProfile { MinDamage = 1, MaxDamage = 2 };
                    var first = sw.ReceiveAttack(lead!.State.Stats, hit, new Combat.HitContext(), party.Rng, lead.State.Name);
                    var thrown1 = sw.Thrown;
                    var second = sw.ReceiveAttack(lead.State.Stats, hit, new Combat.HitContext(), party.Rng, lead.State.Name);
                    GD.Print($"[chase] 3 ride 3 armed at S={rc.State.S:0.0}: a hit on the switch_out lever (hit={first.Hit}) threw it={thrown1}; a second hit (hit={second.Hit}) leaves it thrown={sw.Thrown}");
                    if (!thrown1 || !sw.Thrown || second.Hit) { Fail("a hit must throw the lever and a second hit must not set it back"); return; }
                }
                if (!ChaseHeldAt(rc, 3, Fail)) break;
                GD.Print($"[chase] 3 ride 3 done, held at stop 3: thrown={_chThrown} across={_chAcross} rams={enc.Rams - _chRams0} the live back thrown from the seat={_chBackThrown} back on the party lane={_chBack}; plating={rc.State.Plating:0}");
                if (!_chThrown || !_chAcross) { Fail("the cart did not take the switch onto the enemy lane"); return; }
                if (!_chBackThrown) { Fail("the seated lead never threw ride 3's live switch back from the seat"); return; }
                if (enc.Rams - _chRams0 < 1) { Fail("the cart did not ram the slag cart ahead"); return; }
                if (!_chBack) { Fail("the cart did not cross back to its own lane"); return; }
                if (enc.SetPieceNode(Kiln(rc, "spur")) is not SpurSwitch { Lever.Thrown: true }) { Fail($"arriving at stop 3 set back ride {Shape(rc).LastRide}'s spur lever, thrown ahead"); return; }
                if (enc.SetPieceNode("r3_switch_out") is not TrackSwitch { Thrown: false }) { Fail("arriving at stop 3 did not set ride 3's lever back"); return; }
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
                _chStep = Shape(rc).LastRideStop == 3 ? 16 : 13; // a four-ride line leaves stop 3 on the kiln
                _chClock = 0f;
                break;
            }
            case 13:
                if (!ChaseDepart(rc, enc, ctrl, 3, Fail)) break;
                _chStep++;
                _chClock = 0f;
                break;
            case 14:
            {
                // 4. rides 4 up to the kiln's stop, ridden for real (a stop on the way: its holders die, its cage is left, the party boards)
                var kilnStop = Shape(rc).LastRideStop;
                if (!RideOnTo(main, rc, kilnStop, "chase", Fail)) break;
                if (!ChaseHeldAt(rc, kilnStop, Fail)) break;
                GD.Print($"[chase] 4 held at stop {kilnStop}, the kiln's stop: plating={rc.State.Plating:0}, train=[{string.Join(",", rc.Train.Select(w => w.Number))}]");
                if (enc.SetPieceNode(Kiln(rc, "spur")) is not SpurSwitch { Lever.Thrown: true }) { Fail($"arriving at stop {kilnStop} set back ride {Shape(rc).LastRide}'s spur lever, thrown ahead"); return; }
                if (!ChaseKillGuards(main, layout, rc, kilnStop, Fail)) return;
                _chStep++;
                _chClock = 0f;
                break;
            }
            case 15:
            {
                var kilnStop = Shape(rc).LastRideStop;
                if (!rc.GateUp(kilnStop)) { if (_chClock > 2f) Fail($"stop {kilnStop}'s gate did not lift with its guards dead"); break; }
                ChaseMend(main, root);
                if (!ctrl.Cages.First(c => c.Stop == kilnStop).Open && !ChaseOpenCage(main, ctrl, kilnStop, Fail)) return; // opened at the landing (its crew in W1 since)
                _chStep++;
                _chClock = 0f;
                break;
            }
            case 16:
            {
                var shape = Shape(rc);
                if (rc.State.Mode == CartMode.Rolling && enc.GolemBody == null && !(rc.State.S >= ChaseGolemS(main, rc) + 10f) && _chClock < 60f) break; // the golem bursts out as the cart reaches its At: wait
                if (!ChaseDepart(rc, enc, ctrl, shape.LastRideStop, Fail)) break;
                // 5. the kiln: the golem out of the kiln wall as the cart reaches its At; the spur lever thrown ahead on ride 2 (the debug hook)
                if (!ChaseGolemOut(main, rc, enc, Fail)) break;
                if (!ChaseGolemAtItsAt(main, rc, Fail)) return;
                if (enc.SetPieceNode(Kiln(rc, "spur")) is not SpurSwitch spur) { Fail($"no spur on ride {shape.LastRide}"); return; }
                GD.Print($"[chase] 4 the spur lever, thrown ahead on ride 2, at the departure from stop {shape.LastRideStop} (S={rc.State.S:0.0}): thrown={spur.Lever.Thrown}");
                if (!spur.Lever.Thrown) { Fail($"the spur lever thrown on ride 2 is not thrown as ride {shape.LastRide} starts"); return; }
                _chStep++;
                _chClock = 0f;
                break;
            }
            case 17:
            {
                var shape = Shape(rc);
                if (!ChaseHeldAt(rc, shape.Rides, Fail)) break;
                var buffer = enc.SetPieceNode(Kiln(rc, "buffer")) as BufferStop;
                var golem = enc.GolemBody;
                GD.Print($"[chase] 4 ride {shape.LastRide} done, held at stop {shape.Rides} (the end of the line): golem took the spur={_chOnSpur} off the spur={_chDiverted} ({(golem == null ? "gone" : $"alive={golem.IsAlive} hp {golem.Hp:0}/{golem.MaxHp:0}")}), slams={_chSlams}; jump made={_chJumped} at {_chJumpV:0.00} m/s with {_chPumpers} pumping; buffer smashed={buffer?.Smashed} (cue={_chBuffer}); plating={rc.State.Plating:0}");
                if (_chSeatSpurFail != null) { Fail(_chSeatSpurFail); return; }
                if (_chSeatSpur != 2) { Fail($"the seated window did not set ride {shape.LastRide}'s spur lever back and throw it again (presses {_chSeatSpur})"); return; }
                if (!_chDiverted) { Fail("the golem was not diverted at the spur"); return; }
                if (!_chJumped || _chJumpV < SetPieceRules.JumpSpeed || _chPumpers != 2) { Fail($"the jump: made={_chJumped} at {_chJumpV:0.00} m/s with {_chPumpers} pumping (want made, >= {SetPieceRules.JumpSpeed}, 2)"); return; }
                if (buffer is not { Smashed: true } || !_chBuffer) { Fail("the cart did not come through the buffer"); return; }
                _chStep++;
                _chClock = 0f;
                break;
            }
            case 18:
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

    // ---- section 2: the heap, the hazards hit, a save and a load mid-kiln ----------------------------------------------

    private void RunChaseSection2(Main main, FloorLayout layout, Node3D root, RailController rc, RailEncounterController enc, CrewRescueController ctrl, Action<string> fail)
    {
        var party = main.Party;
        var lead = party.Local!;
        if (_chThrowerPhase < 8) { RunChaseThrowerSave(main, layout, root, rc, enc, ctrl, fail); return; }
        switch (_chStep)
        {
            case 0 when _chClock >= 1.5f:
            {
                ChaseMend(main, root);
                rc.RestoreGates(Enumerable.Range(1, Shape(rc).Intermediate)); // the test's shortcut over rides 1 and 2 (section 1 rode them): the cart stands at stop 2
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
                GD.Print($"[chase] 7 the heap path reached stop 3 (the heap took {RailEncounterController.HeapDent:0} on the way): plating {rc.State.Plating:0}, mended at the stop");
                ChaseMend(main, root);
                _chStep++;
                _chClock = 0f;
                break;
            }
            case 9:
            {
                // on to the kiln's stop, every ride for real (a stop on the way: its gate stands up, its cage is left, the party boards)
                var kilnStop = Shape(rc).LastRideStop;
                if (!RideOnTo(main, rc, kilnStop, "chase", fail)) break;
                if (!ChaseHeldAt(rc, kilnStop, fail)) break;
                GD.Print($"[chase] 8 held at stop {kilnStop}, the kiln's stop: plating {rc.State.Plating:0}, mended at the stop");
                ChaseMend(main, root);
                _chHammer0 = enc.HammerHits;
                _chPour0 = enc.PourHits;
                _chGolemKilled = false;
                _chGolemSeen = false;
                _chSlams = 0;
                _chStep++;
                _chClock = 0f;
                break;
            }
            case 10:
            {
                var kilnStop = Shape(rc).LastRideStop;
                if (rc.State.Mode != CartMode.Rolling) { if (_chClock > 10f) fail($"the cart did not leave stop {kilnStop} (mode {rc.State.Mode}, gate up {rc.GateUp(kilnStop)})"); break; }
                _chStep++;
                _chClock = 0f;
                break;
            }
            case 11:
            {
                // 8. the kiln, first try: the cart crawls under a hammer and the pour until each hits; the golem (out at its At, past
                // them) dies after a slam
                var kiln = Shape(rc).LastRide;
                if (rc.State.Mode != CartMode.Rolling) { fail($"the cart stopped ({rc.State.Mode} at stop {rc.StopIndex}) before the save"); return; }
                var s = rc.State.S;
                if (!_chGolemSeen && enc.GolemBody != null)
                {
                    _chGolemSeen = true;
                    if (!ChaseGolemOut(main, rc, enc, fail)) return;
                }
                if (!_chGolemKilled && enc.GolemBody is { IsAlive: true } golem && enc.GolemSlams >= 1)
                {
                    _chGolemKilled = true;
                    golem.Kill();
                    GD.Print($"[chase] 8 the golem killed by the test after {enc.GolemSlams} slam(s) at S={s:0.0}");
                }
                var mid = (enc.SetPieceNode(Kiln(rc, "hammers")) as Node3D)?.GetNodeOrNull<DropHammer>("Hammer1");
                var pour = enc.SetPieceNode(Kiln(rc, "pour")) as MoltenPour;
                if (mid == null || pour == null) { fail($"ride {kiln} has no hammers or no pour"); return; }
                var crawl = (enc.HammerHits == _chHammer0 && s >= mid.S - 0.3f) || (enc.PourHits == _chPour0 && s >= pour.S - 1.5f);
                if (crawl) rc.Party.State = rc.State with { V = Mathf.Min(rc.State.V, 0.15f) }; // the test holds the cart under it until it hits
                if (enc.HammerHits > _chHammer0 && enc.PourHits > _chPour0 && enc.GolemDefeated)
                {
                    var r = pour.Valve.ReceiveAttack(lead.State.Stats, new Combat.AttackProfile { MinDamage = 1, MaxDamage = 2 }, new Combat.HitContext(), party.Rng, lead.State.Name);
                    GD.Print($"[chase] 8 hammer hits={enc.HammerHits - _chHammer0} pour hits={enc.PourHits - _chPour0} slams={_chSlams} golem defeated={enc.GolemDefeated} (stairs locked={root.GetNode<Stairs>("DownStairs").Locked} after its death); debug hit on the quench valve at S={s:0.0}: hit={r.Hit}");
                    _chStep++;
                    _chClock = 0f;
                }
                else if (_chClock > 80f) fail($"ride {kiln}'s first try: hammer hits {enc.HammerHits - _chHammer0}, pour hits {enc.PourHits - _chPour0}, slams {_chSlams}, golem defeated {enc.GolemDefeated} after 80 s (S={s:0.0})");
                break;
            }
            case 12 when _chClock >= 0.1f:
            {
                var shape = Shape(rc);
                var pour = (MoltenPour)enc.SetPieceNode(Kiln(rc, "pour"))!;
                if (!pour.Quenched) { fail("the valve's hit did not quench the pour"); return; }
                if (rc.State.Mode != CartMode.Rolling) { fail("the cart is not mid-ride for the save"); return; }
                GD.Print($"[chase] 8 saved mid-ride {shape.LastRide} at S={rc.State.S:0.0} (stop {shape.LastRideStop} {rc.StopS[shape.LastRideStop]:0.0}, stop {shape.Rides} {rc.StopS[shape.Rides]:0.0}): golem defeated={enc.GolemDefeated} valve broken={pour.Quenched}");
                main.SaveGame(ChaseSlot);
                if (!main.LoadGame(ChaseSlot)) { fail("the save would not load"); return; }
                _chSwept = -1;
                _chStep++;
                _chClock = 0f;
                break;
            }
            case 13 when _chClock >= 1f:
            {
                var shape = Shape(rc);
                var pour = enc.SetPieceNode(Kiln(rc, "pour")) as MoltenPour;
                GD.Print($"[chase] 8 after the load: mode={rc.State.Mode} stop={rc.StopIndex} armed ride={enc.ArmedRide} golem defeated={enc.GolemDefeated} valve broken={pour?.Quenched} slag carts={enc.SlagCarts.Count} golem={(enc.GolemBody == null ? "none" : enc.GolemBody.Name)}");
                if (rc.State.Mode != CartMode.Held || rc.StopIndex != shape.LastRideStop || enc.ArmedRide >= 1) { fail($"the cart came back {rc.State.Mode} at stop {rc.StopIndex} (armed ride {enc.ArmedRide}), not held at stop {shape.LastRideStop}"); return; }
                if (!enc.GolemDefeated || pour is not { Quenched: true }) { fail("the load lost the golem's death or the broken valve"); return; }
                ChaseMend(main, root);
                // the hammers fall on walkers with no ride armed: a stunned cinder stood under the middle hammer must be hurt
                var mid = (enc.SetPieceNode(Kiln(rc, "hammers")) as Node3D)?.GetNodeOrNull<DropHammer>("Hammer1");
                if (mid == null) { fail($"ride {shape.LastRide} has no hammers"); return; }
                var at = rc.Party.Track.Point(mid.S) + Vector3.Up * 0.05f;
                _chProbe = main.Builder.SpawnEnemy("enemy.horde_cinder", at, main.Builder.CurrentFloor);
                if (_chProbe == null) { fail("could not spawn the cinder for the hammer"); return; }
                _chProbe.Effects.Apply(new Combat.StatusApplication(Combat.StatusKind.Stun, 30f, 0f));
                _chProbeHp = _chProbe.Hp;
                _chProbeBy = "";
                GD.Print($"[chase] 8 a stunned cinder {_chProbe.Name} stands under hammer {mid.Index} at S={mid.S:0.0} (hp {_chProbeHp:0}); the cart is held at stop {shape.LastRideStop}, armed ride {enc.ArmedRide}");
                _chStep++;
                _chClock = 0f;
                break;
            }
            case 14:
            {
                var p = _chProbe;
                var hurt = p == null || !IsInstanceValid(p) || !p.IsAlive || p.Hp < _chProbeHp - 0.01f;
                if (enc.ArmedRide >= 1) { fail($"ride {enc.ArmedRide} armed while the hammer probe waited"); return; }
                if (!hurt) { if (_chClock > 6f) fail($"no hammer fell on the cinder under it in 6 s with no ride armed (hp {p!.Hp:0})"); break; }
                GD.Print($"[chase] 8 the hammer fell on the cinder with no ride armed after {_chClock:0.0} s: alive={p != null && IsInstanceValid(p) && p.IsAlive} hp {_chProbeHp:0}->{(p != null && IsInstanceValid(p) ? p.Hp : 0f):0} ({(_chProbeBy == "" ? "no hazard kill" : $"killed by \"{_chProbeBy}\"")})");
                if (p != null && IsInstanceValid(p) && p.IsAlive) p.Kill();
                _chProbe = null;
                if (!ChaseOnePumperSetUp(main, root, rc, ctrl, fail)) return;
                _chStep++;
                _chClock = 0f;
                if (!RideBoard(main, layout, rc, out var why)) { fail(why); return; }
                break;
            }
            case 15:
            {
                var shape = Shape(rc);
                if (rc.State.Mode != CartMode.Rolling) { if (_chClock > 8f) fail($"re-boarded at stop {shape.LastRideStop} after the load, but the cart did not depart (mode {rc.State.Mode})"); break; }
                GD.Print($"[chase] 10 departed stop {shape.LastRideStop} for the one-pumper jump: train=[{string.Join(",", rc.Train.Select(w => w.Number))}], {party.Active.Count(a => a.Seated)} heroes seated, pumper cap {PumperCap}; cruise with one pumper and {rc.Train.Count} wagons {CartPhysics.TargetSpeed(1, rc.Train.Count):0.00} m/s, unpumped {CartPhysics.TargetSpeed(0, rc.Train.Count):0.00}, the jump wants {SetPieceRules.JumpSpeed:0.0}");
                if (rc.Train.Count != shape.Intermediate) { fail($"the one-pumper jump wants W1..W{shape.Intermediate} coupled, the train has {rc.Train.Count}"); return; }
                _chStep++;
                _chClock = 0f;
                break;
            }
            case 16 when _chClock >= 0.3f:
            {
                var kiln = Shape(rc).LastRide;
                var golems = _chSpawnRide.Count(kv => kv.Value == kiln && IsInstanceValid(kv.Key) && kv.Key.Profile.Id == "enemy.forge_golem"); // spawned since the load (the map starts again with the loaded floor)
                var pour = enc.SetPieceNode(Kiln(rc, "pour")) as MoltenPour;
                GD.Print($"[chase] 8 departed stop {Shape(rc).LastRideStop} after the load: armed ride {enc.ArmedRide}, golem={(enc.GolemBody == null ? "none" : enc.GolemBody.Name)}, forge golems spawned with the ride={golems}, valve broken={pour?.Quenched}");
                if (enc.ArmedRide != kiln || enc.GolemBody != null || golems > 0) { fail("a golem came again after it was killed and the game loaded"); return; }
                if (pour is not { Quenched: true }) { fail("the valve was mended by the departure"); return; }
                _chStep++;
                _chClock = 0f;
                break;
            }
            case 17:
            {
                var shape = Shape(rc);
                if (!ChaseHeldAt(rc, shape.Rides, fail)) break;
                var golems = _chSpawnRide.Count(kv => kv.Value == shape.LastRide && IsInstanceValid(kv.Key) && kv.Key.Profile.Id == "enemy.forge_golem");
                var buffer = enc.SetPieceNode(Kiln(rc, "buffer")) as BufferStop;
                var pour = enc.SetPieceNode(Kiln(rc, "pour")) as MoltenPour;
                PumperCap = null;
                GD.Print($"[chase] 8 ride {shape.LastRide} again, held at stop {shape.Rides}: no golem={enc.GolemBody == null && golems == 0}, pour hits={enc.PourHits} (valve broken={pour?.Quenched}), hammer hits={enc.HammerHits}, buffer smashed={buffer?.Smashed}");
                GD.Print($"[chase] 10 the one-pumper jump: made={_chJumped} at {_chJumpV:0.00} m/s (want >= {SetPieceRules.JumpSpeed:0.0}) with {_chPumpers} pumping and {rc.Train.Count} wagons coupled [{string.Join(",", rc.Train.Select(w => w.Number))}]");
                if (enc.GolemBody != null || golems > 0 || enc.PourHits > 0 || pour is not { Quenched: true }) { fail("after the load the golem came or the pour ran"); return; }
                if (!_chJumped || _chJumpV < SetPieceRules.JumpSpeed || _chPumpers != 1 || rc.Train.Count != shape.Intermediate) { fail($"the one-pumper jump: made={_chJumped} at {_chJumpV:0.00} m/s with {_chPumpers} pumping and {rc.Train.Count} wagons (want made, >= {SetPieceRules.JumpSpeed}, 1, {shape.Intermediate})"); return; }
                if (buffer is not { Smashed: true }) { fail("after the load the buffer went wrong"); return; }
                if (!ChaseTimings(fail)) return;
                GD.Print($"[chase] music: the pour {_chPourPitch ?? "never played"}; after it {_chAfterPour ?? "no track"}");
                if (_chPourPitch == null) { fail("the pour track never played in the Chase mood"); return; }
                if (_chAfterPour == null) { fail("no track played after the pour to check its pitch"); return; }
                GD.Print("[chase] RESULT ok");
                _done = true;
                GetTree().Quit();
                break;
            }
        }
    }

    /// <summary>
    /// 9. On the rebuilt floor, before the heap: ride 1 departs and its thrower stands as a full enemy (the floor's first); a save
    /// mid-ride 1 is loaded (the cart back at the landing), ride 1 departs again and the thrower given back is a reinforcement;
    /// the save is loaded once more so the heap's section starts from a cart held at the landing, as a fresh floor's.
    /// </summary>
    private void RunChaseThrowerSave(Main main, FloorLayout layout, Node3D root, RailController rc, RailEncounterController enc, CrewRescueController ctrl, Action<string> fail)
    {
        switch (_chThrowerPhase)
        {
            case 0 when _chClock >= 1.5f:
            {
                ChaseMend(main, root);
                _chThrowersBefore = _chThrowersSeen.Count;
                _chThrowerPhase++;
                _chClock = 0f;
                if (!RideBoard(main, layout, rc, out var why)) { fail(why); return; }
                break;
            }
            case 1:
            {
                if (rc.State.Mode != CartMode.Rolling || enc.ArmedRide != 1 || _chThrowersSeen.Count == _chThrowersBefore) { if (_chClock > 10f) fail($"ride 1 did not depart with its thrower on the rebuilt floor (mode {rc.State.Mode}, armed ride {enc.ArmedRide}, throwers seen {_chThrowersSeen.Count - _chThrowersBefore})"); break; }
                var first = _chThrowersSeen[^1];
                GD.Print($"[chase] 9 ride 1 departed on the rebuilt floor: its thrower {first.Thrower.Name} is the floor's first, reinforcement={first.Reinforcement}; saved mid-ride 1 at S={rc.State.S:0.0}");
                if (first.Reinforcement) { fail("the rebuilt floor's first thrower came as a reinforcement"); return; }
                main.SaveGame(ChaseSlot);
                if (!main.LoadGame(ChaseSlot)) { fail("the save mid-ride 1 would not load"); return; }
                _chSwept = -1;
                _chThrowerPhase++;
                _chClock = 0f;
                break;
            }
            case 2 when _chClock >= 1f:
            {
                GD.Print($"[chase] 9 after the load: mode={rc.State.Mode} stop={rc.StopIndex} armed ride={enc.ArmedRide}");
                if (rc.State.Mode != CartMode.Held || rc.StopIndex != 0 || enc.ArmedRide >= 1) { fail($"the save mid-ride 1 came back {rc.State.Mode} at stop {rc.StopIndex} (armed ride {enc.ArmedRide}), not held at the landing"); return; }
                ChaseMend(main, root);
                _chThrowersBefore = _chThrowersSeen.Count;
                _chThrowerPhase++;
                _chClock = 0f;
                if (!RideBoard(main, layout, rc, out var why)) { fail(why); return; }
                break;
            }
            case 3:
            {
                if (rc.State.Mode != CartMode.Rolling || enc.ArmedRide != 1 || _chThrowersSeen.Count == _chThrowersBefore) { if (_chClock > 10f) fail($"ride 1 did not depart with its thrower after the load (mode {rc.State.Mode}, armed ride {enc.ArmedRide}, throwers seen {_chThrowersSeen.Count - _chThrowersBefore})"); break; }
                var again = _chThrowersSeen[^1];
                GD.Print($"[chase] 9 ride 1 departed again after the load: the thrower given back, {again.Thrower.Name}, reinforcement={again.Reinforcement} (the save kept that it was sent)");
                if (!again.Reinforcement) { fail("the thrower given back after a save and a load is a full enemy again: the save lost that it was sent"); return; }
                if (!main.LoadGame(ChaseSlot)) { fail("the save mid-ride 1 would not load a second time"); return; }
                _chSwept = -1;
                _chThrowerPhase++;
                _chClock = 0f;
                break;
            }
            // 2b. ride 2 again, with a blade swapped into a rear seat: the cart restored at stop 1, cage 1 opened so its crew sit in the
            // train, and the same boarder checks as section 1's natural-seating pass
            case 4 when _chClock >= 1f:
            {
                ChaseMend(main, root);
                rc.RestoreGates(Enumerable.Range(1, Shape(rc).Intermediate)); // the test's shortcut: no guards to kill on this pass
                rc.Restore(1);
                if (!ctrl.OrcKeys && root.GetNodeOrNull<OrcKeyring>("OrcKeyring") is { } ring) ring.Interact(main.Party, main.Party.Local!);
                if (!ctrl.OrcKeys) { fail("the orc's keys could not be taken for the second ride-2 pass"); return; }
                if (!ChaseOpenCage(main, ctrl, 1, fail)) return;
                GD.Print($"[chase] 2 the second ride-2 pass: the cart restored at stop {rc.StopIndex} (mode {rc.State.Mode}), cage 1 open");
                _chThrowerPhase++;
                _chClock = 0f;
                break;
            }
            case 5 when _chClock >= 0.5f:
            {
                _chThrowerPhase++;
                _chClock = 0f;
                if (!RideBoard(main, layout, rc, out var why)) { fail(why); return; }
                break;
            }
            case 6:
            {
                if (!ChaseDepart(rc, enc, ctrl, 1, fail)) break;
                ChaseBeginBoarderPass(main.Party, rc, 2);
                if (!ChaseMeleeBehind(main.Party, rc, fail)) return;
                _chThrowerPhase++;
                _chClock = 0f;
                break;
            }
            case 7:
            {
                if (!ChaseTrackBoarders(main.Party, root, rc, fail)) return;
                if (!ChaseHeldAt(rc, 2, fail)) break;
                if (!ChaseWagonBoarderReport(fail)) return;
                if (!main.LoadGame(ChaseSlot)) { fail("the save mid-ride 1 would not load a third time"); return; }
                _chSwept = -1;
                _chThrowerPhase++;
                _chClock = 0f; // section 2's first step, on the cart held at the landing (as a fresh floor's)
                break;
            }
        }
    }

    /// <summary>
    /// 10. Before the kiln's ride is ridden again: the orc's keys taken, every cage opened (its crew seated in the train), W1..W4 coupled
    /// from stop 0, and the pumpers capped at one: the jump must still be made with one pumper and every wagon (CartRules.MaxLoadWagons
    /// counts three of them at most).
    /// </summary>
    private bool ChaseOnePumperSetUp(Main main, Node3D root, RailController rc, CrewRescueController ctrl, Action<string> fail)
    {
        var party = main.Party;
        if (!ctrl.OrcKeys && root.GetNodeOrNull<OrcKeyring>("OrcKeyring") is { } ring) ring.Interact(party, party.Local!);
        if (!ctrl.OrcKeys) { fail("the orc's keys could not be taken for the one-pumper jump"); return false; }
        var shape = Shape(rc);
        for (var stop = 1; stop <= shape.Intermediate; stop++)
            if (!ctrl.Cages.First(w => w.Stop == stop).Open && !ChaseOpenCage(main, ctrl, stop, fail, hook: true)) return false; // far from the kiln's stop: seated at once, no walk
        PumperCap = 1;
        GD.Print($"[chase] 10 the one-pumper jump set up at stop {rc.StopIndex}: cages open [{string.Join(",", ctrl.Cages.Where(w => w.Open).Select(w => w.Stop))}], train=[{string.Join(",", rc.Train.Select(w => w.Number))}] (W1..W{rc.Wagons.Count} coupled from stop 0), pumpers capped at {PumperCap}");
        return true;
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
            RailCalls.Called += OnChaseCall;
            Action<string, Vector3, bool> playedAt = (cue, _, ambient) => { if (!ambient) OnChaseSfx(cue); }; // the warnings' cues are positional now (RideAudio.At)
            Audio.AudioManager.PlayedAt += playedAt;
            _staticUnhooks.Add(() => { RailCalls.Called -= OnChaseCall; Audio.AudioManager.PlayedAt -= playedAt; });
            bus.EnemySpawned += e =>
            {
                if (_chEnc != null && IsInstanceValid(_chEnc)) _chSpawnRide[e] = _chEnc.ArmedRide;
                if (e.Profile.Id == "enemy.forge_golem" && RailController.Instance is { } line) _chGolemSpawnS = line.State.S; // the party S the golem's burst prints
            };
            bus.EnemyKilledByHazard += (e, source) =>
            {
                if (e == _chLone) { _chLoneByCart = true; _chLoneSource = source; }
                if (_chBoarders.ContainsKey(e)) _chKiller[e] = $"hazard {source}";
                if (e == _chProbe) _chProbeBy = source;
            };
            // who killed a boarder: the party hit or the crew blow in the frame it died (the hurt event may come just after the death), else a tick
            bus.EnemyDied += e =>
            {
                if (e == _chLone && !_chLoneByCart) _chLoneOther = true;
                if (!_chBoarders.ContainsKey(e) || _chKiller.ContainsKey(e)) return;
                var now = Engine.GetPhysicsFrames();
                if (_chLastHit.TryGetValue(e, out var hit) && hit.Striker >= 0 && hit.Frame == now) { _chKiller[e] = ChaseStriker(hit.Striker); return; }
                _chDiedNow = e;
                _chDiedFrame = now;
                _chKiller[e] = _chLastHit.TryGetValue(e, out var last) && last.Striker >= 0 ? $"a tick or a stray (last party hit by {ChaseStriker(last.Striker)})" : "a tick or a stray (no party hit)";
            };
            bus.EnemyHurt += (e, _, _, striker) =>
            {
                if (!_chBoarders.ContainsKey(e)) return;
                var now = Engine.GetPhysicsFrames();
                _chLastHit[e] = (striker, now);
                if (_chBoardedAt.ContainsKey(e) && !_chFirstMateHit.ContainsKey(e) && striker >= 0 && striker < Main.Instance.Party.Adventurers.Length
                    && Main.Instance.Party.Adventurers[striker] is { } mate && IsInstanceValid(mate) && mate.Control == ControlKind.Companion)
                {
                    _chFirstMateHit[e] = (ChaseStriker(striker), now);
                    GD.Print($"[chase] 2 {mate.State.Name} (seat {mate.Seat}, pumping={mate.Pumping}) strikes the wagon boarder {e.Name} {ChaseSeconds(now - _chBoardedAt[e]):0.00} s after it boarded, from {mate.GlobalPosition.DistanceTo(e.GlobalPosition):0.00} m (hp {e.Hp:0}/{e.MaxHp:0})");
                }
                if (striker >= 0 && e == _chDiedNow && _chDiedFrame == now) { _chKiller[e] = ChaseStriker(striker); _chDiedNow = null; }
            };
            bus.NpcEvent += (id, kind) =>
            {
                if (kind != NpcEventKind.Downed || _chBoarderPass == 0 || _chEnc == null || !IsInstanceValid(_chEnc) || _chEnc.ArmedRide != 2 || Main.Instance.Builder.FloorRoot is not { } floor) return;
                var biter = floor.GetChildren().OfType<Enemies.Enemy>().FirstOrDefault(h => h.IsAlive && h.Carrier is TrainWagon && h.BoardTarget is { } bitten && IsInstanceValid(bitten) && bitten.Id == id);
                var npc = floor.GetChildren().OfType<Npc>().FirstOrDefault(n => n.Id == id);
                if (npc is not { Crew: true }) return;
                (biter != null ? _chCrewBitten : _chCrewDownOther).Add(npc.NpcName);
                GD.Print($"[chase] 2 crew {npc.NpcName} goes down on ride 2{(biter != null ? $" to the wagon boarder {biter.Name}'s bite" : "")}");
            };
            bus.NpcKill += npc => { if (_chDiedNow != null && _chDiedFrame == Engine.GetPhysicsFrames()) { _chKiller[_chDiedNow] = $"crew {npc.Name}"; _chDiedNow = null; } };
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
            case CartEventKind.Departed:
                _chJumped = false; _chPumpers = -1; _chJumpV = 0f; _chOnSpur = _chDiverted = _chBuffer = false; _chAcross = _chBack = _chBackThrown = false;
                _chWarn.Remove("barricade"); // a call pending from a ride before (cut, never hit) must not pair with this ride's smash
                if (!_chSection2) _chSeatSpur = 0;
                break;
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
        if (text == "The cart crashes into the ore heap!") ChaseHit("heap", t, "the cart into the heap");
        else if (text == "The cart smashes through the barricade!") ChaseHit("barricade", t, $"the cart through the timber at S={(RailController.Instance?.State.S ?? 0f):0.0} on ride {_chEnc?.ArmedRide ?? -1}");
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

    /// <summary>The warnings are stamped as the heroes call them (said or only logged): the bell while the traffic runs, the heap, the jump, the barricade.</summary>
    private void OnChaseCall(Adventure.BarkTrigger trigger, string? by)
    {
        if (_done || !_testChase) return;
        var t = Engine.GetPhysicsFrames();
        switch (trigger)
        {
            case Adventure.BarkTrigger.CartComing: if (_chTrafficOn) _chWarn.TryAdd("bell", t); break;
            case Adventure.BarkTrigger.LineBlocked: _chWarn["heap"] = t; break;
            case Adventure.BarkTrigger.JumpCall: _chWarn["jump"] = t; break;
            case Adventure.BarkTrigger.Barricade: _chWarn["barricade"] = t; break;
        }
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
    /// Each frame: the music's front player is at the pitch it should be (the pour's in the Chase mood on the kiln's ride, else 1: the two players
    /// are reused, so a later track must not keep the pour's). Notes the pour and the first other track after it.
    /// </summary>
    private void ChaseMusicPoll()
    {
        if (Audio.MusicDirector.Instance is not { } music || music.CurrentTrack == "") return;
        var pour = music.Mood == Audio.Mood.Chase && music.CurrentTrack == RideMusic.PourTrack;
        if (Mathf.Abs(music.FrontPitch - music.ExpectedPitch) > 1e-5f)
        {
            _chFail ??= $"the music's {music.CurrentTrack} ({music.Mood}) plays at pitch {music.FrontPitch:0.00000}, want {music.ExpectedPitch:0.00000}";
            return;
        }
        if (pour) _chPourPitch ??= $"at pitch {music.FrontPitch:0.00000} (want {RideMusic.PourPitch:0.00000})";
        else if (_chPourPitch != null) _chAfterPour ??= $"{music.CurrentTrack} ({music.Mood}) at pitch {music.FrontPitch:0.00000}";
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
        if (RailController.Instance is { } rc && _chEnc.SetPieceNode(Kiln(rc, "pour")) is MoltenPour pour)
        {
            if (pour.Phase == PourPhase.Dry) _chPourOpen = false;
            else if (pour.Phase == PourPhase.Warn && !_chPourOpen) { _chPourOpen = true; _chWarn["pour"] = t; }
        }
    }

    private static IEnumerable<DropHammer> ChaseHammers(RailEncounterController enc) =>
        RailController.Instance is { } rc && enc.SetPieceNode(Kiln(rc, "hammers")) is Node3D holder ? holder.GetChildren().OfType<DropHammer>() : Enumerable.Empty<DropHammer>();

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
        if (hazard is "bell" or "heap" or "jump" or "trestle" or "barricade") _chWarn.Remove(hazard); // one warning, one hit
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

    /// <summary>Per frame: the slag carts that came and pulled alongside, the lane blend of the cart, the golem on the spur, section 1's seated presses on the spur lever.</summary>
    private void ChaseTrackRides(RailController rc, RailEncounterController enc, PartyController party)
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
            // across with its return open, the live switch back: a hit on it is a miss that leaves it straight (only a human's E moves it), then
            // the seated lead throws it as its window offers it, through the dispatch E uses
            if (!_chBackThrown && rc.Crossing && float.IsNaN(rc.CrossBackS) && enc.SetPieceNode("r3_switch_back") is TrackSwitch { Deadly: true, Thrown: false, Taken: false } back
                && party.Local is { } lead && lead.Cart == rc.Cart && party.LookingAt == back)
            {
                var hit = back.ReceiveAttack(lead.State.Stats, new Combat.AttackProfile { MinDamage = 1, MaxDamage = 2 }, new Combat.HitContext(), party.Rng, lead.State.Name);
                var afterHit = (back.Thrown, back.LastToggledBy);
                var prompt = back.Prompt;
                party.VrUse(true);
                party.VrUse(false);
                _chBackThrown = true;
                GD.Print($"[chase] 3 ride 3's live switch_back offered from the seat (\"{prompt}\", {back.LeverS - rc.Party.FrontS:0.0} m before the lever): a hit on it (hit={hit.Hit}) leaves it thrown={afterHit.Thrown} (by {afterHit.LastToggledBy ?? "nobody"}); the seated lead's press throws it={back.Thrown} by {back.LastToggledBy}, still seated={lead.Cart == rc.Cart}");
                if (hit.Hit || afterHit.Thrown || afterHit.LastToggledBy != null) _chFail ??= "a hit on ride 3's live switch_back moved it";
                else if (prompt != "Throw the switch" || !back.Thrown || back.LastToggledBy != lead.State.Name || lead.Cart != rc.Cart) _chFail ??= "the seated press did not throw ride 3's live switch_back, or got the lead out";
            }
        }
        if (Shape(rc).IsLastRide(enc.ArmedRide) && !_chOnSpur && enc.SetPieceNode(Kiln(rc, "spur")) is SpurSwitch spur && enc.GolemRun is { } run && run.Track == spur.Sampler)
        {
            _chOnSpur = true;
            GD.Print($"[chase] 4 the golem's runner takes the spur (party S={rc.State.S:0.0})");
        }
        // section 1, the kiln's ride: the spur lever (thrown ahead on ride 2) offered from the seat is set back (a mash at once after it is ignored by
        // the re-press guard), then thrown again once the guard's half second is out
        if (!_chSection2 && _chSeatSpur < 2 && _chSeatSpurFail == null && Shape(rc).IsLastRide(enc.ArmedRide) && rc.State.Mode == CartMode.Rolling
            && (_chSeatSpur == 0 || Time.GetTicksMsec() / 1000.0 - _chSeatSpurAt >= SeatedSwitchRules.RepressGuardSeconds)
            && enc.SetPieceNode(Kiln(rc, "spur")) is SpurSwitch seatSpur && party.Local is { } me && me.Cart == rc.Cart && party.LookingAt == seatSpur.Lever)
        {
            var lever = seatSpur.Lever;
            var prompt = lever.Prompt;
            var want = _chSeatSpur == 0 ? "Set the switch back" : "Throw the switch";
            var dist = me.GlobalPosition.DistanceTo(lever.GlobalPosition);
            var ahead = lever.LeverS - rc.Party.FrontS;
            if (prompt != want) { _chSeatSpurFail = $"the seated window offered ride {Shape(rc).LastRide}'s spur lever as \"{prompt}\" at press {_chSeatSpur + 1}, not \"{want}\""; return; }
            party.VrUse(true);
            party.VrUse(false);
            _chSeatSpur++;
            var seated = me.Cart == rc.Cart;
            GD.Print($"[chase] 4 seated window: \"{prompt}\" pressed {ahead:0.0} m before the spur lever, {dist:0.0} m off: thrown={lever.Thrown}, still seated={seated}");
            if (lever.Thrown != (_chSeatSpur == 2) || !seated)
            {
                _chSeatSpurFail = $"the seated press \"{prompt}\" on ride {Shape(rc).LastRide}'s spur lever left it thrown={lever.Thrown}, still seated={seated}";
                return;
            }
            if (_chSeatSpur == 1)
            {
                _chSeatSpurAt = Time.GetTicksMsec() / 1000.0;
                party.VrUse(true); // a mash right after the set-back: the re-press guard ignores it
                party.VrUse(false);
                GD.Print($"[chase] 4 seated mash: a second press at once after the set-back: thrown={lever.Thrown} (want False: ignored), still seated={me.Cart == rc.Cart}");
                if (lever.Thrown || me.Cart != rc.Cart) _chSeatSpurFail = $"a mashed second press right after the seated set-back left ride {Shape(rc).LastRide}'s spur lever thrown={lever.Thrown}, still seated={me.Cart == rc.Cart}";
            }
        }
    }

    /// <summary>The cart rolled in and is held at <paramref name="stop"/> (not halted on the line). False while it rolls.</summary>
    private bool ChaseHeldAt(RailController rc, int stop, Action<string> fail)
    {
        if (rc.State.Mode == CartMode.Rolling) { if (_chClock > 70f) fail($"the cart was not held at stop {stop} within 70 s (S={rc.State.S:0.0})"); return false; }
        if (rc.State.Mode != CartMode.Held || rc.StopIndex != stop || rc.Halted) { fail($"the cart is {rc.State.Mode} at stop {rc.StopIndex} (halted {rc.Halted}), not held at stop {stop}"); return false; }
        if (rc.State.Plating < CartRules.MaxPlating - 0.01f) { fail($"the cart arrived at stop {stop} with plating {rc.State.Plating:0}: a stop mends it to {CartRules.MaxPlating:0}"); return false; }
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

    /// <summary>Where each cage's crew must sit (the first empty seats at the cage's opening), by stop: the departure checks they sat there.</summary>
    private readonly Dictionary<int, List<(Npc Npc, int Wagon, int Seat)>> _chSeated = new();

    /// <summary>
    /// Cage <paramref name="stop"/> opens with the orc's keys, and its living crew must take the first empty seats of the train in
    /// <see cref="TrainRules.SeatOrder"/>: by walking there (the cart held at the cage's stop; the departure checks they sat in before it),
    /// or, with <paramref name="hook"/> (the cage far from where the cart stands), seated at once by the test's hook (DebugSeatCrew).
    /// </summary>
    private bool ChaseOpenCage(Main main, CrewRescueController ctrl, int stop, Action<string> fail, bool hook = false)
    {
        var cage = ctrl.Cages.First(w => w.Stop == stop);
        if (RailController.Instance is not { } rc) { fail("no rail controller"); return false; }
        var crew = ctrl.Crew.Where(c => IsInstanceValid(c) && !c.Dead && c.Ride == cage).ToList();
        var want = TrainRules.SeatOrder(rc.Wagons.Count).Where(p => rc.WagonAt(p.Wagon) is { Coupled: true } w && w.Seats.OccupantOf(p.Seat) == null).Take(crew.Count).ToList();
        if (hook)
        {
            var seated = ctrl.DebugSeatCrew(stop);
            if (seated == null) { fail($"stop {stop}'s crew could not be seated"); return false; }
            _chSeated[stop] = seated;
            var got = seated.Select(c => (c.Wagon, c.Seat)).ToList();
            GD.Print($"[chase] . cage {stop} opened by the test's hook: its crew sat at once in [{string.Join(", ", seated.Select(c => $"{c.Npc.NpcName} W{c.Wagon} seat {c.Seat}"))}] (want [{string.Join(", ", want.Select(p => $"W{p.Wagon} seat {p.Seat}"))}])");
            if (got.Count != crew.Count || !got.SequenceEqual(want) || seated.Any(c => c.Npc.Cart != rc.WagonAt(c.Wagon) || c.Npc.Seat != c.Seat)) { fail($"stop {stop}'s {crew.Count} crew did not sit in the first empty seats"); return false; }
            return true;
        }
        var prompt = cage.Prompt;
        cage.Interact(main.Party, main.Party.Local!);
        GD.Print($"[chase] . cage {stop} by \"{prompt}\": open={cage.Open}; its crew [{string.Join(", ", crew.Select(c => c.NpcName))}] run to [{string.Join(", ", want.Select(p => $"W{p.Wagon} seat {p.Seat}"))}]");
        if (!cage.Open) { fail($"cage {stop} did not open by \"{prompt}\""); return false; }
        if (want.Count != crew.Count) { fail($"the train has {want.Count} empty seat(s) for stop {stop}'s {crew.Count} crew"); return false; }
        _chSeated[stop] = crew.Select((c, i) => (c, want[i].Wagon, want[i].Seat)).ToList();
        return true;
    }

    /// <summary>The cart has left stop <paramref name="stop"/> with W1..Wn coupled, the stop's crew sat in the first empty seats before it.</summary>
    private bool ChaseDepart(RailController rc, RailEncounterController enc, CrewRescueController ctrl, int stop, Action<string> fail)
    {
        if (rc.State.Mode != CartMode.Rolling) { if (_chClock > 12f) fail($"the cart did not leave stop {stop} (mode {rc.State.Mode}, boarding {rc.Boarding.Running}, elapsed {rc.Boarding.Elapsed:0.0} s)"); return false; }
        var train = string.Join(",", rc.Train.Select(w => w.Number));
        var seated = _chSeated.TryGetValue(stop, out var list) ? list : new List<(Npc Npc, int Wagon, int Seat)>();
        var off = seated.Where(c => IsInstanceValid(c.Npc) && !c.Npc.Dead && (c.Npc.Cart != rc.WagonAt(c.Wagon) || c.Npc.Seat != c.Seat)).Select(c => $"{c.Npc.NpcName} ({(c.Npc.Seated ? $"in {c.Npc.Ride?.Name} seat {c.Npc.Seat}" : "on foot")})").ToList();
        GD.Print($"[chase] . departed stop {stop} after {rc.Boarding.Elapsed:0.0} s of countdown: ride {enc.ArmedRide} armed, train=[{train}], stop {stop}'s crew sit in the first empty seats before the departure [{string.Join(", ", seated.Select(c => $"{c.Npc.NpcName} W{c.Wagon} seat {c.Seat}{(c.Npc.Dead ? " (dead)" : "")}"))}], {Main.Instance.Party.Active.Count(a => a.Seated)} heroes seated");
        if (train != string.Join(",", Enumerable.Range(1, rc.Wagons.Count)) || rc.Wagons.Count == 0) { fail($"the train left stop {stop} as [{train}], not W1..W{rc.Wagons.Count}"); return false; }
        if (off.Count > 0) { fail($"stop {stop}'s crew [{string.Join(", ", off)}] were not in the first empty seats at the departure"); return false; }
        return true;
    }

    /// <summary>
    /// A low beam's measure over the train (plan 14): every seated head in it (the cart's riders and the wagons' crew, posed sitting)
    /// against the beam's underside as it passes under (in the beam's own frame, within a quarter metre of its plane). A hero's head is its
    /// seat's eye (<see cref="Adventurer.SeatedEyeHeight"/>) and <see cref="LowBeam.HeadAboveEye"/>, an NPC's its origin and <see cref="Npc.HeadHeight"/>.
    /// </summary>
    private sealed class BeamHeads
    {
        public float Min = float.MaxValue;
        public int Heads, Crew, Grisel;
        public bool Done;

        /// <summary>Sample this frame's heads under <paramref name="beam"/>; true once the train's rear is past it (the measure is complete).</summary>
        public bool Sample(LowBeam beam, RailController rc)
        {
            var inv = beam.GlobalTransform.AffineInverse();
            void Measure(ICartBody body)
            {
                foreach (var r in body.Riders.Values)
                {
                    if (r is not Node3D n || !IsInstanceValid(n) || r.Cart != body) continue;
                    var local = inv * (r is Adventurer ? body.SeatTransform(r.Seat).Origin : n.GlobalPosition);
                    if (Mathf.Abs(local.Z) > 0.25f) continue;
                    var head = r is Npc npc ? npc.HeadHeight : Adventurer.SeatedEyeHeight + LowBeam.HeadAboveEye;
                    Min = Mathf.Min(Min, LowBeam.Underside - (local.Y + head));
                    Heads++;
                    if (r is Npc { Crew: true }) Crew++;
                    if (r is Npc { Id: CrewRescueController.GriselId }) Grisel++;
                }
            }
            Measure(rc.Cart);
            foreach (var wagon in rc.Train) if (IsInstanceValid(wagon)) Measure(wagon);
            return RunawayRules.TrainRearS(rc.Party.BackS, rc.Train.Count, RailController.WagonSpacing) >= beam.S + 0.5f;
        }
    }

    /// <summary>
    /// Ride 1 (plan 14): r1_beam measured over every seated head in the train (<see cref="BeamHeads"/>). Once the train's rear is past, the
    /// least clearance is printed and must be 0.05 m or more, with a crew head among the samples. True when it failed.
    /// </summary>
    private bool ChaseBeamHeads(RailController rc, RailEncounterController enc, Action<string> fail)
    {
        if (_chHeads.Done || enc.ArmedRide != 1) return false;
        if (enc.SetPieceNode("r1_beam") is not LowBeam beam) { fail("floor 12 lacks r1_beam"); return true; }
        if (!_chHeads.Sample(beam, rc)) return false;
        _chHeads.Done = true;
        GD.Print($"[chase] 1 beam: r1_beam clears every seated head in the train by {_chHeads.Min:0.00} m ({_chHeads.Heads} sample(s) under it at S={beam.S:0.0}, {_chHeads.Crew} of them crew sitting in the wagons; the train W1..W{rc.Train.Count}, {rc.Train.Sum(w => w.Riders.Count)} rider(s) in the wagons)");
        if (_chHeads.Heads == 0 || _chHeads.Crew == 0) { fail($"r1_beam passed over {_chHeads.Heads} seated head(s), {_chHeads.Crew} of crew: the train's crew were not measured"); return true; }
        if (_chHeads.Min < 0.05f) { fail($"r1_beam clears a seated head by {_chHeads.Min:0.00} m, under 0.05"); return true; }
        return false;
    }

    /// <summary>The kiln golem's plan (its ride and At) on this floor, or null.</summary>
    private static RailEncounterPlan? ChaseGolemPlan(Main main) => main.Builder.Contents?.RailEncounters.FirstOrDefault(e => e.Kind == "golem");

    /// <summary>Where on the party line the golem's At falls: StopS[ride − 1] + At × the ride's length; NaN without a golem.</summary>
    private static float ChaseGolemS(Main main, RailController rc) =>
        ChaseGolemPlan(main) is { } g && g.Ride >= 1 && g.Ride < rc.StopS.Length ? rc.StopS[g.Ride - 1] + g.At * (rc.StopS[g.Ride] - rc.StopS[g.Ride - 1]) : float.NaN;

    /// <summary>
    /// The kiln's golem is out on the enemy line (waited for until the cart is 10 m past its At, or 8 s without a planned golem): alive,
    /// carried, an elite and no boss. False while it waits or once it failed.
    /// </summary>
    private bool ChaseGolemOut(Main main, RailController rc, RailEncounterController enc, Action<string> fail)
    {
        var g = enc.GolemBody;
        var at = ChaseGolemS(main, rc);
        if (g == null && rc.State.Mode == CartMode.Rolling && (float.IsNaN(at) ? _chClock < 8f : rc.State.S < at + 10f)) return false; // the golem bursts out as the cart reaches its At: wait for it
        GD.Print($"[chase] 4 ride {Shape(rc).LastRide} armed: golem {(g == null ? "none" : $"{g.Name} \"{g.DisplayName}\" ({g.Profile.Id}) alive={g.IsAlive} carried={g.Carried} on the runner={enc.GolemRun != null} elite={g.IsElite} affixes=[{string.Join(",", g.Profile.Affixes ?? Array.Empty<Enemies.EliteAffix>())}] boss={g.IsBoss} hp {g.Hp:0}/{g.MaxHp:0}")}");
        if (g is { IsAlive: true, IsBoss: false, IsElite: true, Carried: true } && enc.GolemRun != null) return true;
        fail("the golem did not come out on the enemy line as a carried elite (never a boss)");
        return false;
    }

    /// <summary>The golem burst out with the party's cart at its At, never before: the party S at its spawn is at least the At's S less 1 m.</summary>
    private bool ChaseGolemAtItsAt(Main main, RailController rc, Action<string> fail)
    {
        var at = ChaseGolemS(main, rc);
        GD.Print($"[chase] 4 the golem burst out with the party at S={_chGolemSpawnS:0.0}; its At {ChaseGolemPlan(main)?.At:0.00} of ride {ChaseGolemPlan(main)?.Ride} falls at S={at:0.0}");
        if (float.IsNaN(at) || float.IsNaN(_chGolemSpawnS)) { fail("no golem in the floor's plan, or its spawn was not seen"); return false; }
        if (_chGolemSpawnS >= at - 1f) return true;
        fail($"the golem burst out with the party at S={_chGolemSpawnS:0.0}, short of its At's S={at:0.0} less 1 m");
        return false;
    }

    /// <summary>
    /// The party to <see cref="_chPartyLevel"/>, once: each member gets the XP to each next level, puts its stat points two into its
    /// class's best primary to one into Vitality, learns what its tree allows in tree order (branch, then tier), and is restored.
    /// No gear is added, so a levelled party here is still weaker than one that played floors 1–11.
    /// </summary>
    private void ChaseLevelParty(Main main)
    {
        if (_chLevelled) return;
        _chLevelled = true;
        var party = main.Party;
        var skills = Game.Instance.Skills;
        for (var i = 0; i < party.Members.Length; i++)
        {
            var m = party.Members[i];
            int lv0 = m.Level;
            var hp0 = m.Stats[Characters.Stat.MaxHp];
            while (m.Level < _chPartyLevel && m.Level < Characters.Leveling.MaxLevel)
                if (Characters.Leveling.AddXp(m, Characters.Leveling.XpToNext(m.Level) - m.Xp) == 0) break;
            var best = Characters.StatInfo.Primaries.Where(p => p != Characters.Stat.Vitality).OrderByDescending(p => m.Class.BasePrimaries.GetValueOrDefault(p)).First();
            for (var n = 0; m.StatPoints > 0; n++)
                if (!m.AllocateStat(n % 3 == 2 ? Characters.Stat.Vitality : best)) break;
            var learned = new List<string>();
            for (var more = true; more;)
            {
                more = false;
                foreach (var sk in skills.ForClass(m.Class.Id).ToList())
                    if (Characters.SkillRules.WhyCannotLearn(m, sk) == null && Characters.SkillRules.Learn(m, sk, skills)) { learned.Add(sk.Name); more = true; break; }
            }
            m.FullRestore();
            EventBus.Instance.RaiseMemberChanged(i);
            GD.Print($"[chase] 0 {m.Name} ({m.Class.Name}) levelled {lv0} -> {m.Level}: max hp {hp0:0} -> {m.Stats[Characters.Stat.MaxHp]:0}, +{best} and Vitality, {learned.Count} skills [{string.Join(", ", learned)}], {m.SkillPoints} skill points left");
        }
    }

    /// <summary>A ride-2 boarder pass starts (the cart has just left stop 1): the boarder books are cleared and the seating printed.</summary>
    private void ChaseBeginBoarderPass(PartyController party, RailController rc, int pass)
    {
        _chBoarderPass = pass;
        _chBoarders.Clear();
        _chBoardedAt.Clear();
        _chFirstMateHit.Clear();
        _chNeedsAnswer.Clear();
        _chAnswerSaid.Clear();
        _chKiller.Clear();
        _chLastHit.Clear();
        _chDiedNow = null;
        _chCrewBitten.Clear();
        _chCrewDownOther.Clear();
        _chCrewedBoards = 0;
        GD.Print($"[chase] 2 boarder pass {pass} ({(pass == 1 ? "natural seating" : "a blade swapped behind")}): {string.Join(", ", party.Adventurers.Where(a => IsInstanceValid(a) && a.Cart == rc.Cart).OrderBy(a => a.Seat).Select(a => $"{a.State.Name} ({a.State.Class.Name}{(a.Control == ControlKind.Companion ? "" : $", {a.Control}")}) seat {a.Seat}"))}");
    }

    /// <summary>
    /// Per frame on ride 2: hounds that board (a wagon boarder's reach from each seat printed). One that boarded a crewed wagon with no hero up
    /// (<see cref="BoarderRules.NeedsAnswer"/>) and is still aboard and unstruck by a companion <see cref="ChaseBoarderAnswer"/> + 3 s on fails;
    /// the first companion strike on it prints the answer: the answerer, its moves, and who struck first.
    /// </summary>
    private bool ChaseTrackBoarders(PartyController party, Node3D root, RailController rc, Action<string> fail)
    {
        foreach (var e in root.GetChildren().OfType<Enemies.Enemy>())
            if (e.IsAlive && e.Profile.Id == RailEncounterController.HoundId && e.Carrier is MineCart or TrainWagon && !_chBoarders.ContainsKey(e))
            {
                _chBoarders[e] = e.Carrier!.Name;
                var prisoners = e.Carrier is TrainWagon boarded ? boarded.Riders.Values.Count(r => r is Npc { Crew: true, Dead: false }) : 0;
                var most = rc.Train.Where(w => IsInstanceValid(w)).Select(w => w.Riders.Values.Count(r => r is Npc { Crew: true, Dead: false })).DefaultIfEmpty(0).Max();
                if (prisoners > 0) _chCrewedBoards++;
                GD.Print($"[chase] 2 {e.Name} boards {e.Carrier.Name} at S={rc.State.S:0.0} (hp {e.Hp:0}/{e.MaxHp:0}){(e.Carrier is TrainWagon ? $", {prisoners} prisoner(s) in it (the most in a wagon {most}), biting {e.BoardTarget?.NpcName ?? "nobody"}" : "")}");
                if (e.Carrier is TrainWagon w)
                {
                    _chBoardedAt[e] = Engine.GetPhysicsFrames();
                    var up = w.Riders.Values.Count(r => r is Adventurer { Downed: false, Parked: false });
                    if (BoarderRules.NeedsAnswer(true, prisoners, up)) _chNeedsAnswer[e] = w.Number;
                    GD.Print($"[chase] 2 {e.Name} on W{w.Number}: {prisoners} crew, {up} hero(es) up in it: {(_chNeedsAnswer.ContainsKey(e) ? "it needs an answer" : "no answer needed")}");
                    ChaseReachReport(party, rc, e);
                }
            }
        var enc = RailEncounterController.Instance;
        foreach (var (e, wagon) in _chNeedsAnswer)
        {
            var at = _chBoardedAt[e];
            if (_chFirstMateHit.TryGetValue(e, out var first) && _chAnswerSaid.Add(e))
            {
                var answer = enc?.AnswersStarted.LastOrDefault(a => a.Wagon == wagon) ?? default;
                GD.Print($"[chase] 2 {(IsInstanceValid(e) ? e.Name : "a hound")} on W{wagon} answered {ChaseSeconds(first.Frame - at):0.00} s after it boarded (bound {ChaseBoarderAnswer + 3f:0} s): "
                    + (answer.Name != null ? $"the answerer {answer.Name} from car {answer.From}, moves [{string.Join(" > ", answer.Route)}]" : "no answerer sent")
                    + $"; the first strike by {first.Who}");
                if (ChaseSeconds(first.Frame - at) > ChaseBoarderAnswer + 3f) { fail($"{e.Name} on W{wagon} was first struck by a companion {ChaseSeconds(first.Frame - at):0.00} s after it boarded, past {ChaseBoarderAnswer + 3f:0} s"); return false; }
            }
            if (!IsInstanceValid(e) || !e.IsAlive || e.Carrier is not TrainWagon || _chFirstMateHit.ContainsKey(e) || ChaseSeconds(Engine.GetPhysicsFrames() - at) <= ChaseBoarderAnswer + 3f) continue;
            ChaseReachReport(party, rc, e);
            var sent = enc?.AnswersStarted.LastOrDefault(a => a.Wagon == wagon) ?? default;
            fail($"{e.Name} boarded {_chBoarders[e]} (W{wagon}, crewed, no hero up) on ride 2 (boarder pass {_chBoarderPass}) and no companion struck it within {ChaseBoarderAnswer + 3f:0} s ({(sent.Name != null ? $"{sent.Name} sent from car {sent.From}, moves [{string.Join(" > ", sent.Route)}]" : "nobody sent")})");
            return false;
        }
        return true;
    }

    /// <summary>At stop 2: every boarder with who first struck it and who killed it; a hound must have boarded a wagon, and no crewman gone down to a wagon boarder's bite.</summary>
    private bool ChaseWagonBoarderReport(Action<string> fail)
    {
        foreach (var (hound, carrier) in _chBoarders)
        {
            var fate = _chKiller.TryGetValue(hound, out var killer) ? $"killed by {killer}" : "alive at the stop (freed with the ride)";
            var answer = !_chBoardedAt.TryGetValue(hound, out var at) ? ""
                : _chFirstMateHit.TryGetValue(hound, out var first) ? $"; first struck by a companion, {first.Who}, {ChaseSeconds(first.Frame - at):0.00} s after it boarded" : "; never struck by a companion";
            GD.Print($"[chase] 2   boarder {(IsInstanceValid(hound) ? hound.Name : "(freed)")} on {carrier}: {fate}{answer}");
        }
        var pass = _chBoarderPass;
        GD.Print($"[chase] 2 boarder pass {pass}: wagon boarders on ride 2: {_chBoardedAt.Count}, {_chNeedsAnswer.Count} needing an answer, {_chAnswerSaid.Count} of them answered; struck by a companion within {ChaseBoarderAnswer + 3f:0} s: {_chFirstMateHit.Count(kv => _chBoardedAt.TryGetValue(kv.Key, out var b) && ChaseSeconds(kv.Value.Frame - b) <= ChaseBoarderAnswer + 3f)}; crew down to a wagon boarder's bite: {_chCrewBitten.Count} [{string.Join(", ", _chCrewBitten)}], to anything else: {_chCrewDownOther.Count} [{string.Join(", ", _chCrewDownOther)}]");
        _chBoarderPass = 0;
        if (_chCrewBitten.Count > 0) { fail($"crew went down to a wagon boarder on ride 2 (boarder pass {pass}): {string.Join(", ", _chCrewBitten)}"); return false; }
        if (_chBoardedAt.Count == 0) { fail($"no hound boarded a wagon on ride 2 (boarder pass {pass}): the companions' answer to a wagon boarder is unproven"); return false; }
        GD.Print($"[chase] 2 boarder pass {pass}: {_chCrewedBoards} hound(s) boarded a crewed wagon (a boarder picks the wagon with the most crew first)");
        if (_chCrewedBoards == 0) { fail($"no hound boarded a crewed wagon on ride 2 (boarder pass {pass}), with crew seated in the train"); return false; }
        return true;
    }

    /// <summary>
    /// Ride 2: a melee companion sits in one of the cart's rear seats (swapped with the ranged one there, by the seat book and
    /// SitIn as boarding does), so a hound on the wagon roof behind meets a seated blade, not only a bow.
    /// </summary>
    private static bool ChaseMeleeBehind(PartyController party, RailController rc, Action<string> fail)
    {
        static bool Melee(Adventurer a) => a.State.Class.Id is "class.warrior" or "class.rogue";
        var mates = party.Adventurers.Where(a => IsInstanceValid(a) && a.Cart == rc.Cart && a.Control == ControlKind.Companion && !a.Downed).ToList();
        string Seats() => string.Join(", ", party.Adventurers.Where(a => IsInstanceValid(a) && a.Cart == rc.Cart).OrderBy(a => a.Seat).Select(a => $"{a.State.Name} ({a.State.Class.Name}) seat {a.Seat}"));
        if (mates.Any(a => a.Seat >= 2 && Melee(a))) { GD.Print($"[chase] 2 a melee companion already sits behind: {Seats()}"); return true; }
        var front = mates.FirstOrDefault(a => a.Seat < 2 && Melee(a));
        var back = mates.FirstOrDefault(a => a.Seat is 2 or 3 && !Melee(a));
        if (front == null || back == null) { fail($"no melee companion to seat behind for ride 2: {Seats()}"); return false; }
        int to = back.Seat, from = front.Seat;
        rc.Cart.Seats.Leave(front.RiderId);
        rc.Cart.Seats.Leave(back.RiderId);
        if (rc.Cart.Seats.Take(back.RiderId, true) != from || rc.Cart.Seats.Take(front.RiderId, true) != to) { fail($"the seat swap for ride 2 went wrong: {Seats()}"); return false; }
        back.SitIn(rc.Cart, from);
        front.SitIn(rc.Cart, to);
        GD.Print($"[chase] 2 {front.State.Name} swaps seats with {back.State.Name} for ride 2, so a blade sits behind: {Seats()}");
        return true;
    }

    /// <summary>
    /// The measured reach to a wagon boarder (plan 14, Ruling 13): each seated hero's car and seat, its body-to-body distance (flat and up) to the
    /// hound, and whether its blade reaches it (its own car's boarder, within BoarderReach).
    /// </summary>
    private static void ChaseReachReport(PartyController party, RailController rc, Enemies.Enemy hound)
    {
        var parts = new List<string>();
        var car = rc.CarCarrying(hound.Carrier);
        foreach (var a in party.Adventurers)
        {
            if (!IsInstanceValid(a) || a.Parked || !a.Seated) continue;
            var d = hound.GlobalPosition - a.GlobalPosition;
            var own = rc.CarOf(a) == car;
            parts.Add($"{a.State.Name} car {rc.CarOf(a)} seat {a.Seat}{(a.Pumping ? " pumping" : "")}{(a.Control == ControlKind.Companion ? "" : $" {a.Control}")}: {d.Length():0.00} m (flat {new Vector2(d.X, d.Z).Length():0.00}, up {d.Y:0.00}), {(own ? d.Length() <= Characters.CompanionBrain.BoarderReach ? "its own car's, in a blade's reach" : "its own car's, out of reach" : "another car's: no blade")}, target {a.Target?.Name ?? "-"}");
        }
        GD.Print($"[chase] 2 reach to {hound.Name} on {hound.Carrier?.Name} (car {car}; BoarderReach {Characters.CompanionBrain.BoarderReach:0.00}): {string.Join("; ", parts)}");
    }

    private static string ChaseStriker(int index)
    {
        var party = Main.Instance.Party;
        if (index < 0 || index >= party.Members.Length) return $"party index {index}";
        var who = party.Members[index];
        var body = index < party.Adventurers.Length ? party.Adventurers[index] : null;
        return $"{who.Name} ({who.Class.Name}{(body != null && IsInstanceValid(body) ? $", {body.Control}, seated={body.Seated}" : "")})";
    }

    /// <summary>Stop 2: the companions who went down on ride 2, and every boarder with who killed it. At the default level or higher, more than one down fails.</summary>
    private bool ChaseBoarderReport(Main main, RailController rc, Action<string> fail)
    {
        var party = main.Party;
        var downed = _chDownedRide2.Select(i => party.Members[i].Name).ToList();
        var crewDown = main.Builder.FloorRoot?.GetChildren().OfType<Npc>().Where(n => n.Crew && (n.Downed || n.Dead)).Select(n => n.Name.ToString()).ToList() ?? new List<string>();
        GD.Print($"[chase] 2 ride 2 at lethality {TierRules.Lethality:0.#}, party level {_chPartyLevel}, lead idle: {_chBoarders.Count} boarders, companions down by stop 2: {downed.Count} [{string.Join(", ", downed)}], crew down: {crewDown.Count} [{string.Join(", ", crewDown)}]; hp [{string.Join(" ", party.Active.Select(a => $"{a.State.Name}:{a.State.Hp:0}/{a.State.Stats[Characters.Stat.MaxHp]:0}{(a.Downed ? " DOWN" : "")}"))}]; plating {rc.State.Plating:0}");
        if (!ChaseWagonBoarderReport(fail)) return false;
        if (downed.Count <= 1) return true;
        if (_chPartyLevel < ChaseDefaultLevel) { GD.Print($"[chase] 2 (more than one down is only asserted at party level {ChaseDefaultLevel} or higher)"); return true; }
        fail($"{downed.Count} companions were down by stop 2 ({string.Join(", ", downed)}) at party level {_chPartyLevel}: at most one may be");
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
