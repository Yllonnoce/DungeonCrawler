namespace DungeonCrawler.Party;

/// <summary>
/// The jig a hero does over a kill: <see cref="Repeats"/> quick HipBucks, as many quick HipRocks, and a Wave at normal
/// pace if nothing is left to fight by then. A step is handed out each time the body asks for the next clip, so the
/// body decides when a clip has finished; moving, attacking or taking a hit cancels the rest. Engine-free for the tests.
/// </summary>
public sealed class VictoryDance
{
    public const int Repeats = 5;
    /// <summary>AnimationPlayer speed for the hip moves: a whole set of five fits in roughly the time one would take.</summary>
    public const float FastSpeed = 2.5f;
    public const string Buck = "HipBuck", Rock = "HipRock", Wave = "Wave";

    private int _step = -1; // index into the sequence below; -1 when not dancing

    public bool Active => _step >= 0;

    public void Start() => _step = 0;
    public void Cancel() => _step = -1;

    /// <summary>The clip to play now and how fast, or null when the dance is over (or never started). The wave only comes when <paramref name="enemiesRemain"/> is false at its turn.</summary>
    public (string Clip, float Speed)? Next(bool enemiesRemain)
    {
        if (_step < 0) return null;
        var step = _step++;
        if (step < Repeats) return (Buck, FastSpeed);
        if (step < Repeats * 2) return (Rock, FastSpeed);
        _step = -1;
        return step == Repeats * 2 && !enemiesRemain ? (Wave, 1f) : null;
    }
}
