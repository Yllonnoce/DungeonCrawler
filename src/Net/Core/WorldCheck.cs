namespace DungeonCrawler.Net;

/// <summary>
/// The pure rules of the item check, the enemy check and the floor epoch (engine-free, tested in tests/Net/WorldCheckTests.cs).
/// The server lists what stands on its floor every <see cref="Protocol.LootCheckSeconds"/>; a client compares its own copy
/// with that list here and the Godot glue frees what the server lacks and asks for what it lacks itself.
/// </summary>
public static class WorldCheck
{
    /// <summary>Seconds an enemy this client just spawned is spared by the enemy check (a list sent before its spawn may cross it).</summary>
    public const double EnemyGraceSeconds = 1.5;

    /// <summary>
    /// Whether a check tagged with <paramref name="theirs"/> is about the floor build this client holds (<paramref name="mine"/>).
    /// The server counts every floor it builds, so a wipe's rebuild of the same floor number is a different build; 0 is
    /// "unknown" (a message from before the epoch) and matches anything, as the floor number alone used to.
    /// </summary>
    public static bool SameBuild(int mine, int theirs) => mine == 0 || theirs == 0 || mine == theirs;

    /// <summary>What the item check compares besides the id: a pile's coins, else the stack's count.</summary>
    public static int ItemCount(int coins, int stackCount) => coins > 0 ? coins : stackCount;

    /// <summary>
    /// The item check on a client. <paramref name="shown"/> is every drop this screen shows, in order;
    /// <paramref name="ids"/> and <paramref name="counts"/> the server's list (counts may be null: ids only).
    /// Free: the indices into <paramref name="shown"/> of drops the server does not list, and of second copies.
    /// Missing: listed ids this screen lacks, and listed ids whose count differs (the server re-sends those whole and
    /// the client's spawn replaces its copy in place, so a partial pickup's new count shows everywhere).
    /// </summary>
    public static (List<int> Free, List<Guid> Missing) CompareItems(IReadOnlyList<(Guid Id, int Count)> shown, IReadOnlyList<Guid> ids, IReadOnlyList<int>? counts)
    {
        var listed = new Dictionary<Guid, int>();
        for (var i = 0; i < ids.Count; i++)
            listed[ids[i]] = counts != null && i < counts.Count ? counts[i] : -1; // -1: no count sent, any count is fine
        var free = new List<int>();
        var kept = new Dictionary<Guid, int>();
        for (var i = 0; i < shown.Count; i++)
        {
            var (id, count) = shown[i];
            if (!listed.ContainsKey(id) || kept.ContainsKey(id)) { free.Add(i); continue; }
            kept[id] = count;
        }
        var missing = new List<Guid>();
        foreach (var id in ids)
        {
            if (missing.Contains(id)) continue;
            if (!kept.TryGetValue(id, out var have)) missing.Add(id);
            else if (listed[id] >= 0 && have != listed[id]) missing.Add(id);
        }
        return (free, missing);
    }

    /// <summary>
    /// The enemy check on a client. <paramref name="local"/> is every enemy under this screen's floor root, in order, with
    /// whether it is alive here and how many seconds ago this client spawned it from a server message (a large number for
    /// one the floor build made). <paramref name="serverAlive"/> is the server's living enemies by net id.
    /// Free: indices of living enemies the server has no living copy of (a ghost: its death or its corpse's end never
    /// arrived, or the floor state brought back one the server lost), unless just spawned, and second living copies of
    /// one net id. Corpses are left alone: they finish their fall and free themselves.
    /// Missing: listed net ids with no living copy here (a spawn this screen never heard of); the server sends them again.
    /// </summary>
    public static (List<int> Free, List<int> Missing) CompareEnemies(IReadOnlyList<(int NetId, bool Alive, double Age)> local, IReadOnlyCollection<int> serverAlive, double grace = EnemyGraceSeconds)
    {
        var listed = serverAlive as ISet<int> ?? serverAlive.ToHashSet();
        var free = new List<int>();
        var living = new HashSet<int>();
        for (var i = 0; i < local.Count; i++)
        {
            var (netId, alive, age) = local[i];
            if (!alive) continue;
            if (!listed.Contains(netId)) { if (age >= grace) free.Add(i); continue; }
            if (!living.Add(netId)) free.Add(i); // a second living copy of one enemy: keep the first
        }
        var missing = listed.Where(id => !living.Contains(id)).OrderBy(id => id).ToList();
        return (free, missing);
    }
}

/// <summary>
/// The client's watch over a floor change (pure; the clock is passed in). FloorChangeBegin starts it; the FloorChange
/// that builds ends it. A change that never arrives (lost, or the server's own build failed) or whose build throws here
/// would otherwise leave the player behind the loading screen with the controls locked for good: the watchdog asks the
/// server for the floor again (<see cref="FloorResyncIntent"/>), and after a few unanswered asks or failed builds tells
/// the client to rejoin, which is the "reconnecting fixed it" path made automatic.
/// </summary>
public sealed class FloorWatchdog
{
    public enum Step { None, Ask, Rejoin }

    /// <summary>How long a change may take before we ask: generous, because a server may be waiting for its AI planner (it ignores an ask while it builds).</summary>
    public double TimeoutSeconds { get; set; } = 20;
    /// <summary>How soon to ask again after our own build failed.</summary>
    public double RetrySeconds { get; set; } = 2;
    public int MaxAsks { get; set; } = 3;
    public int MaxFailures { get; set; } = 2;

    public bool Pending { get; private set; }
    public int Asks { get; private set; }
    public int Failures { get; private set; }
    private double _due;

    public void Begin(double now) { Pending = true; Asks = 0; Failures = 0; _due = now + TimeoutSeconds; }

    /// <summary>The floor is built: nothing to watch.</summary>
    public void Done() { Pending = false; Asks = 0; Failures = 0; }

    /// <summary>Our build of the server's floor threw: ask again soon.</summary>
    public void Failed(double now) { Pending = true; Failures++; _due = now + RetrySeconds; }

    public Step Tick(double now)
    {
        if (!Pending || now < _due) return Step.None;
        if (Asks >= MaxAsks || Failures >= MaxFailures) { Pending = false; return Step.Rejoin; }
        Asks++;
        _due = now + TimeoutSeconds;
        return Step.Ask;
    }
}
