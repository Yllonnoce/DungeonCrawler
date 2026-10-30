namespace DungeonCrawler.Party;

/// <summary>
/// The jig a hero does over a kill: Squat, HipBuck and HipRock in an order drawn fresh each time, <see cref="Repeats"/>
/// of each in a row at a quick pace, and a Wave at normal pace if nothing is left to fight by then. A step is handed
/// out each time the body asks for the next clip, so the body decides when a clip has finished; moving, attacking or
/// taking a hit cancels the rest. Engine-free for the tests.
/// </summary>
public sealed class VictoryDance
{
    public const int Repeats = 2;
    /// <summary>AnimationPlayer speed for the squats and hip moves.</summary>
    public const float FastSpeed = 4f;
    public const string Squat = "Squat", Buck = "HipBuck", Rock = "HipRock", Wave = "Wave";

    private readonly string[] _order = { Squat, Buck, Rock };
    private int _step = -1; // index into the sequence; -1 when not dancing

    public bool Active => _step >= 0;

    /// <summary>Begin a dance whose move order is shuffled from <paramref name="seed"/>.</summary>
    public void Start(int seed)
    {
        var rng = new Random(seed);
        for (var i = _order.Length - 1; i > 0; i--) { var j = rng.Next(i + 1); (_order[i], _order[j]) = (_order[j], _order[i]); }
        _step = 0;
    }

    public void Cancel() => _step = -1;

    /// <summary>The clip to play now and how fast, or null when the dance is over (or never started). The wave only comes when <paramref name="enemiesRemain"/> is false at its turn.</summary>
    public (string Clip, float Speed)? Next(bool enemiesRemain)
    {
        if (_step < 0) return null;
        var step = _step++;
        if (step < Repeats * _order.Length) return (_order[step / Repeats], FastSpeed);
        _step = -1;
        return step == Repeats * _order.Length && !enemiesRemain ? (Wave, 1f) : null;
    }
}
