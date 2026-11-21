using DungeonCrawler.Adventure;
using DungeonCrawler.Characters;
using DungeonCrawler.Core;
using DungeonCrawler.Party;
using Godot;

namespace DungeonCrawler.Dungeon.Rails;

/// <summary>
/// Plan 12's runaway on ride 4 (a set piece "runaway", Rulings 10-14): as the party cart's front first reaches the piece's S, one
/// <see cref="RunawayWagon"/> breaks loose on the party line <see cref="RunawayRules.Behind"/> behind the train's last body, rolling at the
/// train's own unpumped speed + 15% (<see cref="RunawayRules.Target"/>), its bell ringing every <see cref="RunawayRules.BellEvery"/> s while
/// it is within <see cref="RunawayRules.BellNear"/> behind the train. It is sent only with the ride's split live, no chaser or relief cart on
/// the party line, the split's window far enough ahead and room behind the train (<see cref="RunawayRules.Sends"/>, <see cref="RunawayRules.StartS"/>);
/// otherwise the log says why. It rams a rolling train once (<see cref="RunawayRules.Rams"/>): the plating loses <see cref="RunawayRules.RamDent"/>
/// (a wreck at 0), each seated hero who is up loses <see cref="RunawayRules.RamDamage"/> (never below 1 HP; NPCs on the bench and in the wagons only
/// feel it), nobody leaves a seat, and the runaway derails and is freed. A held train (at a stop or halted on the line) is never rammed: it halts
/// with its front <see cref="RunawayRules.HaltShort"/> behind the last body and is freed after <see cref="RunawayLinger"/>. A wreck (a fall too),
/// a restore (a wipe's rise or a load), a chaser or relief cart on the line, a new departure and the controller leaving free it at once. It smashes
/// timber on its line and runs over bodies on it as the traffic runaway does; a stall frees it (<see cref="OwnProgressStuck"/>). Companions never pump
/// for it (Ruling 11): the train's pumping rules are untouched. Server and single player; a client builds its copy from the cart state (RailNet).
/// <para>Until Task 6 builds the split itself, the "split" piece is a marker (<see cref="BuildSplitMarker"/>) that reads <see cref="SplitRules.Live"/>
/// from the layout's <see cref="Generation.RailLine.Split"/>; the runaway's test for a live split is <see cref="SplitLiveOn"/>.</para>
/// </summary>
public partial class RailEncounterController
{
    private sealed class RideRunaway
    {
        public string Id = "";
        public int Ride;
        public CartRun Run = null!;
        public RunawayWagon Body = null!;
        /// <summary>Seconds since it halted behind a held train, or −1 while it runs.</summary>
        public float Halted = -1f;
        public float BellClock;
        /// <summary>The party cart's wrecks when it broke loose: another one frees it.</summary>
        public int Wrecks;
        /// <summary>The S it never runs past while the train rolls (the ride's next stop).</summary>
        public float EndS;
        public CartWatchdog.Window Watch;
    }

    private RideRunaway? _rideRunaway;
    private readonly Dictionary<int, double> _warnedRideRunaway = new();
    /// <summary>The ride whose runaway a test holds (<see cref="DebugHoldRunaway"/>), or −1.</summary>
    private int _heldRunawayRide = -1;

    /// <summary>The ride's runaway wagon on the party line now (running or halted), or null.</summary>
    public RunawayWagon? RideRunawayCart => _rideRunaway is { } w && IsInstanceValid(w.Body) ? w.Body : null;
    /// <summary>The ride's runaway runs (not halted, not freed) and its front is short of the split's points.</summary>
    public bool RunawayComing => _rideRunaway is { Halted: < 0f } w && IsInstanceValid(w.Body) && SplitRide == w.Ride && w.Run.FrontS < SplitPointsS;
    /// <summary>Rams this floor build (tests read it).</summary>
    public int RunawayRams { get; private set; }
    /// <summary>The ride runaway's bell rings this floor build (tests read it).</summary>
    internal int RunawayBells { get; private set; }
    /// <summary>The ride runaway's run (tests read where it is and whether it halted), or null.</summary>
    internal CartRun? RideRunawayRun => _rideRunaway is { } w && IsInstanceValid(w.Body) ? w.Run : null;
    /// <summary>The split piece is live (tests read it).</summary>
    internal bool SplitIsLive => _splitLive;

