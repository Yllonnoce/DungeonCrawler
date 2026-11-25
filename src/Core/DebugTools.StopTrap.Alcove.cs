using DungeonCrawler.Adventure;
using DungeonCrawler.Dungeon;
using DungeonCrawler.Dungeon.Generation;
using DungeonCrawler.Dungeon.Rails;
using DungeonCrawler.Party;
using Godot;

namespace DungeonCrawler.Core;

// ---- --test-stop-trap=pockets and =alcove (plan 16 Task 8) -----------------------------------------------------------------------
// =pockets: the measuring step (Ruling 17). Every pocket's ride, lane, S, lit or dark, and its distances to the nearest stop, hard span and
// soft span, then each ride's clear and placed counts; RESULT ok only if the counts equal AlcoveRules.ShippedCounts, their total reaches
// MinTotal and some ride reaches MinBestRide.
// =alcove: the shooters on the rides that have them (rides 1-3 on the shipped floor: ShippedCounts), ridden for real with RideOnTo. rc.Restore(0);
// the lead boards with a companion in the cart and one in W1 (both Aggressive: they fire at will), and the fourth, on foot, claims ride 1's first
// planned pocket (DebugClaimPocket) before the departure. Ride 3's two levers are thrown as it arms (the test's hook, as --test-boarders=groups).
// Rides 4 and 5 (none placed) are read from their arming lines: departed from stop 3, then rc.Restore(4) and departed from stop 4. It fails unless:
//  placement: each armed ride's placed line matches ShippedCounts (ride 1: a fresh Place with the claim, one fewer at most), the claimed pocket
//    never taken, every S 30 m or more from every hard span and stop and 12 m from every soft span; the floor's total >= 12, some ride >= 4;
//  hidden: no hit on a shooter before it steps out; every shooter a reinforcement;
//  pinned: within 0.3 m of its back while hidden, of its mouth while shooting, of the line between them while stepping out;
//  waking: each steps out with the front 35 +- 2 m off;
//  firing: only within 25 m, at a seated hero, a wind-up every 1.6 +- 0.25 s while it has a target (one that waited for a target, or lost a
//    wind-up, starts no earlier than 1.35 s after the last);
//  lead aim pays: a bolt hits a seated hero side-on (its path more than 45 degrees off the track) at least once;
//  no crew: every bolt's target a hero of the party;
//  gone: each that lived is gone with the train's back 30 +- 2 m past it, and none is alive after an Arrived;
//  refuge: on ride 2 the fourth companion, put on the enemy lane beside a shooter's pocket before a ride slag cart comes (a traffic cart never
//    runs while a ride is armed), seeks refuge and never in a shooter's pocket.
public partial class DebugTools
{
    private void RunStopTrapPockets(RailController rc)
    {
        if (_stClock < 1.5f) return;
        if (RailEncounterController.Instance is not { } enc || !IsInstanceValid(enc)) { StopTrapFail("floor 12 has no RailEncounterController"); return; }
        if (rc.StopS.Length != AlcoveRules.RideCount + 1) { StopTrapFail($"floor 12's line has {rc.StopS.Length} stops, not {AlcoveRules.RideCount + 1}"); return; }
        if (!enc.AlcovesOn) { StopTrapFail("the alcove shooters are off (Settings.Ambushes, NoAmbush or the RIDE pass)"); return; }
        var pockets = enc.AlcovePockets();
        var hard = new List<(float, float)>();
        var soft = new List<(float, float)>();
        var hardNames = new List<string>();
        var softNames = new List<string>();
        var counts = new Dictionary<int, int>();
        var none = new HashSet<int>();
        for (var ride = 1; ride <= AlcoveRules.RideCount; ride++)
        {
            float from = rc.StopS[ride - 1], to = rc.StopS[ride];
            enc.ShooterStructures(ride, hard, soft, hardNames, softNames);
            var clear = 0;
            foreach (var p in pockets.Where(p => p.S > from && p.S < to).OrderBy(p => p.S))
            {
                var stop = Mathf.Min(p.S - from, to - p.S);
                var (hi, hd) = AlcoveRules.Nearest(p.S, hard);
                var (si, sd) = AlcoveRules.Nearest(p.S, soft);
                if (AlcoveRules.Clear(p, from, to, hard, soft, none)) clear++;
                GD.Print($"[alcove] pocket {p.Index}: ride {ride} {(p.EnemyLane ? "enemy" : "party")} S={p.S:0.0} {(p.Lit ? "lit" : "dark")}; stop {stop:0.0} m, hard {(hi >= 0 ? hardNames[hi] : "none")} {hd:0.0} m, soft {(si >= 0 ? softNames[si] : "none")} {sd:0.0} m");
            }
            var placed = enc.PlanAlcoves(ride, none);
            counts[ride] = placed.Count;
            GD.Print($"[alcove] ride {ride}: {clear} clear, {placed.Count} placed");
        }
        var total = counts.Values.Sum();
        var best = counts.Values.Max();
        var shipped = AlcoveRules.ShippedCounts;
        var same = Enumerable.Range(1, AlcoveRules.RideCount).All(r => shipped.TryGetValue(r, out var n) && n == counts[r]);
        var line = string.Join(", ", counts.Select(kv => $"{kv.Key}: {kv.Value}"));
        if (!same) { StopTrapFail($"the placed counts [{line}] are not AlcoveRules.ShippedCounts [{string.Join(", ", shipped.Select(kv => $"{kv.Key}: {kv.Value}"))}]"); return; }
        if (total < AlcoveRules.MinTotal) { StopTrapFail($"{total} shooters on the floor, under {AlcoveRules.MinTotal} [{line}]"); return; }
        if (best < AlcoveRules.MinBestRide) { StopTrapFail($"no ride places {AlcoveRules.MinBestRide} (the best places {best}) [{line}]"); return; }
        StopTrapOk($"{total} shooters [{line}], the best ride {best}");
    }

