using System.Globalization;
using DungeonCrawler.Adventure;
using DungeonCrawler.Adventure.Objectives;
using DungeonCrawler.Core;
using DungeonCrawler.Enemies;
using DungeonCrawler.Net;
using DungeonCrawler.Party;
using Godot;

namespace DungeonCrawler.Dungeon.Rails;

/// <summary>
/// The rails on the wire: a node under a railed floor's root in a networked game (it dies with the floor).
/// <para>On the server (<see cref="Node.ProcessPriority"/> 100: after the rail controllers' ticks) it sends one
/// <see cref="CartStateMsg"/> per physics tick that changed any cart: the party's (any <see cref="CartEvent"/>, or a change of
/// its <see cref="RailController.NetSignature"/>: lane, gap, train, halt, boarding, plating, a wreck) and the ride's others (a
/// spawn, a track change, its riders, a derail or a free, the last two as a state flagged Hidden). A wreck and a spare's
/// takeover in one tick send a wreck frame first (<see cref="CartSendRules.NeedsWreckFrame"/>). Every 0.2 s of elapsed time
/// it sends a <see cref="CartCorrectionMsg"/> with every live cart. It never sends from inside a CartEvents handler.</para>
/// <para>On a client it applies them: the party's cart to the <see cref="RailController"/> (<see cref="RailController.ApplyNet"/>),
/// the others to copies built here as dressing with the server's own constructors (slag and traffic carts, runaway wagons, the
/// golem's runner), each following its own <see cref="CartFollower"/> on its own track and posed every rendered frame, their
/// riders bound by net id as they arrive (the cart's state may come before the riders' spawn).</para>
/// <para>Seats and leaps (plan 5 task 4): the server sends a <see cref="SeatMsg"/> for every seat and unseat in the party's cart and the
/// cage wagons, after the tick's cart state, and the leaps the hounds start (<see cref="LeapMsg"/>, from the encounter controller); a
/// joiner or a client that rebuilt the floor gets who sits where and the leaps in the air (<see cref="SendRailCatchUp"/>). A client books
/// the server's seat (<see cref="CartSeats.Put"/>) and seats its own copy of the rider, flies each leap's arc itself from the server's
/// start time, and keeps the boarders the cart's state lists at their spots on its own carts (posed each rendered frame).</para>
/// </summary>
public partial class RailNet : Node
{
    /// <summary>A live cart besides the party's, as the server lists it (<see cref="RailEncounterController.CollectNetCarts"/>).</summary>
    internal readonly record struct LiveCart(int Id, CartKind Kind, byte Track, CartRun Run, Node3D Body, float Offset, string? EncounterId, SlagCart? Slag, Enemy? Golem);

    public const double CorrectionSeconds = 0.2;
    /// <summary>The RIDE pass prints both sides on this server-time grid (seconds).</summary>
    public const double RideGrid = 0.5;

    public static RailNet? Instance { get; private set; }

    private readonly RailController _rails;

    public RailNet(RailController rails)
    {
        _rails = rails;
        Name = "RailNet";
        ProcessPriority = 100; // after the rail controllers' ticks (-100 and 0) and poses
    }

    /// <summary>For Godot's construction of the script class; the builder always passes the controller.</summary>
    public RailNet() : this(null!) { }

    public override void _EnterTree() => Instance = this;

    public override void _ExitTree()
    {
        if (Instance == this) Instance = null;
        if (_rails != null && IsInstanceValid(_rails))
        {
            _rails.CartEvents -= OnCartEvent;
            if (IsInstanceValid(_rails.Cart)) _rails.Cart.RiderChanged -= OnPartyRider;
        }
        foreach (var (wagon, handler) in _wagonsHeard) if (IsInstanceValid(wagon)) wagon.RiderChanged -= handler;
        _wagonsHeard.Clear();
    }

    public override void _Ready()
    {
        if (!NetSession.IsServer || _rails == null) return;
        _rails.CartEvents += OnCartEvent;
        _rails.Cart.RiderChanged += OnPartyRider;
    }

    private bool Current => _rails != null && IsInstanceValid(_rails) && RailController.Instance == _rails && Main.Instance is { InGame: true, Rebuilding: false };

    // ==== the server ==============================================================================

    private sealed class Known
    {
        public int Id;
        public CartKind Kind;
        public byte Track;
        public int Riders;
        public Node3D Body = null!;
        public bool Seen;
    }

    private bool _partyDirty = true;
    private int _partySignature;
    private int _wrecksSeen;
    private Cadence _correction = new(CorrectionSeconds);
    private readonly Dictionary<int, Known> _known = new();
    private readonly List<LiveCart> _live = new();
    private readonly List<int> _gone = new();

    /// <summary>Any cart event marks the party's cart: it is sent at the end of this tick (never from inside the handler).</summary>
    private void OnCartEvent(CartEvent e) => _partyDirty = true;

