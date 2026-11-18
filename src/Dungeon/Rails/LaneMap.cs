using Godot;

namespace DungeonCrawler.Dungeon.Rails;

/// <summary>
/// Where a point on the party lane stands on the enemy lane beside it, and back: a table of the two lanes' S every metre
/// of the party lane, read by linear interpolation. The lanes run the same way side by side but differ in length (the
/// outer lane is longer round a bend), so the map is not a constant ratio. Built once; no allocation to read.
/// </summary>
public sealed class LaneMap
{
    /// <summary>The table's step along the party lane.</summary>
    public const float Step = 1f;
    /// <summary>
    /// Each sample is looked for within this much of a point <see cref="Lead"/> past the last one's enemy S: never another
    /// stretch of the line. Round a jog's outer corner the enemy lane runs about three metres to the party lane's one, so
    /// one sample can move it some twelve metres on.
    /// </summary>
    const float Window = 16f;
    const float Lead = 4f;

    private readonly float[] _party;
    private readonly float[] _enemy;

    public LaneMap(TrackSampler party, TrackSampler enemy)
    {
        var n = Mathf.Max(2, Mathf.CeilToInt(party.Length / Step) + 1);
        _party = new float[n];
        _enemy = new float[n];
        var first = enemy.Track.Curve.GetClosestOffset(enemy.Track.ToLocal(party.Point(0f))); // the lanes start side by side
        var hint = first;
        for (var i = 0; i < n; i++)
        {
            var s = Mathf.Min(i * Step, party.Length);
            enemy.Measure(party.Point(s), hint + Lead, Window, out var b); // the nearest in the window, wherever it is: the lanes stand a cell apart, beyond Measure's "near the line"
            var e = b.S;
            if (i > 0 && e < _enemy[i - 1]) e = _enemy[i - 1]; // the lanes run the same way: the map never runs backwards
            _party[i] = s;
            _enemy[i] = e;
            hint = e;
        }
    }

    /// <summary>The enemy lane's S beside the party lane's <paramref name="partyS"/> (clamped to the table).</summary>
    public float EnemyS(float partyS) => Lerp(_party, _enemy, partyS);

    /// <summary>The party lane's S beside the enemy lane's <paramref name="enemyS"/> (clamped to the table).</summary>
    public float PartyS(float enemyS) => Lerp(_enemy, _party, enemyS);

    /// <summary>How many metres of enemy lane one metre of party lane spans at <paramref name="partyS"/> (over the table step there).</summary>
    public float Stretch(float partyS)
    {
        float a = EnemyS(partyS - Step), b = EnemyS(partyS + Step);
        float pa = Mathf.Max(0f, partyS - Step), pb = Mathf.Min(_party[^1], partyS + Step);
        return pb - pa < 0.01f ? 1f : (b - a) / (pb - pa);
    }

    /// <summary>Read <paramref name="to"/> at <paramref name="x"/> along the ascending <paramref name="from"/>: a binary search and a lerp.</summary>
    private static float Lerp(float[] from, float[] to, float x)
    {
        if (x <= from[0]) return to[0];
        if (x >= from[^1]) return to[^1];
        int lo = 0, hi = from.Length - 1;
        while (hi - lo > 1)
        {
            var mid = (lo + hi) >> 1;
            if (from[mid] <= x) lo = mid; else hi = mid;
        }
        var span = from[hi] - from[lo];
        return span < 1e-5f ? to[lo] : to[lo] + (to[hi] - to[lo]) * (x - from[lo]) / span;
    }
}
