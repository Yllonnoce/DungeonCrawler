namespace DungeonCrawler.Dungeon.Rails;

/// <summary>
/// Where a <c>RailTrack</c> puts its rail rings and sleepers along S, engine-free. The track's rails and gravel run through
/// its own sample lattice (every <c>RailTrack.Sample</c>) plus the exact ends of each gap, so they stop with a ring at a gap's
/// <c>from</c> and start again with one at its <c>to</c>: "bare over (from, to)" is exact. A span laid over a gap
/// (<c>RailTrack.LaySpan</c>) rings at its own exact ends and the same lattice between them, so it meets the track at the lip
/// with the same frame, and its neighbouring span at the cell edge.
/// </summary>
public static class TrackStations
{
    /// <summary>Two stations closer than this are one (a gap's end on the lattice is not laid twice).</summary>
    public const float Merge = 1e-4f;

    /// <summary>Is <paramref name="s"/> strictly inside one of <paramref name="gaps"/>? A gap's own ends are not bare: the rail rings there.</summary>
    public static bool Bare(float s, IReadOnlyList<(float from, float to)>? gaps)
    {
        if (gaps == null) return false;
        foreach (var (from, to) in gaps) if (s > from && s < to) return true;
        return false;
    }

    /// <summary>
    /// The S of every rail ring along a track of <paramref name="length"/>, in order: the lattice k·<paramref name="sample"/>
    /// short of the length, the length itself, and each gap's from and to within [0, length]. A gap's end within
    /// <see cref="Merge"/> of a lattice station takes its place. Bare stations (<see cref="Bare"/>) are listed too: the
    /// caller breaks the rail there.
    /// </summary>
    public static List<float> Rail(float length, float sample, IReadOnlyList<(float from, float to)>? gaps)
    {
        var stations = new List<float>();
        for (var k = 0; k * sample < length; k++) stations.Add(k * sample);
        stations.Add(length);
        if (gaps == null || gaps.Count == 0) return stations;
        foreach (var (from, to) in gaps)
            foreach (var end in new[] { from, to })
                if (end >= 0f && end <= length) Place(stations, end);
        return stations;
    }

    /// <summary>
    /// The S of a span's rail rings over [<paramref name="from"/>, <paramref name="to"/>]: its two exact ends and the lattice
    /// k·<paramref name="sample"/> strictly between them (more than <see cref="Merge"/> from either end).
    /// </summary>
    public static List<float> Span(float from, float to, float sample)
    {
        var stations = new List<float> { from };
        for (var k = (int)MathF.Floor(from / sample) + 1; k * sample < to - Merge; k++)
            if (k * sample > from + Merge) stations.Add(k * sample);
        stations.Add(to);
        return stations;
    }

    /// <summary>The S of sleeper <paramref name="k"/>: every / 2 + k·every along the whole track (the track's and a span's one phase).</summary>
    public static float Sleeper(int k, float every) => every / 2f + k * every;

    /// <summary>
    /// The sleepers a span of (<paramref name="from"/>, <paramref name="to"/>] lays on a track of <paramref name="length"/>:
    /// half-open, so two spans meeting at a cell edge lay a sleeper exactly on it once (the earlier span's).
    /// </summary>
    public static List<float> SpanSleepers(float from, float to, float every, float length)
    {
        var sleepers = new List<float>();
        for (var k = Math.Max(0, (int)MathF.Floor((from - every / 2f) / every)); ; k++)
        {
            var s = Sleeper(k, every);
            if (s > to || s >= length) break;
            if (s > from) sleepers.Add(s);
        }
        return sleepers;
    }

    /// <summary>Put <paramref name="s"/> in the sorted <paramref name="stations"/>, replacing a station within <see cref="Merge"/>.</summary>
    private static void Place(List<float> stations, float s)
    {
        var i = stations.BinarySearch(s);
        if (i >= 0) return;
        i = ~i;
        if (i < stations.Count && stations[i] - s < Merge) { stations[i] = s; return; }
        if (i > 0 && s - stations[i - 1] < Merge) { stations[i - 1] = s; return; }
        stations.Insert(i, s);
    }
}
