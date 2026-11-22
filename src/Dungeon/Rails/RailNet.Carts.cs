using System.Globalization;
using DungeonCrawler.Core;
using DungeonCrawler.Enemies;
using DungeonCrawler.Net;
using Godot;

namespace DungeonCrawler.Dungeon.Rails;

/// <summary>The rails on the wire: a client's copies of the carts besides the party's (see the main part).</summary>
public partial class RailNet
{
    // ==== a client ================================================================================

    private sealed class ClientCart
    {
        public int Id;
        public CartKind Kind;
        public byte Track;
        /// <summary>The server's encounter id: a ride's runaway carries its set piece's (the traffic runaway none).</summary>
        public string? EncounterId;
        public Node3D Body = null!;
        public CartRun Run = null!;
        public CartFollower Follow = null!;
        public SlagCart? Slag;
        /// <summary>A chaser's tub: its riders are booked and seated in it here (SeatMsgs).</summary>
        public MineCart? Chaser;
        public Enemy? Rider;
        /// <summary>Its sound (the golem runner's: a track change only), and the follower's snaps it was last fed at (a snap primes it).</summary>
        public CartSound? Sound;
        public int Snaps = int.MinValue;
        /// <summary>The riders' net ids by spot as the server last named them (−1 none).</summary>
        public readonly int[] Wanted = { -1, -1, -1 };
        /// <summary>A chaser's countdown seconds left as the server last told (−1 none), and this client's server time then.</summary>
        public float BoardingLeft = -1f;
        public double BoardingAt;
        /// <summary>A runaway on the party line: seconds toward its next ring behind our train (<see cref="RunawayRules.Rings"/>, the server's rule).</summary>
        public float BellClock;

        /// <summary>The chaser's countdown seconds left now, run on from the server's last word (−1 none). No allocation.</summary>
        public float BoardingLeftNow()
        {
            if (BoardingLeft < 0f) return -1f;
            if (NetSession.Instance?.Client is not { } client) return BoardingLeft;
            return Math.Max(0f, BoardingLeft - (float)(client.ServerNow - BoardingAt));
        }
    }

    private readonly Dictionary<int, ClientCart> _carts = new();
    private readonly HashSet<int> _listed = new();
    private readonly List<int> _drop = new();
    private readonly HashSet<byte> _unresolved = new();
    private readonly List<ICartRider> _freeRiders = new();

    /// <summary>Client: the carts besides the party's that run here now (the RIDE pass reads them).</summary>
    internal int ClientCartCount => _carts.Count;

    /// <summary>Client: the chaser's copy here and its follower (the RIDE pass reads them), or null.</summary>
    internal (MineCart Cart, CartFollower Follow)? ClientChaser
    {
        get
        {
            foreach (var cc in _carts.Values)
                if (cc.Chaser is { } m && IsInstanceValid(m) && !m.IsQueuedForDeletion()) return (m, cc.Follow);
            return null;
        }
    }

    /// <summary>Client: when <paramref name="seat"/> is the chaser's copy here, its mode (the follower's) and countdown left (−1 none) for the HUD, else null. No allocation.</summary>
    internal (CartMode Mode, float Left)? ClientChaserHud(MineCart seat)
    {
        foreach (var cc in _carts.Values)
            if (cc.Chaser is { } m && m == seat && IsInstanceValid(m) && !m.IsQueuedForDeletion()) return (cc.Follow.Mode, cc.BoardingLeftNow());
        return null;
    }

