using DungeonCrawler.Core;
using DungeonCrawler.Enemies;
using DungeonCrawler.Enemies.Ai;
using DungeonCrawler.Net;
using DungeonCrawler.Party;
using Godot;

namespace DungeonCrawler.Dungeon.Rails;

/// <summary>
/// Plan 16 Task 8: the alcove shooters. Each ride's departure arms its shooters in the track's refuge pockets (<see cref="AlcoveRules.Place"/>,
/// spec Ruling 4: by code, on any railed floor with <see cref="AlcoveRules.RideCount"/> rides), only where the corridor ambushes are on
/// (<c>Settings.Ambushes</c> and no <c>NoAmbush</c>, Ruling 2) and never on the RIDE pass's server. A shooter is a reinforcement (re-sent on every
/// re-arm: no loot farm), pinned in its pocket in <see cref="AlcoveState"/>: hidden and untargetable at the back until the train's front is 35 m off,
/// then at the mouth shooting the seated riders with lead aim; once the train's back is 30 m past it (Done) it goes quietly, and the ride's end
/// (an arrival, a wreck, a restore, the party on foot) takes the rest with it, so none outlives its ride into a reverse roll, a return or a relief.
/// A refuge seeker never takes a pocket with a live shooter in it (<see cref="SeekRefuge"/>). Server and solo; a client sees them through
/// <c>EnemySpawned</c> (no new wire).
/// </summary>
public partial class RailEncounterController
{
    private sealed class Shooter
    {
        public Enemy Enemy = null!;
        public AlcoveState State = null!;
        public int Pocket;
        public float S;
        public Vector3 Centre;
    }

    private readonly List<Shooter> _shooters = new();
    private List<AlcoveRules.Pocket>? _alcovePockets;
    private Vector3[] _pocketCentre = Array.Empty<Vector3>();
    private Vector3[] _pocketToLane = Array.Empty<Vector3>();

    /// <summary>The shooters of the armed ride alive now (a test hook).</summary>
    internal IReadOnlyList<Enemy> Alcoves
    {
        get
        {
            _alcoveView.Clear();
            foreach (var s in _shooters) if (IsInstanceValid(s.Enemy) && s.Enemy.IsAlive) _alcoveView.Add(s.Enemy);
            return _alcoveView;
        }
    }
    private readonly List<Enemy> _alcoveView = new();

    /// <summary>
    /// What the shooters did, for the flows: the time (the controller's clock), the shooter, what ("placed", "steps out", "fired", "gone", "cleared"),
    /// the post's S, metres (the front's distance before the post, the back's past it, the target's distance) and the target (fired only).
    /// </summary>
    internal readonly List<(double T, Enemy Who, string What, float S, float Metres, Adventurer? Target, Vector3 Aim, float Waited)> AlcoveLog = new();

    /// <summary>Each arming's placement (tests read it): the ride, the pockets picked and the S of each, the count wanted and the pockets claimed then.</summary>
    internal readonly List<(int Ride, List<int> Pockets, List<float> S, int Want, List<int> Claimed)> AlcoveArms = new();

    /// <summary>The shooters may be placed on this floor and role (Ruling 2): the corridor ambushes on, no <c>NoAmbush</c>, not the RIDE pass's server, a line of 6 stops.</summary>
    internal bool AlcovesOn => Settings.Ambushes && DebugTools.Instance?.NoAmbush != true && NetSession.Instance?.TestRide != true
                               && _rails.StopS.Length == AlcoveRules.RideCount + 1;

