using DungeonCrawler.Adventure.Objectives;
using DungeonCrawler.Net;
using Godot;

namespace DungeonCrawler.Dungeon.Rails;

/// <summary>
/// The party's cart on the wire. The server captures its state for <see cref="RailNet"/> (<see cref="CaptureNet"/>, with a cheap
/// <see cref="NetSignature"/> to tell when it changed). A client follows that state: <see cref="ApplyNet"/> feeds a
/// <see cref="CartFollower"/> (this controller is its <see cref="ISlopeSource"/>), applies the lane, the gap, the train, the
/// flags and the HUD fields, and plays the tumble on a wreck's rising edge; each rendered frame poses the cart, the train and
/// the riders at the follower's S (<see cref="ClientPose"/>). A client never steps, judges or raises anything.
/// </summary>
public partial class RailController : ISlopeSource
{
    /// <summary>Wrecks so far (the server's): a change tells the cart sender a wreck happened, whenever its event is told.</summary>
    internal int Wrecks { get; private set; }

    /// <summary>The track a cart state names: 0 the party line, 1 the enemy line, 2+k the k-th spur.</summary>
    internal TrackSampler? TrackOf(byte track) => track switch
    {
        0 => Party.Track,
        1 => _enemyLane,
        _ => RailEncounterController.Instance?.SpurTrack(track - 2),
    };

    float ISlopeSource.SlopeAt(byte track, float s) => TrackOf(track) is { Valid: true } t ? t.Slope(s) : 0f;

    // ---- the server's side -------------------------------------------------------------------

    private byte NetFlags()
    {
        var f = CartFlags.None;
        if (Halted) f |= CartFlags.Halted;
        if (State.Mode == CartMode.Wrecked) f |= CartFlags.Wrecked;
        if (!Cart.Visible) f |= CartFlags.Hidden;
        if (LineBlocked) f |= CartFlags.LineBlocked;
        if (GateClear) f |= CartFlags.GateClear;
        if (ChaseLive) f |= CartFlags.ChaseLive;
        return (byte)f;
    }

    /// <summary>The cart's flags now (<see cref="CartFlags"/>).</summary>
    internal byte NetFlagsNow => NetFlags();

    /// <summary>Changes whenever anything a client shows of the cart (short of its motion) changes: the cart sender's dirty test. No allocation.</summary>
    internal int NetSignature()
    {
        var train = _train.Count;
        for (var i = 0; i < _train.Count; i++) train = train * 31 + _train.Wagons[i].Stop;
        var line = HashCode.Combine(_crossOut, _crossBack, _gapFrom, _gapTo, _jumpV);
        var boarders = 17;
        for (var i = 0; i < Cart.Boarders.Length; i++) boarders = boarders * 31 + AboardId(Cart.Boarders[i], Cart);
        for (var i = 0; i < _train.Count; i++) boarders = boarders * 31 + AboardId(_train.Wagons[i].Boarders[0], _train.Wagons[i]);
        // a player's brake or pump (their RideIntent) reaches clients at once, not with the next correction; companions' pumping is left
        // out (it can flip at the 0.7·Cruise threshold, and it follows the state smoothly)
        var input = HashCode.Combine(Party.LastInput.Brake, _playersPumping);
        // the armed ride and its arm time (a client's buckets run from it): an arming in a cart event is sent with it anyway, this is the backstop
        var armed = RailEncounterController.Instance is { } enc && IsInstanceValid(enc) ? HashCode.Combine(enc.ArmedRide, enc.ArmedAt) : 0;
        return HashCode.Combine((int)State.Mode, StopIndex, NetFlags(), line, train, Boarding.Running, (int)State.Plating, HashCode.Combine(Wrecks, boarders, input, armed));
    }

    /// <summary>Players pumping the cart this tick (<see cref="Inputs"/>; 0 under a brake).</summary>
    private int _playersPumping;

    /// <summary>The net id of a hound riding <paramref name="body"/> at a boarder spot (landed: carried by it), or −1 (free, reserved for a leap still in the air, gone).</summary>
    private static int AboardId(Enemies.Enemy? hound, Node3D body)
        => hound != null && IsInstanceValid(hound) && !hound.IsQueuedForDeletion() && hound.Carrier == body ? hound.NetId : -1;

    /// <summary>The boarders aboard the cart by spot, and each coupled wagon's (train order), for the wire: null when none rides.</summary>
    private void CaptureBoarders(out List<int>? cart, out List<int>? wagons)
    {
        cart = wagons = null;
        for (var i = 0; i < Cart.Boarders.Length; i++)
        {
            var id = AboardId(Cart.Boarders[i], Cart);
            if (id < 0) continue;
            if (cart == null) { cart = new List<int>(Cart.Boarders.Length); for (var k = 0; k < Cart.Boarders.Length; k++) cart.Add(-1); }
            cart[i] = id;
        }
        for (var i = 0; i < _train.Count; i++)
        {
            var id = AboardId(_train.Wagons[i].Boarders[0], _train.Wagons[i]);
            if (id < 0) continue;
            if (wagons == null) { wagons = new List<int>(_train.Count); for (var k = 0; k < _train.Count; k++) wagons.Add(-1); }
            wagons[i] = id;
        }
    }