    public override void _PhysicsProcess(double delta)
    {
        if (!NetSession.IsServer || !Current || NetSession.Instance.Server is not { } world) return;
        var now = NetSession.Instance.Now;
        var floor = Game.Instance.CurrentFloor;
        var epoch = world.FloorEpoch;
        List<CartStateDto>? changed = null;

        // the party's cart: a wreck taken over by a spare in the same tick is shown as a wreck first
        var signature = _rails.NetSignature();
        if (signature != _partySignature) { _partySignature = signature; _partyDirty = true; }
        var wrecked = _rails.Wrecks != _wrecksSeen;
        _wrecksSeen = _rails.Wrecks;
        var flagsNow = _rails.NetFlagsNow;
        if (CartSendRules.NeedsWreckFrame(wrecked, wrecked && _rails.State.Mode != CartMode.Wrecked, flagsNow, out var first))
            world.Broadcast(new CartStateMsg(floor, epoch, new List<CartStateDto> { _rails.CaptureNet(now, first) }));
        if (_partyDirty) (changed ??= new()).Add(_rails.CaptureNet(now));
        _partyDirty = false;

        // the others: new ones, a track change or new riders, and the ones gone (freed or derailed)
        if (RailEncounterController.Instance is { } enc && IsInstanceValid(enc)) enc.CollectNetCarts(_live);
        else _live.Clear();
        foreach (var k in _known.Values) k.Seen = false;
        foreach (var c in _live)
        {
            var riders = RiderSignature(c);
            if (!_known.TryGetValue(c.Id, out var k))
            {
                _known[c.Id] = new Known { Id = c.Id, Kind = c.Kind, Track = c.Track, Riders = riders, Body = c.Body, Seen = true };
                (changed ??= new()).Add(Capture(c, now));
                continue;
            }
            k.Seen = true;
            if (k.Track == c.Track && k.Riders == riders) continue;
            k.Track = c.Track;
            k.Riders = riders;
            (changed ??= new()).Add(Capture(c, now));
        }
        _gone.Clear();
        foreach (var k in _known.Values) if (!k.Seen) _gone.Add(k.Id);
        foreach (var id in _gone)
        {
            var k = _known[id];
            _known.Remove(id);
            var flags = CartFlags.Hidden;
            if (k.Body is SlagCart { Derailed: true }) flags |= CartFlags.Derailed; // a managed field: readable though the node is freed
            (changed ??= new()).Add(new CartStateDto { Cart = k.Id, Kind = (byte)k.Kind, Track = k.Track, Flags = (byte)flags, ServerTime = now });
        }
        if (changed != null) world.Broadcast(new CartStateMsg(floor, epoch, changed));

        // who sat down and who got out since the last tick, after the cart they ride is on its way
        HearWagons();
        FlushSeats(world);

        // every live cart, five times a second, while anyone is in game
        if (_correction.Advance(delta) > 0 && world.Handshake.InGamePeers.Any())
        {
            var all = new List<CartStateDto>(_live.Count + 1) { _rails.CaptureNet(now) };
            foreach (var c in _live) all.Add(Capture(c, now));
            world.Broadcast(new CartCorrectionMsg(floor, epoch, all));
        }

        if (NetSession.Instance.TestRide) RideTraceServer(now, enc: RailEncounterController.Instance);
    }

    // ---- seats (the server) -------------------------------------------------------------------

    private readonly record struct SeatChange(ICartRider Rider, int Cart, int Seat, bool Seated);

    private readonly List<SeatChange> _seats = new();
    private readonly List<(CageWagon Wagon, Action<ICartRider, bool> Handler)> _wagonsHeard = new();
    private readonly List<LeapMsg> _airLeaps = new();

    /// <summary>The party cart's book changed (any path): sent after this tick's cart state. The seat is read now, while the rider still holds it.</summary>
    private void OnPartyRider(ICartRider rider, bool seated) => _seats.Add(new SeatChange(rider, CartIds.Party, rider.Seat, seated));

    /// <summary>Listen to each cage wagon's book (the crew controller builds them after this node): once, the first tick they stand.</summary>
    private void HearWagons()
    {
        if (ObjectiveController.Instance is not CrewRescueController crew || crew.Wagons.Count == _wagonsHeard.Count) return;
        var wagons = crew.Wagons;
        for (var k = 0; k < wagons.Count; k++)
        {
            var wagon = wagons[k];
            if (!IsInstanceValid(wagon)) continue;
            var heard = false;
            foreach (var (w, _) in _wagonsHeard) if (w == wagon) { heard = true; break; }
            if (heard) continue;
            var id = CartIds.Wagon(wagon.Stop);
            Action<ICartRider, bool> handler = (rider, seated) => _seats.Add(new SeatChange(rider, id, rider.Seat, seated));
            wagon.RiderChanged += handler;
            _wagonsHeard.Add((wagon, handler));
        }
    }

