namespace DungeonCrawler.Net;

/// <summary>What runs on the rails: the party's cart, a ride's slag cart, a traffic slag cart, a runaway wagon, the golem's runner, a chaser (a left-behind co-op player's spare running after the train).</summary>
public enum CartKind : byte { Party, Slag, Traffic, Runaway, Golem, Chaser }

/// <summary>
/// Every cart's id on the wire. The party's cart is 0; slag, traffic, runaway and golem carts count up from 1 per floor build
/// (the server assigns them, below <see cref="WagonBase"/>); a cage wagon is <see cref="WagonBase"/> + its stop, a chaser
/// <see cref="ChaserBase"/> + its stop.
/// </summary>
public static class CartIds
{
    public const int Party = 0;
    public const int FirstDynamic = 1;
    public const int WagonBase = 1000;
    public const int ChaserBase = 2000;
    public static int Wagon(int stop) => WagonBase + stop;
    public static int Chaser(int stop) => ChaserBase + stop;
}

/// <summary>A cart's flags on the wire (<see cref="CartStateDto.Flags"/>).</summary>
[Flags]
public enum CartFlags : byte
{
    None = 0,
    /// <summary>The party's cart is held on the line short of a stop (it ran into an obstacle).</summary>
    Halted = 1,
    /// <summary>The cart was wrecked (its rising edge plays the tumble on clients).</summary>
    Wrecked = 2,
    /// <summary>The party's cart is hidden (a wreck with no spare left); any other cart is gone (freed).</summary>
    Hidden = 4,
    /// <summary>An obstacle stands on the party lane ahead.</summary>
    LineBlocked = 8,
    /// <summary>Nothing at the party cart's place holds it (its stop's gate is up, or it is halted past it).</summary>
    GateClear = 16,
    /// <summary>A slag cart jumped the rails (its tumble plays, then it is freed).</summary>
    Derailed = 32,
    /// <summary>The party's cart: a left-behind player's chaser runs after the train (one at a time: no spare is offered meanwhile).</summary>
    ChaseLive = 64,
    /// <summary>The party's cart stands at a stop whose cage wagon is locked with living crew, and nobody has chosen to leave them: no countdown.</summary>
    CageHeld = 128,
}

/// <summary>
/// One cart's state as the server sees it: who it is, where it runs, what its integrator steps on (so a client predicts it
/// with the same <c>CartPhysics</c>), its condition and, for the party's cart, the lane, the gap, the boarding and the train.
/// Engine-free. <see cref="Limit"/> is the integrator's StopAt; NaN floats mean none.
/// </summary>
public sealed class CartStateDto
{
    // identity
    public int Cart { get; set; }
    public byte Kind { get; set; }
    /// <summary>0 the party line, 1 the enemy line, 2+k the k-th spur.</summary>
    public byte Track { get; set; }
    /// <summary>+1 runs toward higher S, −1 toward lower S.</summary>
    public sbyte Dir { get; set; } = 1;

    // motion
    public float S { get; set; }
    public float V { get; set; }
    /// <summary>A <c>CartMode</c>: 0 Held, 1 Rolling, 2 Wrecked.</summary>
    public byte Mode { get; set; }
    public float Limit { get; set; }

    // the integrator's input
    public byte Pumpers { get; set; }
    public bool Brake { get; set; }
    public byte CrewWagons { get; set; }
    /// <summary>The commanded speed (a slag cart's pace, a coasting runner), or negative for none.</summary>
    public float TargetOverride { get; set; } = -1f;

    // condition
    public float Plating { get; set; }
    public int StopIndex { get; set; }
    /// <summary><see cref="CartFlags"/>.</summary>
    public byte Flags { get; set; }

    // the party's cart only
    public float CrossOut { get; set; } = float.NaN;
    public float CrossBack { get; set; } = float.NaN;
    public float GapFrom { get; set; } = float.NaN;
    public float GapTo { get; set; } = float.NaN;
    /// <summary>The speed the cart left a gap's lip at, or NaN while it has not flown.</summary>
    public float JumpV { get; set; } = float.NaN;
    /// <summary>The S on the party lane where an obstacle's face stands in the cart's way (a cart reaching it is halted there), or NaN when none blocks it.</summary>
    public float BlockedAt { get; set; } = float.NaN;
    /// <summary>Seconds left on the boarding countdown, or −1 when none runs.</summary>
    public float BoardingLeft { get; set; } = -1f;
    /// <summary>The coupled wagons' stops, nearest the cart first.</summary>
    public List<int>? Train { get; set; }
    /// <summary>The ride whose items wait for the cart (1-based), or −1 when none is armed (plan 5 task 7).</summary>
    public int ArmedRide { get; set; } = -1;
    /// <summary>The server's clock when <see cref="ArmedRide"/> armed: a client's ore buckets run the server's schedule from it.</summary>
    public double ArmedAt { get; set; }
    /// <summary>
    /// The party's cart: its last wreck was a fall into a crevasse (plan 10; <see cref="CartFlags"/> has no free bit). On the Wrecked
    /// flag's rising edge a client plays the plunge, not the tumble, and hides the wagons of the last train it was told.
    /// </summary>
    public bool Fell { get; set; }

