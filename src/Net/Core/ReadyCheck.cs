namespace DungeonCrawler.Net;

/// <summary>
/// The stairs ready-check (co-op, pillar 7): on a server with two or more players a Yes on the landing starts a vote;
/// the floor changes when every living player is ready or when <see cref="TimeoutSeconds"/> pass with a majority; a No
/// cancels. Solo and one-player servers never run one (<see cref="Needed"/>). Engine-free; the server owns the instance.
/// </summary>
public static class ReadyCheckRules
{
    public const float TimeoutSeconds = 15f;
    public const int MinPlayers = 2;

    /// <summary>A ready-check only runs with two or more players online; one player takes the stairs at once.</summary>
    public static bool Needed(int onlinePlayers) => onlinePlayers >= MinPlayers;

    /// <summary>Strictly more than half: one of two is not a majority.</summary>
    public static bool Majority(int ready, int total) => total > 0 && ready * 2 > total;

    /// <summary>"Descend? 1/3 ready · 12 s" (down) or "Climb? ..." (up); empty when no check runs.</summary>
    public static string HudLine(int count, int total, int secondsLeft, int direction)
        => total <= 0 ? "" : $"{(direction < 0 ? "Climb" : "Descend")}? {count}/{total} ready · {secondsLeft} s";
}

public enum ReadyVerdict { Pending, Go, Cancelled }

/// <summary>One stairs vote: who said Yes, on which stairs, since when. <see cref="Vote"/> and <see cref="Tick"/> decide.</summary>
public sealed class ReadyCheck
{
    private readonly HashSet<int> _ready = new();

    public bool Active { get; private set; }
    /// <summary>The stairs being voted on ("" when inactive).</summary>
    public string StairsRef { get; private set; } = "";
    public int Direction { get; private set; }
    public double StartedAt { get; private set; }
    /// <summary>Member indices that said Yes.</summary>
    public IReadOnlySet<int> Ready => _ready;

    /// <summary>How many of the living players said Yes (a leaver's or a downed player's vote stops counting).</summary>
    public int ReadyCount(IReadOnlyCollection<int> living) => living.Count(_ready.Contains);

    public float SecondsLeft(double now)
        => !Active ? ReadyCheckRules.TimeoutSeconds : Math.Clamp(ReadyCheckRules.TimeoutSeconds - (float)(now - StartedAt), 0f, ReadyCheckRules.TimeoutSeconds);

    /// <summary>
    /// A Yes starts the check (or restarts it for other stairs) and counts once per member; a No cancels the running check
    /// on the stairs it names and is nothing without one. Only a living player's vote counts: a downed, parked or unknown
    /// member can neither start a check nor veto one. Returns the verdict after the vote.
    /// </summary>
    public ReadyVerdict Vote(int member, bool yes, string stairsRef, int direction, IReadOnlyCollection<int> living, double now)
    {
        if (!living.Contains(member)) return ReadyVerdict.Pending;
        if (!yes)
        {
            if (!Active || StairsRef != stairsRef) return ReadyVerdict.Pending;
            Clear();
            return ReadyVerdict.Cancelled;
        }
        if (Active && StairsRef != stairsRef) Clear();
        if (!Active)
        {
            Active = true;
            StairsRef = stairsRef;
            Direction = direction;
            StartedAt = now;
        }
        _ready.Add(member);
        return Tick(living, now);
    }

    /// <summary>Go when all living players are ready, or at the timeout with a majority; Cancelled at the timeout without one.</summary>
    public ReadyVerdict Tick(IReadOnlyCollection<int> living, double now)
    {
        if (!Active) return ReadyVerdict.Pending;
        var ready = ReadyCount(living);
        if (living.Count > 0 && ready >= living.Count) { Clear(); return ReadyVerdict.Go; }
        if (now - StartedAt < ReadyCheckRules.TimeoutSeconds) return ReadyVerdict.Pending;
        var go = ReadyCheckRules.Majority(ready, living.Count);
        Clear();
        return go ? ReadyVerdict.Go : ReadyVerdict.Cancelled;
    }

    public void Clear()
    {
        _ready.Clear();
        Active = false;
        StairsRef = "";
        Direction = 0;
        StartedAt = 0;
    }
}