    private void FlushSeats(ServerWorld world)
    {
        if (_seats.Count == 0) return;
        foreach (var c in _seats)
            if (SeatMsgOf(c.Rider, c.Cart, c.Seat, c.Seated) is { } m) world.Broadcast(m);
        _seats.Clear();
    }

    /// <summary>The wire's word for a seat change: a hero by member index, an NPC by id; an unseated rider's landing is where it stands now (set down by its GetOut).</summary>
    private static SeatMsg? SeatMsgOf(ICartRider rider, int cart, int seat, bool seated)
    {
        if (rider is not Node3D body || !IsInstanceValid(body)) return null;
        var (kind, id) = rider switch
        {
            Adventurer hero => (SeatRiderKind.Hero, hero.Index.ToString(CultureInfo.InvariantCulture)),
            Npc npc => (SeatRiderKind.Npc, npc.Id),
            _ => (-1, ""),
        };
        if (kind < 0) return null;
        var at = seated ? Vector3.Zero : body.GlobalPosition;
        return new SeatMsg(kind, id, cart, seat, seated, at.X, at.Y, at.Z);
    }

    /// <summary>
    /// Server: the rails as a joiner (or a client that rebuilt the floor) must hold them whatever it missed: a <see cref="SeatMsg"/> for
    /// every rider seated in the party's cart and the cage wagons, and every leap still in the air (each from its own start time).
    /// </summary>
    public void SendRailCatchUp(int peer)
    {
        if (!NetSession.IsServer || !Current || NetSession.Instance.Server is not { } world) return;
        SendSeated(world, peer, _rails.Cart, CartIds.Party);
        if (ObjectiveController.Instance is CrewRescueController crew)
            foreach (var wagon in crew.Wagons)
                if (IsInstanceValid(wagon)) SendSeated(world, peer, wagon, CartIds.Wagon(wagon.Stop));
        if (RailEncounterController.Instance is { } enc && IsInstanceValid(enc))
        {
            enc.CollectAirLeaps(_airLeaps);
            foreach (var leap in _airLeaps) world.SendTo(peer, leap);
            _airLeaps.Clear();
        }
    }

    private static void SendSeated(ServerWorld world, int peer, ICartBody cart, int id)
    {
        foreach (var rider in cart.Riders.Values)
            if (rider.Cart == cart && SeatMsgOf(rider, id, rider.Seat, true) is { } m) world.SendTo(peer, m);
    }

    /// <summary>The riders' net ids folded into one number (no allocation): a change sends the cart.</summary>
    private static int RiderSignature(in LiveCart c)
    {
        var h = 17;
        if (c.Slag is { } slag) for (var i = 0; i < SlagCart.MaxRiders; i++) h = h * 31 + slag.RiderNetIdAt(i);
        if (c.Golem is { } golem && IsInstanceValid(golem)) h = h * 31 + golem.NetId;
        return h;
    }

    private static CartStateDto Capture(in LiveCart c, double now)
    {
        var run = c.Run;
        var input = run.LastInput;
        List<int>? riders = null;
        if (c.Slag is { } slag)
        {
            riders = new List<int>(SlagCart.MaxRiders);
            for (var i = 0; i < SlagCart.MaxRiders; i++) riders.Add(slag.RiderNetIdAt(i));
            while (riders.Count > 0 && riders[^1] < 0) riders.RemoveAt(riders.Count - 1);
        }
        else if (c.Golem is { } golem && IsInstanceValid(golem)) riders = new List<int> { golem.NetId };
        return new CartStateDto
        {
            Cart = c.Id, Kind = (byte)c.Kind, Track = c.Track, Dir = (sbyte)run.Dir,
            S = run.State.S, V = run.State.V, Mode = (byte)run.State.Mode, Limit = run.Limit,
            Pumpers = (byte)Math.Clamp(input.Pumpers, 0, 255), Brake = input.Brake, CrewWagons = (byte)Math.Clamp(input.CrewWagons, 0, 255), TargetOverride = input.TargetOverride,
            Plating = run.State.Plating, Offset = c.Offset, EncounterId = c.EncounterId, RiderNetIds = riders, ServerTime = now,
        };
    }

    // ==== a client ================================================================================

    private sealed class ClientCart
    {
        public int Id;
        public CartKind Kind;
        public byte Track;
        public Node3D Body = null!;
        public CartRun Run = null!;
        public CartFollower Follow = null!;
        public SlagCart? Slag;
        public Enemy? Rider;
        /// <summary>The riders' net ids by spot as the server last named them (−1 none).</summary>
        public readonly int[] Wanted = { -1, -1, -1 };
    }

    private readonly Dictionary<int, ClientCart> _carts = new();
    private readonly HashSet<int> _listed = new();
    private readonly List<int> _drop = new();

