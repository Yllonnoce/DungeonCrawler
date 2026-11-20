using DungeonCrawler.Adventure;
using DungeonCrawler.Core;
using DungeonCrawler.Dungeon.Generation;
using DungeonCrawler.Dungeon.Props;
using DungeonCrawler.Enemies;
using DungeonCrawler.Net;
using DungeonCrawler.Party;
using Godot;

namespace DungeonCrawler.Dungeon.Rails;

/// <summary>
/// Runs a railed floor's authored rides (<see cref="FloorContents.RailEncounters"/>, <see cref="FloorContents.RailSetPieces"/>):
/// a node under the floor root, made after the objective when the floor has a <see cref="RailController"/> and either list.
/// Ride k is the stretch from stop k−1 to stop k: the party cart's departure from stop k−1 arms ride k's items, and each
/// starts once the cart passes its fraction At of the ride. The cart reaching a stop, being wrecked or restored, or the
/// party going on foot clears the ride's live items (slag carts and their riders, silently). The rides are the server's: a
/// client builds the set pieces as visual copies (the same nodes, the same collision) and shows the state the server tells
/// (PropEvt, the prop check's rail part), and judges, arms and ticks nothing.
/// <para>Slag carts (kind "slag_cart") roll up the enemy line from behind their pace point and hold pace beside the party's
/// cart (<see cref="PaceRules"/>), their riders carried; each runs over whoever stands on the enemy lane and rings its bell
/// first. Other kinds come with later tasks (<see cref="Spawn"/>).</para>
/// </summary>
public partial class RailEncounterController : Node
{
    /// <summary>A slag cart starts this far behind the point it paces at, so it is seen rolling up rather than appearing beside the party.</summary>
    public const float SpawnBehind = 10f;
    /// <summary>A slag cart this close to the end of the enemy line has run off it: freed.</summary>
    const float LineEnd = 1f;
    const float WarnSeconds = 3f;
    const float WarnEvery = 4f;

    private DungeonBuilder _builder = null!;
    private FloorLayout _layout = null!;
    private RailController _rails = null!;
    private RailTrack _enemyTrack = null!;
    private TrackSampler _enemy = null!;
    /// <summary>The party lane's S against the enemy lane's.</summary>
    public LaneMap Lanes { get; private set; } = null!;
    private IReadOnlyList<RailEncounterPlan> _encounters = Array.Empty<RailEncounterPlan>();
    private IReadOnlyList<RailSetPiecePlan> _setPieces = Array.Empty<RailSetPiecePlan>();
    /// <summary>The ride whose items wait for the cart (1-based), or −1 when none is armed. A client's is the server's (the party cart's state).</summary>
    public int ArmedRide { get; private set; } = -1;
    /// <summary>The server's clock (<c>NetSession.Instance.Now</c>) when <see cref="ArmedRide"/> armed; a client's buckets run from it.</summary>
    public double ArmedAt { get; private set; }
    private bool[] _started = Array.Empty<bool>();
    private bool[] _lineShown = Array.Empty<bool>();
    private readonly List<SlagCart> _carts = new();
    private readonly List<Node3D> _bodies = new();
    private readonly List<(Node3D body, TrackBody tb)> _hits = new();
    private readonly Dictionary<int, double> _warnedAt = new();
    private readonly Func<Vector3, Vector3> _setAside;
    private readonly Func<Vector3, Vector3> _setAsideParty;
    private double _now;

    /// <summary>The slag carts on the line now (live, not derailed), for tests and later tasks.</summary>
    public IReadOnlyList<SlagCart> SlagCarts => _carts;

    /// <summary>The controller of the floor built now (the resolver finds the set pieces through it), or null.</summary>
    public static RailEncounterController? Instance { get; private set; }

    public RailEncounterController()
    {
        Name = "RailEncounterController";
        _setAside = pos => _rails.SetAsideBesideEnemyLane(pos);
        _setAsideParty = pos => _rails.StandBesideLane(pos);
    }

