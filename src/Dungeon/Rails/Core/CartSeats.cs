using DungeonCrawler.Dungeon.Generation;

namespace DungeonCrawler.Dungeon.Rails;

/// <summary>Who sits where in the cart: heroes take seats 0-3, NPCs the bench seats 4-5.</summary>
public sealed class CartSeats
{
    public const int HeroSeats = 4;
    public const int BenchSeats = 2;

    private readonly int?[] _seat = new int?[HeroSeats + BenchSeats];

    public int Count { get { int n = 0; foreach (var s in _seat) if (s != null) n++; return n; } }

    public IReadOnlyList<int> Occupants { get { var l = new List<int>(); foreach (var s in _seat) if (s is int o) l.Add(o); return l; } }

    public int? SeatOf(int occupant)
    {
        for (int i = 0; i < _seat.Length; i++) if (_seat[i] == occupant) return i;
        return null;
    }

    public int? Take(int occupant, bool isHero)
    {
        if (SeatOf(occupant) is int have) return have;
        int from = isHero ? 0 : HeroSeats, to = isHero ? HeroSeats : HeroSeats + BenchSeats;
        for (int i = from; i < to; i++)
            if (_seat[i] == null) { _seat[i] = occupant; return i; }
        return null;
    }

    /// <summary>
    /// Book exactly <paramref name="seat"/> for <paramref name="occupant"/> (a client following the server's <c>SeatMsg</c>): false when the
    /// index is outside the book (0-3 heroes, 4-5 the bench) or another occupant holds it. An occupant already elsewhere in the book moves.
    /// </summary>
    public bool Put(int occupant, int seat)
    {
        if (seat < 0 || seat >= _seat.Length) return false;
        if (_seat[seat] is int holder) return holder == occupant;
        if (SeatOf(occupant) is int had) _seat[had] = null;
        _seat[seat] = occupant;
        return true;
    }

    /// <summary>Who holds <paramref name="seat"/>, or null (free, or no such seat).</summary>
    public int? OccupantOf(int seat) => seat >= 0 && seat < _seat.Length ? _seat[seat] : null;

    public bool Leave(int occupant)
    {
        var s = SeatOf(occupant);
        if (s == null) return false;
        _seat[s.Value] = null;
        return true;
    }

    /// <summary>Where someone getting out stands. At a stop: the nearest walkable non-lane cell beside the lane near laneIndex; otherwise the lane cell itself.</summary>
    public static GridPos ExitCell(IReadOnlyList<GridPos> lane, int laneIndex, Func<GridPos, bool> walkable, bool atStop)
    {
        int at = Math.Clamp(laneIndex, 0, lane.Count - 1);
        if (!atStop) return lane[at];
        var laneSet = new HashSet<GridPos>(lane);
        foreach (int d in new[] { 0, -1, 1, -2, 2 })
        {
            int i = Math.Clamp(laneIndex + d, 0, lane.Count - 1);
            var c = lane[i];
            foreach (var n in new[] { new GridPos(c.X, c.Y - 1), new GridPos(c.X + 1, c.Y), new GridPos(c.X, c.Y + 1), new GridPos(c.X - 1, c.Y) })
                if (!laneSet.Contains(n) && walkable(n)) return n;
        }
        return lane[at];
    }
}

/// <summary>
/// The countdown at a stop: the cart leaves when time is up, or once every living hero is aboard and the short minimum wait has passed.
/// While crew run to their seats (<see cref="TrainRules.HoldsDeparture"/>) a departure that comes due is held: the countdown keeps running
/// at nought left until the hold ends.
/// </summary>
public sealed class Boarding
{
    public const float Countdown = 5f;
    /// <summary>Even with everyone aboard the cart waits this long after the countdown starts.</summary>
    public const float MinWait = 2f;

    public bool Running { get; private set; }
    public float Left { get; private set; }
    /// <summary>Seconds since <see cref="Start"/>, any hold included (nought before the first).</summary>
    public float Elapsed { get; private set; }

    public void Start() { Running = true; Left = Countdown; Elapsed = 0f; }

    /// <summary>Stop the countdown without departing.</summary>
    public void Cancel() => Running = false;

    /// <summary>
    /// Returns true once per Start, when the cart should depart. A departure due while <paramref name="hold"/> is set waits: the
    /// countdown stays running at <see cref="Left"/> 0 and this returns false.
    /// </summary>
    public bool Tick(float dt, int seatedHeroes, int livingHeroes, bool hold = false)
    {
        if (!Running) return false;
        Elapsed += dt;
        Left = Math.Max(0f, Left - dt);
        bool allAboard = seatedHeroes >= 1 && seatedHeroes == livingHeroes && Countdown - Left >= MinWait - 1e-4f;
        if (!(Left <= 0f || allAboard) || hold) return false;
        Running = false;
        return true;
    }
}