    /// <summary>The cart's state now, for the wire (<paramref name="flags"/> in place of its own: the wreck frame).</summary>
    internal CartStateDto CaptureNet(double serverTime, byte? flags = null)
    {
        var input = Party.LastInput;
        CaptureBoarders(out var boarders, out var wagonBoarders);
        List<int>? train = null;
        if (_train.Count > 0)
        {
            train = new List<int>(_train.Count);
            foreach (var w in _train.Wagons) train.Add(w.Stop);
        }
        var enc = RailEncounterController.Instance is { } e && IsInstanceValid(e) ? e : null;
        return new CartStateDto
        {
            Cart = CartIds.Party, Kind = (byte)Cart.Kind, Track = 0, Dir = (sbyte)Party.Dir,
            S = State.S, V = State.V, Mode = (byte)State.Mode, Limit = Party.Limit,
            Pumpers = (byte)Math.Clamp(input.Pumpers, 0, 255), Brake = input.Brake, CrewWagons = (byte)_train.Count, TargetOverride = -1f,
            Plating = State.Plating, StopIndex = StopIndex, Flags = flags ?? NetFlags(),
            CrossOut = _crossOut, CrossBack = _crossBack, GapFrom = _gapFrom, GapTo = _gapTo, JumpV = _jumpV > 0f ? _jumpV : float.NaN,
            BlockedAt = LineBlocked && !OnEnemyLane(BlockedAt) ? BlockedAt : float.NaN, // as HitBlock judges it: a cart across on the enemy lane passes it
            BoardingLeft = Boarding.Running ? Boarding.Left : -1f, Train = train, Boarders = boarders, WagonBoarders = wagonBoarders, ServerTime = serverTime,
            ArmedRide = enc?.ArmedRide ?? -1, ArmedAt = enc != null && enc.ArmedRide >= 1 ? enc.ArmedAt : 0,
        };
    }

    /// <summary>Test hook (--test-net-server's RIDE pass): the stop's holders die by the test's hand, so its gate lifts for real at the next gate check. Returns how many died.</summary>
    internal int DebugKillHolders(int stop) => NetSession.IsClient ? 0 : _gates.DebugKillHolders(stop);

    /// <summary>Test hook (--test-net-server's RIDE pass): the cart is wrecked where it stands, as a lost plating would (a spare takes over when one is left).</summary>
    internal void DebugWreck()
    {
        if (NetSession.IsClient) return;
        Wreck();
    }

    // ---- a client's side ---------------------------------------------------------------------

    private bool _sentPump, _sentBrake;
    private Net.Cadence _rideResend = new(RideInputRules.ResendSeconds);

    /// <summary>
    /// Client, each physics tick: this client's hero seated in the cart tells the server its stick (<see cref="RideIntent"/>): on every
    /// change, and every <see cref="RideInputRules.ResendSeconds"/> of elapsed time while it pumps or brakes. On foot nothing is sent
    /// (the server let go of it at the unseat). No allocation unless it sends.
    /// </summary>
    private void SendRideIntent(double delta)
    {
        var me = Core.Main.Instance?.Party?.Local;
        if (me == null || me.Cart != Cart || me.Downed)
        {
            _sentPump = _sentBrake = false;
            _rideResend.Reset();
            return;
        }
        var (pump, brake) = RideInputRules.FromMove(me.Intent.Move.Y);
        var changed = pump != _sentPump || brake != _sentBrake;
        var resend = (pump || brake) && _rideResend.Advance(delta) > 0;
        if (!changed && !resend) return;
        if (changed) _rideResend.Reset();
        _sentPump = pump;
        _sentBrake = brake;
        NetSession.Instance.Send(new RideIntent(pump, brake));
    }

    private CartFollower? _follow;
    private byte _netFlags;
    private CartMode _netMode = CartMode.Held;
    private float _netBoardingLeft = -1f;
    private double _netBoardingAt;

    /// <summary>Client: the cart follows the server's state (a state has arrived).</summary>
    public bool Following => _follow is { HasState: true };
    /// <summary>Client: the follower (the RIDE pass reads its S), or null before the first state.</summary>
    internal CartFollower? Follower => _follow;
    /// <summary>Client: the server's clock in the first state that showed the cart rolling from a stop (its departure), or −1 (the RIDE pass reads it).</summary>
    internal double NetDepartedAt { get; private set; } = -1;
    /// <summary>Client: the server's clock as this screen reckons it when the train last changed here, or −1 (the RIDE pass reads it).</summary>
    internal double NetTrainChangedAt { get; private set; } = -1;
    /// <summary>Client: how many wrecks this screen has shown (a Wrecked flag's rising edge), and the server's clock of the last one's state (the RIDE pass reads them).</summary>
    internal int NetWrecksSeen { get; private set; }
    internal double NetWreckedAt { get; private set; } = -1;

