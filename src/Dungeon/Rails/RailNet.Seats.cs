using System.Globalization;
using DungeonCrawler.Adventure;
using DungeonCrawler.Adventure.Objectives;
using DungeonCrawler.Core;
using DungeonCrawler.Net;
using DungeonCrawler.Party;
using Godot;

namespace DungeonCrawler.Dungeon.Rails;

/// <summary>The rails on the wire: seats, the server's sends and catch-up and a client's books (see the main part).</summary>
public partial class RailNet
{
    // ---- seats (the server) -------------------------------------------------------------------

    private readonly record struct SeatChange(ICartRider Rider, int Cart, int Seat, bool Seated);

    private readonly List<SeatChange> _seats = new();
    /// <summary>The train's wagons and the stops' cages whose books are heard, each with its handler (let go when this node goes).</summary>
    private readonly List<(ICartBody Body, Action<ICartRider, bool> Handler)> _wagonsHeard = new();
    private readonly List<LeapMsg> _airLeaps = new();

    /// <summary>The party cart's book changed (any path): sent after this tick's cart state. The seat is read now, while the rider still holds it.</summary>
    private void OnPartyRider(ICartRider rider, bool seated) => _seats.Add(new SeatChange(rider, CartIds.Party, rider.Seat, seated));

    /// <summary>A chaser was placed (task 9): its book is heard from before anyone sits in it, under its wire id (ChaserBase + its stop). Once per chase.</summary>
    private void OnChaserPlaced(MineCart chaser, int stop)
    {
        var id = CartIds.Chaser(stop);
        chaser.RiderChanged += (rider, seated) => _seats.Add(new SeatChange(rider, id, rider.Seat, seated));
    }

    /// <summary>
    /// A ride's slag cart was spawned (server): its book (the heroes who leapt onto its deck, plan 13) is heard under its wire id from now on.
    /// Once per cart; the handler goes with the cart.
    /// </summary>
    internal void HearSlagCart(SlagCart cart)
    {
        if (!NetSession.IsServer || !IsInstanceValid(cart)) return;
        var id = cart.CartId;
        cart.RiderChanged += (rider, seated) => _seats.Add(new SeatChange(rider, id, rider.Seat, seated));
    }

    /// <summary>
    /// Listen to each train wagon's book (under <see cref="CartIds.Wagon"/> by its number) and each cage's (under <see cref="CartIds.Cage"/> by its
    /// stop): the crew controller builds them after this node, so once each, the first tick they stand.
    /// </summary>
    private void HearWagons()
    {
        var wagons = _rails.Wagons;
        var crew = ObjectiveController.Instance as CrewRescueController;
        var cages = crew?.Cages;
        if (wagons.Count + (cages?.Count ?? 0) == _wagonsHeard.Count) return;
        foreach (var wagon in wagons)
            if (IsInstanceValid(wagon)) Hear(wagon, CartIds.Wagon(wagon.Number));
        if (cages != null)
            foreach (var cage in cages)
                if (IsInstanceValid(cage)) Hear(cage, CartIds.Cage(cage.Stop));
    }

    private void Hear(ICartBody body, int id)
    {
        foreach (var (b, _) in _wagonsHeard) if (b == body) return;
        Action<ICartRider, bool> handler = (rider, seated) => _seats.Add(new SeatChange(rider, id, rider.Seat, seated));
        body.RiderChanged += handler;
        _wagonsHeard.Add((body, handler));
    }

