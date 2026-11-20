namespace DungeonCrawler.Audio;

/// <summary>
/// Which music tracks stay loaded as a floor is built: the tracks that floor can play (its tier's exploration pool, the battle and
/// boss tracks, and on a railed floor the rides' chase and pour) and the track playing now. Anything else the music read before (a
/// tier left behind, the menu's, a sting) is let go, read again if it plays again. Engine-free.
/// </summary>
public static class FloorMusic
{
    /// <summary>The tracks a floor of tier <paramref name="tier"/> can play (the tier clamped to the pools there are); <paramref name="ride"/> null on a floor without rails.</summary>
    public static HashSet<string> Wanted(IReadOnlyList<string[]> exploreByTier, int tier, IEnumerable<string> combat, IEnumerable<string> boss, IEnumerable<string>? ride)
    {
        var wanted = new HashSet<string>(exploreByTier[Math.Clamp(tier, 0, exploreByTier.Count - 1)]);
        wanted.UnionWith(combat);
        wanted.UnionWith(boss);
        if (ride != null) wanted.UnionWith(ride);
        return wanted;
    }

    /// <summary>Is <paramref name="file"/> kept: wanted by the floor, or the one playing ("" while silent keeps nothing).</summary>
    public static bool Keep(string file, IReadOnlySet<string> wanted, string playing) => wanted.Contains(file) || (playing != "" && file == playing);

    /// <summary>The loaded tracks to let go.</summary>
    public static List<string> Stale(IEnumerable<string> cached, IReadOnlySet<string> wanted, string playing)
    {
        var stale = new List<string>();
        foreach (var file in cached) if (!Keep(file, wanted, playing)) stale.Add(file);
        return stale;
    }
}
