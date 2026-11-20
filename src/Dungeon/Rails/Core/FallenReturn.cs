namespace DungeonCrawler.Dungeon.Rails;

/// <summary>
/// When heroes fallen into a crevasse come back (ruling 8): the party's cart rolls into a stop (its arrival, never a spare's
/// takeover), or a living hero on foot steps onto a stop's platform after the fall. A hero standing on a platform when the fall
/// came (<see cref="Baseline"/>) has to step off it and back on. A seated or dead hero is on no platform: one who gets out of a
/// seat onto a platform steps onto it. Engine-free; the RailController ticks it while any hero is fallen. No allocation.
/// </summary>
public sealed class FallenReturn
{
    private int[] _last = Array.Empty<int>();

    /// <summary>The hero (by index) whose step onto a platform made the last return, or −1 (an arrival, or none yet).</summary>
    public int Hero { get; private set; } = -1;

    /// <summary>At the fall: each hero's platform stop (−1 off any). A hero not named here starts off every platform.</summary>
    public void Baseline(IReadOnlyList<int> platformStopOfEach)
    {
        if (_last.Length < platformStopOfEach.Count) _last = new int[platformStopOfEach.Count];
        for (var i = 0; i < _last.Length; i++) _last[i] = i < platformStopOfEach.Count ? platformStopOfEach[i] : -1;
    }

    /// <summary>
    /// One tick: <paramref name="arrivedStop"/> when it is ≥ 0 (the cart rolled into that stop); else the platform stop of the
    /// first living, unseated hero whose stop changed to ≥ 0 since its last tick; else −1. Every hero's stop is remembered for the
    /// next tick (a seated or dead one's as −1).
    /// </summary>
    public int Tick(IReadOnlyList<(bool Living, bool Seated, int PlatformStop)> heroes, int arrivedStop)
    {
        if (_last.Length < heroes.Count)
        {
            var grown = new int[heroes.Count];
            for (var i = 0; i < grown.Length; i++) grown[i] = i < _last.Length ? _last[i] : -1;
            _last = grown;
        }
        var stop = -1;
        var hero = -1;
        for (var i = 0; i < heroes.Count; i++)
        {
            var (living, seated, platform) = heroes[i];
            var now = living && !seated ? platform : -1;
            if (stop < 0 && now >= 0 && now != _last[i]) { stop = now; hero = i; }
            _last[i] = now;
        }
        if (arrivedStop >= 0) { Hero = -1; return arrivedStop; }
        Hero = hero;
        return stop;
    }
}
