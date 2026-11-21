using DungeonCrawler.Adventure;
using DungeonCrawler.Core;
using DungeonCrawler.Net;
using DungeonCrawler.Party;
using Godot;

namespace DungeonCrawler.Dungeon.Rails;

/// <summary>
/// Ride 4's split (plan 12, Rulings 3-9 and 15): "throw it after you pass". Built from the layout's points (<see cref="Generation.RailSplit.Points"/>,
/// measured on the party track) as a <see cref="SplitSwitch"/>: its spur into the crevasse beyond the slag line, its deadly lever
/// <see cref="SplitRules.LeverPast"/> past the points. The seat offers the lever while the ride's runaway is coming, from the window's start
/// (the points less the straight before them, ruling R-bend) until the runaway's front takes the points (<see cref="SplitRules.Offered"/>).
/// <list type="bullet">
/// <item>The party's branch is decided as its front reaches the points (<see cref="SplitRules.PartyRoute"/>): thrown with a human aboard able to
/// throw it, the spur (<see cref="RailController.Cross"/> onto the slag line, no return), and the train falls with its front
/// <see cref="SplitRules.FallPast"/> past the points; thrown with nobody able, the main line (ruling R-lone, the lever stays thrown); straight,
/// the main line.</item>
/// <item>A throw pressed short of the points within the hold zone (the whole window before them, at least <see cref="SplitRules.HoldLead"/>) is held
/// and applied the instant after the train passes straight (<see cref="TrackSwitch.HoldThrow"/>): a press before the points never sends it down the spur.</item>
/// <item>The runaway takes the lever as it stands when its own front reaches the points (Ruling 8): thrown, it goes down the spur and off its end
/// into the crevasse, quietly (<see cref="PlungeOffSpur"/>); straight, it stays on the party line behind the train. Either way the lever is taken.</item>
/// <item>A companion calls it (<see cref="SplitRules.Call"/>): wait at the window's start, not yet for a lever thrown short of the hold zone, now
/// a metre past the points; nobody but a human moves it.</item>
/// <item>The watchdog sets a thrown split back before freeing a train stuck short of its points (<see cref="WatchdogSplit"/>), so it never grinds
/// onto the spur; plan 8's set-back guard applies only past the points (<see cref="GuardsSetBack"/>).</item>
/// </list>
/// A split whose piece stands too far from the layout's points, whose spur's end is not over the crevasse, or whose fall would not be beside it,
/// is dead: no spur, no lever, and no runaway (Ruling 3).
/// </summary>
public partial class RailEncounterController
{
    /// <summary>The split's points on the party line (the layout's points cell centre), or NaN without a split piece.</summary>
    public float SplitPointsS { get; private set; } = float.NaN;
    /// <summary>The split piece's ride, or −1.</summary>
    public int SplitRide { get; private set; } = -1;
    /// <summary>The built (live) split, or null (none, or dead).</summary>
    private SplitSwitch? _split;
    private Piece? _splitPiece;
    /// <summary>The live split (tests read it), or null.</summary>
    internal SplitSwitch? SplitSwitchNode => _split;
    /// <summary>The split is built live (tests read it).</summary>
    internal bool SplitIsLive => _split != null;
    /// <summary>The party's train took the spur this arming: it falls at the points + <see cref="SplitRules.FallPast"/>.</summary>
    private bool _partyOnSpur;
    /// <summary>The split's three calls made this arming.</summary>
    private bool _splitWaitCalled, _splitNotYetCalled, _splitNowCalled;
    /// <summary>How far short of the points the throw now held was pressed.</summary>
    private float _splitHeldShort = float.NaN;
    /// <summary>Runaways that went off the spur into the crevasse this floor build (tests read it).</summary>
    internal int SplitPlunges { get; private set; }

    /// <summary>Ride <paramref name="ride"/> has a live split (its switch built).</summary>
    private bool SplitLiveOn(int ride) => _split != null && SplitRide == ride && !float.IsNaN(SplitPointsS);
    /// <summary>Where the split's seated window opens (ruling R-bend: the points less the straight before them, at most 12 m), or NaN.</summary>
    internal float SplitWindowStartS => _split?.WindowStartS ?? float.NaN;

    // ---- building --------------------------------------------------------------------------------

