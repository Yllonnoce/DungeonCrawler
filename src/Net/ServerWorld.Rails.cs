using DungeonCrawler.Core;
using DungeonCrawler.Dungeon.Rails;
using DungeonCrawler.Party;
using Godot;

namespace DungeonCrawler.Net;

/// <summary>
/// Riding the party's cart from a client (plan 5 task 5): each seated player's last <see cref="RideIntent"/> (pump, brake), which the
/// <see cref="RailController"/>'s inputs read, and the quiet unseat of a player who leaves (or a bot set aside) while seated.
/// </summary>
public partial class ServerWorld
{
    private readonly record struct HeldRide(bool Pump, bool Brake, double At);

    /// <summary>Each member's last ride intent and when it came (this process's clock).</summary>
    private readonly Dictionary<int, HeldRide> _rides = new();

    /// <summary>Where the last quiet unseat set its body down (the RIDE pass checks the parked pose against it).</summary>
    internal Vector3? LastQuietUnseat { get; private set; }

    /// <summary>A seated player's pump and brake (client to server): kept only while its body sits in the party's cart.</summary>
    private void OnRideIntent(PlayerSlot slot, Adventurer body, RideIntent r)
    {
        if (!body.Seated || RailController.Instance is not { } rc || !GodotObject.IsInstanceValid(rc) || body.Cart != rc.Cart)
        {
            _rides.Remove(slot.MemberIndex);
            return;
        }
        _rides[slot.MemberIndex] = new HeldRide(r.Pump, r.Brake, NetSession.Instance.Now);
    }

    /// <summary>
    /// Member <paramref name="member"/>'s pump and brake now: none unless its body is seated, and a held one not heard again for
    /// <see cref="RideInputRules.HeldTimeoutSeconds"/> is let go. No allocation.
    /// </summary>
    public (bool Pump, bool Brake) RideInputOf(int member)
    {
        if (!_rides.TryGetValue(member, out var held)) return (false, false);
        var party = Main.Instance?.Party;
        var seated = party != null && member >= 0 && member < party.Adventurers.Length && party.Adventurers[member].Seated;
        if (!seated || RideInputRules.Stale(held.Pump || held.Brake, held.At, NetSession.Instance.Now))
        {
            _rides.Remove(member);
            return (false, false);
        }
        return (held.Pump, held.Brake);
    }

    /// <summary>
    /// A player (or a bot) leaves the world while seated in the party's cart: it steps out beside the lane, quietly (no run-over, no log),
    /// and its seat is freed, before its body is parked; the parked pose is that spot. Its ride intent goes with it.
    /// </summary>
    private void UnseatLeaving(PlayerSlot slot)
    {
        _rides.Remove(slot.MemberIndex);
        _unseatedPending.Remove(slot.MemberIndex);
        if (BodyOf(slot) is not { Seated: true } body || RailController.Instance is not { } rc || !GodotObject.IsInstanceValid(rc) || body.Cart != rc.Cart) return;
        rc.UnseatQuietly(body);
        _unseatedPending.Remove(slot.MemberIndex); // nobody is left to send poses
        var at = body.GlobalPosition;
        LastQuietUnseat = at;
        Trace.Add($"unseat-quiet:{slot.Name}");
        GD.Print(FormattableString.Invariant($"[net-server] {slot.Name} left while seated: unseated quietly at ({at.X:0.00}, {at.Y:0.00}, {at.Z:0.00}), seat freed {!body.Seated && rc.Cart.Seats.SeatOf(body.RiderId) == null}"));
    }
}
