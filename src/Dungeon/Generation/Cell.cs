namespace DungeonCrawler.Dungeon.Generation;

/// <summary>Cell classification for a generated floor. Byte-sized so layouts stay cheap to hash and compare.</summary>
public enum Cell : byte
{
    Void = 0,
    Floor = 1,
    Wall = 2,
    Door = 3,
    /// <summary>Sloped corridor cell; see FloorLayout.Ramps.</summary>
    Ramp = 4,
    /// <summary>A high room's floor cell with no floor: drops into the cellar below.</summary>
    Pit = 5,
    /// <summary>A wall that opens once found; not walkable until then.</summary>
    Secret = 6,
}

public readonly record struct GridPos(int X, int Y)
{
    public int ManhattanDistance(GridPos other) => Math.Abs(X - other.X) + Math.Abs(Y - other.Y);
    public static GridPos operator +(GridPos a, GridPos b) => new(a.X + b.X, a.Y + b.Y);
    public static GridPos operator -(GridPos a, GridPos b) => new(a.X - b.X, a.Y - b.Y);
    public static GridPos operator -(GridPos a) => new(-a.X, -a.Y);
    public GridPos Perp => new(-Y, X);
    public bool IsParallelTo(GridPos other) => X * other.Y - Y * other.X == 0;
    public override string ToString() => $"({X},{Y})";
}
