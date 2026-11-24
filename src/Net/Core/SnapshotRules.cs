namespace DungeonCrawler.Net;

/// <summary>Which enemies a world snapshot carries between full ones (<c>ServerWorld.SendSnapshotTo</c>). Engine-free.</summary>
public static class SnapshotRules
{
    /// <summary>An enemy has moved since it was last sent when its place lies farther than this (squared, m²) from where it was sent.</summary>
    public const float MovedSq = 0.0004f;

    /// <summary>
    /// An enemy goes in a snapshot to a peer when the snapshot is <paramref name="full"/>, it <paramref name="moved"/> since it was last sent, it stands
    /// near a body and aware (<paramref name="nearAndAware"/>), or its flags (alive, hostile, aware, boss, in water) changed since they were last sent to
    /// that peer (<paramref name="flagsChanged"/>, plan 15 Task 8): an enemy that turns unaware standing still reaches each client at once, so a client's
    /// revive rule (<c>RitualRules.ReviveTaken</c>, read from its copies' aware flag) is never left on a stale flag until the next full snapshot.
    /// </summary>
    public static bool SendEnemy(bool full, bool moved, bool nearAndAware, bool flagsChanged) => full || moved || nearAndAware || flagsChanged;
}

/// <summary>
/// What each peer was last sent of each enemy (its place and flags), by (peer, net id) (plan 15 Task 8): every peer judges "moved" and
/// "flags changed" against what it was sent itself, so the second and later peers are not starved by the first one's send in the same tick. A peer's
/// rows go when it leaves or has the floor resent, a gone enemy's at the peer's next full snapshot (<see cref="Keep"/>); all go with a floor change. Engine-free.
/// </summary>
public sealed class EnemySnapshotMemory
{
    private readonly Dictionary<(int Peer, int NetId), (float X, float Y, float Z, byte Flags)> _sent = new();
    private readonly List<(int Peer, int NetId)> _drop = new();

    /// <summary>Rows held (tests read it).</summary>
    public int Count => _sent.Count;

    /// <summary>
    /// Whether the enemy <paramref name="netId"/> at (<paramref name="x"/>, <paramref name="y"/>, <paramref name="z"/>) with <paramref name="flags"/> goes
    /// to <paramref name="peer"/> in this snapshot (<see cref="SnapshotRules.SendEnemy"/>; never sent to that peer before: it goes). Sent, it is remembered.
    /// </summary>
    public bool Offer(int peer, int netId, float x, float y, float z, byte flags, bool full, bool nearAndAware)
    {
        var known = _sent.TryGetValue((peer, netId), out var last);
        float dx = x - last.X, dy = y - last.Y, dz = z - last.Z;
        var moved = !known || dx * dx + dy * dy + dz * dz > SnapshotRules.MovedSq;
        var flagsChanged = !known || last.Flags != flags;
        if (!SnapshotRules.SendEnemy(full, moved, nearAndAware, flagsChanged)) return false;
        _sent[(peer, netId)] = (x, y, z, flags);
        return true;
    }

    /// <summary>A peer left or has the floor resent: what it was sent is forgotten (its next snapshot carries every enemy).</summary>
    public void Forget(int peer)
    {
        _drop.Clear();
        foreach (var key in _sent.Keys) if (key.Peer == peer) _drop.Add(key);
        foreach (var key in _drop) _sent.Remove(key);
    }

    /// <summary>
    /// A full snapshot to <paramref name="peer"/> carried the enemies <paramref name="present"/>: its rows of any other (an enemy freed since) go, so the
    /// memory never outgrows the floor's living roster (a long floor 12 of portal waves, hounds and slag riders).
    /// </summary>
    public void Keep(int peer, IReadOnlySet<int> present)
    {
        _drop.Clear();
        foreach (var key in _sent.Keys) if (key.Peer == peer && !present.Contains(key.NetId)) _drop.Add(key);
        foreach (var key in _drop) _sent.Remove(key);
    }

    /// <summary>A floor change: nothing sent on the old floor counts.</summary>
    public void Clear() => _sent.Clear();
}
