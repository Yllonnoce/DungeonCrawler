using DungeonCrawler.Dungeon.Generation;

namespace DungeonCrawler.Adventure;

/// <summary>
/// One row of the stop siege's table (plan 16, spec section 1): the first wave <see cref="FirstAfter"/> seconds after the arrival,
/// then one every <see cref="Gap"/> seconds; each wave opens <see cref="Portals"/> portals with <see cref="MinPerPortal"/> to
/// <see cref="MaxPerPortal"/> creatures each, while the siege's creatures alive stay under <see cref="AliveCap"/>.
/// </summary>
public readonly record struct SiegeWave(float FirstAfter, float Gap, int Portals, int MinPerPortal, int MaxPerPortal, int AliveCap);

/// <summary>An up hero as a siege site sees them: the cell they stand on and the way they face on the grid (x, y = world x, z).</summary>
public readonly record struct SiegeHero(GridPos Cell, float FacingX, float FacingY);

/// <summary>
/// The stop siege (plan 16 Task 3): while the train waits at a cage stop, portals tear open around the platform in waves until it
/// leaves, each stop harder than the last. The table, the co-op and heavy scaling, the hard cap (Ruling 36), the roster (Ruling 6),
/// the restart's grace (Ruling 3), the stragglers' cull and the sites. Every number is a tunable pinned by a unit test. Engine-free.
/// </summary>
public static class StopSiegeRules
{
    /// <summary>The restart's grace after a load (Ruling 3): the first wave of a siege restarted by a Restored event.</summary>
    public const float LoadGrace = 10f;
    /// <summary>The restart's grace after a wipe's rise at the cart.</summary>
    public const float RiseGrace = 15f;
    /// <summary>A straggler (a creature of a siege that has ended) is freed once it is farther than this from every hero...</summary>
    public const float StragglerFar = 60f;
    /// <summary>...and no hero has been in its sight for this long.</summary>
    public const float StragglerUnseen = 10f;
    /// <summary>A site stands at least this far (metres, cell centre to cell centre) from every up hero...</summary>
    public const float HeroClear = 6f;
    /// <summary>...this far from the stop's cage...</summary>
    public const float CageClear = 4f;
    /// <summary>...and this far from every platform cell.</summary>
    public const float PlatformClear = 4f;
    /// <summary>A wave's portals stand at least this far apart.</summary>
    public const float Spread = 8f;
    /// <summary>A wave that cannot open (no room under the cap, or no site) tries again this much later.</summary>
    public const float RetrySeconds = 2f;
    /// <summary>A site is in the stop's room or within this many cells of it (its corridor mouths).</summary>
    public const int Around = 3;
    /// <summary>Intensity (<c>Settings.Reinforcements</c>) at or above this is heavy: the gaps shrink by a third.</summary>
    public const float HeavyIntensity = 1.5f;
    /// <summary>The heavy gap's share of the table's.</summary>
    public const float HeavyGapScale = 2f / 3f;
    /// <summary>Metres per cell (cell centre to cell centre).</summary>
    private const float CellMetres = 4f;

    private static readonly SiegeWave[] Table =
    {
        new(4f, 14f, 1, 2, 2, 6),
        new(4f, 12f, 2, 2, 2, 8),
        new(3f, 10f, 2, 3, 3, 10),
        new(3f, 8f, 3, 2, 3, 12),
    };

    /// <summary>Stop <paramref name="stop"/>'s row (1-based; a stop past 4 reads row 4).</summary>
    public static SiegeWave For(int stop) => Table[Math.Clamp(stop, 1, Table.Length) - 1];

    /// <summary>The cage stops are sieged: 1 to <paramref name="stopCount"/> − 2, never the landing or the last stop.</summary>
    public static bool Sieges(int stop, int stopCount) => stop >= 1 && stop <= stopCount - 2;

    /// <summary>Creatures per portal: the row's min to max by <paramref name="roll"/> (0..1), plus one for each human past the first.</summary>
    public static int PerPortal(SiegeWave w, int humans, double roll)
    {
        var span = w.MaxPerPortal - w.MinPerPortal + 1;
        var n = w.MinPerPortal + Math.Min(span - 1, (int)Math.Floor(Math.Clamp(roll, 0.0, 1.0) * span));
        return n + Math.Max(0, humans - 1);
    }

    /// <summary>The siege's alive cap: the row's, plus two for each human past the first.</summary>
    public static int AliveCap(SiegeWave w, int humans) => w.AliveCap + 2 * Math.Max(0, humans - 1);

    /// <summary>The gap between waves at <paramref name="intensity"/>: the row's, a third shorter on heavy.</summary>
    public static float Gap(SiegeWave w, float intensity) => intensity >= HeavyIntensity ? w.Gap * HeavyGapScale : w.Gap;

    /// <summary>How many creatures a wave may send (Ruling 36: the cap is hard): what the cap leaves over those alive and those still to come through.</summary>
    public static int Room(int cap, int alive, int pending) => Math.Max(0, cap - alive - pending);