    /// <summary>Read the floor's ride lists and map the lanes. Call before adding the controller to the tree.</summary>
    public void Setup(DungeonBuilder builder, FloorLayout layout, RailController rails, RailTrack enemyTrack, FloorContents contents)
    {
        _builder = builder;
        _layout = layout;
        _rails = rails;
        _enemyTrack = enemyTrack;
        _enemy = new TrackSampler(enemyTrack);
        Lanes = new LaneMap(rails.Party.Track, _enemy);
        rails.UseLanes(_enemy, Lanes); // a thrown switch takes the party's cart across
        _encounters = contents.RailEncounters;
        _setPieces = contents.RailSetPieces;
        _started = new bool[_encounters.Count];
        _lineShown = new bool[_encounters.Count];
    }

    public override void _EnterTree() => Instance = this;

    public override void _Ready()
    {
        _rails.CartEvents += OnCartEvent;
        BuildSetPieces(); // every role: a client's copies are dressing that shows what the server tells
        WarmModels(ride: null); // every role (a client spawns the server's riders too): the floor's creatures read on a loader thread while the party walks to the cart
        if (_encounters.Any(e => e.Kind == "slag_cart") && _enemy.Valid) SlagCart.Prewarm(_enemy); // every role: a client builds the server's carts as dressing
    }

    /// <summary>Have the models of the creatures the rides spawn (ride <paramref name="ride"/>'s only, when given) read on a loader thread, so no spawn reads one from disk.</summary>
    private void WarmModels(int? ride)
    {
        foreach (var id in RailWarmup.EnemyIds(_encounters, ride))
            if (_builder.EnemyProfile(id) is { } profile) Enemies.EnemyModels.Warm(profile.Model);
    }

    /// <summary>
    /// Have ride <paramref name="ride"/>'s encounter lines read on a loader thread: each plays as the cart comes up to it. At the arming
    /// (solo and a dedicated server, whose director reads each narrated line's length to time what follows it, <c>AdventureDirector.Heard</c>),
    /// and on a co-op client, which plays them, when its cart's state names the ride armed (<see cref="ApplyArmNet"/>).
    /// </summary>
    private void WarmLines(int ride)
    {
        if (Adventure.VoiceRenderer.Instance is not { } voice) return;
        foreach (var id in RailWarmup.VoiceIds(_encounters, ride)) voice.Prefetch(id);
    }

    public override void _ExitTree()
    {
        if (Instance == this) Instance = null;
        if (IsInstanceValid(_rails)) _rails.CartEvents -= OnCartEvent;
        ReleaseSeekers(); // a hero must not keep walking to a refuge on a floor that is gone
    }

    /// <summary>The floor this controller belongs to is still the one built (a cleared floor waits a frame to be freed).</summary>
    private bool Current => Main.Instance is { InGame: true } main && main.Builder.EnemyTrack == _enemyTrack && IsInstanceValid(_rails);

    // ---- rides -----------------------------------------------------------------------------------

    private void OnCartEvent(CartEvent e)
    {
        if (NetSession.IsClient) return;
        switch (e.Kind)
        {
            case CartEventKind.Departed:
                ClearRide();
                Arm(e.Stop + 1);
                break;
            case CartEventKind.Wrecked:
                ClearRide(wrecked: true);
                ResetSwitchyard(undone: true);
                ResetKiln(e.Kind);
                break;
            case CartEventKind.Restored:
                ClearRide(loaded: true);
                ClearThrowers();
                ResetSwitchyard(undone: true);
                ResetKiln(e.Kind);
                break;
            case CartEventKind.Arrived:
                ClearRide();
                ResetSwitchyard(undone: false, upToRide: e.Stop); // the finished rides' levers only: one thrown ahead for a later ride stays thrown
                break;
            case CartEventKind.OnFoot:
                ClearRide();
                ResetSwitchyard(undone: false);
                break;
        }
    }

    /// <summary>Ride <paramref name="ride"/>'s items wait for the cart again (a ride after a wreck runs again from its start).</summary>
    private void Arm(int ride)
    {
        ArmedRide = ride >= 1 && ride < _rails.StopS.Length ? ride : -1;
        ArmedAt = NetSession.Instance?.Now ?? 0;
        if (ArmedRide >= 1) FreeTraffic(); // the ride has the enemy line: a traffic cart in the way goes, so the two are never live together
        for (var i = 0; i < _encounters.Count; i++)
            if (_encounters[i].Ride == ride) _started[i] = false;
        if (ArmedRide < 1) return;
        WarmModels(ArmedRide); // nothing to do once the floor's warm-up is in: a model is kept for the session
        WarmLines(ArmedRide);
        ArmStanders(ArmedRide);
    }

