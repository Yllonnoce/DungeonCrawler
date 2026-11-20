namespace DungeonCrawler.Dungeon.Rails;

/// <summary>
/// What a line of <paramref name="Stops"/> stops means: one ride between each pair, a crew cage at every stop but the start and the exit,
/// and the kiln run for the last ride. Five stops are today's line (four rides); seven are six. Engine-free.
/// </summary>
public readonly record struct LineShape(int Stops)
{
    /// <summary>The rides: one from each stop to the next.</summary>
    public int Rides => Math.Max(0, Stops - 1);
    /// <summary>The stops between the start and the exit: where a crew cage waits.</summary>
    public int Intermediate => Math.Max(0, Stops - 2);
    /// <summary>The last ride, the kiln run (rides count from 1).</summary>
    public int LastRide => Rides;
    /// <summary>The stop the last ride leaves from.</summary>
    public int LastRideStop => Math.Max(0, Stops - 2);
    public bool IsLastRide(int ride) => ride >= 1 && ride == Rides;
}
