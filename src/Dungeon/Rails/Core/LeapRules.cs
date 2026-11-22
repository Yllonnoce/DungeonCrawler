using DungeonCrawler.Dungeon.Generation;

namespace DungeonCrawler.Dungeon.Rails;

/// <summary>
/// Ride 4's leap to the slag cart (plan 13, Rulings 1 and 9), its geometry: the stretch runs from the cavern's start
/// (<see cref="RailLine.Cavern"/>'s near edge on the party track) + <see cref="LeapIn"/> to the drop's near edge, and is dead under
/// <see cref="MinStretch"/>; the drop (<see cref="DropSpan"/>) is the slag deck's <see cref="DropCells"/> cells, <see cref="DropFromEnd"/>
/// short of the cavern's end. The offer (Task 4, Rulings 11-13 and 20): a seat change from the party's rolling cart onto a slag cart's deck
/// alongside, "Leap across" (<see cref="AcrossOffered"/>) and back, "Leap back" (<see cref="BackOffered"/>, the server's <see cref="BackAccepted"/>),
/// in reach by the rim gap and the decks' overlap (<see cref="InReach"/>). Engine-free.
/// </summary>
public static class LeapRules
{
    /// <summary>The leap stretch starts this far past the cavern's start (the near edge of its first party cell).</summary>
    public const float LeapIn = 10f;
    /// <summary>A stretch shorter than this (its start to the drop's near edge) is dead: the leap offers nothing.</summary>
    public const float MinStretch = 30f;
    /// <summary>The slag deck cells that give way at the drop.</summary>
    public const int DropCells = 2;
    /// <summary>The drop's last cell stands this many cells short of the cavern's last.</summary>
    public const int DropFromEnd = 2;

    /// <summary>
    /// The drop as party-lane indices (their slag-lane twins are the cells that go): the <see cref="DropCells"/> cells ending
    /// <see cref="DropFromEnd"/> short of <paramref name="cavern"/>'s end; null with no cavern, or one too short to hold it
    /// (under <see cref="DropFromEnd"/> + <see cref="DropCells"/> cells: the generator's shortest is <see cref="RailRules.CavernCells"/>).
    /// </summary>
    public static RailSpan? DropSpan(RailSpan? cavern)
        => cavern is { } c && c.To - c.From + 1 >= DropFromEnd + DropCells ? new RailSpan(c.To - DropFromEnd - DropCells + 1, c.To - DropFromEnd) : null;

    /// <summary>
    /// The step from a cavern's party-lane cell to its slag twin: the enemy lane's 4-neighbour of the span's first cell (the span is
    /// straight, so every span cell's twin lies the same step aside); null with no such neighbour.
    /// </summary>
    public static GridPos? SlagSide(FloorLayout layout, RailLine rail, RailSpan cavern)
    {
        var at = rail.Party[cavern.From];
        foreach (var n in at.Neighbours4())
            if (layout.IsRail(n) && !layout.IsPartyRail(n)) return n - at;
        return null;
    }

    /// <summary>
    /// Is the cavern flat (plan 13, Ruling 2; a cavern that is not gets neither a live bridge nor a live leap)? Every span cell of both
    /// lanes at one level and none a ramp, and neither lane's cell just before <c>From</c> or just after <c>To</c> a ramp cell (the bed
    /// eases 0.5 m into a flat neighbour: <c>RailBed.SurfaceAt</c>). False with no cavern, no line, or no slag twin.
    /// </summary>
    public static bool Flat(FloorLayout layout, RailSpan? cavern)
    {
        if (cavern is not { } c || layout.Rail is not { } rail || c.From < 0 || c.From > c.To || c.To >= rail.Party.Count) return false;
        if (SlagSide(layout, rail, c) is not { } side) return false;
        bool Ramp(GridPos p) => layout.Ramps.ContainsKey(p) || layout[p] == Cell.Ramp;
        var level = layout.LevelOf(rail.Party[c.From]);
        for (var i = c.From; i <= c.To; i++)
            foreach (var p in new[] { rail.Party[i], rail.Party[i] + side })
                if (Ramp(p) || layout.LevelOf(p) != level) return false;
        foreach (var i in new[] { c.From - 1, c.To + 1 })
            if (i >= 0 && i < rail.Party.Count && (Ramp(rail.Party[i]) || Ramp(rail.Party[i] + side))) return false;
        return true;
    }

    // ---- the leap across and back (Task 4) --------------------------------------------------------------------------