    /// <summary>
    /// The line's pockets as the shooters see them, in the layout's final order: S the party track's closest offset to the pocket's surface (as
    /// <c>PlaceCage</c> measures a platform), the lane by its rail neighbour (as <see cref="EnsurePockets"/> sorts them), lit by an even index.
    /// </summary>
    internal IReadOnlyList<AlcoveRules.Pocket> AlcovePockets()
    {
        if (_alcovePockets != null) return _alcovePockets;
        var rail = _layout.Rail!;
        var list = new List<AlcoveRules.Pocket>(rail.Pockets.Count);
        _pocketCentre = new Vector3[rail.Pockets.Count];
        _pocketToLane = new Vector3[rail.Pockets.Count];
        for (var i = 0; i < rail.Pockets.Count; i++)
        {
            var cell = rail.Pockets[i];
            var centre = _builder.Surface(cell);
            var toParty = PartyNeighbour(cell);
            var enemyLane = toParty == null;
            var s = _rails.Party.Track.ClosestS(centre);
            Vector3 toLane;
            if (toParty is { } dp) toLane = new Vector3(dp.X, 0f, dp.Y);
            else if (EnemyNeighbour(cell) is { } de) toLane = new Vector3(de.X, 0f, de.Y);
            else
            {
                var lane = _enemy.Valid ? _enemy.Point(_enemy.ClosestS(centre)) : _rails.Party.Track.Point(s);
                toLane = new Vector3(lane.X - centre.X, 0f, lane.Z - centre.Z);
            }
            _pocketCentre[i] = centre;
            _pocketToLane[i] = toLane.LengthSquared() > 1e-6f ? toLane.Normalized() : Vector3.Forward;
            list.Add(new AlcoveRules.Pocket(i, s, enemyLane, AlcoveRules.LitIndex(i)));
        }
        _alcovePockets = list;
        return list;
    }

    private static readonly Generation.GridPos[] Cardinal = { new(1, 0), new(-1, 0), new(0, 1), new(0, -1) };

    private Generation.GridPos? PartyNeighbour(Generation.GridPos cell)
    {
        foreach (var d in Cardinal) if (_layout.IsPartyRail(cell + d)) return d;
        return null;
    }

    private Generation.GridPos? EnemyNeighbour(Generation.GridPos cell)
    {
        foreach (var d in Cardinal) if (_layout.IsRail(cell + d) && !_layout.IsPartyRail(cell + d)) return d;
        return null;
    }

    /// <summary>Ruling 17's spans for ride <paramref name="ride"/> (party S, From..To), into the two lists (cleared first).</summary>
    internal void ShooterStructures(int ride, List<(float, float)> hard, List<(float, float)> soft) => ShooterStructures(ride, hard, soft, null, null);