    private const float AlWakeSlack = 2f, AlGoneSlack = 2f, AlCadenceSlack = 0.25f, AlPinned = 0.3f, AlSideOn = 45f;

    private bool _alListening;
    private RailController? _alRc;
    private readonly List<(double At, int Stop)> _alArrived = new();
    private readonly List<(double At, Enemies.Enemy Who, AlcovePhase Phase)> _alHurt = new();
    private readonly List<Combat.Projectile> _alBolts = new();
    private readonly List<(double At, string Hero, bool Seated, float OffTrack, string Shooter)> _alHits = new();
    private readonly List<string> _alFaults = new();
    private readonly HashSet<Enemies.Enemy> _alSeen = new();
    private readonly Dictionary<Enemies.Enemy, (Vector3 Back, Vector3 Mouth, float S)> _alPosts = new();
    private readonly Dictionary<Enemies.Enemy, double> _alLastFire = new();
    private readonly Dictionary<Enemies.Enemy, bool> _alLostSince = new();
    private int _alLogRead, _alArmsRead, _alClaimed = -1, _alFires, _alCadenceOk;
    private Adventurer? _alSeeker;
    private float _alMaxPinned;
    private bool _alLeversThrown, _alRefugeDone;
    private string? _alRefuge;
    private int _alRefugeStep;
    private Enemies.Enemy? _alRefugeShooter;
    private Vector3 _alRefugeAt;
    private double _alRefugeSince;
    private readonly Dictionary<int, int> _alPlaced = new();

    private void AlcoveListen(Main main, RailEncounterController enc)
    {
        if (_alListening) return;
        _alListening = true;
        var bus = EventBus.Instance;
        Action<Enemies.Enemy, float, Combat.HitFlavor, int> hurt = (e, amount, _, _) =>
        {
            foreach (var sh in enc.DebugShooters())
                if (sh.Enemy == e) { _alHurt.Add((StNow, e, sh.Phase)); if (sh.Phase == AlcovePhase.Hidden) GD.Print($"[stoptrap] alcove: {e.Name} hurt {amount:0.0} while hidden"); }
        };
        Action<Combat.Projectile> fired = p => { if (p.Shooter is { } sh && IsInstanceValid(sh) && enc.IsAlcoveShooter(sh)) _alBolts.Add(p); };
        Action<int, float> damaged = (index, amount) => AlcoveHeroHit(main, index);
        bus.EnemyHurt += hurt;
        bus.ProjectileFired += fired;
        bus.MemberDamaged += damaged;
        _staticUnhooks.Add(() => { bus.EnemyHurt -= hurt; bus.ProjectileFired -= fired; bus.MemberDamaged -= damaged; });
    }

