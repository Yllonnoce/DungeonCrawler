namespace DungeonCrawler.Dungeon.Generation;

/// <summary>Sizes shared by the generator and the rail routing.</summary>
public static class RailRules
{
    /// <summary>Cells between the grid edge and the ring's room circle on a railed floor: the line runs outside the rooms.</summary>
    public const int Band = 6;
    /// <summary>One side pocket every this many line cells.</summary>
    public const int PocketEvery = 3;
    /// <summary>A meandered ride is lengthened to at least this many cells, platform middle to platform middle (220 m)...</summary>
    public const int RideMinCells = 55;
    /// <summary>...and never past this many (330 m).</summary>
    public const int RideMaxCells = 82;
    /// <summary>The cells a meander bulge runs along its top, parallel to the stretch it replaces.</summary>
    public const int BulgeSpan = 4;
    /// <summary>The farthest a meander bulge reaches outward from the stretch it replaces.</summary>
    public const int BulgeMaxDepth = 9;
    /// <summary>The shortest straight run of party lane kept for the cavern (<see cref="RailLine.Cavern"/>).</summary>
    public const int CavernCells = 10;
    /// <summary>The solid rock kept outside the cavern span, past the enemy lane and its pockets.</summary>
    public const int CavernDepth = 5;
    /// <summary>
    /// Line cells that touch (8-adjacent) are at most this many party indices apart (<c>FloorGenerator.SelfClear</c>):
    /// enough for the enemy lane's inside corners, where its two twins merge and the cells either side stand diagonal 4
    /// indices apart, but never for two legs of the line side by side.
    /// </summary>
    public const int SelfClearGap = 4;

    /// <summary>The shortest straight run of party lane an edge strip (a crevasse beside the line) is cut along...</summary>
    public const int StripMinCells = 4;
    /// <summary>...and the most of a longer run it takes (the gorge on the last ride is not capped).</summary>
    public const int StripMaxCells = 8;
    /// <summary>How many cells deep an edge strip is cut, beside the lane it runs along.</summary>
    public const int StripDepth = 2;
    /// <summary>Party-lane cells kept clear of strips at either end of a ride, next to each platform.</summary>
    public const int StripPlatformGap = 4;
    /// <summary>The gorge's window on the last ride, as fractions of the ride from platform middle to platform middle.</summary>
    public const float GorgeFrom = 0.30f, GorgeTo = 0.90f;
    /// <summary>The most plank crossings (<c>FloorLayout.Walkways</c>) cut into a crevasse floor's halls.</summary>
    public const int WalkwayCount = 3;
    /// <summary>How many cells of chasm a plank crossing has on each side, across its corridor.</summary>
    public const int WalkwayReach = 2;
    /// <summary>The fewest cells (Chebyshev) between a plank and any room.</summary>
    public const int WalkwayRoomGap = 3;
    /// <summary>The fewest cells (Manhattan) between two planks.</summary>
    public const int WalkwaySpacing = 8;
    /// <summary>The broken bridge's window on its ride (<see cref="FloorSpec.BrokenBridgeRide"/>), as fractions of the ride from platform middle to platform middle.</summary>
    public const float BreakFrom = 0.55f, BreakTo = 0.90f;
    /// <summary>The shortest outward straight run the broken bridge is laid along (a longer one is clipped to its middle <see cref="StripMaxCells"/>).</summary>
    public const int BreakMinCells = 5;
    /// <summary>The split's window on its ride (<see cref="FloorSpec.SplitRide"/>), as fractions of the ride from platform middle to platform middle.</summary>
    public const float SplitFrom = 0.25f, SplitTo = 0.55f;
    /// <summary>The shortest outward straight run the split is laid along: its points, the cells before them, and its crevasse.</summary>
    public const int SplitMinCells = 6;
    /// <summary>The split's crevasse: the last this many cells of its run, cut outward of the slag line.</summary>
    public const int SplitCells = 4;
    /// <summary>On the cavern's ride, the split's run ends at least this many cells before the cavern's first cell (its last index is at most <c>Cavern.From - SplitCavernGap</c>).</summary>
    public const int SplitCavernGap = 3;
    /// <summary>The shortest straight run of party lane the pit (<see cref="FloorSpec.PitRide"/>) is laid along: its lead, the pit and the cells after it.</summary>
    public const int PitRunCells = 11;
    /// <summary>The pit is this many cells into its run, so the run's first this many cells (28 m) are the straight before its lip.</summary>
    public const int PitLeadCells = 7;
    /// <summary>The pit's window on its ride, as fractions of the ride from platform middle to platform middle.</summary>
    public const float PitFrom = 0.05f, PitTo = 0.60f;
    /// <summary>The fewest party-lane cells the exit bridge (<see cref="FloorSpec.ExitIsland"/>) crosses.</summary>
    public const int BridgeMinCells = 4;
    /// <summary>The solid party-lane cells kept between the exit bridge's last cell and the exit platform's first.</summary>
    public const int BridgeIslandCells = 4;
    /// <summary>How many cells deep the exit bridge's moat is cut on each side of it.</summary>
    public const int MoatDepth = 2;
    /// <summary>The party-lane cells just before the exit bridge where the parked train stands (<see cref="RailIsland.Parked"/>).</summary>
    public const int ParkedCells = 4;
}