    /// <summary>
    /// Ruling 17's spans that reach ride <paramref name="ride"/> (its stops widened by <see cref="AlcoveRules.HardClear"/>): hard (its stops, the dark,
    /// ride 4's bridge and cavern stretch, every switch's seated window, ride 5's pit, pour, trestle, hammers, parked train and exit bridge) and soft
    /// (every other piece, the broken bridge's gap, every encounter's start and the thrower's pocket), each named when the name lists are given.
    /// </summary>
    internal void ShooterStructures(int ride, List<(float, float)> hard, List<(float, float)> soft, List<string>? hardNames, List<string>? softNames)
    {
        hard.Clear(); soft.Clear(); hardNames?.Clear(); softNames?.Clear();
        if (ride < 1 || ride >= _rails.StopS.Length) return;
        float lo = _rails.StopS[ride - 1] - AlcoveRules.HardClear, hi = _rails.StopS[ride] + AlcoveRules.HardClear;
        void Add(List<(float, float)> into, List<string>? names, string name, float from, float to)
        {
            if (float.IsNaN(from) || float.IsNaN(to)) return;
            if (from > to) (from, to) = (to, from);
            if (to < lo - AlcoveRules.HardClear || from > hi + AlcoveRules.HardClear) return; // nowhere near this ride
            into.Add((from, to));
            names?.Add(name);
        }
        void Hard(string name, float from, float to) => Add(hard, hardNames, name, from, to);
        void Soft(string name, float from, float to) => Add(soft, softNames, name, from, to);

        for (var k = 0; k < _rails.StopS.Length; k++) Hard($"stop {k}", _rails.StopS[k], _rails.StopS[k]);
        if (!float.IsNaN(DarkFromS)) Hard("the dark", DarkFromS, Mathf.Min(DarkFromS + DarkRules.Length, _rails.Party.Track.Length));
        Hard("the bridge", BridgeFromS, BridgeToS);
        Hard("the cavern", LeapFromS, LeapDropS);
        Hard("the pit", PitS, PitFarS);
        Hard("the exit bridge", ExitFromS, ExitToS);
        if (!float.IsNaN(BreakGapS)) Soft("the broken bridge", BreakGapS, float.IsNaN(BreakGapFarEnemyS) ? BreakGapS : Lanes.PartyS(BreakGapFarEnemyS)); // the slag line's hole: a piece of the layout, not of the plan
        foreach (var p in _pieces)
        {
            var id = p.Plan.Id;
            if (p.Switch is { } sw && IsInstanceValid(sw))
            {
                var view = sw.SeatedView;
                var from = Mathf.Min(Mathf.Min(view.LeverS - SeatedSwitchRules.WindowAhead, sw.LeverS), sw.PointsS);
                var to = Mathf.Max(Mathf.Max(sw.PointsS, sw.LeverS), float.IsInfinity(view.PointsS) ? sw.PointsS : view.PointsS);
                Hard($"{id} (its window)", from, to);
                continue;
            }
            switch (p.Plan.Kind)
            {
                case "switch_out" or "switch_back" or "split" or "spur_switch" or "dark" or "bridge" or "leap" or "pit" or "pour":
                    Hard(id, p.S, p.S); // a dead marker, or the piece's own S beside its span
                    break;
                case "trestle":
                    if (p.Trestle is { } t && IsInstanceValid(t)) Hard(id, t.From, t.To); else Hard(id, p.S, p.S);
                    break;
                case "hammers":
                    Hard(id, p.S, p.S);
                    if (p.Hammers != null) foreach (var h in p.Hammers) if (IsInstanceValid(h)) Hard(id, h.S, h.S);
                    break;
                case "parked":
                    Hard(id, p.S, p.S);
                    if (!float.IsNaN(ExitFromS)) Hard(id, ParkedRules.BackS(ExitFromS, ParkedRules.Wagons), ExitFromS);
                    break;
                default:
                    Soft(id, p.S, p.S); // barricades, beams, heaps, buckets, the runaway's marker, the buffer
                    break;
            }
        }
        foreach (var e in _encounters)
        {
            if (e.Ride < 1 || e.Ride >= _rails.StopS.Length) continue;
            var s = RideS(e.Ride, e.At);
            Soft(e.Id, s, s); // a slag cart's trigger S, a lone's spot, the golem's, the thrower's line
            if (e.Kind == "thrower" && IsInstanceValid(_rails.Party.Track.Track))
            {
                EnsurePockets();
                if (NearestPocket(_partyPockets!, _rails.Party.Track.Point(s)) is { } pocket)
                {
                    var ps = _rails.Party.Track.ClosestS(pocket);
                    Soft($"{e.Id}'s pocket", ps, ps);
                }
            }
        }
    }

    /// <summary>The pockets a refuge seeker holds now (its spot is the pocket's surface): no shooter is placed in one (Ruling 17).</summary>
    internal HashSet<int> ClaimedPockets()
    {
        var claimed = new HashSet<int>();
        AlcovePockets();
        foreach (var k in _seekers.Values)
            for (var i = 0; i < _pocketCentre.Length; i++)
                if (Flat(_pocketCentre[i], k.Spot) < 1f) claimed.Add(i);
        return claimed;
    }

    /// <summary>Ride <paramref name="ride"/>'s picks for <paramref name="claimed"/> (the flows plan with it): <see cref="AlcoveRules.Place"/> on this floor's pockets and spans.</summary>
    internal List<int> PlanAlcoves(int ride, IReadOnlySet<int> claimed)
    {
        var hard = new List<(float, float)>();
        var soft = new List<(float, float)>();
        ShooterStructures(ride, hard, soft);
        return AlcoveRules.Place(ride, _rails.StopS[ride - 1], _rails.StopS[ride], AlcovePockets(), hard, soft, claimed, _layout.Seed);
    }

    /// <summary>Pocket <paramref name="index"/>'s surface (tests read it).</summary>
    internal Vector3 AlcovePocketCentre(int index) { AlcovePockets(); return _pocketCentre[index]; }

    /// <summary>A post's back and mouth for pocket <paramref name="index"/>.</summary>
    internal (Vector3 Back, Vector3 Mouth) AlcoveSpots(int index)
    {
        AlcovePockets();
        var c = _pocketCentre[index];
        var d = _pocketToLane[index];
        return (c - d * AlcoveRules.BackFromCentre + Vector3.Up * 0.05f, c + d * AlcoveRules.MouthFromCentre + Vector3.Up * 0.05f);
    }

