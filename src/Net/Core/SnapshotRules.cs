namespace DungeonCrawler.Net;

/// <summary>Which enemies a world snapshot carries between full ones (<c>ServerWorld.SendSnapshotTo</c>). Engine-free.</summary>
public static class SnapshotRules
{
    /// <summary>
    /// An enemy goes in a snapshot to a peer when the snapshot is <paramref name="full"/>, it <paramref name="moved"/> since it was last sent, it stands
    /// near a body and aware (<paramref name="nearAndAware"/>), or its flags (alive, hostile, aware, boss, in water) changed since they were last sent to
    /// that peer (<paramref name="flagsChanged"/>, plan 15 Task 8): an enemy that turns unaware standing still reaches each client at once, so a client's
    /// revive rule (<c>RitualRules.ReviveTaken</c>, read from its copies' aware flag) is never left on a stale flag until the next full snapshot.
    /// </summary>
    public static bool SendEnemy(bool full, bool moved, bool nearAndAware, bool flagsChanged) => full || moved || nearAndAware || flagsChanged;
}