    /// <summary>A hero was hurt: a tracked bolt where it stands is a shooter's hit (the bolt is still in the tree, at its hit point, while the hit is dealt).</summary>
    private void AlcoveHeroHit(Main main, int index)
    {
        var hero = main.Party.Adventurers.FirstOrDefault(a => IsInstanceValid(a) && a.Index == index);
        if (hero == null || RailController.Instance is not { } rc || !IsInstanceValid(rc)) return;
        Combat.Projectile? bolt = null;
        var best = 2.5f;
        foreach (var b in _alBolts)
        {
            if (!IsInstanceValid(b)) continue;
            var d = b.GlobalPosition.DistanceTo(hero.GlobalPosition + Vector3.Up * 0.9f);
            if (d < best) { best = d; bolt = b; }
        }
        if (bolt == null) return;
        _alBolts.Remove(bolt);
        var track = rc.Party.Track;
        var along = track.Frame(track.ClosestS(hero.GlobalPosition)).Basis.Z;
        var flatAlong = new Vector2(along.X, along.Z).Normalized();
        var flatV = new Vector2(bolt.Velocity.X, bolt.Velocity.Z).Normalized();
        var off = Mathf.RadToDeg(Mathf.Acos(Mathf.Clamp(Mathf.Abs(flatAlong.Dot(flatV)), 0f, 1f)));
        var shooter = bolt.Shooter is { } e && IsInstanceValid(e) ? e.Name.ToString() : "?";
        _alHits.Add((StNow, hero.State.Name, hero.Seated, off, shooter));
        GD.Print($"[stoptrap] alcove: {shooter}'s bolt hits {hero.State.Name} ({(hero.Seated ? "seated" : "on foot")}), its path {off:0} deg off the track");
    }

    private void AlcoveWatchRails(RailController rc)
    {
        if (rc == _alRc) return;
        _alRc = rc;
        rc.CartEvents += e => { if (e.Kind == CartEventKind.Arrived) _alArrived.Add((StNow, e.Stop)); };
    }

    /// <summary>Each frame: the live shooters' places and flags, the new log lines and the new armings judged.</summary>
    private void AlcoveWatch(Main main, RailController rc, RailEncounterController enc)
    {
        foreach (var sh in enc.DebugShooters())
        {
            if (_alSeen.Add(sh.Enemy))
            {
                _alPosts[sh.Enemy] = (sh.Back, sh.Mouth, enc.AlcovePockets()[sh.Pocket].S);
                if (!sh.Enemy.IsReinforcement) _alFaults.Add($"{sh.Enemy.Name} is not a reinforcement");
                if (!sh.Enemy.Pinned) _alFaults.Add($"{sh.Enemy.Name} is not pinned");
            }
            var at = sh.Enemy.GlobalPosition;
            var d = sh.Phase switch
            {
                AlcovePhase.Hidden => at.DistanceTo(sh.Back),
                AlcovePhase.Shooting => at.DistanceTo(sh.Mouth),
                _ => AlSegment(at, sh.Back, sh.Mouth),
            };
            if (d > _alMaxPinned) _alMaxPinned = d;
            if (d > AlPinned && _alFaults.Count < 8) _alFaults.Add($"{sh.Enemy.Name} {d:0.00} m off its {(sh.Phase == AlcovePhase.Hidden ? "back" : sh.Phase == AlcovePhase.Shooting ? "mouth" : "step")} while {sh.Phase}");
            if (sh.Phase == AlcovePhase.Hidden && !sh.Enemy.Untargetable && _alFaults.Count < 8) _alFaults.Add($"{sh.Enemy.Name} is targetable while hidden");
        }
        var log = enc.AlcoveLog;
        for (; _alLogRead < log.Count; _alLogRead++)
        {
            var (t, who, what, s, metres, target, _, waited) = log[_alLogRead];
            switch (what)
            {
                case "steps out":
                    if (Mathf.Abs(metres - AlcoveRules.WakeAt) > AlWakeSlack) _alFaults.Add($"{who.Name} stepped out with the front {metres:0.0} m off, not 35 +- 2");
                    break;
                case "fired":
                {
                    _alFires++;
                    if (metres > AlcoveRules.ShootRange) _alFaults.Add($"{who.Name} fired at {metres:0.0} m, beyond 25");
                    if (target == null || !main.Party.Adventurers.Contains(target)) _alFaults.Add($"{who.Name} fired at no hero of the party");
                    else if (!target.Seated) _alFaults.Add($"{who.Name} fired at {target.State.Name} on foot");
                    var lost = _alLostSince.GetValueOrDefault(who);
                    if (_alLastFire.TryGetValue(who, out var last))
                    {
                        var gap = t - last;
                        if (gap < AlcoveRules.ShootEvery - AlCadenceSlack) _alFaults.Add($"{who.Name} fired {gap:0.00} s after its last bolt");
                        else if (!lost && waited < 0.05f)
                        {
                            if (gap > AlcoveRules.ShootEvery + AlCadenceSlack) _alFaults.Add($"{who.Name} fired {gap:0.00} s after its last bolt with a target all along, not 1.6 +- 0.25");
                            else _alCadenceOk++;
                        }
                    }
                    _alLastFire[who] = t;
                    _alLostSince[who] = false;
                    break;
                }
                case "gone":
                    if (Mathf.Abs(metres - AlcoveRules.GoneBehind) > AlGoneSlack) _alFaults.Add($"{who.Name} went with the train's back {metres:0.0} m past, not 30 +- 2");
                    break;
                default:
                    if (what.StartsWith("lost")) _alLostSince[who] = true;
                    break;
            }
        }
        for (; _alArmsRead < enc.AlcoveArms.Count; _alArmsRead++) AlcoveJudgeArm(rc, enc, enc.AlcoveArms[_alArmsRead]);
    }

