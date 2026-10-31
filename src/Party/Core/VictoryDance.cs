namespace DungeonCrawler.Party;

/// <summary>
/// The jig a hero does over a kill: a Squat to open, then <see cref="RandomMoves"/> different moves drawn fresh each time from
/// the rest of <see cref="Pool"/>, <see cref="Repeats"/> of each in a row (the three hip moves at a quick pace, the gestures at
/// their natural pace), and a
/// Wave at normal pace if nothing is left to fight by then. A step is handed out each time the body asks for the next
/// clip, so the body decides when a clip has finished; moving, attacking or taking a hit cancels the rest. Engine-free
/// for the tests.
/// </summary>
public sealed class VictoryDance
{
    public const int Repeats = 2;
    /// <summary>How many different moves one dance uses: the Squat that opens it plus <see cref="RandomMoves"/> drawn ones.</summary>
    public const int MovesPerDance = 1 + RandomMoves;
    /// <summary>How many moves are drawn from the pool after the opening Squat.</summary>
    public const int RandomMoves = 2;
    /// <summary>AnimationPlayer speed for the squats and hip moves; everything else plays at 1.</summary>
    public const float FastSpeed = 4f;
    public const string Squat = "Squat", Buck = "HipBuck", Rock = "HipRock", Wave = "Wave";

    /// <summary>Every move a dance may draw from (the Wave is the fixed finish, not part of the draw).</summary>
    public static readonly IReadOnlyList<string> Pool = new[]
    {
        Squat, Buck, Rock, "Cheer", "Punch", "Shrug", "HitReact", "Cower", "Pain", "Talk", "Flinch", "Laugh", "Point", "ShakeHead",
    };
    private static readonly HashSet<string> Fast = new() { Squat, Buck, Rock };

    private readonly string[] _order = new string[MovesPerDance];
    private int _step = -1; // index into the sequence; -1 when not dancing

    public bool Active => _step >= 0;

    /// <summary>Begin a dance: the Squat first, then <see cref="RandomMoves"/> other moves drawn from <paramref name="seed"/> (a partial shuffle of the rest of the pool, so no move comes twice).</summary>
    public void Start(int seed)
    {
        var rng = new Random(seed);
        var deck = Pool.Where(m => m != Squat).ToArray();
        _order[0] = Squat;
        for (var i = 0; i < RandomMoves; i++)
        {
            var j = i + rng.Next(deck.Length - i);
            (deck[i], deck[j]) = (deck[j], deck[i]);
            _order[1 + i] = deck[i];
        }
        _step = 0;
    }

    public void Cancel() => _step = -1;

    /// <summary>The clip to play now and how fast, or null when the dance is over (or never started). The wave only comes when <paramref name="enemiesRemain"/> is false at its turn.</summary>
    public (string Clip, float Speed)? Next(bool enemiesRemain)
    {
        if (_step < 0) return null;
        var step = _step++;
        if (step < Repeats * MovesPerDance) { var clip = _order[step / Repeats]; return (clip, Fast.Contains(clip) ? FastSpeed : 1f); }
        _step = -1;
        return step == Repeats * MovesPerDance && !enemiesRemain ? (Wave, 1f) : null;
    }
}