    // slag and traffic carts
    public float Offset { get; set; }
    public string? EncounterId { get; set; }

    // slag, traffic and golem carts: the carried enemies' net ids by spot (−1 an empty spot)
    public List<int>? RiderNetIds { get; set; }

    // the party's cart only (plan 5 task 4): the hounds aboard it by boarder spot, and each coupled wagon's roof boarder in train
    // order (nearest the cart first). −1 is a free spot (net id 0 is a real id); null when none is held. A hound still crouching
    // or in the air is not listed: its LeapMsg flies it.
    public List<int>? Boarders { get; set; }
    public List<int>? WagonBoarders { get; set; }

    /// <summary>The server's clock (<c>NetSession.Instance.Now</c>) when this state was true.</summary>
    public double ServerTime { get; set; }

    public bool Has(CartFlags f) => (Flags & (byte)f) != 0;
}

/// <summary>The carts that changed this physics tick (event-driven: a cart event, a lane, gap, train, halt or boarding change, a spawn, derail or free). Not transient: a joiner replays them before its catch-up.</summary>
[Msg(MsgKind.CartState)] public sealed record CartStateMsg(int Floor, int Epoch, List<CartStateDto> Carts) : NetMessage;

/// <summary>Every live cart, 5 times a second (transient: never held for a joiner). A client frees any live cart of its own that is missing from it.</summary>
[Msg(MsgKind.CartCorrection)] public sealed record CartCorrectionMsg(int Floor, int Epoch, List<CartStateDto> Carts) : NetMessage;

/// <summary>Who rides in a seat book: a hero (<see cref="SeatMsg.RiderId"/> its member index) or an NPC (its <c>Npc.Id</c>).</summary>
public static class SeatRiderKind
{
    public const int Hero = 0;
    public const int Npc = 1;
}

/// <summary>
/// A rider took (<paramref name="Seated"/>) or left a seat in a cart (<see cref="CartIds"/>: the party's, a cage wagon, a chaser). Sent for every
/// seat and unseat on the server, after the tick's <see cref="CartStateMsg"/>; not transient (a joiner replays them, then the catch-up).
/// <paramref name="X"/>, <paramref name="Y"/>, <paramref name="Z"/>: where an unseated rider landed (unused when seated).
/// </summary>
[Msg(MsgKind.Seat)] public sealed record SeatMsg(int RiderKind, string RiderId, int Cart, int Seat, bool Seated, float X, float Y, float Z) : NetMessage;

/// <summary>
/// A hound left its slag cart (<paramref name="FromCart"/>) on a leap toward <paramref name="ToCart"/> (the party's cart, or
/// <see cref="CartIds.WagonBase"/> + stop) and boarder spot <paramref name="Spot"/>, or toward the ground beside it (<paramref name="Miss"/>,
/// spot −1), at the server's clock <paramref name="ServerTime"/>. A client flies the arc itself from there. Transient: the catch-up resends
/// leaps still in the air.
/// </summary>
[Msg(MsgKind.Leap)] public sealed record LeapMsg(int NetId, int FromCart, int ToCart, int Spot, bool Miss, double ServerTime) : NetMessage;

/// <summary>
/// A seated player's say over the party's cart (client to server): forward pumps, back brakes. Sent on every change while
/// seated and every <see cref="Dungeon.Rails.RideInputRules.ResendSeconds"/> while either is held. The server keeps the last one per
/// member and lets it go on an unseat, a disconnect, or <see cref="Dungeon.Rails.RideInputRules.HeldTimeoutSeconds"/> of silence.
/// </summary>
[Msg(MsgKind.RideIntent)] public sealed record RideIntent(bool Pump, bool Brake) : NetMessage;
