namespace DungeonCrawler.Dungeon.Rails;

/// <summary>
/// When the rides want the Chase music, and where ride 4's pour track starts so the drop hammers fall on its beat (plan 6). Engine-free;
/// <c>MusicDirector</c> asks it every poll, on every role but a dedicated server, from the state this peer shows. No allocation.
/// </summary>
public static class RideMusic
{
    /// <summary>The Chase music holds this long after the last poll that wanted it (at stop 3 it settles before the golem).</summary>
    public const double HoldSeconds = 4;
    /// <summary>A hero on foot this close to the rolling party cart hears the ride's music too.</summary>
    public const float NearMetres = 30f;

    /// <summary>
    /// The seconds from the start of <c>forge_heavy_dungeon.ogg</c> (the pour track) to its first downbeat. Measured 2026-11-19: silencedetect
    /// (n=-30dB, d=0.05) finds no leading silence, the first strong onset is the track's first 10 ms window, and a comb fit of the hi-hat
    /// band's onsets over the whole track puts the grid's phase within 4 ms of 0.
    /// </summary>
    public const double PourBeatOrigin = 0.0;

    /// <summary>
    /// The Chase music is wanted now: the party cart rolls with the listener seated in it or within <see cref="NearMetres"/> of it, or a
    /// chaser rolls (the chase is live) with the listener seated in it, however far behind the train; and for <see cref="HoldSeconds"/>
    /// after either. <paramref name="lastSeen"/> is the last time it was live (−∞: never, or reset by a load or a floor change).
    /// </summary>
    public static bool Wanted(bool cartRolling, bool listenerSeated, float listenerDistance, bool chaserRolling, bool listenerInChaser,
        double now, ref double lastSeen)
    {
        var live = cartRolling && (listenerSeated || listenerDistance <= NearMetres) || chaserRolling && listenerInChaser;
        if (live) lastSeen = now;
        return now - lastSeen < HoldSeconds;
    }

    /// <summary>The ride a cart is on, from the stop it left (ride 1 runs from stop 0 to stop 1; ride 4 is the kiln's).</summary>
    public static int Ride(int stopLeft) => stopLeft + 1;

    /// <summary>
    /// Where to start the pour track at <paramref name="floorSeconds"/> so its downbeat (<paramref name="beatOrigin"/>) lands on a hammer's
    /// fall: the origin plus the time since the last fall (<paramref name="fallSeconds"/> on a <paramref name="cycleSeconds"/> cycle),
    /// brought back below <paramref name="trackLength"/> by whole cycles. Always in [0, trackLength).
    /// </summary>
    public static double PhaseSeek(double floorSeconds, double fallSeconds, double cycleSeconds, double beatOrigin, double trackLength)
    {
        if (trackLength <= 0) return 0;
        if (cycleSeconds <= 0) return beatOrigin >= 0 && beatOrigin < trackLength ? beatOrigin : 0;
        var at = beatOrigin + Mod(floorSeconds - fallSeconds, cycleSeconds);
        while (at >= trackLength) at -= cycleSeconds;
        if (at < 0) at = Mod(at, trackLength); // a track shorter than the origin and a cycle cannot keep the beat: stay inside it
        return at < trackLength ? at : 0;
    }

    /// <summary>a mod m in [0, m); a result within 1e-6 of m is 0 (a fall reckoned in floating seconds).</summary>
    private static double Mod(double a, double m)
    {
        var r = a % m;
        if (r < 0) r += m;
        return m - r < 1e-6 ? 0 : r;
    }
}