    private static float AlSegment(Vector3 p, Vector3 a, Vector3 b)
    {
        var ab = b - a;
        var t = ab.LengthSquared() < 1e-6f ? 0f : Mathf.Clamp((p - a).Dot(ab) / ab.LengthSquared(), 0f, 1f);
        return p.DistanceTo(a + ab * t);
    }

    /// <summary>An arming's placement against Ruling 17 and the pinned counts.</summary>
    private void AlcoveJudgeArm(RailController rc, RailEncounterController enc, (int Ride, List<int> Pockets, List<float> S, int Want, List<int> Claimed) arm)
    {
        var hard = new List<(float, float)>();
        var soft = new List<(float, float)>();
        var hardNames = new List<string>();
        var softNames = new List<string>();
        enc.ShooterStructures(arm.Ride, hard, soft, hardNames, softNames);
        _alPlaced[arm.Ride] = arm.Pockets.Count;
        var shipped = AlcoveRules.ShippedCounts.GetValueOrDefault(arm.Ride);
        var claimed = arm.Claimed.ToHashSet();
        var fresh = enc.PlanAlcoves(arm.Ride, claimed);
        GD.Print($"[stoptrap] alcove: ride {arm.Ride} armed: {arm.Pockets.Count} placed (shipped {shipped}, a fresh Place with claims [{string.Join(",", arm.Claimed)}] gives {fresh.Count}) at pockets [{string.Join(",", arm.Pockets)}]");
        if (arm.Pockets.Count != fresh.Count || !arm.Pockets.SequenceEqual(fresh)) _alFaults.Add($"ride {arm.Ride}: placed [{string.Join(",", arm.Pockets)}], a fresh Place gives [{string.Join(",", fresh)}]");
        if (claimed.Count == 0 ? arm.Pockets.Count != shipped : arm.Pockets.Count < shipped - 1 || arm.Pockets.Count > shipped) _alFaults.Add($"ride {arm.Ride}: {arm.Pockets.Count} placed, ShippedCounts {shipped} (claims [{string.Join(",", arm.Claimed)}])");
        if (arm.Ride == 1 && (_alClaimed < 0 || !claimed.Contains(_alClaimed))) _alFaults.Add($"ride 1 armed without the claimed pocket {_alClaimed} claimed");
        foreach (var c in claimed) if (arm.Pockets.Contains(c)) _alFaults.Add($"ride {arm.Ride}: the claimed pocket {c} was taken");
        float from = rc.StopS[arm.Ride - 1], to = rc.StopS[arm.Ride];
        foreach (var s in arm.S)
        {
            var (hi, hd) = AlcoveRules.Nearest(s, hard);
            var (si, sd) = AlcoveRules.Nearest(s, soft);
            if (s - from < AlcoveRules.HardClear || to - s < AlcoveRules.HardClear) _alFaults.Add($"ride {arm.Ride}: S={s:0.0} within 30 m of a stop");
            if (hd < AlcoveRules.HardClear) _alFaults.Add($"ride {arm.Ride}: S={s:0.0} {hd:0.0} m from {hardNames[hi]}");
            if (sd < AlcoveRules.SoftClear) _alFaults.Add($"ride {arm.Ride}: S={s:0.0} {sd:0.0} m from {softNames[si]}");
        }
    }

