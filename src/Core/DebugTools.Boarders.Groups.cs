using DungeonCrawler.Dungeon.Rails;
using Godot;

namespace DungeonCrawler.Core;

// ---- --test-boarders=groups (plan 14, Task 6): see DebugTools.Boarders.cs for the flow as a whole ----------------------------------------
// Floor 12 solo at lethality 3, ridden from the landing to the last stop for real (RideOnTo) with nobody pumping (PumperCap 0) and every enemy
// culled but the three boarding groups' hounds (r1_hounds, r3_hounds, r5_hounds, Ruling 18). Stops 1-3's crew sit in W1..W3 from the landing
// (the test's hook); stop 4's cage stays shut and its hold is waived. Companions answer as in play. A group's hound cannot die before its leap
// (Enemy.DebugHoldHp while it rides its slag cart: the gate is the leap, not the roll-up's crossfire); once it has leapt it is the game's. On
// ride 3 both levers are thrown at the arming (the heap blocks the party lane; the live switch back left straight runs the train to the gap).
// The pumps come back (the game's own two) once the cart is 1 m past r5_golem's S, for the trestle and the jump. Held at the last stop, it prints
// one line per group hound:
//   [boarders] group {id}: spawned at party S={s}, in reach at S={r}, alongside at S={a}, {hound} leapt at S={l} onto {W{k}|the cart|a miss} (...), its cart freed at S={f} ({why}; ...)
// and fails unless every hound leapt, each landing on a body within TrainRules.LeapReach (8.6 m) (a miss: its length printed only, its
// take-off within the reach of the nearest coupled car's body, the bound the crouch rule sets) and each r5_hounds hound landing 1.5 s or more before the
// golem's burst (fix round 1); r1_hounds' leap came with the cart 5 m past r1_barricade or more and the front at r1_beam or short of it;
// r3_hounds' cart was freed by Ruling 19 (its hounds gone, or the crossing) with the front at r3_switch_out's points - 2 m or short of it;
// r5_hounds' cart was freed by Ruling 19 before the front reached r5_golem's S, or by the golem's burst, and never stood within 3 m of the
// golem's runner; and every throw on the floor came after its wagon's call and glow (RailEncounterController.ThrowLog). Then the crossing pass
// (fix round 1): restored at stop 2, ride 3 with every hound held aboard (RailEncounterController.DebugHoldCrouches), r3_hounds' cart must be freed
// "the train crosses onto its lane" with the front within CrossFreeAhead of r3_switch_out's points.
public partial class DebugTools
{
    /// <summary>The boarding groups =groups measures (Ruling 18), in ride order.</summary>
    private static readonly string[] BdGroups = { "r1_hounds", "r3_hounds", "r5_hounds" };
    private readonly HashSet<Enemies.Enemy> _bdGroupHounds = new();
    private readonly Dictionary<string, float> _bdAlongside = new();
    private readonly Dictionary<string, SlagCart> _bdGroupCarts = new();
    private readonly Dictionary<string, (float S, float Front)> _bdGoneOtherwise = new();
    private float _bdGolemNearest = float.PositiveInfinity;
    private bool _bdLeversThrown, _bdPumpsBack;
    // the crossing pass (fix round 1, (e))
    private int _bdXPass, _bdXStep, _bdXMark;
    private float _bdXClock;
    /// <summary>A hound lands at least this long before the golem's burst (fix round 1, (d)).</summary>
    private const float BdGolemMargin = 1.5f;
    // a miss's take-off (Task 8 fix round 1): each hound riding a slag cart with its flat distance to the nearest coupled car's body (the
    // party's cart or a coupled wagon), read every frame, so the frame before a miss starts gives its take-off distance
    private readonly Dictionary<string, float> _leapNearest = new();
    private readonly Dictionary<(string Who, double T), float> _missTakeOffs = new();
    private RailEncounterController? _leapLogOf;
    private int _leapLogMark;