    /// <summary>The rim gap a leap spans at most (|lateral| − both carts' half widths, metres).</summary>
    public const float Reach = 2.6f;
    /// <summary>The cart centres lie at most this far apart along the party cart's line (metres).</summary>
    public const float AlongReach = 1.2f;
    /// <summary>The server takes a press with this much more on both (a client's copy may run behind the server's cart).</summary>
    public const float AcceptSlack = 1f;
    /// <summary>"Leap back" closes with the slag cart's front this far short of the drop's near edge (the server takes it to the edge).</summary>
    public const float BackCloses = 3f;
    /// <summary>A slag cart's deck seats: at most this many heroes on it.</summary>
    public const int MaxLeapers = 2;
    public const string AcrossPrompt = "Leap across";
    public const string BackPrompt = "Leap back";

    /// <summary>
    /// The other cart is in reach (Ruling 11): measured between the cart centres in the party cart's frame, the rim gap
    /// (|<paramref name="lateral"/>| − 2 × <see cref="CartRules.HalfWidth"/>) within <see cref="Reach"/> and |<paramref name="along"/>| within
    /// <see cref="AlongReach"/>, each with <paramref name="slack"/> more (the server's <see cref="AcceptSlack"/>).
    /// </summary>
    public static bool InReach(float lateral, float along, float slack = 0f)
        => MathF.Abs(lateral) - 2f * CartRules.HalfWidth <= Reach + slack && MathF.Abs(along) <= AlongReach + slack;

    /// <summary>
    /// "Leap across" (Ruling 12): a human-controlled hero, up, seated in the party's rolling cart, the stretch live and not yet called, a slag
    /// cart in reach with fewer than <see cref="MaxLeapers"/> heroes on its deck.
    /// </summary>
    public static bool AcrossOffered(bool human, bool up, bool seatedInParty, bool rolling, bool stretchLive, bool called, bool inReach, int leapersAboard)
        => human && up && seatedInParty && rolling && stretchLive && !called && inReach && leapersAboard < MaxLeapers;

    /// <summary>
    /// "Leap back" (Ruling 12): a hero up on that slag cart, the party's cart in reach with a hero seat free, and the slag cart's front at least
    /// <see cref="BackCloses"/> short of the drop's near edge (<paramref name="dropS"/>, on the slag cart's own line; NaN: no live drop, never).
    /// </summary>
    public static bool BackOffered(bool up, bool onThatSlag, bool inReach, bool seatFree, float slagFrontS, float dropS)
        => up && onThatSlag && inReach && seatFree && slagFrontS < dropS - BackCloses;

    /// <summary>The server takes a leap back (with <see cref="AcceptSlack"/> on the reach) until the slag cart's front reaches the drop's edge.</summary>
    public static bool BackAccepted(bool up, bool onThatSlag, bool inReachWithSlack, bool seatFree, float slagFrontS, float dropS)
        => up && onThatSlag && inReachWithSlack && seatFree && slagFrontS < dropS;

    /// <summary>
    /// Why the leap is dead (Rulings 2 and 9; a dead leap offers nothing and leaves the slag line whole), or null: the cavern not
    /// <see cref="Flat"/> (<paramref name="flat"/>; neither a live bridge nor a live leap), no <see cref="DropSpan"/> (<paramref name="hasDrop"/>),
    /// the stretch (its start <paramref name="stretchFromS"/>, the piece's At) starting short of the cavern (<paramref name="cavernFromS"/>), or
    /// shorter than <see cref="MinStretch"/> to the drop's near edge (<paramref name="dropS"/>). All party S.
    /// </summary>
    public static string? Dead(float stretchFromS, float dropS, float cavernFromS, bool flat = true, bool hasDrop = true)
    {
        if (!flat) return "the cavern is not flat";
        if (!hasDrop || float.IsNaN(dropS)) return "the cavern has no drop";
        if (!(stretchFromS >= cavernFromS)) return "the stretch starts short of the cavern";
        if (!(dropS - stretchFromS >= MinStretch)) return "the stretch is shorter than 30 m";
        return null;
    }

    /// <summary>
    /// The server sends a rider's seat change (Ruling 20): always a seating; an unseat only when the rider sits in no other cart at the flush
    /// (a leap's seat in the new cart moves it on every client).
    /// </summary>
    public static bool SendsUnseat(bool seated, bool seatedElsewhereNow) => seated || !seatedElsewhereNow;
}
