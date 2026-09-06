namespace DungeonCrawler.Dungeon.Generation;

/// <summary>A ramp cell rises 2 m from LowHeight (metres above the floor grid) toward Dir. Two ramp cells join the two levels.</summary>
public sealed record RampInfo(GridPos Dir, int LowHeight)
{
    public int HighHeight => LowHeight + 2;
    public float MidHeight => LowHeight + 1f;
}

/// <summary>A raised strip along the top edge of a low room, reached by a two-cell ramp at its left end.</summary>
public sealed record Ledge(Room Room, List<GridPos> Cells, GridPos RampBottom, GridPos RampTop, GridPos EnemySpawn, GridPos? Chest);

/// <summary>A hole in a high room's floor dropping into a 3x3 cellar underneath, with a tunnel back to the low level.</summary>
public sealed record Cellar(Room Room, GridPos Pit, List<GridPos> Cells, List<GridPos> Exit, GridPos ExitTarget, GridPos Chest, List<GridPos> Enemies);

/// <summary>A hidden alcove behind a wall cell that opens when found.</summary>
public sealed record SecretAlcove(int Index, GridPos Wall, GridPos Alcove, GridPos Facing);
