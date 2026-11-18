using System.Globalization;
using DungeonCrawler.Core;
using DungeonCrawler.Enemies;
using DungeonCrawler.Net;
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
        if (_rails != null && IsInstanceValid(_rails)) _rails.CartEvents -= OnCartEvent;
    }

    public override void _Ready()
    {
        if (NetSession.IsServer && _rails != null) _rails.CartEvents += OnCartEvent;
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

        // every live cart, five times a second, while anyone is in game
        if (_correction.Advance(delta) > 0 && world.Handshake.InGamePeers.Any())
        {
            var all = new List<CartStateDto>(_live.Count + 1) { _rails.CaptureNet(now) };
            foreach (var c in _live) all.Add(Capture(c, now));
            world.Broadcast(new CartCorrectionMsg(floor, epoch, all));
        }

        if (NetSession.Instance.TestRide) RideTraceServer(now, enc: RailEncounterController.Instance);
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
            if (d.Cart == CartIds.Party) _rails.ApplyNet(d, now);
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
                if (want < 0 || client?.EnemyByNetId(want) is not { } e || e.Carried) continue;
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
        if (NetSession.Instance.TestRide && NetSession.Instance.Client is { } client) RideTraceClient(client.ServerNow);
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