    /// <summary>The ride is over (or undone): its live items go, the riders without a death or a word.</summary>
    private void ClearRide(bool wrecked = false, bool loaded = false)
    {
        ArmedRide = -1;
        ClearStanders();
        ClearSwitchyard();
        ClearLeaps(wrecked); // boarders first: they are not on a slag cart's list
        ClearKiln(payOff: !loaded); // the golem goes like a rider (its corpse stays); a thrown spur lever sends it off the spur first
        foreach (var cart in _carts) Free(cart);
        _carts.Clear();
        _warnedAt.Clear();
    }

    private static void Free(SlagCart cart)
    {
        if (!IsInstanceValid(cart)) return;
        foreach (var rider in cart.Aboard)
            if (IsInstanceValid(rider) && !rider.IsQueuedForDeletion()) rider.QueueFree();
        cart.Aboard.Clear();
        cart.QueueFree();
    }

    /// <summary>The party cart's S where ride <paramref name="ride"/>'s fraction <paramref name="at"/> lies.</summary>
    private float RideS(int ride, float at)
    {
        float from = _rails.StopS[ride - 1], to = _rails.StopS[ride];
        return from + at * (to - from);
    }

    /// <summary>Start the armed ride's items the cart has just reached.</summary>
    private void StartReached()
    {
        if (ArmedRide < 1 || _rails.State.Mode != CartMode.Rolling) return;
        var s = _rails.State.S;
        for (var i = 0; i < _encounters.Count; i++)
        {
            var enc = _encounters[i];
            if (enc.Ride != ArmedRide || _started[i] || s < RideS(enc.Ride, enc.At)) continue;
            _started[i] = true;
            Spawn(i, enc);
        }
    }

    /// <summary>One encounter starts, by its kind, as the cart reaches it. "lone" and "thrower" are there from the ride's departure instead (<see cref="ArmStanders"/>).</summary>
    private void Spawn(int index, RailEncounterPlan enc)
    {
        switch (enc.Kind)
        {
            case "slag_cart":
                SpawnSlagCart(index, enc);
                break;
            case "golem":
                SpawnGolem(index, enc); // it bursts out of the kiln wall beside the cart as the cart reaches its At
                break;
        }
    }

    private void ShowLine(int index, RailEncounterPlan enc)
    {
        if (_lineShown[index] || enc.Line is not { } line || string.IsNullOrWhiteSpace(line.Text)) return;
        _lineShown[index] = true;
        EventBus.Instance.RaiseNarration(line.Text, line.VoiceId); // the narrator's, in its recording once the voice pass gives it one
    }

    // ---- slag carts ------------------------------------------------------------------------------

    /// <summary>
    /// A slag cart rolls up the enemy line <see cref="SpawnBehind"/> short of its pace point (the party cart's place on that
    /// lane plus the offset) at the party's speed, its riders carried on the heap and awake.
    /// </summary>
    private void SpawnSlagCart(int index, RailEncounterPlan enc)
    {
        if (GetParent() is not Node3D root || !_enemy.Valid) return;
        var partyS = _rails.State.S;
        var paceS = PaceRules.TargetS(Lanes.EnemyS(partyS), enc.Offset);
        var s = Mathf.Clamp(paceS - SpawnBehind, 0f, Mathf.Max(0f, _enemy.Length - LineEnd - 1f));
        if (_rails.FrontOnEnemyLane) s = Mathf.Min(Mathf.Max(paceS, Lanes.EnemyS(_rails.Party.FrontS) + CartRules.HalfLength + SpawnClear), Mathf.Max(0f, _enemy.Length - LineEnd - 1f)); // the party's cart is on this lane: seen ahead of it, never in it
        var v = _rails.State.V * Lanes.Stretch(partyS);
        var cart = new SlagCart { Name = $"SlagCart_{enc.Id}", EncounterId = enc.Id, InnerSide = InnerSideAt(s), CartId = NextCartId() };
        cart.Setup(_enemy, new CartState(s, v, CartMode.Rolling, CartRules.MaxPlating), enc.Offset);
        root.AddChild(cart);
        cart.Run.PoseAt(s);
        cart.Run.Sync();
        foreach (var member in enc.Riders)
            for (var n = 0; n < member.Count && cart.Aboard.Count < SlagCart.MaxRiders; n++)
            {
                var spot = cart.RiderSpot(cart.Aboard.Count).Origin;
                if (_builder.SpawnEnemy(member.EnemyId, spot, _builder.CurrentFloor) is not { } rider) continue;
                rider.IsReinforcement = true; // a ride re-armed after a wreck or a load sends them again: they must not be a loot farm
                cart.Board(rider);
                rider.ForceAggro(); // it has seen the cart it hunts
            }
        _carts.Add(cart);
        GD.Print($"[rails] slag cart {enc.Id} (ride {enc.Ride}) on the enemy line at S={s:0.0} (pace point {paceS:0.0}, party S={partyS:0.0}) riders=[{string.Join(", ", cart.Aboard.Select(r => r.Name + ":" + r.Profile.Name))}]");
        ShowLine(index, enc);
    }

