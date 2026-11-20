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
    public const float GorgeFrom = 0.50f, GorgeTo = 0.85f;
    /// <summary>The most plank crossings (<c>FloorLayout.Walkways</c>) cut into a crevasse floor's halls.</summary>
    public const int WalkwayCount = 3;
    /// <summary>How many cells of chasm a plank crossing has on each side, across its corridor.</summary>
    public const int WalkwayReach = 2;
    /// <summary>The fewest cells (Chebyshev) between a plank and any room.</summary>
    public const int WalkwayRoomGap = 3;
    /// <summary>The fewest cells (Manhattan) between two planks.</summary>
    public const int WalkwaySpacing = 8;
}

/// <summary>A stop on the line: a room the cart halts in, and the party-lane cells inside it, in line order.</summary>
public sealed record RailStop(int Room, IReadOnlyList<GridPos> Platform);

/// <summary>A run of party-lane cells, by their indices in <see cref="RailLine.Party"/>, both ends included.</summary>
public sealed record RailSpan(int From, int To);

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
    /// one per ride but the cavern's and the last, and the last ride's gorge (<see cref="Gorge"/>). Not part of the content hash.
    /// </summary>
    public IReadOnlyList<RailSpan> Strips { get; init; } = Array.Empty<RailSpan>();

    /// <summary>
    /// The last ride's strips, the gorge: every eligible straight run within <see cref="RailRules.GorgeFrom"/> to
    /// <see cref="RailRules.GorgeTo"/> of the ride, as party-lane index spans (also in <see cref="Strips"/>). Not part of the content hash.
    /// </summary>
    public IReadOnlyList<RailSpan> Gorge { get; init; } = Array.Empty<RailSpan>();
}