    // ---- the split's marker (Task 6 replaces it with the split itself) ----------------------------

    /// <summary>The split's points on the party line (the layout's points cell centre), or NaN without a split piece.</summary>
    public float SplitPointsS { get; private set; } = float.NaN;
    /// <summary>The split piece's ride, or −1.</summary>
    public int SplitRide { get; private set; } = -1;
    /// <summary>The split piece stands at the layout's points and its spur's end lies over the crevasse (Ruling 3).</summary>
    private bool _splitLive;

    /// <summary>
    /// The "split" piece until Task 6 (every role): a marker at the layout's points (<see cref="Generation.RailSplit.Points"/>, measured on the
    /// party track), live when the piece's At lies within <see cref="SplitRules.AtSlack"/> of them and the spur's end over the crevasse
    /// (<see cref="SplitRules.Live"/>); otherwise it is dead and logged why (no runaway then, Ruling 10).
    /// </summary>
    private Node3D? BuildSplitMarker(Node3D root, Piece piece)
    {
        if (_layout.Rail is not { } rail || !IsInstanceValid(_rails.Party.Track.Track)) return null;
        var track = _rails.Party.Track;
        var half = DungeonBuilder.CellSize / 2f;
        float points = float.NaN, from = float.NaN, to = float.NaN;
        string? dead = null;
        if (rail.Split is not { } split || rail.SplitWhy != "ok") dead = $"the layout has no split (split={rail.SplitWhy ?? "not asked"})";
        else if (split.Ride != piece.Plan.Ride) dead = $"the layout's split is on ride {split.Ride}";
        else if (split.Points < 0 || split.Crevasse.From < 0 || split.Crevasse.To >= rail.Party.Count) dead = "the layout's split lies off the line";
        else
        {
            points = track.ClosestS(_builder.Surface(rail.Party[split.Points]));
            from = track.ClosestS(_builder.Surface(rail.Party[split.Crevasse.From])) - half;
            to = track.ClosestS(_builder.Surface(rail.Party[split.Crevasse.To])) + half;
            if (Mathf.Abs(piece.S - points) > SplitRules.AtSlack) dead = $"its At (S={piece.S:0.0}) lies {Mathf.Abs(piece.S - points):0.0} m from the points, over {SplitRules.AtSlack:0} m";
            else if (!SplitRules.Live(points, from, to)) dead = $"the spur's end S={points + SplitRules.SpurLength:0.0} is not over the crevasse";
        }
        SplitRide = piece.Plan.Ride;
        SplitPointsS = points;
        _splitLive = dead == null;
        GD.Print($"[rails] split {piece.Plan.Id} on ride {piece.Plan.Ride}: points at party S={points:0.0}, crevasse S={from:0.0}..{to:0.0}, spur end S={points + SplitRules.SpurLength:0.0} ({(dead == null ? "live" : $"dead: {dead}")})");
        var marker = new Node3D { Name = $"Split_{piece.Plan.Id}" };
        root.AddChild(marker);
        marker.GlobalPosition = track.Point(float.IsNaN(points) ? piece.S : points);
        return marker;
    }

    /// <summary>Ride <paramref name="ride"/> has a live split (Task 6 tightens it to its built switch).</summary>
    private bool SplitLiveOn(int ride) => _splitLive && SplitRide == ride && !float.IsNaN(SplitPointsS);