    /// <summary>Client: the golem's runner here (its body's place: the party cart's sound hears a hit beside it as the golem's slam), or null.</summary>
    internal Node3D? ClientGolem
    {
        get
        {
            foreach (var cc in _carts.Values)
                if (cc.Kind == CartKind.Golem && IsInstanceValid(cc.Body) && !cc.Body.IsQueuedForDeletion()) return cc.Body;
            return null;
        }
    }

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
            if (cc != null) Free(cc, d.Has(CartFlags.Derailed), d.S);
            return;
        }
        if (cc == null && (cc = Build(d)) == null) return;
        if (d.Track != cc.Track)
        {
            if (_rails.TrackOf(d.Track) is not { Valid: true } track)
            {
                // a spur this client cannot find: stay on the old track rather than pose the cart there at the spur's S (the next state retries)
                if (_unresolved.Add(d.Track)) GD.PushWarning($"[net] cart {d.Cart}: track {d.Track} is not on this floor here; its state is skipped");
                return;
            }
            cc.Track = d.Track; // the golem's runner onto the spur: a new run on the new track (the follower snaps)
            cc.Run = new CartRun(track, cc.Body, new CartState(d.S, d.V, CartMode.Rolling, d.Plating), d.Dir);
        }
        cc.Run.Limit = d.Limit;
        cc.Follow.Apply(d, now);
        (cc.BoardingLeft, cc.BoardingAt) = (d.BoardingLeft, now);
        if (cc.Chaser is { } chaser) chaser.CollisionLayer = d.Mode == (byte)CartMode.Held ? 1u | 8u : 0u; // solid and a thing to use (its rider's "Get out") while it stands, as the server's
        for (var i = 0; i < cc.Wanted.Length; i++) cc.Wanted[i] = d.RiderNetIds != null && i < d.RiderNetIds.Count ? d.RiderNetIds[i] : -1;
        Bind(cc);
    }

    /// <summary>A copy of the server's cart, built as the server builds it, as dressing: it judges, paces and warns nothing.</summary>
    private ClientCart? Build(CartStateDto d)
    {
        if (GetParent() is not Node3D root || _rails.TrackOf(d.Track) is not { Valid: true } track) return null;
        var kind = (CartKind)d.Kind;
        var state = new CartState(d.S, d.V, CartMode.Rolling, d.Plating);
        var cc = new ClientCart { Id = d.Cart, Kind = kind, Track = d.Track, EncounterId = d.EncounterId, Follow = new CartFollower(_rails) };
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
                cc.Sound = cart.Sound;
                if (cart.Sound != null) cart.Sound.AnnounceOnStart = kind == CartKind.Traffic; // as the server's traffic cart rings entering the line
                break;
            }
            case CartKind.Runaway:
            {
                var wagon = new RunawayWagon { CartId = d.Cart };
                root.AddChild(wagon);
                cc.Body = wagon;
                cc.Run = new CartRun(track, wagon, state, d.Dir);
                cc.Sound = wagon.Sound;
                if (wagon.Sound != null) wagon.Sound.AnnounceOnStart = true; // it rings breaking loose, as the server's
                break;
            }
            case CartKind.Golem:
            {
                var runner = new Node3D { Name = $"GolemRunner_{d.EncounterId ?? d.Cart.ToString(CultureInfo.InvariantCulture)}" };
                root.AddChild(runner);
                cc.Sound = new CartSound { Wheels = false };
                runner.AddChild(cc.Sound);
                cc.Body = runner;
                cc.Run = new CartRun(track, runner, state, d.Dir);
                break;
            }
            case CartKind.Chaser:
            {
                // a left-behind player's spare running after the train: the party's tub, its riders seated by SeatMsgs; standing, its rider's "Get out" goes as spare:{stop}
                var chaser = new MineCart { Name = $"ChaserCart{d.Cart - CartIds.ChaserBase}", CartId = d.Cart, CollisionLayer = 1u | 8u }.AsChaser();
                var follow = cc.Follow;
                chaser.PromptFor = hero => SpareRules.ChaserPrompt(hero.Cart == chaser, follow.HasState && follow.Mode == CartMode.Held, hero.Downed, cc.BoardingLeftNow()); // the server's rule, from its state
                root.AddChild(chaser);
                cc.Body = chaser;
                cc.Chaser = chaser;
                cc.Run = new CartRun(track, chaser, state, d.Dir);
                (chaser.NetRun, chaser.NetFollow) = (cc.Run, cc.Follow); // a seated headset's comfort inputs read the server's ride
                cc.Sound = chaser.Sound;
                break;
            }
            default:
                return null;
        }
        cc.Run.Limit = d.Limit;
        cc.Run.PoseAt(d.S);
        _carts[d.Cart] = cc;
        if (cc.Chaser is { } placed) _rails.ChaserPlacedSound(placed, track, d.S); // its first state: announced, and the spawn bell by the server's rule
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

    /// <summary>
    /// A copy goes: derailed (its tumble, its riders set down), or through the broken bridge's gap or over the slag line's drop when the server's
    /// cart went at enemy S <paramref name="goneS"/> there (<see cref="SwitchBackRules.PlungedAt"/>: quiet, its riders laid in the dark under it), or freed (its
    /// riders go with it, as the server frees them; the golem is set down).
    /// </summary>
    private void Free(ClientCart cc, bool derailed, float goneS = float.NaN)
    {
        _carts.Remove(cc.Id);
        if (!IsInstanceValid(cc.Body)) return;
        if (cc.Slag is { } slag)
        {
            HoldLeapers(slag); // a leaper still on its deck here (its fate still in flight) is held hidden where it sat, never set down there (ruling (d))

            if (derailed && RailEncounterController.Instance is { } enc && IsInstanceValid(enc) && enc.PlungedAt(goneS)) { slag.ShowPlunged(enc.UnderTheGap()); return; }
            if (derailed && RailEncounterController.Instance is { } leap && IsInstanceValid(leap) && leap.DropPlungedAt(goneS))
            {
                slag.ShowPlunged(leap.UnderTheDrop()); // quiet: the crash where it goes, its riders laid in the dark under the drop (the log line is the server's)
                GD.Print(FormattableString.Invariant($"[net] ride: slag cart {cc.Id} went over the drop here (it went at enemy S={goneS:0.0}; the drop's edge at {leap.LeapDropEnemyS:0.0})"));
                return;
            }
            if (derailed) { slag.ShowDerailed(); return; }
            foreach (var rider in slag.Aboard)
                if (IsInstanceValid(rider) && rider.Carrier == slag && !rider.IsQueuedForDeletion()) rider.QueueFree();
            slag.Aboard.Clear();
        }
        else if (cc.Kind == CartKind.Runaway && derailed && RailEncounterController.Instance is { } split && IsInstanceValid(split) && split.SplitPlungedAt(cc.Track, goneS)) ShowSplitPlunge(cc, goneS);
        else if (RunawayRules.ShowsRam(cc.Kind == CartKind.Runaway, cc.Track, derailed)) ShowRam(cc, goneS);
        else if (cc.Rider is { } golem && IsInstanceValid(golem) && golem.Carrier == cc.Body) golem.Drop(golem.GlobalPosition);
        else if (cc.Chaser is { } chaser) SetDownSeated(chaser); // the server sets its riders down before it goes; one still seated here (an unseat still in flight) gets out where it sits
        cc.Body.QueueFree();
    }

    /// <summary>Heroes held off a slag copy that went (ruling (d)), each with where it sat and when the hold began (this process's clock, s).</summary>
    private readonly List<(Party.Adventurer Hero, Vector3 At, double Since)> _held = new();
    /// <summary>Heroes held off a slag copy as it went, on this floor build (the RIDE pass reads it).</summary>
    internal int LeapersHeld { get; private set; }

    /// <summary>
    /// Client: every hero still seated on <paramref name="slag"/>'s copy as it goes (Task 7 ruling (d); its seat book freed with it): out of the
    /// copy's book, hidden and held still where it sat, never set down there (over the drop's gone cells, or a gap), until the server's word
    /// (<see cref="LeapRules.HeldLeaperNow"/>, judged each frame by <see cref="TickHeld"/>): a seat (a haul back) shows it there, Fallen leaves it
    /// to the crevasse's hold, a set-down's ForcePose or poses move it and it is shown once <see cref="LeapRules.HoldForServer"/> has run out.
    /// Any other rider (none sits a slag deck but a hero) gets out where it sits, as before.
    /// </summary>
    private void HoldLeapers(SlagCart slag)
    {
        if (slag.Riders.Count == 0) return;
        _freeRiders.Clear();
        foreach (var rider in slag.Riders.Values) _freeRiders.Add(rider);
        var now = Time.GetTicksMsec() / 1000.0;
        foreach (var rider in _freeRiders)
        {
            if (rider is not Node3D n || !IsInstanceValid(n) || rider.Cart != slag) continue;
            var at = n.GlobalPosition;
            rider.GetOut(at, Vector3.Zero); // out of the going copy's book (a client sends nothing for it)
            if (rider is not Party.Adventurer hero) continue;
            hero.Visible = false;
            hero.Velocity = Vector3.Zero;
            _held.Add((hero, at, now));
            LeapersHeld++;
            GD.Print(FormattableString.Invariant($"[net] ride: {hero.State.Name} was on slag cart {slag.CartId}'s copy as it went here: held hidden where it sat ({DungeonBuilder.WorldToCell(at)}) for the server's word"));
        }
        _freeRiders.Clear();
    }

    /// <summary>Client, each rendered frame: the held heroes (<see cref="HoldLeapers"/>) kept hidden and still until the server's word, then shown or left to the crevasse.</summary>
    private void TickHeld()
    {
        if (_held.Count == 0) return;
        var now = Time.GetTicksMsec() / 1000.0;
        for (var i = _held.Count - 1; i >= 0; i--)
        {
            var (hero, at, since) = _held[i];
            if (!IsInstanceValid(hero) || hero.Parked) { _held.RemoveAt(i); continue; }
            var placed = hero.IsLocal && hero.GlobalPosition.DistanceTo(at) > 0.5f; // our own body kept still here, moved since: the server's ForcePose placed it
            switch (placed ? HeldLeaper.Show : LeapRules.HeldLeaperNow(hero.Seated, hero.State.Fallen, now - since))
            {
                case HeldLeaper.Show:
                    hero.Visible = true;
                    _held.RemoveAt(i);
                    break;
                case HeldLeaper.Fallen:
                    _held.RemoveAt(i); // the crevasse's hold keeps it hidden (Adventurer.HoldInCrevasse)
                    break;
                default:
                    if (hero.Visible) hero.Visible = false;
                    if (hero.IsLocal) { hero.GlobalPosition = at; hero.Velocity = Vector3.Zero; } // our own body is ours to keep still; a remote one follows the server's poses, hidden
                    break;
            }
        }
    }

    /// <summary>Client: every rider still seated in <paramref name="cart"/>'s copy gets out where it sits (the copy is about to go).</summary>
    private void SetDownSeated(ICartBody cart)
    {
        if (cart.Riders.Count == 0) return;
        _freeRiders.Clear();
        foreach (var rider in cart.Riders.Values) _freeRiders.Add(rider);
        foreach (var rider in _freeRiders)
            if (rider is Node3D n && IsInstanceValid(n) && rider.Cart == cart) rider.GetOut(n.GlobalPosition, Vector3.Zero);
        _freeRiders.Clear();
    }

    /// <summary>
    /// Client: the server sent ride 4's runaway gone from the party line with its Derailed flag, which only its ram sets (<see cref="RunawayRules.ShowsRam"/>,
    /// Ruling 19: an existing bit of the existing message): the jolt and the smash, once, at our copy. The seat shake is the server's (relayed);
    /// the HP and the plating come as their own messages. A wrecking ram is shown too (the wreck has its own cues besides).
    /// </summary>
    private void ShowRam(ClientCart cc, float goneS)
    {
        Props.Tremor.ShakeOnly(0.04f, 0.4f);
        RideAudio.At("timber_smash", cc.Body.GlobalPosition);
        GD.Print(FormattableString.Invariant($"[net] ride: the runaway rammed the train here (it went at S={goneS:0.0}; our copy at S={cc.Run.State.S:0.0}, our train's rear S={OwnTrainRearS():0.0})"));
    }

    /// <summary>
    /// Client: the server sent ride 4's runaway gone from the split's spur with its Derailed flag at the spur's end (<see cref="SplitRules.PlungedOffSpur"/>,
    /// Ruling 19): it went into the crevasse, quietly (a thud and the crash where our copy is; the log line is the server's).
    /// </summary>
    private void ShowSplitPlunge(ClientCart cc, float goneS)
    {
        if (GetParent() is Node3D root) Audio.Synth.PlayAt(root, Audio.Synth.Thud(), cc.Body.GlobalPosition, -1f);
        RideAudio.At("cart_crash", cc.Body.GlobalPosition);
        GD.Print(FormattableString.Invariant($"[net] ride: the runaway took the spur here and plunged (it went at spur S={goneS:0.0}; our copy at S={cc.Run.State.S:0.0})"));
    }

    /// <summary>Client: a ride's runaway copy (one named by its set piece, never the traffic runaway) runs on the party line with its front short of party S <paramref name="pointsS"/> (the split's lever is offered while one comes).</summary>
    internal bool ClientRunawayShortOf(float pointsS)
    {
        foreach (var cc in _carts.Values)
            if (cc.Kind == CartKind.Runaway && cc.EncounterId != null && cc.Track == 0 && IsInstanceValid(cc.Body) && cc.Follow.HasState && cc.Follow.Mode == CartMode.Rolling && cc.Follow.S + cc.Run.Dir * CartRules.HalfLength < pointsS) return true;
        return false;
    }

    /// <summary>Client: a ride's runaway copy here (one named by its set piece, never the traffic runaway): its cart id, piece id, follower's S and track (the RIDE pass reads it), or null.</summary>
    internal (int Id, string Piece, float S, byte Track)? ClientRideRunaway
    {
        get
        {
            foreach (var cc in _carts.Values)
                if (cc.Kind == CartKind.Runaway && cc.EncounterId is { } piece && IsInstanceValid(cc.Body) && !cc.Body.IsQueuedForDeletion() && cc.Follow.HasState) return (cc.Id, piece, cc.Follow.S, cc.Track);
            return null;
        }
    }

    /// <summary>Client: our own train's last body's back S, as this screen shows it (the RIDE pass reads it too).</summary>
    internal float OwnTrainRearS() => RunawayRules.TrainRearS(_rails.Party.BackS, _rails.Train.Count, RailController.WagonSpacing);

    /// <summary>Client: a runaway on the party line rings behind our train by the server's rule (<see cref="RunawayRules.Rings"/>), from its own place: positional.</summary>
    private void RingBehind(ClientCart cc, float s, float dt)
    {
        if (cc.Kind != CartKind.Runaway || cc.Track != 0 || cc.Sound is not { } bell || !IsInstanceValid(bell)) return;
        var front = s + cc.Run.Dir * CartRules.HalfLength;
        var rear = OwnTrainRearS();
        if (!RunawayRules.Rings(cc.Follow.Mode == CartMode.Rolling, front, rear) || (cc.BellClock += dt) < RunawayRules.BellEvery) return;
        cc.BellClock = 0f;
        bell.RingNow();
    }

    public override void _Process(double delta)
    {
        if (!NetSession.IsClient || !Current) return;
        var dt = (float)delta;
        TickHeld();
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
            else if (cc.Chaser is { } chaser)
                foreach (var rider in chaser.Riders.Values)
                    if (rider is not GodotObject o || IsInstanceValid(o)) rider.FollowSeat();
            Hear(cc, s);
            RingBehind(cc, s, dt);
        }
        if (NetSession.Instance.Client is not { } client) return;
        FlyLeaps(client.ServerNow); // after every cart is posed: the arcs start on one and end on another
        FollowBoarders();
        if (NetSession.Instance.TestRide) RideTraceClient(client.ServerNow);
    }

    /// <summary>
    /// Client, each rendered frame: a copy's sound from its follower (its S, speed, mode and plating; its track), a follower snap of more than
    /// a metre or onto another track priming it.
    /// The golem's runner hears its track only (the DTO's: its one sound is the points' clunk, and a track change snaps the follower, so its
    /// snaps never prime it).
    /// </summary>
    private static void Hear(ClientCart cc, float s)
    {
        if (cc.Sound is not { } sound || !IsInstanceValid(sound)) return;
        if (!sound.Wheels) { sound.FeedTrack(cc.Track); return; }
        if (cc.Follow.Snaps != cc.Snaps)
        {
            if (cc.Snaps != int.MinValue) sound.Snapped(s, cc.Track);
            cc.Snaps = cc.Follow.Snaps;
        }
        sound.FeedRun(cc.Run, s, cc.Follow.V, cc.Follow.Mode, cc.Follow.Plating, cc.Track);
    }
}