    /// <summary>
    /// Every frame of =groups and --test-chase: each miss newly in <see cref="RailEncounterController.SlagLog"/> takes its hound's nearest-body
    /// distance from the frame before (the hound still crouched on its cart; NaN when it was not seen there), then every carried hound's
    /// distance is read again. A miss lands beside the party lane's cell nearest where that body will be 0.6 s on, so its own length is no
    /// bound; its take-off is what the crouch rule bounds (a body it would pick within <see cref="TrainRules.LeapReach"/>).
    /// </summary>
    private void WatchMissTakeOffs(Node3D root, RailController rc, RailEncounterController enc)
    {
        if (_leapLogOf != enc) { _leapLogOf = enc; _leapLogMark = 0; _leapNearest.Clear(); }
        for (; _leapLogMark < enc.SlagLog.Count; _leapLogMark++)
        {
            var x = enc.SlagLog[_leapLogMark];
            if (x.What == "leapt a miss") _missTakeOffs[(x.Who, x.T)] = _leapNearest.TryGetValue(x.Who, out var d) ? d : float.NaN;
        }
        _leapNearest.Clear();
        foreach (var e in root.GetChildren().OfType<Enemies.Enemy>())
        {
            if (!e.IsAlive || e.Carrier is not SlagCart) continue;
            var p = e.GlobalPosition;
            float Flat(Vector3 b) => new Vector2(p.X - b.X, p.Z - b.Z).Length();
            var best = Flat(rc.Cart.GlobalPosition);
            foreach (var w in rc.Train) best = Mathf.Min(best, Flat(w.GlobalPosition));
            _leapNearest[e.Name] = best;
        }
    }

    /// <summary>The hounds a group's slag cart carries are spared the cull (their spawn and the cull run in the same frame, the spare first).</summary>
    private void BdSpareGroups(RailEncounterController enc)
    {
        foreach (var cart in enc.SlagCarts)
        {
            if (!IsInstanceValid(cart) || Array.IndexOf(BdGroups, cart.EncounterId) < 0) continue;
            foreach (var rider in cart.Aboard)
                if (IsInstanceValid(rider) && rider.Profile.Id == RailEncounterController.HoundId) _bdGroupHounds.Add(rider);
        }
    }

    /// <summary>Where on the party line the plan item <paramref name="id"/>'s At falls (an encounter's or a set piece's), or NaN.</summary>
    private static float BdPlanS(Main main, RailController rc, string id)
    {
        var contents = main.Builder.Contents;
        (int Ride, float At)? item = contents?.RailEncounters.FirstOrDefault(e => e.Id == id) is { } e ? (e.Ride, e.At)
            : contents?.RailSetPieces.FirstOrDefault(p => p.Id == id) is { } p ? (p.Ride, p.At) : null;
        if (item is not { } it || it.Ride < 1 || it.Ride >= rc.StopS.Length) return float.NaN;
        return rc.StopS[it.Ride - 1] + it.At * (rc.StopS[it.Ride] - rc.StopS[it.Ride - 1]);
    }