    /// <summary>Client: the carts besides the party's that run here now (the RIDE pass reads them).</summary>
    internal int ClientCartCount => _carts.Count;

    /// <summary>Client: cart states from the server (<paramref name="correction"/>: every live cart, so any other one here is freed).</summary>
    internal void OnCarts(List<CartStateDto> carts, bool correction)
    {
        if (!NetSession.IsClient || !Current || NetSession.Instance.Client is not { } client) return;
        var now = client.ServerNow;
        _listed.Clear();
        foreach (var d in carts)
        {
            _listed.Add(d.Cart);
            if (d.Cart == CartIds.Party) { _rails.ApplyNet(d, now); ApplyBoarders(d); }
            else ApplyOther(d, now);
        }
        if (!correction) return;
        _drop.Clear();
        foreach (var id in _carts.Keys) if (!_listed.Contains(id)) _drop.Add(id);
        foreach (var id in _drop) Free(_carts[id], derailed: false);
    }

    private void ApplyOther(CartStateDto d, double now)
    {
        _carts.TryGetValue(d.Cart, out var cc);
        if (d.Has(CartFlags.Hidden) || d.Has(CartFlags.Derailed))
        {
            if (cc != null) Free(cc, d.Has(CartFlags.Derailed));
            return;
        }
        if (cc == null && (cc = Build(d)) == null) return;
        if (d.Track != cc.Track && _rails.TrackOf(d.Track) is { Valid: true } track)
        {
            cc.Track = d.Track; // the golem's runner onto the spur: a new run on the new track (the follower snaps)
            cc.Run = new CartRun(track, cc.Body, new CartState(d.S, d.V, CartMode.Rolling, d.Plating), d.Dir);
        }
        cc.Run.Limit = d.Limit;
        cc.Follow.Apply(d, now);
        for (var i = 0; i < cc.Wanted.Length; i++) cc.Wanted[i] = d.RiderNetIds != null && i < d.RiderNetIds.Count ? d.RiderNetIds[i] : -1;
        Bind(cc);
    }

    /// <summary>A copy of the server's cart, built as the server builds it, as dressing: it judges, paces and warns nothing.</summary>
    private ClientCart? Build(CartStateDto d)
    {
        if (GetParent() is not Node3D root || _rails.TrackOf(d.Track) is not { Valid: true } track) return null;
        var kind = (CartKind)d.Kind;
        var state = new CartState(d.S, d.V, CartMode.Rolling, d.Plating);
        var cc = new ClientCart { Id = d.Cart, Kind = kind, Track = d.Track, Follow = new CartFollower(_rails) };
        switch (kind)
        {
            case CartKind.Slag:
            case CartKind.Traffic:
            {
                var id = d.EncounterId ?? (kind == CartKind.Traffic ? "traffic" : "");
                var side = RailEncounterController.Instance is { } enc && IsInstanceValid(enc) ? enc.InnerSideFor(d.S) : 1;
                var cart = new SlagCart { Name = $"SlagCart_{id}", EncounterId = id, InnerSide = side, CartId = d.Cart };
                cart.Setup(track, state, d.Offset);
                root.AddChild(cart);
                cc.Body = cart;
                cc.Slag = cart;
                cc.Run = cart.Run;
                break;
            }
            case CartKind.Runaway:
            {
                var wagon = new RunawayWagon { CartId = d.Cart };
                root.AddChild(wagon);
                cc.Body = wagon;
                cc.Run = new CartRun(track, wagon, state, d.Dir);
                break;
            }
            case CartKind.Golem:
            {
                var runner = new Node3D { Name = $"GolemRunner_{d.EncounterId ?? d.Cart.ToString(CultureInfo.InvariantCulture)}" };
                root.AddChild(runner);
                cc.Body = runner;
                cc.Run = new CartRun(track, runner, state, d.Dir);
                break;
            }
            default:
                return null; // a chaser comes with Task 9
        }
        cc.Run.Limit = d.Limit;
        cc.Run.PoseAt(d.S);
        _carts[d.Cart] = cc;
        return cc;
    }

    /// <summary>Bind the riders the server names to their spots (each that is here yet), and let go of the ones it no longer names.</summary>
    private void Bind(ClientCart cc)
    {
        var client = NetSession.Instance?.Client;
        if (cc.Slag is { } slag)
        {
            for (var i = 0; i < SlagCart.MaxRiders; i++)
            {
                var want = cc.Wanted[i];
                var have = slag.RiderAt(i);
                if (have != null && have.NetId == want) continue;
                if (have != null) slag.Unbind(i);
                if (want < 0 || client?.EnemyByNetId(want) is not { } e || e.Carried || e.Leaping) continue; // a hound in the air is its leap's
                slag.BoardAt(e, i);
            }
            return;
        }
        if (cc.Kind != CartKind.Golem) return;
        var wanted = cc.Wanted[0];
        if (cc.Rider != null && (!IsInstanceValid(cc.Rider) || cc.Rider.NetId != wanted || cc.Rider.Carrier != cc.Body))
        {
            if (IsInstanceValid(cc.Rider) && cc.Rider.Carrier == cc.Body) cc.Rider.Drop(cc.Rider.GlobalPosition);
            cc.Rider = null;
        }
        if (cc.Rider != null || wanted < 0 || client?.EnemyByNetId(wanted) is not { } golem || golem.Carried) return;
        var runner = cc.Body;
        cc.Rider = golem;
        golem.Carry(runner, () => IsInstanceValid(runner) ? new Transform3D(runner.GlobalBasis, runner.GlobalPosition + Vector3.Up * 0.05f) : Transform3D.Identity);
    }