    /// <summary>Where the split's seated window opens: 12 m before its points (Ruling 5; Task 6 takes it from its lever), or NaN.</summary>
    private float SplitWindowStartS => SplitPointsS - (SeatedSwitchRules.WindowAhead - SplitLeverPast);
    /// <summary>The split's lever stands this far past its points (Ruling 5's LeverPast, Task 6's).</summary>
    private const float SplitLeverPast = 3f;

    /// <summary>The runaway piece's marker on the party line at its trigger (every role).</summary>
    private Node3D BuildRunawayMarker(Node3D root, Piece piece)
    {
        var marker = new Node3D { Name = $"Runaway_{piece.Plan.Id}" };
        root.AddChild(marker);
        marker.GlobalPosition = _rails.Party.Track.Point(piece.S);
        return marker;
    }

    // ---- test hook -------------------------------------------------------------------------------

    /// <summary>Server test hook (Ruling 22): ride <paramref name="ride"/>'s runaway is never sent on this floor build (one running now goes).</summary>
    internal void DebugHoldRunaway(int ride)
    {
        if (_heldRunawayRide == ride) return;
        _heldRunawayRide = ride;
        if (_rideRunaway is { } w && w.Ride == ride) FreeRideRunaway("held by the test");
    }

    // ---- breaking loose --------------------------------------------------------------------------

    /// <summary>The train's last body's back now (the cart's, behind its coupled wagons).</summary>
    private float TrainRearS => RunawayRules.TrainRearS(_rails.Party.BackS, _rails.Train.Count, RailController.WagonSpacing);

    /// <summary>
    /// The party cart's front first reached ride <paramref name="ride"/>'s runaway piece: the wagon breaks loose behind the train when
    /// <see cref="RunawayRules.Sends"/> holds and its start has room, else the log says why not.
    /// </summary>
    private void ArmRunaway(int ride)
    {
        Piece? piece = null;
        foreach (var p in _pieces) if (p.Plan.Kind == "runaway" && p.Plan.Ride == ride) { piece = p; break; }
        if (piece == null || GetParent() is not Node3D root) return;
        var id = piece.Plan.Id;
        if (_heldRunawayRide == ride) { GD.Print($"[rails] runaway {id}: none (held by the test)"); return; }
        if (_rideRunaway != null) FreeRideRunaway("another breaks loose");
        var wagons = _rails.Train.Count;
        var rear = TrainRearS;
        var start = RunawayRules.StartS(rear, _rails.StopS[ride - 1]);
        var live = SplitLiveOn(ride);
        var chaser = _rails.Chaser != null; // a chaser or the relief cart on the party line
        var window = SplitWindowStartS;
        if (!RunawayRules.Sends(live, chaser, piece.S, window) || float.IsNaN(start))
        {
            var why = !live ? $"no live split on ride {ride}"
                : chaser ? "a chaser on the party line"
                : !(window - piece.S >= RunawayRules.MinLead) ? $"the split's window opens {window - piece.S:0.0} m past the trigger, under {RunawayRules.MinLead:0} m"
                : $"no room behind the train: its start S={rear - RunawayRules.Behind:0.0} lies under {RunawayRules.MinRoom:0} m past stop {ride - 1} (S={_rails.StopS[ride - 1]:0.0})";
            GD.Print($"[rails] runaway {id}: none ({why})");
            return;
        }
        var v = RunawayRules.Target(wagons);
        if (BodyOnTheLineAt(start, v) is { } body)
        {
            GD.Print($"[rails] runaway {id}: none (a body on the line behind the train: {body.Name})");
            return;
        }
        var centre = start - CartRules.HalfLength; // its front at the start
        var wagon = new RunawayWagon { Name = $"RunawayWagon_{id}", CartId = NextCartId() };
        var end = _rails.StopS[Math.Min(ride, _rails.StopS.Length - 1)];
        var run = new CartRun(_rails.Party.Track, wagon, new CartState(centre, v, CartMode.Rolling, CartRules.MaxPlating), 1) { Limit = Mathf.Max(end, centre + 1f) };
        root.AddChild(wagon);
        run.PoseAt(centre);
        run.Sync();
        if (wagon.Sound != null) wagon.Sound.AnnounceOnStart = true; // it rings as it breaks loose
        _rideRunaway = new RideRunaway { Id = id, Ride = ride, Run = run, Body = wagon, Wrecks = _rails.Wrecks, EndS = run.Limit };
        _warnedRideRunaway.Clear();
        EventBus.Instance.Log("A runaway ore wagon breaks loose behind us!");
        RailCalls.Say(Main.Instance.Party, _rails.Cart, BarkTrigger.Runaway, "Runaway on the line behind us — pump!");
        GD.Print($"[rails] t={_now:0.00} runaway {id} breaks loose at party S={start:0.0}, {rear - start:0.0} m behind the train ({wagons} wagon(s)), target {v:0.0} m/s");
    }