    /// <summary>
    /// The "split" piece (every role): live when it stands within <see cref="SplitRules.AtSlack"/> of the layout's points, the spur's end lies
    /// over the crevasse (<see cref="SplitRules.Live"/>, and the cell under the built end is a drop) and the slag line beside the fall's place
    /// borders it; then the spur, the lever and the lamps. Dead, a marker only, and the log says why (no runaway then, Ruling 10).
    /// </summary>
    private Node3D? BuildSplit(Node3D root, Piece piece)
    {
        if (_layout.Rail is not { } rail || !IsInstanceValid(_rails.Party.Track.Track)) return null;
        var track = _rails.Party.Track;
        var half = DungeonBuilder.CellSize / 2f;
        float points = float.NaN, from = float.NaN, to = float.NaN;
        string? dead = null;
        if (rail.Split is not { } split || rail.SplitWhy != "ok") dead = $"the layout has no split (split={rail.SplitWhy ?? "not asked"})";
        else if (split.Ride != piece.Plan.Ride) dead = $"the layout's split is on ride {split.Ride}";
        else if (split.Points < 0 || split.Crevasse.From < 0 || split.Crevasse.To >= rail.Party.Count) dead = "the layout's split lies off the line";
        else if (!_enemy.Valid) dead = "there is no slag line";
        else
        {
            points = track.ClosestS(_builder.Surface(rail.Party[split.Points]));
            from = track.ClosestS(_builder.Surface(rail.Party[split.Crevasse.From])) - half;
            to = track.ClosestS(_builder.Surface(rail.Party[split.Crevasse.To])) + half;
            if (Mathf.Abs(piece.S - points) > SplitRules.AtSlack) dead = $"its At (S={piece.S:0.0}) lies {Mathf.Abs(piece.S - points):0.0} m from the points, over {SplitRules.AtSlack:0} m";
            else if (!SplitRules.Live(points, from, to)) dead = $"the spur's end S={points + SplitRules.SpurLength:0.0} is not over the crevasse";
            else if (!BesideTheDrop(points + SplitRules.FallPast)) dead = $"the slag line at the fall's place (S={points + SplitRules.FallPast:0.0}) is not beside the crevasse";
        }
        SplitRide = piece.Plan.Ride;
        SplitPointsS = points;
        SplitSwitch? built = null;
        List<Vector3>? path = null;
        if (dead == null)
        {
            built = new SplitSwitch
            {
                Name = $"Split_{piece.Plan.Id}", PointsS = points, LeverRef = NetRefs.Switch(piece.Index),
                Offered = () => SplitOffered(piece), Accepts = () => SplitAccepts(piece), Hold = SplitHolds, Past = () => _rails.Party.FrontS >= SplitPointsS, Stands = LeverStands,
            };
            root.AddChild(built);
            path = built.Build(track, _enemy, Lanes, _steel ??= DungeonBuilder.RailSteel(), _sleeper ??= DungeonBuilder.RailSleeper());
            var endCell = DungeonBuilder.WorldToCell(built.SpurEnd);
            if (!_layout.IsChasm(endCell)) dead = $"the built spur's end {endCell} is not over the crevasse";
            else if (!built.LeverPlaced) dead = "its lever has no walkable place clear of the spur";
            if (dead != null)
            {
                built.QueueFree();
                built = null;
            }
        }
        var id = piece.Plan.Id;
        GD.Print($"[rails] split {id}: points at party S={points:0.0}, lever at S={(built?.LeverS ?? points + SplitRules.LeverPast):0.0}, spur {(built?.Sampler.Length ?? SplitRules.SpurLength):0.0} m, its end over the crevasse S={from:0.0}..{to:0.0} ({(dead == null ? "live" : $"dead: {dead}")})");
        if (built == null)
        {
            var marker = new Node3D { Name = $"Split_{id}" };
            root.AddChild(marker);
            marker.GlobalPosition = track.Point(float.IsNaN(points) ? piece.S : points);
            return marker;
        }
        ClearTimber(root, path!);
        var lever = built.Lever;
        GD.Print($"[rails] split {id}: the straight before the points {built.StraightBefore:0.0} m, the seated window from S={built.WindowStartS:0.0} ({built.WindowAhead:0.0} m short) until the runaway takes the points; throws held from {built.HoldReach:0.0} m short; lever {built.LeverOutUsed:0.0} m inward at S={built.LeverS:0.0} on cell {DungeonBuilder.WorldToCell(lever.GlobalPosition)} (walkable, no drop, clear of the spur); spur end cell {DungeonBuilder.WorldToCell(built.SpurEnd)} (a drop)");
        piece.Split = built;
        piece.Switch = lever; // armed and reset with the other levers; on the wire as a switch (ThrownSwitches, PropEvt)
        _split = built;
        _splitPiece = piece;
        return built;
    }

