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
}