    /// <summary>Client: an enemy arrived (its spawn may come after the cart that names it): bind it if a cart here waits for it.</summary>
    internal void OnEnemySpawned(Enemy e)
    {
        if (!NetSession.IsClient) return;
        foreach (var cc in _carts.Values)
            if (Array.IndexOf(cc.Wanted, e.NetId) >= 0) Bind(cc);
    }

    /// <summary>A copy goes: derailed (its tumble, its riders set down) or freed (its riders go with it, as the server frees them; the golem is set down).</summary>
    private void Free(ClientCart cc, bool derailed)
    {
        _carts.Remove(cc.Id);
        if (!IsInstanceValid(cc.Body)) return;
        if (cc.Slag is { } slag)
        {
            if (derailed) { slag.ShowDerailed(); return; }
            foreach (var rider in slag.Aboard)
                if (IsInstanceValid(rider) && rider.Carrier == slag && !rider.IsQueuedForDeletion()) rider.QueueFree();
            slag.Aboard.Clear();
        }
        else if (cc.Rider is { } golem && IsInstanceValid(golem) && golem.Carrier == cc.Body) golem.Drop(golem.GlobalPosition);
        cc.Body.QueueFree();
    }

    public override void _Process(double delta)
    {
        if (!NetSession.IsClient || !Current) return;
        var dt = (float)delta;
        foreach (var cc in _carts.Values)
        {
            if (!IsInstanceValid(cc.Body)) continue;
            var s = cc.Follow.Advance(dt);
            cc.Run.State = new CartState(s, cc.Follow.V, CartMode.Rolling, cc.Follow.Plating);
            cc.Run.PoseAt(s);
            if (cc.Body is ICartBody body)
            {
                body.Speed = cc.Follow.V;
                body.Curvature = cc.Run.Dir * cc.Run.Track.Curvature(s);
            }
            if (cc.Slag != null) cc.Slag.FollowRiders();
            else if (cc.Rider is { } golem && IsInstanceValid(golem) && golem.Carrier == cc.Body) golem.FollowCarrier();
        }
        if (NetSession.Instance.Client is not { } client) return;
        FlyLeaps(client.ServerNow); // after every cart is posed: the arcs start on one and end on another
        FollowBoarders();
        if (NetSession.Instance.TestRide) RideTraceClient(client.ServerNow);
    }

    // ---- seats (a client) ----------------------------------------------------------------------

    /// <summary>
    /// Client: the server seated or unseated a rider. Seated: its seat index is booked in this client's copy of the cart (a different
    /// occupant there gets out where it sits first) and the rider takes it. Unseated: a rider still in that cart here gets out at the
    /// server's landing. Idempotent: a rider already in that seat is only posed.
    /// </summary>
    internal void OnSeat(SeatMsg m)
    {
        if (!NetSession.IsClient || !Current || RiderOf(m.RiderKind, m.RiderId) is not { } rider) return;
        var cart = CartById(m.Cart);
        if (!m.Seated)
        {
            if (m.RiderKind == SeatRiderKind.Hero && rider is Adventurer { IsLocal: true }) LastLocalLanding = (new Vector3(m.X, m.Y, m.Z), NetSession.Instance.Now);
            if (cart != null && rider.Cart == cart) rider.GetOut(new Vector3(m.X, m.Y, m.Z), Vector3.Zero);
            return;
        }
        if (cart == null) return;
        if (rider.Cart == cart && rider.Seat == m.Seat) { rider.FollowSeat(); return; }
        if (!cart.Seats.Put(rider.RiderId, m.Seat))
        {
            if (cart.Seats.OccupantOf(m.Seat) is int holder)
            {
                if (cart.Riders.TryGetValue(holder, out var other) && other is Node3D n && IsInstanceValid(n)) other.GetOut(n.GlobalPosition, Vector3.Zero);
                cart.Seats.Leave(holder);
            }
            if (!cart.Seats.Put(rider.RiderId, m.Seat)) return;
        }
        rider.SitIn(cart, m.Seat);
    }

