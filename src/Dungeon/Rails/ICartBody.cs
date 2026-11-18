using Godot;

namespace DungeonCrawler.Dungeon.Rails;

/// <summary>
/// Something on the rails that carries riders: the party's <see cref="MineCart"/> now; cage wagons and slag carts later.
/// It keeps the seat book and who rides it, and tells every change through <see cref="RiderChanged"/>, whichever path
/// seated or unseated the rider (the cart's owner, a teleport, a takeover, a load).
/// </summary>
public interface ICartBody
{
    /// <summary>Who rides, by <see cref="ICartRider.RiderId"/>.</summary>
    Dictionary<int, ICartRider> Riders { get; }
    CartSeats Seats { get; }
    /// <summary>Where a rider in <paramref name="seat"/> sits, in world space (−Z forward).</summary>
    Transform3D SeatTransform(int seat);
    /// <summary>The body's own transform: its pitch on a ramp, its heading.</summary>
    Transform3D GlobalTransform { get; }
    /// <summary>Speed along the line (m/s).</summary>
    float Speed { get; set; }
    /// <summary>How hard the line bends here (1/m, signed, positive left).</summary>
    float Curvature { get; set; }
    /// <summary>The velocity in the world.</summary>
    Vector3 Velocity { get; }

    /// <summary>The rider (seated by its own SitIn, its seat already booked) now rides here. Raises <see cref="RiderChanged"/> (true).</summary>
    void Admit(ICartRider rider);
    /// <summary>The rider no longer rides here and its seat is free. Raises <see cref="RiderChanged"/> (false). A rider not aboard changes nothing.</summary>
    void Release(ICartRider rider);
    /// <summary>A rider got in (true) or out (false).</summary>
    event Action<ICartRider, bool>? RiderChanged;
}