/// <summary>A stop on the line: a room the cart halts in, and the party-lane cells inside it, in line order.</summary>
public sealed record RailStop(int Room, IReadOnlyList<GridPos> Platform);

/// <summary>A run of party-lane cells, by their indices in <see cref="RailLine.Party"/>, both ends included.</summary>
public sealed record RailSpan(int From, int To);

/// <summary>
/// The broken bridge (<see cref="FloorSpec.BrokenBridgeRide"/>): on 1-based ride <paramref name="Ride"/>, the slag line runs
/// over a strip of chasm cut outward of it along <paramref name="Run"/> (party-lane indices) on a deck, but for the enemy
/// twin of party index <paramref name="Gap"/> (the run's middle), which is a <see cref="Cell.Chasm"/>: the 4 m gap.
/// </summary>
public sealed record RailBreak(int Ride, RailSpan Run, int Gap);

/// <summary>
/// The split (<see cref="FloorSpec.SplitRide"/>): on 1-based ride <paramref name="Ride"/>, a crevasse cut outward of the slag
/// line beside party-lane indices <paramref name="Crevasse"/>, both lanes left solid floor with no deck; <paramref name="Points"/>
/// is the party-lane index of the split's points, the cell before the cut (<c>Crevasse.From - 1</c>).
/// </summary>
public sealed record RailSplit(int Ride, RailSpan Crevasse, int Points);

/// <summary>
/// The pit (<see cref="FloorSpec.PitRide"/>): on 1-based ride <paramref name="Ride"/>, party-lane index <paramref name="Cell"/> is cut to
/// <see cref="Cell.Chasm"/> at the lane's level, with the rock or pocket cells inward of it; its enemy twin stays solid lane with no
/// deck. <paramref name="Cut"/> is the pit cell, then its inward cells that were cut.
/// </summary>
public sealed record RailPit(int Ride, int Cell, IReadOnlyList<GridPos> Cut);

/// <summary>
/// The exit island (<see cref="FloorSpec.ExitIsland"/>): the exit room and the rooms off it (<paramref name="Rooms"/>), their halls to
/// the rest of the floor sealed, each with a lava trench at its mouth (<paramref name="Trenches"/>) and a cave-in behind it to its fork
/// or room (<paramref name="CaveIns"/>, <see cref="Cell.Wall"/>). The last straight party-lane run before the exit platform,
/// <paramref name="Bridge"/>, is cut to chasm with its track kept, and so are its straight enemy twins (<paramref name="EnemyBridge"/>);
/// <paramref name="Moat"/> is the rock and pocket cells the moat cut beside it. <paramref name="Parked"/> is the
/// <see cref="RailRules.ParkedCells"/> party-lane cells just before the bridge. <paramref name="Cells"/> is every cell walkable from the
/// down stairs once cut (the rails open, the secrets shut). Every list is in (Y, X) order.
/// </summary>
public sealed record RailIsland(IReadOnlyList<int> Rooms, RailSpan Bridge, RailSpan Parked, IReadOnlyList<GridPos> EnemyBridge, IReadOnlyList<GridPos> Trenches,
    IReadOnlyList<GridPos> CaveIns, IReadOnlyList<GridPos> Moat, IReadOnlySet<GridPos> Cells)
{
    /// <summary>Every cell the island cut to chasm: the party bridge cells, <see cref="EnemyBridge"/>, <see cref="Moat"/> and <see cref="Trenches"/>.</summary>
    public IEnumerable<GridPos> Cut(RailLine rail)
    {
        for (var i = Bridge.From; i <= Bridge.To; i++) yield return rail.Party[i];
        foreach (var c in EnemyBridge) yield return c;
        foreach (var c in Moat) yield return c;
        foreach (var c in Trenches) yield return c;
    }
}