    /// <summary>Which side of the enemy track (at <paramref name="enemyS"/>) the party's lane lies on: +1 the track frame's +X.</summary>
    private int InnerSideAt(float enemyS)
    {
        var frame = _enemy.Frame(enemyS);
        var party = _rails.Party.Track.Point(Lanes.PartyS(enemyS));
        return (party - frame.Origin).Dot(frame.Basis.X) >= 0f ? 1 : -1;
    }

    public override void _PhysicsProcess(double delta)
    {
        _now += delta;
        if (NetSession.IsClient || !Current) return;
        var dt = (float)delta;
        StartReached();
        TickSetPieces(Main.Instance.Party, dt);
        TickGolem(dt);
        TickLeaps(dt); // boarders outlive the slag cart that threw them
        TickTrafficClocks(dt); // may start a traffic cart or a runaway wagon
        if (_carts.Count == 0 && _traffic == null && _runaway == null)
        {
            TickSeekers();
            return;
        }
        var party = Main.Instance.Party;
        GatherBodies(party);
        for (var i = _carts.Count - 1; i >= 0; i--)
        {
            var cart = _carts[i];
            if (!IsInstanceValid(cart) || cart.Derailed) { _carts.RemoveAt(i); continue; }
            if (Ram(cart)) { _carts.RemoveAt(i); continue; } // before it moves or sweeps: a rammed cart does nothing more
            Pace(cart, dt);
            RunOver(cart);
            Warn(cart, party);
            if (cart.Run.State.S >= _enemy.Length - LineEnd || cart.Run.State.Mode != CartMode.Rolling || PacingStuck(cart, dt)) { Free(cart); _carts.RemoveAt(i); }
        }
        TickTraffic(dt, party);
        TickRunaway(dt, party);
        TickSeekers();
    }

    /// <summary>Hold pace with the party's cart: aim at the pace point, at the party's speed carried over to this lane plus a capped correction.</summary>
    private void Pace(SlagCart cart, float dt)
    {
        PaceRun(cart.Run, cart.Offset, dt);
        cart.FollowRiders();
    }

    /// <summary>Step <paramref name="run"/> on the enemy line toward its pace point <paramref name="offset"/> metres from the party's cart, and pose it for this tick's physics.</summary>
    private void PaceRun(CartRun run, float offset, float dt)
    {
        var partyS = _rails.State.S;
        var partyV = _rails.State.Mode == CartMode.Rolling ? _rails.State.V * Lanes.Stretch(partyS) : 0f;
        var target = PaceRules.TargetS(Lanes.EnemyS(partyS), offset);
        var command = PaceRules.SpeedFor(run.State.S, run.State.V, target, partyV);
        if (_rails.FrontOnEnemyLane) // the party's cart took this lane: one ahead cannot outrun it, one behind never runs into it
            command = run.State.S > Lanes.EnemyS(partyS) ? Mathf.Min(command, Mathf.Max(0f, partyV - RamCloseIn)) : Mathf.Min(command, partyV);
        run.Step(new CartInput(run.SlopeAhead, false, 0, 0, run.Limit, command), dt);
        run.Sync();
        run.PoseAt(run.State.S); // physics this tick (riders' aim, hits on the shoe) sees the cart where it is; _Process smooths it
    }