    private void BoardersGroups(Main main, RailController rc, RailEncounterController enc, Action<string> Fail)
    {
        if (_bdXPass > 0) { BoardersGroupsCrossing(main, rc, enc, Fail); return; }
        var rolling = rc.State.Mode == CartMode.Rolling;
        // ride 3: both levers thrown as it arms (the cart crosses at r3_switch_out's points and comes home at r3_switch_back's)
        if (!_bdLeversThrown && enc.ArmedRide == 3 && rolling)
        {
            if (enc.SetPieceNode("r3_switch_out") is not TrackSwitch outSw || enc.SetPieceNode("r3_switch_back") is not TrackSwitch backSw) { Fail("floor 12 lacks ride 3's switches"); return; }
            outSw.DebugThrow();
            backSw.DebugThrow();
            _bdLeversThrown = true;
            GD.Print($"[boarders] groups: ride 3 armed at S={rc.State.S:0.0}: r3_switch_out thrown={outSw.Thrown}, r3_switch_back thrown={backSw.Thrown} (the test's hook)");
        }
        // each group's cart: first alongside its pace point, gone other than by Ruling 19, and its nearest to the golem's runner
        foreach (var cart in enc.SlagCarts)
        {
            if (!IsInstanceValid(cart) || cart.Derailed || Array.IndexOf(BdGroups, cart.EncounterId) < 0) continue;
            var id = cart.EncounterId;
            _bdGroupCarts[id] = cart;
            if (!_bdAlongside.ContainsKey(id) && PaceRules.Alongside(cart.Run.State.S, PaceRules.TargetS(enc.Lanes.EnemyS(rc.State.S), cart.Offset)))
            {
                _bdAlongside[id] = rc.State.S;
                GD.Print($"[boarders] groups: {id} alongside at party S={rc.State.S:0.0} (front {rc.Party.FrontS:0.0}; its S {cart.Run.State.S:0.0} on the enemy line)");
            }
            if (enc.GolemBody is { } golem) _bdGolemNearest = Mathf.Min(_bdGolemNearest, cart.GlobalPosition.DistanceTo(golem.GlobalPosition));
        }
        foreach (var (id, cart) in _bdGroupCarts)
        {
            if (IsInstanceValid(cart) && !cart.Derailed && enc.SlagCarts.Contains(cart)) continue;
            if (_bdGoneOtherwise.ContainsKey(id) || enc.SlagLog.Any(x => x.Encounter == id && x.What.StartsWith("freed: ", StringComparison.Ordinal))) continue;
            _bdGoneOtherwise[id] = (rc.State.S, rc.Party.FrontS);
            GD.Print($"[boarders] groups: {id}'s cart went other than by Ruling 19 at party S={rc.State.S:0.0} (front {rc.Party.FrontS:0.0}; derailed={IsInstanceValid(cart) && cart.Derailed})");
        }
        // a group hound lives until it leaps (it is the game's from its leap on)
        foreach (var h in _bdGroupHounds)
            if (IsInstanceValid(h)) h.DebugHoldHp = h.IsAlive && h.Carrier is SlagCart;
        var golemS = BdPlanS(main, rc, "r5_golem");
        if (!_bdPumpsBack && enc.ArmedRide == 5 && rc.State.S >= golemS + 1f)
        {
            _bdPumpsBack = true;
            PumperCap = null; // the trestle and the jump want the game's own pumping
            GD.Print($"[boarders] groups: the cart 1 m past r5_golem's S ({golemS:0.0}) at S={rc.State.S:0.0}: the companions may pump again (the trestle and the jump)");
        }

        if (!RideOnTo(main, rc, Shape(rc).Rides, "boarders", Fail)) return;
        PumperCap = null;
        GD.Print($"[boarders] groups: held at stop {rc.StopIndex}, the end of the line (S={rc.State.S:0.0}); train=[{string.Join(",", rc.Train.Select(w => w.Number))}], throws {enc.Throws}");
        foreach (var x in enc.SlagLog) GD.Print($"[boarders] groups: log {x.Encounter} {x.What} ({x.Who}) at party S={x.PartyS:0.0}, front {x.FrontS:0.0}, {x.Metres:0.0} (t={x.T:0.00})");

        var fails = BoardingLeapChecks(enc, "boarders");
        float barricadeS = BdPlanS(main, rc, "r1_barricade"), beamS = BdPlanS(main, rc, "r1_beam"), pointsS = BdPlanS(main, rc, "r3_switch_out");
        foreach (var id in BdGroups)
        {
            var count = main.Builder.Contents?.RailEncounters.FirstOrDefault(e => e.Id == id)?.Riders.Sum(m => m.Count) ?? 0;
            var spawn = enc.SlagLog.FirstOrDefault(x => x.Encounter == id && x.What == "spawned");
            var leaps = enc.SlagLog.Where(x => x.Encounter == id && x.What.StartsWith("leapt ", StringComparison.Ordinal)).ToList();
            var freed = enc.SlagLog.FirstOrDefault(x => x.Encounter == id && x.What.StartsWith("freed: ", StringComparison.Ordinal));
            var why = freed.What?["freed: ".Length..];
            var alongside = _bdAlongside.TryGetValue(id, out var a) ? $"{a:0.0}" : "never";
            var reach = enc.SlagLog.FirstOrDefault(x => x.Encounter == id && x.What == "in reach");
            var inReach = reach.Encounter != null ? $"{reach.PartyS:0.0} ({reach.Who}, {reach.Metres:0.0} m from a car's body)" : "never";
            var freeText = why != null ? $"its cart freed at S={freed.PartyS:0.0} ({why}; front {freed.FrontS:0.0}{(why == "the golem bursts out" ? $", {freed.Metres:0.0} m from the golem's runner" : "")})"
                : _bdGoneOtherwise.TryGetValue(id, out var gone) ? $"its cart not freed by Ruling 19: gone otherwise at S={gone.S:0.0} (front {gone.Front:0.0})" : "its cart never freed";
            if (spawn.Encounter == null) { fails.Add($"{id} never spawned"); GD.Print($"[boarders] group {id}: never spawned"); continue; }
            foreach (var l in leaps)
                GD.Print($"[boarders] group {id}: spawned at party S={spawn.PartyS:0.0}, in reach at S={inReach}, alongside at S={alongside}, {l.Who} leapt at S={l.PartyS:0.0} onto {l.What["leapt ".Length..]} (a {l.Metres:0.0} m leap; front {l.FrontS:0.0}), {freeText}");
            if (leaps.Count == 0) GD.Print($"[boarders] group {id}: spawned at party S={spawn.PartyS:0.0}, in reach at S={inReach}, alongside at S={alongside}, no hound leapt, {freeText}");
            if (leaps.Count != count) fails.Add($"{id}: {leaps.Count} of its {count} hound(s) leapt");
            if (id == "r1_hounds")
                foreach (var l in leaps)
                    if (l.PartyS < barricadeS + 5f || l.FrontS > beamS) fails.Add($"r1_hounds' {l.Who} leapt at S={l.PartyS:0.0} (front {l.FrontS:0.0}), not between r1_barricade + 5 m ({barricadeS + 5f:0.0}) and r1_beam ({beamS:0.0})");
            if (id == "r3_hounds" && !(why is "its hounds are gone" or "the train crosses onto its lane" && freed.FrontS <= pointsS - 2f))
                fails.Add($"r3_hounds' cart was not freed by Ruling 19 with the front at r3_switch_out's points - 2 m ({pointsS - 2f:0.0}) or short of it ({freeText})");
            if (id == "r5_hounds")
            {
                var byGolem = why == "the golem bursts out";
                if (!(byGolem || why != null && freed.FrontS < golemS)) fails.Add($"r5_hounds' cart was not freed before the front reached r5_golem's S ({golemS:0.0}) nor by the golem's burst ({freeText})");
                var nearest = Mathf.Min(_bdGolemNearest, byGolem ? freed.Metres : float.PositiveInfinity);
                GD.Print($"[boarders] group r5_hounds: nearest to the golem's runner {(float.IsPositiveInfinity(nearest) ? "never beside it" : $"{nearest:0.0} m")}");
                if (nearest < 3f) fails.Add($"r5_hounds' cart stood {nearest:0.0} m from the golem's runner (3 m at least)");
            }
        }
        // every throw after its wagon's call and glow (the call opens the stretch; each glow after the throw before it)
        var throws = 0;
        foreach (var k in enc.ThrowLog.Select(x => x.Wagon).Distinct().OrderBy(k => k))
        {
            var log = enc.ThrowLog.Where(x => x.Wagon == k).ToList();
            GD.Print($"[boarders] groups: W{k}'s throw log [{string.Join(", ", log.Select(x => $"{x.Step} at {x.Counted:0.00} s ({x.Who})"))}]");
            bool called = false, glowed = false;
            foreach (var x in log)
            {
                if (x.Step == ThrowStep.Call) { called = true; glowed = false; }
                else if (x.Step == ThrowStep.Glow) glowed = called;
                else if (x.Step == ThrowStep.Throw)
                {
                    throws++;
                    if (!called || !glowed) fails.Add($"W{k}'s throw of {x.Who} at {x.Counted:0.00} s came without its call and glow before it");
                    glowed = false;
                }
            }
        }
        if (fails.Count > 0) { Fail(string.Join("; ", fails)); return; }
        _bdMainOk = $"every group's hounds leapt, each landing on a body within {TrainRules.LeapReach:0.0} m (a miss's take-off within it); r1_hounds between r1_barricade + 5 m and r1_beam; r3_hounds' cart freed short of r3_switch_out's points - 2 m; r5_hounds landed {BdGolemMargin:0.0} s or more before the golem's burst and its cart was freed short of the golem or by its burst, 3 m clear of its runner; {throws} throw(s), each after its call and glow";
        GD.Print($"[boarders] groups: the main pass ok: {_bdMainOk}");
        _bdXPass = 1; // the crossing pass (fix round 1, (e))
        _bdXStep = 0;
        _bdXClock = 0f;
    }

