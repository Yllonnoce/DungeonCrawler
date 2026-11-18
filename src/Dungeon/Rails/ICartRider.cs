using Godot;

namespace DungeonCrawler.Dungeon.Rails;

/// <summary>
/// A body that can sit in a <see cref="MineCart"/>: a hero (<see cref="RiderId"/> = its party index) or, from plan 3,
/// an NPC (100 + i). The cart's owner books the seat in <see cref="MineCart.Seats"/>; <see cref="SitIn"/> enters the
/// rider in <see cref="MineCart.Riders"/>, and leaving the seat (GetOut, a teleport) gives it back through
/// <see cref="MineCart.Release"/>.
/// </summary>
public interface ICartRider
{
    int RiderId { get; }
    /// <summary>The cart this body sits in, null on foot.</summary>
    MineCart? Cart { get; }
    /// <summary>The seat in <see cref="Cart"/>, −1 on foot.</summary>
    int Seat { get; }
    /// <summary>Where the rider looks relative to its seat's forward, in radians.</summary>
    float SeatYaw { get; set; }

    /// <summary>Take the booked <paramref name="seat"/> in <paramref name="cart"/> and ride from now on.</summary>
    void SitIn(MineCart cart, int seat);
    /// <summary>Out of the seat and onto the ground at <paramref name="at"/>, moving at <paramref name="velocity"/>.</summary>
    void GetOut(Vector3 at, Vector3 velocity);
    /// <summary>Put the body on its seat as the cart stands now (every rendered frame).</summary>
    void FollowSeat();
}