    /// <summary>
    /// The roster's roll (Ruling 6): cinder imps under 0.35, ember hounds under 0.70, forge priests under 0.95, and above that a cinder
    /// brute from stop 3 when the wave has none yet (<paramref name="bruteTaken"/>), else a cinder imp. Never a forge golem.
    /// </summary>
    public static string Pick(int stop, bool bruteTaken, double roll)
        => roll < CinderBelow ? Cinder
         : roll < HoundBelow ? Hound
         : roll < PriestBelow ? Priest
         : stop >= BruteFromStop && !bruteTaken ? Brute
         : Cinder;

    public const string Cinder = "enemy.horde_cinder", Hound = "enemy.ember_hound", Priest = "enemy.forge_priest", Brute = "enemy.horde_cinder_brute";
    /// <summary>The roster's bands: a roll under each gives that kind (the rest a brute, or a cinder imp).</summary>
    public const double CinderBelow = 0.35, HoundBelow = 0.70, PriestBelow = 0.95;
    /// <summary>The first stop a cinder brute may come through at (one a wave at most).</summary>
    public const int BruteFromStop = 3;

    /// <summary>The restart's grace: longer after a wipe's rise than after a load.</summary>
    public static float RestartGrace(bool rose) => rose ? RiseGrace : LoadGrace;

    /// <summary>A straggler is freed when it is farther than <see cref="StragglerFar"/> from every hero and unseen for <see cref="StragglerUnseen"/>.</summary>
    public static bool Culls(float nearestHeroMetres, float unseenSeconds) => nearestHeroMetres > StragglerFar && unseenSeconds >= StragglerUnseen;

    /// <summary>
    /// Up to <paramref name="count"/> portal sites for a wave at <paramref name="stop"/>, in pick order; fewer, or none, when fewer qualify.
    /// A candidate is a walkable <see cref="Cell.Floor"/> cell in <paramref name="room"/> or within <see cref="Around"/> cells of it (its
    /// corridor mouths) that is not reserved (<see cref="FloorLayout.IsReserved"/>: the line, its pockets), not the platform, not water,
    /// a bridge, a ledge or a ledge's ramp end, nor barred by <paramref name="blocked"/> (a prop on it); and that stands at least
    /// <see cref="HeroClear"/> m from every hero, <see cref="CageClear"/> m from the cage and <see cref="PlatformClear"/> m from every
    /// platform cell (cell centre to cell centre, 4 m a cell). Each scores 0 when it is behind every hero (dot(site − hero, facing) &lt; 0),
    /// else 10, plus a roll; the lowest go first, each at least <see cref="Spread"/> m from those already picked.
    /// </summary>
    public static List<GridPos> Sites(FloorLayout layout, Room room, RailStop stop, GridPos? cage, IReadOnlyList<SiegeHero> heroes, int count, Random rng, Func<GridPos, bool>? blocked = null)
    {
        var picked = new List<GridPos>();
        if (count <= 0) return picked;
        var scored = new List<(GridPos Cell, double Score)>();
        for (var y = room.Y - Around; y <= room.Bottom + Around; y++)
            for (var x = room.X - Around; x <= room.Right + Around; x++)
            {
                var c = new GridPos(x, y);
                if (!Candidate(layout, c, stop, blocked)) continue;
                if (!Clear(c, stop, cage, heroes)) continue;
                var behind = true;
                foreach (var h in heroes)
                    if ((c.X - h.Cell.X) * h.FacingX + (c.Y - h.Cell.Y) * h.FacingY >= 0f) { behind = false; break; }
                scored.Add((c, (behind ? 0.0 : 10.0) + rng.NextDouble()));
            }
        scored.Sort((a, b) => a.Score.CompareTo(b.Score));
        foreach (var (c, _) in scored)
        {
            if (picked.Count >= count) break;
            var spread = true;
            foreach (var p in picked) if (Metres(c, p) < Spread) { spread = false; break; }
            if (spread) picked.Add(c);
        }
        return picked;
    }

    private static bool Candidate(FloorLayout layout, GridPos c, RailStop stop, Func<GridPos, bool>? blocked)
    {
        if (!layout.Contains(c) || layout[c] != Cell.Floor || !layout.IsWalkable(c)) return false;
        if (layout.IsReserved(c) || layout.IsWater(c) || layout.IsBridge(c)) return false;
        foreach (var p in stop.Platform) if (p == c) return false;
        foreach (var l in layout.Ledges) if (l.RampBottom == c || l.RampTop == c || l.Cells.Contains(c)) return false;
        return blocked?.Invoke(c) != true;
    }

    private static bool Clear(GridPos c, RailStop stop, GridPos? cage, IReadOnlyList<SiegeHero> heroes)
    {
        foreach (var h in heroes) if (Metres(c, h.Cell) < HeroClear) return false;
        if (cage is { } k && Metres(c, k) < CageClear) return false;
        foreach (var p in stop.Platform) if (Metres(c, p) < PlatformClear) return false;
        return true;
    }

    /// <summary>Cell centre to cell centre, in metres.</summary>
    private static float Metres(GridPos a, GridPos b)
    {
        float dx = a.X - b.X, dy = a.Y - b.Y;
        return MathF.Sqrt(dx * dx + dy * dy) * CellMetres;
    }
}