    /// <summary>The slag line's cell beside party S <paramref name="partyS"/> borders a drop (the fall there is beside the crevasse).</summary>
    private bool BesideTheDrop(float partyS)
    {
        var cell = DungeonBuilder.WorldToCell(_enemy.Point(Lanes.EnemyS(partyS)));
        foreach (var n in cell.Neighbours4())
            if (_layout.IsChasm(n)) return true;
        return false;
    }

    /// <summary>The split's lever may stand at <paramref name="at"/>: walkable floor, not a drop.</summary>
    private bool LeverStands(Vector3 at)
    {
        var cell = DungeonBuilder.WorldToCell(at);
        return _layout.IsWalkable(cell) && !_layout.IsChasm(cell);
    }

    /// <summary>
    /// The split's lever is offered now (Ruling 5), every peer: its ride armed, the ride's runaway coming (a client: a runaway copy on the party
    /// line short of the points, as the server sends it), not taken, the front in the window (ruling R-bend).
    /// </summary>
    private bool SplitOffered(Piece p)
    {
        if (_split is not { } split) return false;
        return SplitRules.Offered(SplitArmed(p), SplitRunawayComing, _rails.Party.FrontS, SplitPointsS, split.Lever.Taken, split.WindowAhead);
    }

    /// <summary>Server and solo: a press on the split's lever is taken now (<see cref="SplitRules.Accepts"/>: the offer, and the early lead before it, held).</summary>
    private bool SplitAccepts(Piece p)
    {
        if (_split is not { } split) return false;
        return SplitRules.Accepts(SplitArmed(p), SplitRunawayComing, _rails.Party.FrontS, SplitPointsS, split.Lever.Taken, split.WindowAhead);
    }

    private bool SplitArmed(Piece p) => ArmedRide == p.Plan.Ride && SplitLiveOn(p.Plan.Ride);

    /// <summary>The ride's runaway is coming to the split's points (a client: a runaway copy on the party line short of them, as the server sends it).</summary>
    private bool SplitRunawayComing => NetSession.IsClient ? RailNet.Instance is { } net && IsInstanceValid(net) && net.ClientRunawayShortOf(SplitPointsS) : RunawayComing;

    /// <summary>
    /// Server and solo: a throw pressed now is held to the points: in the hold zone short of them (<see cref="SplitRules.HoldPress"/>), and at or past
    /// them while the party's branch is still undecided (review M1: whatever order a press and the decision come in, it is never taken before the
    /// branch). How far short is kept for the log.
    /// </summary>
    private bool SplitHolds(Adventurer actor)
    {
        if (_split is not { } split) return false;
        var front = _rails.Party.FrontS;
        var undecided = _splitPiece is { Fired: false } && ArmedRide == SplitRide;
        if (!SplitRules.HoldPress(true, front, SplitPointsS, split.WindowAhead) && !(undecided && front >= SplitPointsS)) return false;
        _splitHeldShort = SplitPointsS - front;
        return true;
    }

    /// <summary>The split's ride arms: its calls and the party's spur start over.</summary>
    private void ArmSplit(int ride)
    {
        if (ride != SplitRide) return;
        _partyOnSpur = false;
        _splitResetWaits = false;
        _splitWaitCalled = _splitNotYetCalled = _splitNowCalled = false;
        _splitHeldShort = float.NaN;
        _split?.Lever.DropHeld();
    }

    // ---- ticking (server and solo) ---------------------------------------------------------------