    /// <summary>
    /// Ride <paramref name="ride"/>'s shooters stand hidden in their pockets (from its arming: <see cref="Arm"/>). One armed from past its post (the lift's
    /// takeover, <see cref="ArmFrom"/>) is not placed: nothing stands behind the train. Any of an earlier arming go first.
    /// </summary>
    private void ArmAlcoves(int ride)
    {
        ClearAlcoves();
        if (!AlcovesOn || ride < 1 || ride > AlcoveRules.RideCount) return;
        var want = AlcoveRules.For(ride).Count;
        var claimed = ClaimedPockets();
        var picks = PlanAlcoves(ride, claimed);
        var pockets = AlcovePockets();
        var placedS = new List<float>();
        var placed = new List<int>();
        int enemyLane = 0, partyLane = 0;
        for (var k = 0; k < picks.Count; k++)
        {
            var pocket = pockets[picks[k]];
            if (pocket.S < _armFromS) continue; // behind the takeover's start
            var (back, mouth) = AlcoveSpots(pocket.Index);
            if (_builder.SpawnEnemy(AlcoveRules.Kind(_layout.Seed, ride, k), back, _builder.CurrentFloor) is not { } enemy) continue;
            enemy.Name = $"Alcove_r{ride}_{k}";
            enemy.IsReinforcement = true; // re-sent on every re-arm of the ride, as a re-sent thrower is: no loot farm (pre-flight F23a)
            var shooter = new Shooter { Enemy = enemy, Pocket = pocket.Index, S = pocket.S, Centre = _pocketCentre[pocket.Index] };
            var post = new AlcovePost { Back = back, Mouth = mouth };
            var s = pocket.S;
            post.FrontToPost = () => s - _rails.Party.FrontS;
            post.BackPastPost = () => TrainRearS - s;
            post.Target = () => ShooterTarget(enemy);
            post.SteppedOut = () =>
            {
                var d = s - _rails.Party.FrontS;
                GD.Print($"[alcove] {enemy.Name} steps out at S={s:0.0} (front {d:0.0} m)");
                AlcoveLog.Add((_now, enemy, "steps out", s, d, null, Vector3.Zero, 0f));
            };
            post.Fired = (target, aim, waited) => AlcoveLog.Add((_now, enemy, "fired", s, enemy.GlobalPosition.DistanceTo(target.GlobalPosition), target, aim, waited));
            post.Lost = why => AlcoveLog.Add((_now, enemy, "lost: " + why, s, 0f, null, Vector3.Zero, 0f));
            shooter.State = new AlcoveState(post);
            enemy.ChangeState(shooter.State);
            enemy.Pinned = true; // after its own state: nothing else takes it out of it but death
            _shooters.Add(shooter);
            placed.Add(pocket.Index);
            placedS.Add(s);
            if (pocket.EnemyLane) enemyLane++; else partyLane++;
            AlcoveLog.Add((_now, enemy, "placed", s, 0f, null, Vector3.Zero, 0f));
        }
        AlcoveArms.Add((ride, placed, placedS, want, claimed.OrderBy(i => i).ToList()));
        GD.Print($"[alcove] ride {ride}: {placed.Count} of {want} placed at S=[{string.Join(", ", placedS.Select(v => v.ToString("0.0")))}] ({enemyLane} enemy-lane, {partyLane} party-lane)");
    }

    /// <summary>The nearest seated hero (cart or wagon) within <see cref="AlcoveRules.ShootRange"/> of <paramref name="enemy"/> and in its sight, or null. Never a crew member.</summary>
    private Adventurer? ShooterTarget(Enemy enemy)
    {
        if (Main.Instance?.Party is not { } party) return null;
        Adventurer? best = null;
        var bestD = AlcoveRules.ShootRange;
        foreach (var hero in party.Adventurers)
        {
            if (!IsInstanceValid(hero) || !hero.Seated || hero.Downed || hero.Parked) continue;
            var d = enemy.GlobalPosition.DistanceTo(hero.GlobalPosition);
            if (d > bestD || !enemy.LineOfSightTo(hero.GlobalPosition)) continue;
            bestD = d;
            best = hero;
        }
        return best;
    }