    /// <summary>
    /// A body on foot (a hero, an NPC, a creature: <see cref="GatherBodies"/>; anyone riding a cart is carried, never run over) on the party line
    /// where a runaway breaking loose with its front at <paramref name="startFrontS"/> at <paramref name="v"/> would be on it or hit it before its warning
    /// (<see cref="RunawayRules.SpawnBlocked"/>), or null.
    /// </summary>
    private Node3D? BodyOnTheLineAt(float startFrontS, float v)
    {
        GatherBodies(Main.Instance.Party);
        var track = _rails.Party.Track;
        var ahead = Mathf.Max(v, RunOverRules.WarnMinSpeed) * RunawayRules.WarnSeconds;
        float from = startFrontS - 2f * CartRules.HalfLength - 2f, to = startFrontS + ahead + CartRules.HalfLength + 2f;
        var mid = track.Point((from + to) / 2f);
        var reach = (to - from) / 2f + 4f;
        foreach (var body in _bodies)
        {
            if (!IsInstanceValid(body) || body is ICartRider { Cart: not null }) continue;
            var pos = body.GlobalPosition;
            if (pos.DistanceSquaredTo(mid) > reach * reach) continue;
            if (!track.Measure(pos, (from + to) / 2f, (to - from) / 2f, out var b)) continue;
            if (RunawayRules.SpawnBlocked(startFrontS, v, b with { Radius = ReliefRules.BodyRadius })) return body;
        }
        return null;
    }

    // ---- the run ---------------------------------------------------------------------------------

    /// <summary>
    /// The ride's runaway, each physics tick: freed on a wreck or a chaser; stepped at its target toward the train (its limit far ahead while the
    /// train rolls, its halt behind a held one); a ram, the timber and the bodies on its line, the warnings and the bell; its halt's linger.
    /// </summary>
    private void TickRideRunaway(float dt, PartyController party)
    {
        var w = _rideRunaway;
        if (w == null) return;
        if (!IsInstanceValid(w.Body)) { _rideRunaway = null; _warnedRideRunaway.Clear(); return; }
        if (_rails.State.Mode == CartMode.Wrecked || _rails.Wrecks != w.Wrecks) { FreeRideRunaway("the train is wrecked"); return; } // before the event's flush
        if (_rails.Chaser != null) { FreeRideRunaway("a chaser on the party line"); return; }
        if (w.Halted >= 0f)
        {
            if ((w.Halted += dt) >= RunawayLinger) FreeRideRunaway("its halt behind the held train");
            return;
        }
        var run = w.Run;
        var wagons = _rails.Train.Count;
        var rear = TrainRearS;
        var rolling = _rails.State.Mode == CartMode.Rolling;
        run.Limit = Mathf.Max(run.State.S, rolling ? w.EndS : RunawayRules.HaltS(rear)); // a held train: halt behind it (never backwards)
        run.Step(new CartInput(run.SlopeAhead, false, 0, wagons, run.Limit, RunawayRules.Target(wagons)), dt);
        run.Sync();
        run.PoseAt(run.State.S);
        if (RunawayRules.Rams(rolling, run.FrontS, rear)) { Ram(w, rear, party); return; }
        SmashAhead(run, w.Body, enemyTrack: false, riders: null); // timber on the party line ahead of it (ride 4 has none, Ruling 18)
        Warn(run, w.Body, _warnedRideRunaway, true, party); // the call before any hit
        RunOver(run, w.Body.HitAt, RunawayWagon.SourceName, RunawayWagon.HazardIndex, _setAsideParty);
        if (RunawayRules.Rings(run.State.Mode == CartMode.Rolling, run.FrontS, rear) && (w.BellClock += dt) >= RunawayRules.BellEvery)
        {
            w.BellClock = 0f;
            RunawayBells++;
            w.Body.Sound?.RingNow(); // positional: its own player follows it (a client's copy rings by the same rule)
        }
        if (run.State.Mode != CartMode.Rolling)
        {
            if (rolling) { FreeRideRunaway("it ran to the end of its line"); return; }
            w.Halted = 0f;
            GD.Print($"[rails] t={_now:0.00} runaway {w.Id} halts at party S={run.State.S:0.0}, its front {rear - run.FrontS:0.0} m behind the held train");
        }
        else if (OwnProgressStuck(ref w.Watch, run, dt, w.Body)) FreeRideRunaway("stuck");
    }