    /// <summary>Client: the server's state of the party's cart, applied at <paramref name="serverNow"/>. Sets no server state.</summary>
    internal void ApplyNet(CartStateDto d, double serverNow)
    {
        if (!NetSession.IsClient || !Current) return;
        _follow ??= new CartFollower(this);
        (Cart.NetRun, Cart.NetFollow) = (Party, _follow); // a seated headset's comfort inputs read the server's ride
        if (CartSendRules.WreckedRose(_netFlags, d.Flags))
        {
            NetWrecksSeen++;
            NetWreckedAt = d.ServerTime;
            if (Cart.Visible) Tumble(sound: false); // where it stands now, before the state moves it
        }
        _netFlags = d.Flags;
        var mode = d.Mode <= (byte)CartMode.Wrecked ? (CartMode)d.Mode : CartMode.Held;
        if (mode == CartMode.Rolling && _netMode == CartMode.Held && !Halted) NetDepartedAt = d.ServerTime; // left a stop (a halt's resume is not a departure)
        _netMode = mode;
        StopIndex = Math.Clamp(d.StopIndex, 0, Math.Max(0, StopS.Length - 1));
        Halted = d.Has(CartFlags.Halted);
        ApplyLaneNet(d.CrossOut, d.CrossBack);
        ApplyGapNet(d.GapFrom, d.GapTo, d.JumpV);
        ApplyTrain(d.Train);
        Party.Dir = d.Dir >= 0 ? 1 : -1;
        Party.Limit = d.Limit;
        _follow.Apply(d, serverNow);
        Party.State = new CartState(_follow.S, _follow.V, mode, d.Plating);
        RailEncounterController.Instance?.ApplyArmNet(d.ArmedRide, d.ArmedAt); // after the cart is placed: a joiner's beam already behind it stays still
        var shown = CartSendRules.Shown(d.Flags); // the cart only: the coupled wagons stay shown, as the server's HideWreck leaves them
        Cart.Visible = shown;
        if (shown) SetCartLayer();
        else Cart.CollisionLayer = 0;
        _netBoardingLeft = d.BoardingLeft;
        _netBoardingAt = d.ServerTime;
        if (mode != CartMode.Rolling) Cart.Speed = 0f;
        RefreshHud();
    }

    /// <summary>Client: couple and uncouple the cage wagons so the train is the server's (stops nearest first).</summary>
    internal void ApplyTrain(List<int>? stops)
    {
        var count = stops?.Count ?? 0;
        var same = count == _train.Count;
        for (var i = 0; same && i < count; i++) same = _train.Wagons[i].Stop == stops![i];
        if (same) return;
        NetTrainChangedAt = NetSession.Instance?.Client?.ServerNow ?? -1;
        _train.UncoupleAll();
        if (count == 0 || ObjectiveController.Instance is not CrewRescueController crew) return;
        foreach (var stop in stops!)
            foreach (var wagon in crew.Wagons)
                if (wagon.Stop == stop) { _train.Couple(wagon); break; }
    }

    /// <summary>Client, each rendered frame: the follower moves on and the cart, the train and the riders are posed at its S; speed and bend for the ride feel.</summary>
    private void ClientPose(float dt)
    {
        float s;
        if (_follow is { HasState: true } f)
        {
            s = f.Advance(dt);
            var v = _netMode == CartMode.Rolling ? f.V : 0f;
            Party.State = new CartState(s, v, _netMode, State.Plating);
            Cart.Speed = v;
            Cart.Curvature = Party.Dir * Party.Track.Curvature(s);
            _train.Sync();
        }
        else s = State.S; // parked at the first stop until the server's state arrives
        Party.PoseAt(s);
        _train.Pose(s);
        foreach (var rider in Cart.Riders.Values)
            if (rider is not GodotObject o || IsInstanceValid(o)) rider.FollowSeat();
        _train.Follow();
        _hudTimer -= dt;
        if (_hudTimer <= 0f) RefreshHud();
    }

    /// <summary>Client: seconds left on the server's boarding countdown now, or −1 when none runs.</summary>
    private float NetBoardingLeft()
    {
        if (_netBoardingLeft < 0f) return -1f;
        var now = NetSession.Instance?.Client?.ServerNow ?? _netBoardingAt;
        return Mathf.Max(0f, _netBoardingLeft - (float)(now - _netBoardingAt));
    }
}
