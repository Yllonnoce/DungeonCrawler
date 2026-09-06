namespace DungeonCrawler.Dungeon.Generation;

/// <summary>Axis-aligned rectangle of floor cells. X/Y is the top-left floor cell; W/H count floor cells.</summary>
public sealed record Room(int X, int Y, int W, int H)
{
    public int Right => X + W - 1;
    public int Bottom => Y + H - 1;
    public int Area => W * H;
    public GridPos Center => new(X + W / 2, Y + H / 2);

    public bool Contains(GridPos p) => p.X >= X && p.X <= Right && p.Y >= Y && p.Y <= Bottom;

    /// <summary>True when the rooms would touch or overlap once expanded by <paramref name="margin"/> cells of wall.</summary>
    public bool Intersects(Room other, int margin)
        => X - margin <= other.Right && Right + margin >= other.X
        && Y - margin <= other.Bottom && Bottom + margin >= other.Y;

    public IEnumerable<GridPos> Cells()
    {
        for (var y = Y; y <= Bottom; y++)
            for (var x = X; x <= Right; x++)
                yield return new GridPos(x, y);
    }
}