    /// <summary>The ram: the plating dented (a wreck at 0), the seated heroes who are up hurt (never below 1 HP), the riders shaken in their seats; the runaway derails.</summary>
    private void Ram(RideRunaway w, float rear, PartyController party)
    {
        var before = _rails.State.Plating;
        var wrecks = _rails.Wrecks;
        var at = w.Body.GlobalPosition;
        EventBus.Instance.Log("The runaway wagon rams the train!");
        RunawayRams++;
        _rails.Smash(RunawayRules.RamDent, 1f); // shakes every rider in the seat (a remote one by its relay) and the ground
        RideAudio.At("timber_smash", at); // a client plays its own as its copy goes (RailNet)
        var hit = 0;
        var wrecked = _rails.Wrecks != wrecks; // a spare may have taken over in the same call
        if (!wrecked) // a wreck has its own toll
        {
            _smashedHeroes.Clear();
            foreach (var rider in _rails.Cart.Riders.Values) // the book first: a hit must not change it under the loop
                if (rider is Adventurer { Downed: false } hero && IsInstanceValid(hero)) _smashedHeroes.Add(hero);
            var scale = TierRules.PartyDamageScale;
            foreach (var hero in _smashedHeroes)
            {
                var maxHp = hero.State.Stats[Stat.MaxHp];
                var damage = RunawayRules.RamDamage(maxHp, hero.State.Hp, scale);
                if (damage <= 0f || maxHp <= 0f) continue;
                hero.ReceiveHazard(HazardDamage.RunOver, new HazardOutcome(false, damage / maxHp, 0f), "The runaway", -1);
                hit++;
            }
            _smashedHeroes.Clear();
        }
        w.Body.Rammed = true; // the cart sender tells clients with the Derailed flag
        w.Body.GoneS = w.Run.State.S;
        GD.Print($"[rails] runaway rams the train at S={rear:0.0}: plating {before:0}->{(wrecked ? "0 (wrecked)" : _rails.State.Plating.ToString("0"))}; heroes hit {hit}");
        FreeRideRunaway("it rammed the train and derailed");
    }

    /// <summary>The ride's runaway goes at once (its body freed), saying why in the log.</summary>
    private void FreeRideRunaway(string why)
    {
        var w = _rideRunaway;
        if (w == null) return;
        _rideRunaway = null;
        _warnedRideRunaway.Clear();
        if (IsInstanceValid(w.Body))
        {
            w.Body.Visible = false;
            w.Body.QueueFree();
        }
        GD.Print($"[rails] t={_now:0.00} runaway {w.Id} freed ({why})");
    }
}