    /// <summary>Client: where the server last set this client's own hero down from a seat, and when (this process's clock); the RIDE pass reads it.</summary>
    internal (Vector3 At, double When)? LastLocalLanding { get; private set; }

    /// <summary>A cart on the wire as a seat book here: the party's, or a cage wagon by its stop (a chaser comes with Task 9).</summary>
    private ICartBody? CartById(int id)
    {
        if (id == CartIds.Party) return IsInstanceValid(_rails.Cart) ? _rails.Cart : null;
        if (id >= CartIds.WagonBase && id < CartIds.ChaserBase) return WagonAt(id - CartIds.WagonBase);
        return null;
    }

    private static CageWagon? WagonAt(int stop)
    {
        if (ObjectiveController.Instance is not CrewRescueController crew) return null;
        foreach (var wagon in crew.Wagons) if (IsInstanceValid(wagon) && wagon.Stop == stop) return wagon;
        return null;
    }

    private ICartRider? RiderOf(int kind, string id)
    {
        if (kind == SeatRiderKind.Hero)
        {
            if (Main.Instance?.Party is not { } party || !int.TryParse(id, NumberStyles.Integer, CultureInfo.InvariantCulture, out var index)
                || index < 0 || index >= party.Adventurers.Length || !IsInstanceValid(party.Adventurers[index])) return null;
            return party.Adventurers[index];
        }
        if (kind == SeatRiderKind.Npc && GetParent()?.GetNodeOrNull<Npc>($"Npc_{id}") is { } npc && !npc.IsQueuedForDeletion()) return npc;
        return null;
    }

    // ---- leaps and boarders (a client) ---------------------------------------------------------

    /// <summary>A hound in the air here: flown from where it stood (riding on the cart it left) to its spot, or beside the body it misses.</summary>
    private sealed class Flight
    {
        public Enemy Hound = null!;
        public Node3D? Source;
        public Vector3 FromLocal, FromWorld, To;
        public bool HasLocal;
        public Node3D? Dest;
        public int Spot;
        public bool Miss;
        public double Start;
    }

    private readonly List<Flight> _flights = new();

    /// <summary>Client: hounds in the air here now (the RIDE pass reads it).</summary>
    internal int ClientFlights => _flights.Count;

    /// <summary>
    /// Client: a hound left its slag cart on a leap (or the catch-up says one is in the air): fly it from where it stands, already
    /// <c>serverNow − ServerTime</c> into the arc. Ignored when that hound already flies, or already rides the spot named.
    /// </summary>
    internal void OnLeap(LeapMsg m)
    {
        if (!NetSession.IsClient || !Current || NetSession.Instance.Client is not { } client) return;
        if (client.EnemyByNetId(m.NetId) is not { } hound || !hound.IsAlive) return;
        foreach (var f in _flights) if (f.Hound == hound) return;
        var dest = BodyById(m.ToCart);
        if (!m.Miss && dest != null && hound.Carrier == dest && HeldAt(dest, m.Spot) == hound) return; // it landed here already (the boarder list)
        Node3D? source = _carts.TryGetValue(m.FromCart, out var cc) && IsInstanceValid(cc.Body) ? cc.Body : null;
        if (cc?.Slag is { } slag)
            for (var i = 0; i < SlagCart.MaxRiders; i++)
                if (slag.RiderAt(i) == hound) { slag.Unbind(i); break; }
        ForgetBoarder(hound); // a boarder the client had elsewhere (a stale list) leaves that spot
        if (hound.Carried) hound.Drop(hound.GlobalPosition);
        var flight = new Flight
        {
            Hound = hound, Source = source, FromWorld = hound.GlobalPosition, Dest = dest, Spot = m.Spot, Miss = m.Miss || dest == null || m.Spot < 0, Start = m.ServerTime,
        };
        if (source != null) { flight.FromLocal = source.GlobalTransform.AffineInverse() * hound.GlobalPosition; flight.HasLocal = true; }
        if (flight.Miss)
        {
            // where the server sets it down: beside the party lane, ahead by where the body it missed will be when it lands
            var anchor = dest ?? _rails.Cart;
            var lead = anchor is ICartBody body ? body.Velocity : Vector3.Zero;
            var left = Mathf.Max(0f, RailEncounterController.LeapSeconds - (float)(client.ServerNow - m.ServerTime));
            flight.To = _rails.StandBesideLane(anchor.GlobalPosition + lead * left);
        }
        hound.BeginNetLeap();
        _flights.Add(flight);
        if (NetSession.Instance.TestRide)
            GD.Print(FormattableString.Invariant($"[net] ride: hound {m.NetId} leaps from cart {m.FromCart} to {(flight.Miss ? "beside" : "cart")} {m.ToCart} spot {m.Spot}, {client.ServerNow - m.ServerTime:0.000} s into the arc"));
    }

    private Node3D? BodyById(int id) => CartById(id) as Node3D;

