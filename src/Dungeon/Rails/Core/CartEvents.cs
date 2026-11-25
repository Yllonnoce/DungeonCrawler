namespace DungeonCrawler.Dungeon.Rails;

/// <summary>What happened to a cart, for whoever listens (plan 5 syncs it to clients).</summary>
/// <summary>Halted: the cart ran into an obstacle on the line and is held there, short of the next stop. Resumed: it left such a halt for the next stop (not a departure from a stop).
/// CageHeld: a countdown would have started at a stop but the stop's locked cage holds the cart (plan 7; not on the wire).
/// Hijacked: the train starts rolling back from a stop (plan 16); HijackOver: it is held at that stop again. Both server-side, not on the wire,
/// and neither is a departure or an arrival (no ride arms, no snapshot is taken, no stop is counted).</summary>
public enum CartEventKind { Departed, Arrived, Hit, Wrecked, Seated, Unseated, Restored, OnFoot, Boarding, Halted, Resumed, CageHeld, Hijacked, HijackOver }

/// <param name="Stop">The stop concerned (Departed, Wrecked, Halted, Resumed: the one last left; Arrived, Restored: the one reached; Boarding: the one the countdown runs at, −1 on the line after a halt; CageHeld: the stop whose cage holds the cart; Hijacked, HijackOver: the stop rolled back from), or −1.</param>
/// <param name="Rider">The rider's <see cref="ICartRider.RiderId"/> (Seated, Unseated), or −1.</param>
/// <param name="Target">The instance id of the body run over (Hit), or 0.</param>
/// <param name="Thrown">Unseated from a moving cart at speed.</param>
public readonly record struct CartEvent(CartEventKind Kind, int Stop = -1, int Rider = -1, ulong Target = 0, bool Thrown = false);