    private string _bdMainOk = "";

    /// <summary>
    /// The crossing pass (Task 6 fix round 1, (e)): restored at stop 2, ride 3 ridden unpumped with both its levers thrown and every hound held
    /// aboard its slag cart (<see cref="RailEncounterController.DebugHoldCrouches"/>), so r3_hounds' cart, never emptied, reaches the crossing:
    /// it must be freed "the train crosses onto its lane" with the front within <see cref="SetPieceRules.CrossFreeAhead"/> of r3_switch_out's points.
    /// </summary>
    private void BoardersGroupsCrossing(Main main, RailController rc, RailEncounterController enc, Action<string> Fail)
    {
        _bdXClock += (float)GetProcessDeltaTime();
        var pointsS = BdPlanS(main, rc, "r3_switch_out");
        void Fail2(string why) { enc.DebugHoldCrouches = false; Fail("crossing pass: " + why); }
        switch (_bdXStep)
        {
            case 0:
                rc.Restore(2);
                enc.DebugHoldCrouches = true;
                PumperCap = 0;
                _bdLeversThrown = false;
                _bdXMark = enc.SlagLog.Count;
                GD.Print($"[boarders] groups: crossing pass: restored at stop {rc.StopIndex} (S={rc.State.S:0.0}); every hound held aboard its slag cart (the test's hook); nobody pumping");
                _bdXStep++; _bdXClock = 0f;
                break;
            case 1 when _bdXClock >= 1f:
                RideWaiveCages(main, rc, "boarders");
                if (main.Party.Local is { } me && me.Cart != rc.Cart && !RideBoard(main, main.Builder.Layout!, rc, out var why)) { Fail2(why); return; }
                _bdXStep++; _bdXClock = 0f;
                break;
            case 2:
                RideWaiveCages(main, rc, "boarders");
                if (rc.State.Mode != CartMode.Rolling || enc.ArmedRide != 3) { if (_bdXClock > 20f) Fail2($"ride 3 did not depart (mode {rc.State.Mode}, armed {enc.ArmedRide})"); break; }
                if (enc.SetPieceNode("r3_switch_out") is not TrackSwitch outSw || enc.SetPieceNode("r3_switch_back") is not TrackSwitch backSw) { Fail2("floor 12 lacks ride 3's switches"); return; }
                outSw.DebugThrow();
                backSw.DebugThrow();
                GD.Print($"[boarders] groups: crossing pass: ride 3 departed at S={rc.State.S:0.0}; r3_switch_out thrown={outSw.Thrown}, r3_switch_back thrown={backSw.Thrown}");
                _bdXStep++; _bdXClock = 0f;
                break;
            case 3:
            {
                for (var i = _bdXMark; i < enc.SlagLog.Count; i++)
                    if (enc.SlagLog[i] is { Encounter: "r3_hounds" } x && x.What.StartsWith("freed: ", StringComparison.Ordinal))
                    {
                        var freedWhy = x.What["freed: ".Length..];
                        var leaps = enc.SlagLog.Skip(_bdXMark).Count(y => y.Encounter == "r3_hounds" && y.What.StartsWith("leapt ", StringComparison.Ordinal));
                        GD.Print($"[boarders] groups: crossing pass: r3_hounds' cart freed: {freedWhy} at party S={x.PartyS:0.0}, front {x.FrontS:0.0} (r3_switch_out's points {pointsS:0.0}, the window from {pointsS - SetPieceRules.CrossFreeAhead:0.0}); {leaps} leap(s)");
                        if (freedWhy != "the train crosses onto its lane") { Fail2($"r3_hounds' cart was freed by \"{freedWhy}\", not the crossing"); return; }
                        if (x.FrontS < pointsS - SetPieceRules.CrossFreeAhead || x.FrontS >= pointsS) { Fail2($"the crossing freed r3_hounds' cart with the front at {x.FrontS:0.0}, outside [{pointsS - SetPieceRules.CrossFreeAhead:0.0}, {pointsS:0.0})"); return; }
                        if (leaps != 0) { Fail2("a hound leapt while held"); return; }
                        enc.DebugHoldCrouches = false;
                        PumperCap = null;
                        GD.Print($"[boarders] RESULT ok ({_bdVariant}): {_bdMainOk}; the crossing pass: r3_hounds' cart freed as the train crosses onto its lane, the front at {x.FrontS:0.0}, within {SetPieceRules.CrossFreeAhead:0} m of r3_switch_out's points ({pointsS:0.0})");
                        _done = true;
                        GetTree().Quit(0);
                        return;
                    }
                if (rc.Party.FrontS > pointsS + 5f || _bdXClock > 40f) Fail2($"r3_hounds' cart was not freed by the crossing (front {rc.Party.FrontS:0.0}, its cart live: {enc.SlagCarts.Any(c => c.EncounterId == "r3_hounds")})");
                break;
            }
        }
    }