    /// <summary>The boarder spot <paramref name="spot"/> of the party's cart or a cage wagon, in world space.</summary>
    private static Transform3D SpotOf(Node3D dest, int spot) => dest is MineCart cart ? cart.BoarderSpotTransform(spot) : ((CageWagon)dest).BoarderSpotTransform(spot);

    private static Enemy?[]? HeldOf(Node3D body) => body is MineCart cart ? cart.Boarders : body is CageWagon wagon ? wagon.Boarders : null;

    private static Enemy? HeldAt(Node3D body, int spot) => HeldOf(body) is { } held && spot >= 0 && spot < held.Length ? held[spot] : null;

    /// <summary>Pose each hound in the air on its arc at the server's time; at the end it lands on its spot (carried from then on) or on the ground (the snapshots move it).</summary>
    private void FlyLeaps(double serverNow)
    {
        for (var i = _flights.Count - 1; i >= 0; i--)
        {
            var f = _flights[i];
            var h = f.Hound;
            if (!IsInstanceValid(h) || h.IsQueuedForDeletion() || !h.Leaping) { _flights.RemoveAt(i); continue; }
            var t = Mathf.Clamp((float)((serverNow - f.Start) / RailEncounterController.LeapSeconds), 0f, 1f);
            if (f.HasLocal && f.Source != null && IsInstanceValid(f.Source)) f.FromWorld = f.Source.GlobalTransform * f.FromLocal;
            var landing = !f.Miss && f.Dest != null && IsInstanceValid(f.Dest);
            var to = landing ? SpotOf(f.Dest!, f.Spot).Origin : f.To;
            var pos = f.FromWorld.Lerp(to, t) + Vector3.Up * (RailEncounterController.LeapApex * 4f * t * (1f - t));
            var flat = new Vector3(to.X - f.FromWorld.X, 0f, to.Z - f.FromWorld.Z);
            var yaw = flat.LengthSquared() > 0.01f ? Mathf.Atan2(-flat.X, -flat.Z) : h.GlobalRotation.Y;
            h.GlobalTransform = new Transform3D(new Basis(Vector3.Up, yaw), pos);
            if (t < 1f && h.IsAlive) continue;
            _flights.RemoveAt(i);
            if (landing && h.IsAlive) { Land(h, f.Dest!, f.Spot); NoteLanding(h, f.Start + RailEncounterController.LeapSeconds); }
            else h.LandAt(landing ? pos : f.To);
            if (NetSession.Instance.TestRide)
                GD.Print(FormattableString.Invariant($"[net] ride: hound {h.NetId} {(landing && h.IsAlive ? $"boarded {f.Dest!.Name} spot {f.Spot}" : "landed beside the lane")} at ({h.GlobalPosition.X:0.00}, {h.GlobalPosition.Y:0.00}, {h.GlobalPosition.Z:0.00})"));
        }
    }

    /// <summary>Hounds that landed here lately and when (the server's time of the landing): a cart state older than that does not let them go.</summary>
    private readonly List<(Enemy Hound, double At)> _landed = new();

    private void NoteLanding(Enemy hound, double at)
    {
        for (var i = _landed.Count - 1; i >= 0; i--)
            if (_landed[i].Hound == hound || at - _landed[i].At > 2.0) _landed.RemoveAt(i);
        _landed.Add((hound, at));
    }

    /// <summary>The state was taken before <paramref name="hound"/> landed here (its boarder list could not name it yet).</summary>
    private bool LandedAfter(Enemy hound, double stateTime)
    {
        foreach (var (h, at) in _landed) if (h == hound && at > stateTime) return true;
        return false;
    }

    /// <summary>A hound rides <paramref name="dest"/> at boarder spot <paramref name="spot"/> here (its leap landed, or the boarder list says so).</summary>
    private static void Land(Enemy hound, Node3D dest, int spot)
    {
        if (HeldOf(dest) is not { } held || spot < 0 || spot >= held.Length) { if (hound.Leaping) hound.LandAt(hound.GlobalPosition); return; }
        if (held[spot] is { } other && other != hound && IsInstanceValid(other) && other.Carrier == dest) other.Drop(other.GlobalPosition);
        held[spot] = hound;
        Func<Transform3D> at = () => IsInstanceValid(dest) ? SpotOf(dest, spot) : new Transform3D(Basis.Identity, hound.GlobalPosition);
        if (hound.Leaping) hound.LandOn(dest, at);
        else hound.Carry(dest, at);
    }

    /// <summary>A hound leaves whichever boarder spot this client had it at (the party cart's or a wagon's).</summary>
    private void ForgetBoarder(Enemy hound)
    {
        Forget(_rails.Cart.Boarders, hound);
        var train = _rails.Train;
        for (var k = 0; k < train.Count; k++) if (IsInstanceValid(train[k])) Forget(train[k].Boarders, hound);
        static void Forget(Enemy?[] held, Enemy hound) { for (var i = 0; i < held.Length; i++) if (held[i] == hound) held[i] = null; }
    }