    /// <summary>The ride's end: no shooter alive (the list and the floor's live enemies).</summary>
    private string? AlcoveNoneLeft(RailEncounterController enc, int stop)
    {
        var live = Enemies.Enemy.Live.Count(e => IsInstanceValid(e) && e.IsAlive && !e.IsQueuedForDeletion() && e.Name.ToString().StartsWith("Alcove_"));
        GD.Print($"[stoptrap] alcove: arrived at stop {stop}: {enc.Alcoves.Count} shooter(s) listed, {live} alive on the floor");
        return enc.Alcoves.Count > 0 || live > 0 ? $"{Math.Max(enc.Alcoves.Count, live)} shooter(s) alive after the Arrived at stop {stop}" : null;
    }

    /// <summary>Ride 2's refuge: the fourth companion on the enemy lane beside a shooter's pocket while a slag cart of the ride comes.</summary>
    private void AlcoveRefuge(Main main, RailController rc, RailEncounterController enc, Action<string> fail)
    {
        if (_alRefugeDone || _alSeeker is not { } c || !IsInstanceValid(c) || enc.ArmedRide != 2 || rc.State.Mode != CartMode.Rolling) return;
        switch (_alRefugeStep)
        {
            case 0:
            {
                if (!enc.SlagCarts.Any(k => IsInstanceValid(k) && !k.Derailed && k.EncounterId.StartsWith("r2_"))) return;
                var front = rc.Party.FrontS;
                foreach (var sh in enc.DebugShooters())
                {
                    var pocket = enc.AlcovePockets()[sh.Pocket];
                    if (!pocket.EnemyLane || pocket.S - front < 22f || pocket.S - front > 40f) continue;
                    var toLane = (sh.Mouth - sh.Back); toLane.Y = 0f;
                    _alRefugeAt = sh.Centre + toLane.Normalized() * 4f + Vector3.Up * 0.3f; // the enemy lane's cell beside the pocket
                    _alRefugeShooter = sh.Enemy;
                    enc.DebugReleaseClaims();
                    if (c.Seated) c.GetOut(c.GlobalPosition, Vector3.Zero);
                    c.Teleport(_alRefugeAt, sh.Centre);
                    _alRefugeSince = StNow;
                    _alRefugeStep = 1;
                    var nearest = enc.AlcovePockets().Where(p => p.EnemyLane).OrderBy(p => enc.AlcovePocketCentre(p.Index).DistanceTo(_alRefugeAt)).First();
                    GD.Print($"[stoptrap] alcove: refuge: {c.State.Name} on the enemy lane at {_alRefugeAt} beside {sh.Enemy.Name}'s pocket {sh.Pocket} (S={pocket.S:0.0}, 4 m off; the nearest enemy-lane pocket is {nearest.Index}, {enc.AlcovePocketCentre(nearest.Index).DistanceTo(_alRefugeAt):0.0} m), the front at S={front:0.0}; slag carts [{string.Join(", ", enc.SlagCarts.Select(k => $"{k.EncounterId}@{k.Run.State.S:0.0}"))}]");
                    if (nearest.Index != sh.Pocket) { _alRefugeDone = true; _alRefuge = $"the refuge check is vacuous: the shooter's pocket {sh.Pocket} is not the nearest to {c.State.Name} ({nearest.Index} is)"; }
                    return;
                }
                return;
            }
            case 1:
            {
                var spot = enc.SeekingSpot(c);
                if (spot == null)
                {
                    c.GlobalPosition = _alRefugeAt; // a player who ignores the bell, until the call comes
                    c.Velocity = Vector3.Zero;
                    if (StNow - _alRefugeSince > 15f) { _alRefugeDone = true; _alRefuge = $"{c.State.Name} was never sent to a refuge in 15 s"; }
                    return;
                }
                var into = enc.DebugShooters().Where(sh => sh.Centre.DistanceTo(spot.Value) < 1f).Select(sh => sh.Enemy.Name.ToString()).ToList();
                var pocketD = _alRefugeShooter is { } rs && IsInstanceValid(rs) ? _alPosts[rs].Back.DistanceTo(spot.Value) : float.NaN;
                GD.Print($"[stoptrap] alcove: refuge: {c.State.Name} seeks {spot.Value} ({StNow - _alRefugeSince:0.00} s after being put there), {pocketD:0.0} m from the shooter's back; into a shooter's pocket: [{string.Join(", ", into)}]");
                _alRefugeDone = true;
                if (into.Count > 0) _alRefuge = $"{c.State.Name} sought refuge in {into[0]}'s pocket";
                return;
            }
        }
    }