    /// <summary>Who a slag cart can run over: heroes and NPCs on foot (the sweep skips anyone seated) and creatures on their own feet.</summary>
    private void GatherBodies(PartyController party)
    {
        _bodies.Clear();
        foreach (var hero in party.Adventurers)
            if (IsInstanceValid(hero) && !hero.Parked && !hero.Downed) _bodies.Add(hero);
        var parent = GetParent();
        for (var i = 0; i < parent.GetChildCount(); i++)
        {
            var child = parent.GetChild(i);
            if (child is Enemy { IsAlive: true, Carried: false, Leaping: false } enemy) _bodies.Add(enemy); // riders (its own or another cart's) ride
            else if (child is Npc { Dead: false, Downed: false, Seated: false } npc) _bodies.Add(npc);
        }
    }

    private void RunOver(SlagCart cart) => RunOver(cart.Run, cart.HitAt, SlagCart.SourceName, SlagCart.HazardIndex, _setAside); // its own plating is not kept: a big body is only knocked aside

    /// <summary>Judge what <paramref name="run"/>'s front swept and apply the run-over outcomes by <see cref="TierRules.Lethality"/>.</summary>
    private void RunOver(CartRun run, Dictionary<ulong, double> hitAt, string source, int hazardIndex, Func<Vector3, Vector3> setAside)
    {
        RunOverSweep.Judge(run, _bodies, _now, hitAt, _hits);
        if (_hits.Count == 0) return;
        if (TraceTraffic) foreach (var (hb, tb) in _hits) GD.Print($"[rails] t={_now:0.00} {source} runs over {hb.Name} (cart S={run.State.S:0.0}, body S={tb.S:0.0}, lateral {tb.Lateral:0.0})");
        RunOverEffects.Apply(_hits, source, TierRules.Lethality, setAside, hazardIndex: hazardIndex);
        foreach (var (hb, _) in _hits) _bodies.Remove(hb); // hit once this tick: another cart's sweep does not hit it again
    }

    /// <summary>"Cart coming!" and the bell for a hero on foot on the enemy lane with a slag cart bearing down (as the party's cart warns).</summary>
    private void Warn(SlagCart cart, PartyController party) => Warn(cart.Run, cart, _warnedAt, false, party);

    /// <summary>
    /// A cart bears down (the body stands on its lane within <see cref="WarnSeconds"/> of look-ahead): a hero on foot gets the bark
    /// (the bell is the cart's own sound's), and an AI-controlled body (a companion, a crew or escorting NPC) within 30 m also walks to a refuge
    /// (<see cref="SeekRefuge"/>). <paramref name="warned"/> throttles the bark per hero. A seated hero of <paramref name="speakFrom"/>
    /// (the party's cart unless named: a chaser's riders call their own) calls it in their own words (<see cref="RailCalls"/>), else the log.
    /// </summary>
    private void Warn(CartRun run, Node3D carrier, Dictionary<int, double> warned, bool partyLane, PartyController party, ICartBody? speakFrom = null)
    {
        var speakers = speakFrom ?? _rails.Cart;
        var look = Mathf.Max(run.State.V, RunOverRules.WarnMinSpeed) * WarnSeconds;
        float hint = run.FrontS + look / 2f, window = look / 2f + 2f;
        var reach = Mathf.Min(look + CartRules.HalfLength + 3f, SeekScan);
        var at = run.Track.Point(run.State.S);
        foreach (var hero in party.Adventurers)
        {
            if (!IsInstanceValid(hero) || hero.Parked || hero.Seated || hero.Downed) continue;
            var pos = hero.GlobalPosition;
            if (pos.DistanceSquaredTo(at) > reach * reach) continue;
            if (!run.Track.Measure(pos, hint, window, out var b)) continue;
            if (!RunOverRules.InWarning(run.State.S, run.State.V, b with { Radius = RunOverSweep.BodyRadius }, WarnSeconds)) continue;
            if (hero.Control == ControlKind.Companion) SeekRefuge(hero, run, carrier, partyLane, b.S);
            if (warned.TryGetValue(hero.Index, out var t) && _now - t < WarnEvery) continue;
            warned[hero.Index] = _now;
            RailCalls.Say(party, speakers, BarkTrigger.CartComing, "Cart coming!"); // the bell is the cart's own (CartSound)
            if (TraceTraffic) GD.Print($"[rails] t={_now:0.00} warn {hero.State.Name} by {carrier.Name}: cart S={run.State.S:0.0} v={run.State.V:0.0}, body S={b.S:0.0} ({b.S - run.State.S:0.0} m ahead)");
        }
        for (var i = 0; i < _bodies.Count; i++)
        {
            if (_bodies[i] is not Npc { Dead: false, Downed: false, Seated: false } npc || !(npc.Crew || npc.Escorting) || !IsInstanceValid(npc)) continue;
            var pos = npc.GlobalPosition;
            if (pos.DistanceSquaredTo(at) > reach * reach) continue;
            if (!run.Track.Measure(pos, hint, window, out var b)) continue;
            if (!RunOverRules.InWarning(run.State.S, run.State.V, b with { Radius = RunOverSweep.BodyRadius }, WarnSeconds)) continue;
            SeekRefuge(npc, run, carrier, partyLane, b.S);
        }
    }