    /// <summary>Each physics tick (server and solo): a dead shooter leaves the list (its corpse stays), a done one goes quietly.</summary>
    private void TickAlcoves()
    {
        for (var i = _shooters.Count - 1; i >= 0; i--)
        {
            var sh = _shooters[i];
            if (!IsInstanceValid(sh.Enemy) || sh.Enemy.IsQueuedForDeletion() || !sh.Enemy.IsAlive) { _shooters.RemoveAt(i); continue; }
            if (sh.State.Phase != AlcovePhase.Done) continue;
            var past = TrainRearS - sh.S;
            GD.Print($"[alcove] {sh.Enemy.Name} gone (the train's back {past:0.0} m past)");
            AlcoveLog.Add((_now, sh.Enemy, "gone", sh.S, past, null, Vector3.Zero, 0f));
            sh.Enemy.QueueFree();
            _shooters.RemoveAt(i);
        }
    }

    /// <summary>The ride is over or undone: its shooters go silently (as <see cref="ClearStanders"/>' lone does).</summary>
    private void ClearAlcoves()
    {
        foreach (var sh in _shooters)
            if (IsInstanceValid(sh.Enemy) && !sh.Enemy.IsQueuedForDeletion())
            {
                if (sh.Enemy.IsAlive) AlcoveLog.Add((_now, sh.Enemy, "cleared", sh.S, 0f, null, Vector3.Zero, 0f));
                sh.Enemy.QueueFree();
            }
        _shooters.Clear();
    }

    /// <summary>
    /// Test hook (the =alcove flow, Ruling 17's claim): <paramref name="hero"/>, on foot, takes refuge in pocket <paramref name="pocket"/> and is held there as a
    /// seeker until <see cref="DebugReleaseClaims"/>, whatever the train does, so an arming sees the pocket claimed.
    /// </summary>
    internal void DebugClaimPocket(Adventurer hero, int pocket)
    {
        AlcovePockets();
        var spot = _pocketCentre[pocket];
        _seekers[hero.GetInstanceId()] = new Seeker { Body = hero, Run = _rails.Party, Carrier = _rails.Cart, Spot = spot, Held = true };
        hero.SeekSpot = spot;
        GD.Print($"[alcove] {hero.State.Name} claims pocket {pocket} at S={_alcovePockets![pocket].S:0.0} (a test's hook)");
    }

    /// <summary>Test hook: the seekers <see cref="DebugClaimPocket"/> held are let go (as the cart passing them would).</summary>
    internal void DebugReleaseClaims()
    {
        foreach (var k in _seekers.Values) k.Held = false;
    }

    /// <summary>Where <paramref name="hero"/> walks to for refuge now, or null (tests read it).</summary>
    internal Vector3? SeekingSpot(Adventurer hero) => _seekers.TryGetValue(hero.GetInstanceId(), out var k) ? k.Spot : null;

    /// <summary>The live shooters' pocket indices and centres (tests read them).</summary>
    internal IEnumerable<(int Pocket, Vector3 Centre, Enemy Enemy, AlcovePhase Phase, Vector3 Back, Vector3 Mouth)> DebugShooters()
    {
        foreach (var sh in _shooters)
            if (IsInstanceValid(sh.Enemy) && sh.Enemy.IsAlive) yield return (sh.Pocket, sh.Centre, sh.Enemy, sh.State.Phase, sh.State.Post.Back, sh.State.Post.Mouth);
    }

    /// <summary>Whether <paramref name="enemy"/> is one of the armed ride's alcove shooters (the flows' culls spare them).</summary>
    internal bool IsAlcoveShooter(Enemy enemy)
    {
        foreach (var sh in _shooters) if (sh.Enemy == enemy) return true;
        return false;
    }

    /// <summary>A live shooter stands in the pocket at <paramref name="pocket"/> (a refuge seeker never takes it, Ruling 17).</summary>
    private bool ShooterIn(Vector3 pocket)
    {
        foreach (var sh in _shooters)
            if (IsInstanceValid(sh.Enemy) && sh.Enemy.IsAlive && Flat(sh.Centre, pocket) < 1f) return true;
        return false;
    }
}