    private void RunStopTrapAlcove(Main main, FloorLayout layout, Node3D root, RailController rc)
    {
        var party = main.Party;
        void Fail(string why) => StopTrapFail(why);
        if (RailEncounterController.Instance is not { } enc || !IsInstanceValid(enc) || enc.GetParent() != root) { if (_stClock > 3f) Fail("floor 12 has no RailEncounterController"); return; }
        AlcoveWatchRails(rc);
        if (_stStep > 0) AlcoveWatch(main, rc, enc);
        if (_alFaults.Count > 0) { Fail(_alFaults[0] + (_alFaults.Count > 1 ? $" (and {_alFaults.Count - 1} more: {string.Join("; ", _alFaults.Skip(1))})" : "")); return; }
        if (_alRefuge != null) { Fail(_alRefuge); return; }
        if (_stStep is >= 2 and <= 5) AlcoveRefuge(main, rc, enc, Fail);
        if (_stStep == 2 && _alSeeker is { } held && IsInstanceValid(held) && _alClaimed >= 0 && enc.SeekingSpot(held) is { } claimSpot) { held.GlobalPosition = claimSpot + Vector3.Up * 0.05f; held.Velocity = Vector3.Zero; } // the claim holds its pocket
        if (_stStep >= 3 && !_alLeversThrown && enc.ArmedRide == 3 && rc.State.Mode == CartMode.Rolling)
        {
            if (enc.SetPieceNode("r3_switch_out") is not TrackSwitch outSw || enc.SetPieceNode("r3_switch_back") is not TrackSwitch backSw) { Fail("floor 12 lacks ride 3's switches"); return; }
            outSw.DebugThrow();
            backSw.DebugThrow();
            _alLeversThrown = true;
            GD.Print($"[stoptrap] alcove: ride 3 armed at S={rc.State.S:0.0}: r3_switch_out thrown={outSw.Thrown}, r3_switch_back thrown={backSw.Thrown} (the test's hook)");
        }

        switch (_stStep)
        {
            case 0 when _stClock >= 1.5f:
            {
                if (rc.StopS.Length != Floor12Stops) { Fail($"floor 12's line has {rc.StopS.Length} stops, not {Floor12Stops}"); return; }
                if (!enc.AlcovesOn) { Fail("the alcove shooters are off (Settings.Ambushes, NoAmbush or the RIDE pass)"); return; }
                AlcoveListen(main, enc);
                rc.Restore(0);
                _stStep++; _stClock = 0f; _stSub = 0;
                break;
            }
            case 1: // the lead boards; a companion in the cart, one in W1, the fourth claims ride 1's first planned pocket
            {
                if (_stSub == 0 && _stClock >= 1f)
                {
                    if (!RideBoard(main, layout, rc, out var why)) { Fail(why); return; }
                    _stSub = 1;
                    break;
                }
                if (_stSub != 1 || _stClock < 2f) break;
                var me = party.Local;
                var mates = party.Adventurers.Where(a => IsInstanceValid(a) && !a.Parked && a.Control == ControlKind.Companion).ToList();
                if (me == null || me.Cart != rc.Cart) { Fail("the lead is not seated in the cart"); return; }
                if (mates.Count < 3) { Fail($"{mates.Count} companions, want 3"); return; }
                var (cartMate, wagonMate, seeker) = (mates[0], mates[1], mates[2]);
                foreach (var m in new[] { wagonMate, seeker }) if (m.Seated) m.GetOut(m.GlobalPosition, Vector3.Zero);
                if (cartMate.Cart != rc.Cart && !rc.DebugSeat(cartMate, 0)) { Fail($"{cartMate.State.Name} could not be seated in the cart"); return; }
                if (!rc.DebugSeat(wagonMate, 1)) { Fail($"{wagonMate.State.Name} could not be seated in W1"); return; }
                cartMate.Stance = Characters.Stance.Aggressive; // they fire at will
                wagonMate.Stance = Characters.Stance.Aggressive;
                var plan = enc.PlanAlcoves(1, new HashSet<int>());
                if (plan.Count == 0) { Fail("ride 1 plans no shooter"); return; }
                _alClaimed = plan[0];
                _alSeeker = seeker;
                seeker.Teleport(enc.AlcoveSpots(_alClaimed).Back + Vector3.Up * 0.3f, rc.Cart.GlobalPosition);
                enc.DebugClaimPocket(seeker, _alClaimed);
                GD.Print($"[stoptrap] alcove: {me.State.Name} and {cartMate.State.Name} in the cart, {wagonMate.State.Name} in W1 (car {rc.CarOf(wagonMate)}); {seeker.State.Name} claims ride 1's first planned pocket {_alClaimed} of [{string.Join(",", plan)}]");
                _stStep++; _stClock = 0f; _stSub = 0; _rideOnStop = -1; _rideOnPhase = 0; _rideOnClock = 0f;
                break;
            }
            case 2: // ride 1
            {
                if (_stSub == 0)
                {
                    if (rc.State.Mode != CartMode.Rolling) { if (_stClock > 15f) Fail($"ride 1 did not depart (mode {rc.State.Mode})"); break; }
                    GD.Print($"[stoptrap] alcove: departed stop 0 with [{string.Join(", ", party.Active.Where(a => a.Seated).Select(a => $"{a.State.Name} car {rc.CarOf(a)}"))}] seated");
                    _stSub = 1;
                }
                if (!RideOnTo(main, rc, 1, "stoptrap", Fail)) break;
                if (AlcoveNoneLeft(enc, 1) is { } why) { Fail(why); return; }
                enc.DebugReleaseClaims();
                _stStep++; _stClock = 0f; _rideOnStop = -1; _rideOnPhase = 0; _rideOnClock = 0f;
                break;
            }
            case 3: // ride 2 (the refuge on the way)
            {
                if (!RideOnTo(main, rc, 2, "stoptrap", Fail)) break;
                if (AlcoveNoneLeft(enc, 2) is { } why) { Fail(why); return; }
                if (!_alRefugeDone) { Fail("ride 2's refuge check never ran (no enemy-lane shooter with a slag cart coming)"); return; }
                _stStep++; _stClock = 0f; _rideOnStop = -1; _rideOnPhase = 0; _rideOnClock = 0f;
                break;
            }
            case 4: // ride 3
            {
                if (!RideOnTo(main, rc, 3, "stoptrap", Fail)) break;
                if (AlcoveNoneLeft(enc, 3) is { } why) { Fail(why); return; }
                if (!_alLeversThrown) { Fail("ride 3's levers were never thrown"); return; }
                _stStep++; _stClock = 0f; _rideOnStop = -1; _rideOnPhase = 0; _rideOnClock = 0f;
                break;
            }
            case 5: // ride 4: armed, read from its line, not ridden
            {
                if (rc.State.Mode != CartMode.Rolling || enc.ArmedRide != 4) { RideOnTo(main, rc, 4, "stoptrap", Fail); if (_stClock > 40f) Fail("ride 4 never armed"); break; }
                if (!_alPlaced.ContainsKey(4)) break;
                rc.Restore(4);
                _stStep++; _stClock = 0f; _stSub = 0; _rideOnStop = -1; _rideOnPhase = 0; _rideOnClock = 0f;
                break;
            }
            case 6 when _stClock >= 1f: // ride 5: armed from stop 4 after rc.Restore(4) (its holders die, the lead boards, it departs), read from its line
            {
                if (rc.State.Mode != CartMode.Rolling || enc.ArmedRide != 5 || !_alPlaced.ContainsKey(5)) { if (rc.State.Mode != CartMode.Rolling) RideOnTo(main, rc, 5, "stoptrap", Fail); if (_stClock > 40f) Fail("ride 5 never armed"); break; }
                AlcoveVerdict(enc);
                break;
            }
        }
    }