    // ---- a left-behind player's chaser (RailController.Chase) ----------------------------------------

    /// <summary>
    /// A chaser rolling on the party lane warns as every party-lane cart does (its lamp is its tub's): a hero on foot ahead gets the bark
    /// and the bell, and AI bodies (companions, crew, escorts) walk to a party-lane pocket. Its own riders call it. Server; no allocation.
    /// </summary>
    internal void WarnPartyLane(CartRun run, MineCart carrier, Dictionary<int, double> warned, PartyController party)
    {
        GatherBodies(party);
        Warn(run, carrier, warned, true, party, carrier);
    }

    /// <summary>The party line's holes a chaser hops (it judges no set piece, but never floats over a pit): every jump's gap and every fallen trestle, From..To. <paramref name="into"/> is cleared first; no allocation.</summary>
    internal void CollectPartyLaneHoles(List<(float From, float To)> into)
    {
        into.Clear();
        foreach (var p in _pieces)
        {
            if (p.Gap is { } gap && IsInstanceValid(gap)) into.Add((gap.From, gap.To));
            if (p.Trestle is { Collapsed: true } trestle && IsInstanceValid(trestle)) into.Add((trestle.From, trestle.To));
        }
    }

    /// <summary>The nearest live ore heap's face on the party line at or ahead of <paramref name="frontS"/> (a chaser halts at it as the party cart does), or +∞.</summary>
    internal float LiveHeapFaceAhead(float frontS)
    {
        var best = float.PositiveInfinity;
        foreach (var p in _pieces)
            if (p.Heap is { IsAlive: true } heap && IsInstanceValid(heap) && heap.FaceS >= frontS - 0.05f && heap.FaceS < best) best = heap.FaceS;
        return best;
    }

    // ---- posing ----------------------------------------------------------------------------------

    public override void _Process(double delta)
    {
        if (!Current) return;
        if (NetSession.IsClient) { ClientPresent((float)delta); return; }
        FollowBoarders();
        var frac = (float)Engine.GetPhysicsInterpolationFraction();
        PoseGolem(frac);
        foreach (var cart in _carts)
        {
            if (!IsInstanceValid(cart) || cart.Derailed) continue;
            PoseAndHear(cart.Run, cart.Sound, frac, 1);
            cart.FollowRiders();
        }
        if (_traffic != null && IsInstanceValid(_traffic) && !_traffic.Derailed) PoseAndHear(_traffic.Run, _traffic.Sound, frac, 1);
        if (_runaway != null && IsInstanceValid(_runaway.Body)) PoseAndHear(_runaway.Run, _runaway.Body.Sound, frac, 0);
    }

    /// <summary>A cart of the ride posed for this frame and its sound fed from its run (server and single player).</summary>
    private static void PoseAndHear(CartRun run, CartSound? sound, float frac, byte track)
    {
        var s = run.RenderS(frac);
        run.PoseAt(s);
        if (!NetSession.IsServer && sound != null && IsInstanceValid(sound)) sound.FeedRun(run, s, run.State.V, run.State.Mode, run.State.Plating, track);
    }
}
