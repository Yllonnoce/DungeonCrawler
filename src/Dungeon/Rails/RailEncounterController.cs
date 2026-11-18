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
/// party going on foot clears the ride's live items (slag carts and their riders, silently). Server only: a client does
/// nothing here (plan 5 shows it).
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
    /// <summary>The ride whose items wait for the cart (1-based), or −1 when none is armed.</summary>
    public int ArmedRide { get; private set; } = -1;
    private bool[] _started = Array.Empty<bool>();
    private bool[] _lineShown = Array.Empty<bool>();
    private readonly List<SlagCart> _carts = new();
    private readonly List<Node3D> _bodies = new();
    private readonly List<(Node3D body, TrackBody tb)> _hits = new();
    private readonly Dictionary<int, double> _warnedAt = new();
    private readonly Func<Vector3, Vector3> _setAside;
    private double _now;

    /// <summary>The slag carts on the line now (live, not derailed), for tests and later tasks.</summary>
    public IReadOnlyList<SlagCart> SlagCarts => _carts;

    public RailEncounterController()
    {
        Name = "RailEncounterController";
        _setAside = pos => _rails.SetAsideBesideEnemyLane(pos);
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
        _encounters = contents.RailEncounters;
        _setPieces = contents.RailSetPieces;
        _started = new bool[_encounters.Count];
        _lineShown = new bool[_encounters.Count];
    }

    public override void _Ready() => _rails.CartEvents += OnCartEvent;

    public override void _ExitTree()
    {
        if (IsInstanceValid(_rails)) _rails.CartEvents -= OnCartEvent;
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
            case CartEventKind.Arrived:
            case CartEventKind.Wrecked:
            case CartEventKind.Restored:
            case CartEventKind.OnFoot:
                ClearRide();
                break;
        }
    }

    /// <summary>Ride <paramref name="ride"/>'s items wait for the cart again (a ride after a wreck runs again from its start).</summary>
    private void Arm(int ride)
    {
        ArmedRide = ride >= 1 && ride < _rails.StopS.Length ? ride : -1;
        for (var i = 0; i < _encounters.Count; i++)
            if (_encounters[i].Ride == ride) _started[i] = false;
    }

    /// <summary>The ride is over (or undone): its live items go, the riders without a death or a word.</summary>
    private void ClearRide()
    {
        ArmedRide = -1;
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

    /// <summary>One encounter starts, by its kind. "lone", "thrower" and "golem" come with later tasks.</summary>
    private void Spawn(int index, RailEncounterPlan enc)
    {
        switch (enc.Kind)
        {
            case "slag_cart":
                SpawnSlagCart(index, enc);
                break;
        }
    }

    private void ShowLine(int index, RailEncounterPlan enc)
    {
        if (_lineShown[index] || string.IsNullOrWhiteSpace(enc.Line)) return;
        _lineShown[index] = true;
        EventBus.Instance.RaiseNarration(enc.Line, null);
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
        var v = _rails.State.V * Lanes.Stretch(partyS);
        var cart = new SlagCart { Name = $"SlagCart_{enc.Id}", EncounterId = enc.Id, InnerSide = InnerSideAt(s) };
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
        if (_carts.Count == 0) return;
        var party = Main.Instance.Party;
        GatherBodies(party);
        for (var i = _carts.Count - 1; i >= 0; i--)
        {
            var cart = _carts[i];
            if (!IsInstanceValid(cart) || cart.Derailed) { _carts.RemoveAt(i); continue; }
            Pace(cart, dt);
            RunOver(cart);
            Warn(cart, party);
            if (cart.Run.State.S >= _enemy.Length - LineEnd || cart.Run.State.Mode != CartMode.Rolling) { Free(cart); _carts.RemoveAt(i); }
        }
    }

    /// <summary>Hold pace with the party's cart: aim at the pace point, at the party's speed carried over to this lane plus a capped correction.</summary>
    private void Pace(SlagCart cart, float dt)
    {
        var run = cart.Run;
        var partyS = _rails.State.S;
        var partyV = _rails.State.Mode == CartMode.Rolling ? _rails.State.V * Lanes.Stretch(partyS) : 0f;
        var target = PaceRules.TargetS(Lanes.EnemyS(partyS), cart.Offset);
        var command = PaceRules.SpeedFor(run.State.S, run.State.V, target, partyV);
        run.Step(new CartInput(run.SlopeAhead, false, 0, 0, run.Limit, command), dt);
        run.Sync();
        run.PoseAt(run.State.S); // physics this tick (riders' aim, hits on the shoe) sees the cart where it is; _Process smooths it
        cart.FollowRiders();
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
            if (child is Enemy { IsAlive: true, Carried: false } enemy) _bodies.Add(enemy); // riders (its own or another cart's) ride
            else if (child is Npc { Dead: false, Downed: false, Seated: false } npc) _bodies.Add(npc);
        }
    }

    private void RunOver(SlagCart cart)
    {
        RunOverSweep.Judge(cart.Run, _bodies, _now, cart.HitAt, _hits);
        if (_hits.Count == 0) return;
        RunOverEffects.Apply(_hits, SlagCart.SourceName, TierRules.Lethality, _setAside, hazardIndex: SlagCart.HazardIndex); // its own plating is not kept: a big body is only knocked aside
    }

    /// <summary>"Cart coming!" and the bell for a hero on foot on the enemy lane with a slag cart bearing down (as the party's cart warns).</summary>
    private void Warn(SlagCart cart, PartyController party)
    {
        var run = cart.Run;
        var look = Mathf.Max(run.State.V, RunOverRules.WarnMinSpeed) * WarnSeconds;
        float hint = run.FrontS + look / 2f, window = look / 2f + 2f;
        var reach = look + CartRules.HalfLength + 3f;
        var at = run.Track.Point(run.State.S);
        foreach (var hero in party.Adventurers)
        {
            if (!IsInstanceValid(hero) || hero.Parked || hero.Seated || hero.Downed) continue;
            var pos = hero.GlobalPosition;
            if (pos.DistanceSquaredTo(at) > reach * reach) continue;
            if (!run.Track.Measure(pos, hint, window, out var b)) continue;
            if (!RunOverRules.InWarning(run.State.S, run.State.V, b with { Radius = RunOverSweep.BodyRadius }, WarnSeconds)) continue;
            if (_warnedAt.TryGetValue(hero.Index, out var t) && _now - t < WarnEvery) continue;
            _warnedAt[hero.Index] = _now;
            Adventurer? speaker = null;
            foreach (var mate in party.Adventurers)
                if (IsInstanceValid(mate) && mate.Cart == _rails.Cart && mate.Control == ControlKind.Companion && !mate.Downed) { speaker = mate; break; }
            if (speaker != null)
            {
                var name = speaker.State.Name;
                EventBus.Instance.Log($"{name}: \"Cart coming!\"");
                EventBus.Instance.RaiseBark(name, new Line("Cart coming!"));
                Main.Instance?.Hud?.ShowBark(name, "Cart coming!", 2f);
            }
            else EventBus.Instance.Log("Cart coming!");
            EventBus.Instance.PlaySfx("cart_bell"); // no clip yet: plan 6 adds the sound
        }
    }

    // ---- posing ----------------------------------------------------------------------------------

    public override void _Process(double delta)
    {
        if (NetSession.IsClient || _carts.Count == 0 || !Current) return;
        var frac = (float)Engine.GetPhysicsInterpolationFraction();
        foreach (var cart in _carts)
        {
            if (!IsInstanceValid(cart) || cart.Derailed) continue;
            cart.Run.Pose(frac);
            cart.FollowRiders();
        }
    }
}
