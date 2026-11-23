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
// and fails unless every hound leapt; r1_hounds' leap came with the cart 5 m past r1_barricade or more and the front at r1_beam or short of it;
// r3_hounds' cart was freed by Ruling 19 (its hounds gone, or the crossing) with the front at r3_switch_out's points - 2 m or short of it;
// r5_hounds' cart was freed by Ruling 19 before the front reached r5_golem's S, or by the golem's burst, and never stood within 3 m of the
// golem's runner; and every throw on the floor came after its wagon's call and glow (RailEncounterController.ThrowLog).
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
        foreach (var x in enc.SlagLog) GD.Print($"[boarders] groups: log {x.Encounter} {x.What} ({x.Who}) at party S={x.PartyS:0.0}, front {x.FrontS:0.0}, {x.Metres:0.0}");

        var fails = new List<string>();
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
        GD.Print($"[boarders] RESULT ok ({_bdVariant}): every group's hounds leapt; r1_hounds between r1_barricade + 5 m and r1_beam; r3_hounds' cart freed short of r3_switch_out's points - 2 m; r5_hounds' cart freed short of the golem or by its burst, 3 m clear of its runner; {throws} throw(s), each after its call and glow");
        _done = true;
        GetTree().Quit(0);
    }
}
