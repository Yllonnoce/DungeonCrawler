namespace DungeonCrawler.Adventure;

/// <summary>
/// The shut-out creatures' clock: runs only while the party is inside the safe room. The first group gathers after
/// <see cref="SafeRoomRules.GraceSeconds"/>, then one more every <see cref="SafeRoomRules.IntervalSeconds"/>, never while
/// <see cref="SafeRoomRules.CapAlive"/> are alive (the clock keeps running meanwhile). An empty room pauses the clock and,
/// after <see cref="SafeRoomRules.EmptyResetSeconds"/>, starts it over (the count of groups sent stays).
/// </summary>
public sealed class GatheringClock
{
    private float _emptyFor;
    private float _nextAt = SafeRoomRules.GraceSeconds;
    private bool _due;

    /// <summary>Groups sent so far.</summary>
    public int Spawned { get; private set; }
    /// <summary>Time occupied since the clock last reset.</summary>
    public float Seconds { get; private set; }

    public void Update(float dt, bool occupied, int aliveFromGathering)
    {
        if (!occupied)
        {
            _emptyFor += dt;
            if (_emptyFor >= SafeRoomRules.EmptyResetSeconds) { Seconds = 0f; _nextAt = SafeRoomRules.GraceSeconds; _due = false; }
            return;
        }
        _emptyFor = 0f;
        Seconds += dt;
        if (Seconds >= _nextAt && aliveFromGathering < SafeRoomRules.CapAlive) _due = true;
    }

    /// <summary>True once per due group; counts it as sent.</summary>
    public bool TakeDue()
    {
        if (!_due) return false;
        _due = false;
        Spawned++;
        while (_nextAt <= Seconds) _nextAt += SafeRoomRules.IntervalSeconds;
        return true;
    }

    /// <summary>A loaded floor resumes inside its interval: <paramref name="spawned"/> groups already sent, grace over.</summary>
    public void Restore(int spawned)
    {
        Spawned = spawned;
        Seconds = SafeRoomRules.GraceSeconds;
        _nextAt = SafeRoomRules.GraceSeconds + SafeRoomRules.IntervalSeconds;
        _emptyFor = 0f;
        _due = false;
    }
}