    /// <summary>
    /// The floor build's leaps (<see cref="RailEncounterController.SlagLog"/>): each printed with its length, which must be within
    /// <see cref="TrainRules.LeapReach"/> for a landing on a body (fix round 1, (a)); a miss's length is printed only, and its take-off
    /// distance to the nearest coupled body (<see cref="WatchMissTakeOffs"/>) must be within it (Task 8 fix round 1); and with the golem out, each r5_hounds hound landing (its leap's start plus
    /// <see cref="RailEncounterController.LeapSeconds"/>) at least <see cref="BdGolemMargin"/> before the golem's burst ((d)). The failures, or none.
    /// </summary>
    private List<string> BoardingLeapChecks(RailEncounterController enc, string tag)
    {
        var fails = new List<string>();
        var burst = enc.SlagLog.FirstOrDefault(x => x.What == "bursts out");
        foreach (var l in enc.SlagLog.Where(x => x.What.StartsWith("leapt ", StringComparison.Ordinal)))
        {
            var margin = burst.What != null && l.Encounter == "r5_hounds" && l.T <= burst.T ? burst.T - (l.T + RailEncounterController.LeapSeconds) : double.NaN;
            // a landing on a body is gated at the reach; a miss lands beside the party lane's cell nearest where the body will be 0.6 s on,
            // so its length is printed only, and its take-off (the nearest body, the frame before it leapt) is gated at the reach, which is
            // what the crouch rule bounds (Task 8 fix round 1, the controller's ruling)
            if (l.What == "leapt a miss")
            {
                var takeOff = _missTakeOffs.TryGetValue((l.Who, l.T), out var d) ? d : float.NaN;
                GD.Print($"[{tag}] leap: {l.Encounter}'s {l.Who} {l.What} at party S={l.PartyS:0.0} (t={l.T:0.00}): {l.Metres:0.0} m (a miss: printed only); its take-off {(float.IsNaN(takeOff) ? "not seen" : $"{takeOff:0.0} m")} from the nearest body (reach {TrainRules.LeapReach:0.0}){(double.IsNaN(margin) ? "" : $"; lands {margin:0.00} s before the golem's burst (t={burst.T:0.00}, party S={burst.PartyS:0.0})")}");
                if (float.IsNaN(takeOff)) fails.Add($"{l.Encounter}'s {l.Who} missed with its take-off unmeasured (never seen riding its slag cart)");
                else if (takeOff > TrainRules.LeapReach) fails.Add($"{l.Encounter}'s {l.Who} missed from {takeOff:0.0} m off the nearest body, past {TrainRules.LeapReach:0.0}");
            }
            else
            {
                GD.Print($"[{tag}] leap: {l.Encounter}'s {l.Who} {l.What} at party S={l.PartyS:0.0} (t={l.T:0.00}): {l.Metres:0.0} m (reach {TrainRules.LeapReach:0.0}){(double.IsNaN(margin) ? "" : $"; lands {margin:0.00} s before the golem's burst (t={burst.T:0.00}, party S={burst.PartyS:0.0})")}");
                if (l.Metres > TrainRules.LeapReach) fails.Add($"{l.Encounter}'s {l.Who} leapt {l.Metres:0.0} m, past {TrainRules.LeapReach:0.0}");
            }
            if (!double.IsNaN(margin) && margin < BdGolemMargin) fails.Add($"r5_hounds' {l.Who} landed {margin:0.00} s before the golem's burst, under {BdGolemMargin:0.0} s");
        }
        if (burst.What != null && enc.SlagLog.Any(x => x.Encounter == "r5_hounds" && x.What == "spawned" && x.T <= burst.T))
        {
            var r5 = enc.SlagLog.Count(x => x.Encounter == "r5_hounds" && x.What.StartsWith("leapt ", StringComparison.Ordinal) && x.T <= burst.T);
            GD.Print($"[{tag}] leap: r5_hounds: {r5} hound(s) leapt before the golem's burst");
            if (r5 < 2) fails.Add($"only {r5} of r5_hounds' 2 hounds leapt before the golem's burst");
        }
        return fails;
    }
}