    private void AlcoveVerdict(RailEncounterController enc)
    {
        var placed = Enumerable.Range(1, AlcoveRules.RideCount).Select(r => _alPlaced.GetValueOrDefault(r, -1)).ToList();
        var total = placed.Where(n => n > 0).Sum();
        var sideOn = _alHits.Where(h => h.Seated && h.OffTrack > AlSideOn).ToList();
        var steppedOut = enc.AlcoveLog.Where(l => l.What == "steps out").Select(l => l.Who).ToHashSet();
        var gone = enc.AlcoveLog.Where(l => l.What == "gone").Select(l => l.Who).ToHashSet();
        var neverOut = _alSeen.Where(e => !steppedOut.Contains(e)).Select(e => e.Name.ToString()).ToList();
        var lostTrack = _alSeen.Where(e => steppedOut.Contains(e) && !gone.Contains(e) && !(enc.AlcoveLog.Any(l => l.Who == e && l.What == "cleared"))).ToList();
        var hiddenHits = _alHurt.Count(h => h.Phase == AlcovePhase.Hidden);
        GD.Print($"[stoptrap] alcove: placed per ride [{string.Join(", ", placed)}] (total {total}); {_alSeen.Count} shooter(s) seen, {steppedOut.Count} stepped out, {gone.Count} gone 30 m behind, {_alSeen.Count - gone.Count} killed or cleared; "
            + $"{_alFires} bolt(s) fired, {_alCadenceOk} at the 1.6 s cadence; {_alHits.Count} hit(s) on heroes ({sideOn.Count} seated side-on: [{string.Join(", ", sideOn.Select(h => $"{h.Shooter}->{h.Hero} {h.OffTrack:0}deg"))}]); "
            + $"{_alHurt.Count} hurt(s) on shooters ({hiddenHits} while hidden); the farthest off its spot {_alMaxPinned:0.000} m");
        if (placed.Any(n => n < 0)) { StopTrapFail($"a ride never armed its shooters: [{string.Join(", ", placed)}]"); return; }
        if (total < AlcoveRules.MinTotal) { StopTrapFail($"{total} shooters on the floor, under {AlcoveRules.MinTotal}"); return; }
        if (placed.Max() < AlcoveRules.MinBestRide) { StopTrapFail($"no ride placed {AlcoveRules.MinBestRide}"); return; }
        if (hiddenHits > 0) { StopTrapFail($"{hiddenHits} hit(s) on a hidden shooter"); return; }
        if (neverOut.Count > 0) { StopTrapFail($"never stepped out: [{string.Join(", ", neverOut)}]"); return; }
        if (lostTrack.Any(e => IsInstanceValid(e) && e.IsAlive)) { StopTrapFail($"stepped out and neither gone nor dead: [{string.Join(", ", lostTrack.Select(e => e.Name))}]"); return; }
        if (_alFires == 0 || _alCadenceOk == 0) { StopTrapFail($"{_alFires} bolt(s), {_alCadenceOk} at the cadence"); return; }
        if (sideOn.Count == 0) { StopTrapFail("no bolt hit a seated hero side-on"); return; }
        StopTrapOk($"[{string.Join(", ", placed)}] placed ({total}), {steppedOut.Count} woke at 35 m, {_alFires} bolts ({_alCadenceOk} at 1.6 s), {sideOn.Count} side-on hit(s), {gone.Count} gone 30 m behind, none after an arrival; the claim kept, the refuge clear");
    }
}
