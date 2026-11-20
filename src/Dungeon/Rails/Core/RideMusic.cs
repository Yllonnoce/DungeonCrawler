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
    /// The pour track's own tempo, measured 2026-11-19 (a 1 ms comb fit of its hi-hat onsets over the whole track; its source file
    /// name says 160): played at 1×, its beat would fall about 2 ms a second behind the hammers' 160.
    /// </summary>
    public const double PourTrackBpm = 159.66;
    /// <summary>The pour track's pitch scale, so it runs at the hammers' 160 BPM (about 1.0021, +3.7 cents).</summary>
    public const double PourPitch = 160.0 / PourTrackBpm;

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

    /// <summary>The ride a cart is on, from the stop it left (ride 1 runs from stop 0 to stop 1; the last ride is the kiln's).</summary>
    public static int Ride(int stopLeft) => stopLeft + 1;

    /// <summary>Every ride but the last one's track (the music folder's file name), looped.</summary>
    public const string ChaseTrack = "chase_determined_pursuit.ogg";
    /// <summary>
    /// The last ride's track (160 BPM by its source's name; its onsets measure <see cref="PourTrackBpm"/>): played at <see cref="PourPitch"/> so its
    /// beat keeps the hammers' 160, once through from <see cref="PhaseSeek"/>, and locked again each time it ends.
    /// </summary>
    public const string PourTrack = "forge_heavy_dungeon.ogg";

    /// <summary>Every ride but the last loops the chase; the last (the kiln's hammers and pour) plays the forge piece on the hammers' beat.</summary>
    public static string TrackFor(int ride, int rides) => ride == rides ? PourTrack : ChaseTrack;

    /// <summary>Every track a ride plays (<see cref="TrackFor"/>): the music loads them when a railed floor is built.</summary>
    public static readonly IReadOnlyList<string> Tracks = new[] { ChaseTrack, PourTrack };

    /// <summary>The last stop at or behind <paramref name="s"/> on the party line (a chaser's stop left); 0 before stop 1 or with no stops.</summary>
    public static int StopBehind(IReadOnlyList<float> stopS, float s)
    {
        var stop = 0;
        for (var i = 1; i < stopS.Count; i++) if (stopS[i] <= s) stop = i;
        return stop;
    }

    /// <summary>
    /// Where to start the pour track (in its own seconds) at <paramref name="floorSeconds"/> so its downbeat (<paramref name="beatOrigin"/>)
    /// lands on a hammer's fall, played at <paramref name="pitch"/>: the origin plus the floor time since the last fall
    /// (<paramref name="fallSeconds"/> on a <paramref name="cycleSeconds"/> cycle) times the pitch (track time runs pitch× the floor's),
    /// brought back below <paramref name="trackLength"/> by whole cycles of cycle·pitch track seconds. Always in [0, trackLength).
    /// </summary>
    public static double PhaseSeek(double floorSeconds, double fallSeconds, double cycleSeconds, double beatOrigin, double trackLength, double pitch = 1)
    {
        if (trackLength <= 0) return 0;
        if (cycleSeconds <= 0 || pitch <= 0) return beatOrigin >= 0 && beatOrigin < trackLength ? beatOrigin : 0;
        var trackCycle = cycleSeconds * pitch;
        var at = beatOrigin + Mod(floorSeconds - fallSeconds, cycleSeconds) * pitch;
        while (at >= trackLength) at -= trackCycle;
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