    private void FlushSeats(ServerWorld world)
    {
        if (_seats.Count == 0) return;
        foreach (var c in _seats)
        {
            // an unseat whose rider sits in another cart now is not sent: its seat there moves it on every client (a leap, Ruling 20)
            if (!LeapRules.SendsUnseat(c.Seated, c.Rider is GodotObject o && IsInstanceValid(o) && c.Rider.Cart != null)) continue;
            if (SeatMsgOf(c.Rider, c.Cart, c.Seat, c.Seated) is { } m) world.Broadcast(m);
        }
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

    private readonly List<LiveCart> _catchUpLive = new();

    /// <summary>
    /// Server: the rails as a joiner (or a client that rebuilt the floor: a floor change, a resync's rebuild, a wipe's reload) must hold
    /// them whatever it missed: one <see cref="CartStateMsg"/> with every live cart (the party's with its train, boarders and flags, and
    /// the ride's others with their riders), a <see cref="SeatMsg"/> for every rider seated in the party's cart, the train's wagons and the cages, and
    /// every leap still in the air (each from its own start time). The callers send the floor clock's pair just before, and the props
    /// (the rail part too) ride in the prop state.
    /// </summary>
    public void SendRailCatchUp(int peer)
    {
        if (!NetSession.IsServer || !Current || NetSession.Instance.Server is not { } world) return;
        var now = NetSession.Instance.Now;
        if (RailEncounterController.Instance is { } live && IsInstanceValid(live)) live.CollectNetCarts(_catchUpLive);
        else _catchUpLive.Clear();
        _rails.CollectChaser(_catchUpLive);
        var carts = new List<CartStateDto>(_catchUpLive.Count + 1) { _rails.CaptureNet(now) };
        foreach (var c in _catchUpLive) carts.Add(Capture(c, now));
        _catchUpLive.Clear();
        world.SendTo(peer, new CartStateMsg(Game.Instance.CurrentFloor, world.FloorEpoch, carts)); // before the seats and leaps that name them
        SendSeated(world, peer, _rails.Cart, CartIds.Party);
        if (_rails.Chaser is { } chaser) SendSeated(world, peer, chaser, chaser.CartId);
        foreach (var wagon in _rails.Wagons)
            if (IsInstanceValid(wagon)) SendSeated(world, peer, wagon, CartIds.Wagon(wagon.Number));
        if (ObjectiveController.Instance is CrewRescueController crew)
            foreach (var cage in crew.Cages)
                if (IsInstanceValid(cage)) SendSeated(world, peer, cage, CartIds.Cage(cage.Stop));
        foreach (var slag in SlagCart.Live) // the leapers on a ride's slag carts' decks (plan 13), after the cart state that lists them
            if (IsInstanceValid(slag) && !slag.IsQueuedForDeletion() && !slag.Derailed && slag.Riders.Count > 0) SendSeated(world, peer, slag, slag.CartId);
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

    // ---- seats (a client) ----------------------------------------------------------------------

    /// <summary>
    /// Client: the server seated or unseated a rider. Seated: its seat index is booked in this client's copy of the cart (a different
    /// occupant there gets out where it sits first) and the rider takes it. Unseated: a rider still in that cart here gets out at the
    /// server's landing; so does one still riding a cart freed here when the cart the unseat names is gone (a slag cart's leaper, plan 13).
    /// A leap (a seating in a slag cart, or back from one) plays its cues where the body left and lands; so does a hero's move car to car along the
    /// party's train (plan 14: from one train body to another, the cart or a wagon), which keeps its seat yaw. Idempotent: a rider already in that
    /// seat is only posed.
    /// </summary>
    internal void OnSeat(SeatMsg m)
    {
        if (!NetSession.IsClient || !Current || RiderOf(m.RiderKind, m.RiderId) is not { } rider) return;
        var cart = CartById(m.Cart);
        if (!m.Seated)
        {
            if (m.RiderKind == SeatRiderKind.Hero && rider is Adventurer { IsLocal: true }) LastLocalLanding = (new Vector3(m.X, m.Y, m.Z), NetSession.Instance.Now);
            if (rider is Adventurer held && PlaceHeld(held, new Vector3(m.X, m.Y, m.Z), m.Cart)) return; // a leaper held off the copy that went: its landing on footing is the server's word (re-review N1, R1)
            if (cart != null && rider.Cart == cart) rider.GetOut(new Vector3(m.X, m.Y, m.Z), Vector3.Zero);
            else if (cart == null && rider.Cart is { } ridden && (ridden is not Node n || !IsInstanceValid(n) || n.IsQueuedForDeletion())) rider.GetOut(new Vector3(m.X, m.Y, m.Z), Vector3.Zero);
            return;
        }
        if (cart == null) return;
        if (rider.Cart == cart && rider.Seat == m.Seat) { rider.FollowSeat(); return; }
        var leapt = rider is Adventurer && (cart is SlagCart || rider.Cart is SlagCart) && rider is Node3D was ? was.GlobalPosition : (Vector3?)null;
        var moved = rider is Adventurer && TrainBody(cart) && rider.Cart is { } old && old != cart && TrainBody(old) && rider is Node3D sat ? sat.GlobalPosition : (Vector3?)null;
        if (!cart.Seats.Put(rider.RiderId, m.Seat))
        {
            if (cart.Seats.OccupantOf(m.Seat) is int holder)
            {
                if (cart.Riders.TryGetValue(holder, out var other) && other is Node3D n && IsInstanceValid(n)) other.GetOut(n.GlobalPosition, Vector3.Zero);
                cart.Seats.Leave(holder);
            }
            if (!cart.Seats.Put(rider.RiderId, m.Seat)) return;
        }
        if (moved != null && rider is Adventurer climber) climber.SitIn(cart, m.Seat, keepYaw: true); // a move keeps where the hero looks
        else rider.SitIn(cart, m.Seat);
        if (leapt is { } from && rider is Adventurer leaper) RailEncounterController.LeapCue(leaper, from, leaper.GlobalPosition, shake: false); // the server tells our own screen's shake
        else if (moved is { } left && rider is Adventurer hopper)
        {
            RailEncounterController.LeapCue(hopper, left, hopper.GlobalPosition, shake: false); // cart_jump where it left, cart_land where it sits
            _rails.MoveTakenFor(hopper); // our own plain press the server made a move (a stale revive): the move's grace starts now (M8)
        }
    }

    /// <summary>A body of the party's train here: its cart or one of its wagons (never a parked tub: plan 15 Task 5, Ruling 23).</summary>
    private bool TrainBody(ICartBody body) => body == _rails.Cart || body is TrainWagon wagon && _rails.WagonAt(wagon.Number) == wagon;

    /// <summary>Client: where the server last set this client's own hero down from a seat, and when (this process's clock); the RIDE pass reads it.</summary>
    internal (Vector3 At, double When)? LastLocalLanding { get; private set; }

    /// <summary>A cart on the wire as a seat book here: the party's, a train wagon by its number, a cage by its stop, a chaser's copy (its riders, NPCs too, sit in it here), or a slag cart's copy (its deck's leapers).</summary>
    private ICartBody? CartById(int id)
    {
        if (id == CartIds.Party) return IsInstanceValid(_rails.Cart) ? _rails.Cart : null;
        if (_carts.TryGetValue(id, out var sc) && sc.Slag is { } slag && IsInstanceValid(slag) && !slag.IsQueuedForDeletion() && !slag.Derailed) return slag;
        if (id >= CartIds.WagonBase && id < CartIds.CageBase) return _rails.WagonAt(id - CartIds.WagonBase);
        if (id >= CartIds.CageBase && id < CartIds.ChaserBase) return CageAt(id - CartIds.CageBase);
        if (id >= CartIds.ChaserBase && _carts.TryGetValue(id, out var cc) && cc.Chaser is { } chaser && IsInstanceValid(chaser) && !chaser.IsQueuedForDeletion()) return chaser;
        return null;
    }

    private static PlatformCage? CageAt(int stop)
    {
        if (ObjectiveController.Instance is not CrewRescueController crew) return null;
        foreach (var cage in crew.Cages) if (IsInstanceValid(cage) && cage.Stop == stop) return cage;
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
}