    private bool InFlight(Enemy hound)
    {
        foreach (var f in _flights) if (f.Hound == hound) return true;
        return false;
    }

    /// <summary>
    /// Client: the party cart's state lists its boarders by spot and each coupled wagon's: a hound named is carried at its spot (unless it is
    /// still flying there), and one carried here that is no longer named is let go where it stands (the snapshots move it). No allocation
    /// unless a boarder changes.
    /// </summary>
    private void ApplyBoarders(CartStateDto d)
    {
        if (!IsInstanceValid(_rails.Cart)) return;
        Reconcile(_rails.Cart, _rails.Cart.Boarders, d.Boarders, 0, d.ServerTime);
        var train = _rails.Train;
        for (var k = 0; k < train.Count; k++)
            if (IsInstanceValid(train[k])) Reconcile(train[k], train[k].Boarders, d.WagonBoarders, k, d.ServerTime);
    }

    /// <summary>Make <paramref name="held"/> (a body's boarder spots here) what <paramref name="wanted"/> says, from index <paramref name="from"/> on (a wagon: its one entry).</summary>
    private void Reconcile(Node3D body, Enemy?[] held, List<int>? wanted, int from, double stateTime)
    {
        var client = NetSession.Instance?.Client;
        for (var i = 0; i < held.Length; i++)
        {
            var at = from + i;
            var want = wanted != null && at < wanted.Count ? wanted[at] : -1;
            var have = held[i];
            if (have != null && (!IsInstanceValid(have) || have.IsQueuedForDeletion() || have.Carrier != body)) { held[i] = null; have = null; }
            if (have != null && have.NetId == want) continue;
            if (have != null && want < 0 && LandedAfter(have, stateTime)) continue; // our flight landed it before the server's did: the next state names it
            if (have != null) { held[i] = null; have.Drop(have.GlobalPosition); }
            if (want < 0 || client?.EnemyByNetId(want) is not { } hound || InFlight(hound)) continue;
            foreach (var cc in _carts.Values) // still bound to a slag cart here (its leap never reached this client): off it first
                if (cc.Slag is { } slag)
                    for (var s = 0; s < SlagCart.MaxRiders; s++)
                        if (slag.RiderAt(s) == hound) slag.Unbind(s);
            ForgetBoarder(hound);
            if (hound.Leaping) hound.LandAt(hound.GlobalPosition);
            Land(hound, body, i);
        }
    }

    /// <summary>Pose the boarders this client holds on the carts it runs (each rendered frame, after the carts).</summary>
    private void FollowBoarders()
    {
        if (!IsInstanceValid(_rails.Cart)) return;
        Follow(_rails.Cart, _rails.Cart.Boarders);
        var train = _rails.Train; // by index: a foreach over the interface would box its enumerator every frame
        for (var k = 0; k < train.Count; k++) if (IsInstanceValid(train[k])) Follow(train[k], train[k].Boarders);
        static void Follow(Node3D body, Enemy?[] held)
        {
            foreach (var h in held) if (h != null && IsInstanceValid(h) && h.Carrier == body) h.FollowCarrier();
        }
    }

    // ==== the RIDE pass's trace: both sides on one server-time grid ===============================

    private long _rideGrid = long.MinValue;

    private void RideTraceServer(double now, RailEncounterController? enc)
    {
        var grid = (long)Math.Floor(now / RideGrid);
        if (grid == _rideGrid) return;
        _rideGrid = grid;
        var t = (grid * RideGrid).ToString("0.0", CultureInfo.InvariantCulture);
        var ride = enc != null && IsInstanceValid(enc) ? enc.ArmedRide : -1;
        GD.Print(FormattableString.Invariant($"[net-server] ride t={t} S={_rails.State.S:0.000} mode={_rails.State.Mode} stop={_rails.StopIndex} armed={ride}"));
        foreach (var c in _live)
            GD.Print(FormattableString.Invariant($"[net-server] ride cart={c.Id} kind={c.Kind} t={t} S={c.Run.State.S:0.000} armed={ride}"));
    }

    private void RideTraceClient(double serverNow)
    {
        if (_rails.Follower is not { HasState: true } f) return;
        var grid = (long)Math.Floor(serverNow / RideGrid);
        if (grid == _rideGrid) return;
        _rideGrid = grid;
        var t = (grid * RideGrid).ToString("0.0", CultureInfo.InvariantCulture);
        GD.Print(FormattableString.Invariant($"[net] ride t={t} S={f.S:0.000}"));
        foreach (var cc in _carts.Values)
            if (IsInstanceValid(cc.Body)) GD.Print(FormattableString.Invariant($"[net] ride cart={cc.Id} kind={cc.Kind} t={t} S={cc.Follow.S:0.000}"));
    }
}
