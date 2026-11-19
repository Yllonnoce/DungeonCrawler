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
/// <para>On the server (<see cref="Node.ProcessPhysicsPriority"/> 100: after the rail controllers' ticks) it sends one
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
        ProcessPriority = 100; // _Process: after the RailController's poses (-100)
        ProcessPhysicsPriority = 100; // _PhysicsProcess: after the RailController's and the RailEncounterController's ticks (both 0), whatever the tree order
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
            _rails.ChaserPlaced -= OnChaserPlaced;
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
        _rails.ChaserPlaced += OnChaserPlaced;
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
        if (NetSession.IsClient)
        {
            if (NetSession.Instance.TestRide && Current && NetSession.Instance.Client is { } client) RideHammerTrace(client.ServerNow, "[net]");
            return;
        }
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
        _rails.CollectChaser(_live); // a left-behind player's chaser (task 9)
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

        if (NetSession.Instance.TestRide)
        {
            RideTraceServer(now, enc: RailEncounterController.Instance);
            RideHammerTrace(now, "[net-server]");
        }
    }

    /// <summary>The riders' net ids folded into one number (no allocation): a change sends the cart. A chaser's mode is in it too (its departure, its halt behind the train).</summary>
    private static int RiderSignature(in LiveCart c)
    {
        var h = 17;
        if (c.Kind == CartKind.Chaser) h = h * 31 + (int)c.Run.State.Mode;
        if (c.Slag is { } slag) for (var i = 0; i < SlagCart.MaxRiders; i++) h = h * 31 + slag.RiderNetIdAt(i);
        if (c.Golem is { } golem && IsInstanceValid(golem)) h = h * 31 + golem.NetId;
        return h;
    }

    private CartStateDto Capture(in LiveCart c, double now)
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
            // a chaser: its spare's stop and its own countdown
            StopIndex = c.Kind == CartKind.Chaser ? c.Id - CartIds.ChaserBase : 0, BoardingLeft = c.Kind == CartKind.Chaser ? _rails.ChaserBoardingLeft : -1f,
        };
    }

    // ==== the RIDE pass's trace: both sides on one server-time grid ===============================

    private long _rideGrid = long.MinValue;

    /// <summary>The RIDE pass's server stops printing its grid lines (set by the pass before it wrecks the cart: the jump to a spare's stop is not a pair).</summary>
    internal bool RideTraceOff { get; set; }

    private void RideTraceServer(double now, RailEncounterController? enc)
    {
        if (RideTraceOff) return;
        var grid = (long)Math.Floor(now / RideGrid);
        if (grid == _rideGrid) return;
        _rideGrid = grid;
        var t = (grid * RideGrid).ToString("0.0", CultureInfo.InvariantCulture);
        var ride = enc != null && IsInstanceValid(enc) ? enc.ArmedRide : -1;
        GD.Print(FormattableString.Invariant($"[net-server] ride t={t} S={_rails.State.S:0.000} mode={_rails.State.Mode} stop={_rails.StopIndex} armed={ride}"));
        foreach (var c in _live)
            GD.Print(FormattableString.Invariant($"[net-server] ride cart={c.Id} kind={c.Kind} t={t} S={c.Run.State.S:0.000} armed={ride}"));
        GD.Print(FormattableString.Invariant($"[net-server] ride book t={t} now={now:0.000} {RideBook()}"));
    }

    /// <summary>
    /// The RIDE pass: who sits where (the party's cart and each cage wagon, seat by seat: a hero by index, an NPC by id), the train behind
    /// the cart, and the props' hash, as one word on each side; the script compares a joiner's with the server's on the grid. Test only.
    /// </summary>
    private string RideBook()
    {
        var b = new System.Text.StringBuilder();
        b.Append("seats=");
        BookSeats(b, "P", _rails.Cart);
        if (BookChaser() is { } chaser) BookSeats(b, "C" + (chaser.CartId - CartIds.ChaserBase).ToString(CultureInfo.InvariantCulture), chaser);
        if (ObjectiveController.Instance is CrewRescueController crew)
            foreach (var wagon in crew.Wagons.Where(w => IsInstanceValid(w)).OrderBy(w => w.Stop))
                BookSeats(b, "W" + wagon.Stop.ToString(CultureInfo.InvariantCulture), wagon);
        b.Append(" train=").Append(_rails.Train.Count == 0 ? "-" : string.Join(",", _rails.Train.Select(w => w.Stop)));
        b.Append(" props=").Append(GetParent() is Node3D root ? PropCheck.Hash(PropSync.Capture(root)).ToString("X8", CultureInfo.InvariantCulture) : "-");
        BookCarts(b);
        BookBoarders(b);
        return b.ToString();
    }

    /// <summary>
    /// The RIDE pass: the other carts (id, kind and, for a slag cart, its living riders' net ids by spot) as this side has them, sorted
    /// by id: the server's live carts, a client's copies. Test only.
    /// </summary>
    private void BookCarts(System.Text.StringBuilder b)
    {
        var carts = new List<(int Id, CartKind Kind, SlagCart? Slag)>();
        if (NetSession.IsClient) { foreach (var cc in _carts.Values) if (IsInstanceValid(cc.Body) && !cc.Body.IsQueuedForDeletion()) carts.Add((cc.Id, cc.Kind, cc.Slag)); }
        else foreach (var c in _live) carts.Add((c.Id, c.Kind, c.Slag));
        carts.Sort((x, y) => x.Id.CompareTo(y.Id));
        b.Append(" carts=");
        if (carts.Count == 0) { b.Append('-'); return; }
        for (var i = 0; i < carts.Count; i++)
        {
            var (id, kind, slag) = carts[i];
            if (i > 0) b.Append(';');
            b.Append(id.ToString(CultureInfo.InvariantCulture)).Append(kind);
            if (slag == null || !IsInstanceValid(slag)) continue;
            b.Append('[');
            for (var s = 0; s < SlagCart.MaxRiders; s++) b.Append(s > 0 ? "," : "").Append(LiveId(slag.RiderNetIdAt(s) >= 0 ? slag.RiderAt(s) : null).ToString(CultureInfo.InvariantCulture));
            b.Append(']');
        }
    }

    /// <summary>The RIDE pass: the hounds riding the party's cart by spot and each coupled wagon's roof (living, carried there), as this side has them. Test only.</summary>
    private void BookBoarders(System.Text.StringBuilder b)
    {
        b.Append(" boarders=P[");
        var held = _rails.Cart.Boarders;
        for (var i = 0; i < held.Length; i++) b.Append(i > 0 ? "," : "").Append(LiveId(held[i] is { } h && IsInstanceValid(h) && h.Carrier == _rails.Cart ? h : null).ToString(CultureInfo.InvariantCulture));
        b.Append(']');
        var train = _rails.Train;
        for (var k = 0; k < train.Count; k++)
        {
            if (!IsInstanceValid(train[k])) continue;
            var roof = train[k].Boarders[0];
            b.Append('W').Append(train[k].Stop.ToString(CultureInfo.InvariantCulture)).Append('[')
                .Append(LiveId(roof is { } r && IsInstanceValid(r) && r.Carrier == train[k] ? r : null).ToString(CultureInfo.InvariantCulture)).Append(']');
        }
    }

    private static int LiveId(Enemy? e) => e != null && IsInstanceValid(e) && !e.IsQueuedForDeletion() && e.IsAlive ? e.NetId : -1;

    /// <summary>The chaser on this side (the server's own, a client's copy), or null.</summary>
    private MineCart? BookChaser()
    {
        if (!NetSession.IsClient) return _rails.Chaser;
        foreach (var cc in _carts.Values) if (cc.Chaser is { } m && IsInstanceValid(m) && !m.IsQueuedForDeletion()) return m;
        return null;
    }

    private static void BookSeats(System.Text.StringBuilder b, string name, ICartBody cart)
    {
        if (cart is not GodotObject o || !IsInstanceValid(o)) return;
        var taken = cart.Riders.Values.Where(r => r.Cart == cart && r is GodotObject g && IsInstanceValid(g)).OrderBy(r => r.Seat)
            .Select(r => r.Seat.ToString(CultureInfo.InvariantCulture) + ":" + (r is Adventurer h ? "h" + h.Index.ToString(CultureInfo.InvariantCulture) : r is Npc n ? n.Id : "?")).ToList();
        b.Append(name).Append('[').Append(string.Join(",", taken)).Append(']');
    }

    private int _rideHammer = -1;
    private HammerPhase _rideHammerWas = HammerPhase.Up;

    /// <summary>
    /// The RIDE pass, each physics tick on both sides (after the floor clock's tick): ride 4's first drop hammer entering its Fall from its
    /// glow, printed at the server's clock. The hammers fall on the floor clock whether or not their ride is armed, so the pass (which
    /// ends at stop 3) pairs them all along: a client's must fall within 50 ms of the server's.
    /// </summary>
    private void RideHammerTrace(double serverNow, string tag)
    {
        if (Main.Instance?.Builder is not { } builder || RailEncounterController.Instance is not { } enc || !IsInstanceValid(enc)) return;
        if (_rideHammer < 0) _rideHammer = enc.HammerIndexOf(4); // asked again until the pieces are built: a −1 is never kept
        if (_rideHammer < 0) return;
        var phase = SetPieceRules.Phase(builder.FloorTick, _rideHammer, out _);
        if (phase == HammerPhase.Fall && _rideHammerWas == HammerPhase.Glow)
            GD.Print(FormattableString.Invariant($"{tag} ride hammer={_rideHammer} fall t={serverNow:0.000} floor={builder.FloorSeconds:0.000}"));
        _rideHammerWas = phase;
    }

    private void RideTraceClient(double serverNow)
    {
        if (_rails.Follower is not { HasState: true } f) return;
        var grid = (long)Math.Floor(serverNow / RideGrid);
        if (grid == _rideGrid) return;
        var first = _rideGrid == long.MinValue;
        _rideGrid = grid;
        if (first) return; // the first state may come late in a grid step (a joiner's catch-up): print from the next boundary, as the server does
        var t = (grid * RideGrid).ToString("0.0", CultureInfo.InvariantCulture);
        GD.Print(FormattableString.Invariant($"[net] ride t={t} S={f.S:0.000}"));
        foreach (var cc in _carts.Values)
            if (IsInstanceValid(cc.Body)) GD.Print(FormattableString.Invariant($"[net] ride cart={cc.Id} kind={cc.Kind} t={t} S={cc.Follow.S:0.000}"));
        GD.Print(FormattableString.Invariant($"[net] ride book t={t} now={serverNow:0.000} {RideBook()}"));
    }
}