/// <summary>
/// The mine-cart line round a ring floor: two parallel ordered, 4-connected lanes from the start room to the exit room.
/// Stops[0] is the up-stairs room, Stops[^1] the down-stairs room.
/// </summary>
public sealed record RailLine(IReadOnlyList<GridPos> Party, IReadOnlyList<GridPos> Enemy, IReadOnlyList<RailStop> Stops, IReadOnlyList<GridPos> Pockets)
{
    /// <summary>
    /// A straight span of the party lane on the second-to-last ride kept for a cavern, or null (always null unless the line
    /// meandered). For each of its cells, the cells 3 to 2 + <see cref="RailRules.CavernDepth"/> steps outward (the enemy
    /// lane's side, past the enemy lane at 1 and its pocket row at 2: <c>FloorGenerator.CavernBox</c>) were solid rock
    /// outside every room when the line was planned, and nothing of the line takes them: the stretches routed after it keep
    /// off them, no pocket opens from 2 steps out (<c>FloorGenerator.CavernReach</c>), and a span whose box a line or pocket
    /// cell took after all is dropped (<see cref="CavernBreached"/>). With <see cref="FloorSpec.Crevasses"/> the reach is cut
    /// to a chasm and both lanes over the span ride a deck. Not part of the content hash.
    /// </summary>
    public RailSpan? Cavern { get; init; }

    /// <summary>True when the line kept a cavern window but a line or pocket cell landed in its rock box, so <see cref="Cavern"/> was dropped. Not part of the content hash.</summary>
    public bool CavernBreached { get; init; }

    /// <summary>
    /// The edge strips cut beside the line (<see cref="FloorSpec.Crevasses"/>), as party-lane index spans in line order:
    /// one per ride but the cavern's and the last, and the last ride's gorge (<see cref="Gorge"/>); the broken bridge's run and
    /// the split's crevasse (<see cref="Split"/>, the cavern's ride too) are their rides' strips. Not part of the content hash.
    /// </summary>
    public IReadOnlyList<RailSpan> Strips { get; init; } = Array.Empty<RailSpan>();

    /// <summary>
    /// The last ride's strips, the gorge: every eligible straight run within <see cref="RailRules.GorgeFrom"/> to
    /// <see cref="RailRules.GorgeTo"/> of the ride, as party-lane index spans (also in <see cref="Strips"/>). Not part of the content hash.
    /// </summary>
    public IReadOnlyList<RailSpan> Gorge { get; init; } = Array.Empty<RailSpan>();

    /// <summary>The broken bridge cut on <see cref="FloorSpec.BrokenBridgeRide"/>'s ride (its run is also in <see cref="Strips"/>), or null. Not part of the content hash.</summary>
    public RailBreak? Break { get; init; }

    /// <summary>
    /// Null when no break was asked for (<see cref="FloorSpec.BrokenBridgeRide"/> 0, or no crevasses); otherwise "ok" (cut),
    /// "none" (the ride had no fitting run) or "refused" (the cavern's ride, the last ride, or no such ride). Not part of the content hash.
    /// </summary>
    public string? BreakWhy { get; init; }

    /// <summary>The split's crevasse cut on <see cref="FloorSpec.SplitRide"/>'s ride (also in <see cref="Strips"/>), or null. Not part of the content hash.</summary>
    public RailSplit? Split { get; init; }

    /// <summary>
    /// Null when no split was asked for (<see cref="FloorSpec.SplitRide"/> 0, or no crevasses); otherwise "ok" (cut),
    /// "none" (the ride had no fitting run) or "refused" (the broken bridge's ride, the last ride, or no such ride). Not part of the content hash.
    /// </summary>
    public string? SplitWhy { get; init; }

    /// <summary>The pit cut on <see cref="FloorSpec.PitRide"/>'s ride, or null. Not part of the content hash.</summary>
    public RailPit? Pit { get; init; }

    /// <summary>
    /// Null when no pit was asked for (<see cref="FloorSpec.PitRide"/> 0, or no crevasses); otherwise "ok" (cut), "none" (the ride had
    /// no fitting run) or "refused" (the broken bridge's, the split's or the cavern's ride, or no such ride). Not part of the content hash.
    /// </summary>
    public string? PitWhy { get; init; }

    /// <summary>The exit island cut for <see cref="FloorSpec.ExitIsland"/>, or null. Not part of the content hash.</summary>
    public RailIsland? Island { get; init; }

    /// <summary>
    /// Null when no island was asked for (<see cref="FloorSpec.ExitIsland"/> false, or no crevasses); otherwise "ok" (cut), "none" (the
    /// last ride had no bridge run) or "refused (…)" with the reason (nothing was cut). Not part of the content hash.
    /// </summary>
    public string? IslandWhy { get; init; }
}