    /// <summary>
    /// Each tick of the armed ride: the party's fall off the spur, the calls, and the party's points (its branch; the held throw after a straight
    /// pass; the safe line with nobody able to set it back).
    /// </summary>
    private void TickSplit(Piece piece, PartyController party, bool rolling)
    {
        if (_split is not { } split || piece.Switch is not { } lever) return;
        var front = _rails.Party.FrontS;
        var id = piece.Plan.Id;
        if (_partyOnSpur)
        {
            if (!rolling || front < SplitPointsS + SplitRules.FallPast) return;
            _partyOnSpur = false;
            _rails.Fall("The cart runs off the spur into the crevasse!");
            GD.Print($"[rails] split {id}: the train runs off the spur into the crevasse with its front at S={front:0.0} (the points + {front - SplitPointsS:0.0} m)");
            return;
        }
        if (!rolling) return;
        var call = SplitRules.Call(RunawayComing, lever.Thrown, front, SplitPointsS, _splitWaitCalled, _splitNotYetCalled, _splitNowCalled, split.WindowAhead);
        if (call != SplitCall.None)
        {
            string? by;
            switch (call)
            {
                case SplitCall.Wait:
                    _splitWaitCalled = true;
                    by = RailCalls.Say(party, _rails.Cart, BarkTrigger.SplitWait, "Let us pass the split, then throw it!");
                    break;
                case SplitCall.NotYet:
                    _splitNotYetCalled = true;
                    by = RailCalls.Say(party, _rails.Cart, BarkTrigger.SplitNotYet, "Not yet — set it back, that's the spur!");
                    break;
                default:
                    _splitNowCalled = true;
                    by = RailCalls.Say(party, _rails.Cart, BarkTrigger.SplitNow, "Now — throw the split!");
                    break;
            }
            GD.Print($"[rails] split {id} called {(call == SplitCall.Wait ? "wait" : call == SplitCall.NotYet ? "not yet" : "now")} at front S={front:0.0}; bark={by ?? "none"}");
        }
        if (piece.Fired || front < SplitPointsS) return;
        piece.Fired = true; // the party's points: the lever stays live for the runaway behind until it takes them
        var able = AbleHumanAboard();
        if (SplitRules.PartyRoute(lever.Thrown, able) == SplitRoute.Spur)
        {
            lever.DropHeld();
            _rails.Cross(SplitPointsS, float.NaN);
            _partyOnSpur = true;
            EventBus.Instance.Log("The cart takes the spur!");
            GD.Print($"[rails] split {id}: the train takes the spur at S={_rails.State.S:0.0} (front {front:0.0}), thrown by {lever.LastToggledBy ?? "nobody's hand"}");
            return;
        }
        if (lever.Thrown)
        {
            EventBus.Instance.Log(SplitRules.SafeLineLog);
            GD.Print($"[rails] split {id}: thrown with no human aboard able to set it back: the main line, the lever left thrown");
        }
        GD.Print($"[rails] split {id}: the train passes straight at S={_rails.State.S:0.0} (front {front:0.0})");
        if (lever.HeldBy is { } held && lever.ThrowHeld(held))
            GD.Print($"[rails] split {id}: {held}'s throw held to the points ({_splitHeldShort:0.0} m short): thrown behind the train at front S={front:0.0}");
        _splitHeldShort = float.NaN;
    }

    /// <summary>
    /// The ride's runaway after its step on the party line (Ruling 8): its front reached the split's points, so it takes the lever as it stands
    /// (taken now: nothing left to pull); thrown, it goes onto the spur's own track once its middle is at the points. True when it is on the spur.
    /// </summary>
    private bool TakeSplitPoints(RideRunaway w)
    {
        if (_split is not { } split || w.Ride != SplitRide) return false;
        var run = w.Run;
        if (!w.PastSplit && run.FrontS >= SplitPointsS)
        {
            w.PastSplit = true;
            w.ToSpur = split.Lever.Thrown;
            split.Lever.DropHeld();
            split.Lever.Taken = true; // told ("taken"): the window ends on every peer
            GD.Print($"[rails] split {_splitPiece?.Plan.Id}: the runaway {(w.ToSpur ? "takes the spur" : "passes straight")} at S={run.FrontS:0.0}");
            ResetSplitIfWaiting("the runaway took its points"); // its route is decided: the arrival's reset may go in now
        }
        if (!w.ToSpur || w.OnSpur || run.State.S < SplitPointsS) return w.OnSpur;
        var st = run.State;
        w.Run = new CartRun(split.Sampler, w.Body, new CartState(st.S - SplitPointsS, Mathf.Max(st.V, 2f), CartMode.Rolling, st.Plating)) { Limit = split.Sampler.Length + 100f }; // no braking: it runs off the end
        w.Run.PoseAt(w.Run.State.S);
        w.Run.Sync();
        w.OnSpur = true;
        return true;
    }

    /// <summary>The runaway on the spur: it coasts off its end, running over nothing it could not before, and goes into the crevasse there.</summary>
    private void TickRunawayOnSpur(RideRunaway w, float dt, PartyController party)
    {
        if (_split is not { } split) { FreeRideRunaway("the split is gone"); return; }
        var run = w.Run;
        run.Step(new CartInput(run.SlopeAhead, false, 0, 0, run.Limit, run.State.V), dt);
        run.Sync();
        run.PoseAt(run.State.S);
        Warn(run, w.Body, _warnedRideRunaway, true, party);
        RunOver(run, w.Body.HitAt, RunawayWagon.SourceName, RunawayWagon.HazardIndex, _setAsideParty);
        if (SplitRules.PlungedOffSpur(run.State.S, split.Sampler.Length)) PlungeOffSpur(w);
    }

    /// <summary>
    /// The runaway goes off the spur's end into the crevasse, quietly (plan 11's quiet plunge): a thud and the crash where it goes, told in the log
    /// only with a hero within <see cref="SwitchBackRules.PlungeHeard"/>; it carries nobody. Clients read it from its last S on the spur's track.
    /// </summary>
    private void PlungeOffSpur(RideRunaway w)
    {
        var at = w.Body.GlobalPosition;
        var nearest = NearestHeroTo(at);
        var told = SwitchBackRules.PlungeTold(nearest);
        if (told) EventBus.Instance.Log("The runaway goes off the spur into the crevasse!");
        if (GetParent() is Node3D root) Audio.Synth.PlayAt(root, Audio.Synth.Thud(), at, -1f);
        RideAudio.At("cart_crash", at); // positional: heard only near the drop
        w.Body.Plunged = true; // the cart sender tells clients with the Derailed flag on the spur's track, at GoneS
        w.Body.GoneS = w.Run.State.S;
        SplitPlunges++;
        GD.Print($"[rails] t={_now:0.00} runaway {w.Id} goes off the split's spur into the crevasse at spur S={w.Run.State.S:0.0} of {_split?.Sampler.Length ?? 0f:0.0}; nearest hero {nearest:0.0} m{(told ? ", told" : ", unseen")}");
        FreeRideRunaway("it went off the spur into the crevasse");
    }

    /// <summary>Client: a runaway the server took off the spur's track <paramref name="track"/> at spur S <paramref name="goneS"/> went into the crevasse (<see cref="SplitRules.PlungedOffSpur"/>).</summary>
    internal bool SplitPlungedAt(byte track, float goneS) => _split is { } split && track == SpurTrackNumber(split) && SplitRules.PlungedOffSpur(goneS, split.Sampler.Length);

    // ---- the arrival's reset (review M2) ---------------------------------------------------------

    /// <summary>The arrival's reset of the split's lever waits for the runaway (<see cref="SplitResetWaits"/>).</summary>
    private bool _splitResetWaits;

    /// <summary>
    /// An arrival resets the finished rides' levers (<see cref="ResetSwitchyard"/>); the split's waits while the ride's runaway is live and has
    /// not reached its points, so a correct throw behind the train still sends it down the spur. It is reset when the runaway takes the points
    /// or is freed. True when it waits.
    /// </summary>
    private bool SplitResetWaits(Piece p)
    {
        if (p.Split == null || _rideRunaway is not { PastSplit: false } w || !IsInstanceValid(w.Body) || w.Ride != SplitRide) return false;
        _splitResetWaits = true;
        GD.Print($"[rails] split {p.Plan.Id}: the arrival's reset waits for the runaway (its front at S={w.Run.FrontS:0.0}, short of the points; the lever thrown={p.Switch?.Thrown})");
        return true;
    }

    /// <summary>The split's lever is reset now if an arrival left its reset waiting (<see cref="SplitResetWaits"/>).</summary>
    private void ResetSplitIfWaiting(string why)
    {
        if (!_splitResetWaits) return;
        _splitResetWaits = false;
        if (_split?.Lever is not { } lever || !IsInstanceValid(lever)) return;
        lever.Reset();
        GD.Print($"[rails] split {_splitPiece?.Plan.Id}: the arrival's reset done ({why})");
    }

    // ---- the watchdog and the server's guard -----------------------------------------------------

    /// <summary>
    /// The watchdog's view of the split (Ruling 15, server): a train stuck with its front short of the split's points and the lever thrown (or a
    /// throw held to them) has it set straight first, so it never grinds onto the spur; then it is freed as ever. True when it was set back.
    /// </summary>
    internal bool WatchdogSplit()
    {
        if (_split is not { } split || _splitPiece is not { Fired: false } || ArmedRide != SplitRide) return false;
        var front = _rails.Party.FrontS;
        if (!(front < SplitPointsS)) return false;
        var held = split.Lever.HeldBy != null;
        if (!split.Lever.SetBack() && !held) return false;
        GD.Print($"[rails] watchdog: the split set back (front S={front:0.0}, {SplitPointsS - front:0.0} m short of its points{(held ? ", a held throw dropped" : "")})");
        return true;
    }

    /// <summary>Server: plan 8's seated set-back guard applies to <paramref name="lever"/>: any lever but the split's, and the split's only past its points (Ruling 7).</summary>
    internal bool GuardsSetBack(TrackSwitch lever) => _split == null || lever != _split.Lever || SplitRules.GuardsSetBack(_rails.Party.FrontS, SplitPointsS);
}
