namespace DungeonCrawler.Dungeon.Rails;

/// <summary>An alcove shooter's phase (plan 16 Task 8): hidden at the back of its pocket, stepping out to the mouth, shooting, and done (freed).</summary>
public enum AlcovePhase { Hidden, StepOut, Shooting, Done }

/// <summary>
/// The alcove shooters (plan 16, spec section 4, Rulings 2, 4 and 17): enemies hidden in the track's refuge pockets who shoot the seated
/// riders as the train goes by. Placed by code, not by floor data: a table of counts and stretches per ride (<see cref="For"/>), applied on
/// any railed floor with <see cref="RideCount"/> rides when the ambushes are on. Each ride places as many as fit (<see cref="Place"/>):
/// its pockets outside every widened span (<see cref="HardClear"/> of the stops, the dark, the bridge and the cavern, the switch windows and
/// ride 5's deadly pieces; <see cref="SoftClear"/> of every other piece or encounter) and not claimed by a refuge seeker, in tiers (enemy lane
/// in the stretch, enemy lane outside it, party lane in, party lane out), the unlit pockets first in each, seeded, <see cref="Spacing"/> apart.
/// Engine-free.
/// </summary>
public static class AlcoveRules
{
    /// <summary>The rides a line must have for the table to apply (6 stops).</summary>
    public const int RideCount = 5;
    /// <summary>A shooter is never this close (m of party S) to a stop, the dark, the bridge, the cavern, a switch window or ride 5's deadly pieces (Ruling 17).</summary>
    public const float HardClear = 30f;
    /// <summary>A shooter is never this close (m of party S) to any other piece or encounter (Ruling 17).</summary>
    public const float SoftClear = 12f;
    /// <summary>Two shooters of a ride stand at least this far apart in S (m).</summary>
    public const float Spacing = 8f;
    /// <summary>A hidden shooter steps out when the train's front is this close before its post (m).</summary>
    public const float WakeAt = 35f;
    /// <summary>It shoots only a seated hero this close (m).</summary>
    public const float ShootRange = 25f;
    /// <summary>A wind-up starts this often while it has a target (s).</summary>
    public const float ShootEvery = 1.6f;
    /// <summary>It is done (freed) once the train's back is this far past its post (m).</summary>
    public const float GoneBehind = 30f;
    /// <summary>The stepping out from the back of the pocket to its mouth takes this long (s).</summary>
    public const float StepOutSeconds = 0.5f;
    /// <summary>A hidden shooter's distance check runs this often (s).</summary>
    public const float HiddenCheckEvery = 0.25f;
    /// <summary>The post's back: the pocket centre this far away from the lane (m).</summary>
    public const float BackFromCentre = 1.2f;
    /// <summary>The post's mouth: the pocket centre this far toward the lane (m).</summary>
    public const float MouthFromCentre = 0.8f;
    /// <summary>A bolt is aimed this far above the target's feet (m).</summary>
    public const float AimUp = 1.2f;
    /// <summary>The floor's shooters in all must reach this (the flow fails below it).</summary>
    public const int MinTotal = 12;
    /// <summary>Some ride must place at least this many.</summary>
    public const int MinBestRide = 4;

    /// <summary>A ride's row of the table: at most <see cref="Count"/> shooters, the stretch <see cref="From"/>..<see cref="To"/> (fractions of the ride) preferred.</summary>
    public readonly record struct AlcoveRide(int Count, float From, float To);

    /// <summary>A refuge pocket: its index in the line's final pocket list, its party S, its lane, and whether it has a lamp (an even index, as DungeonBuilder lights them).</summary>
    public readonly record struct Pocket(int Index, float S, bool EnemyLane, bool Lit);

    /// <summary>The spec's table (counts are maximums, Ruling 17). A ride outside 1..5 places none.</summary>
    public static AlcoveRide For(int ride) => ride switch
    {
        1 => new AlcoveRide(4, 0.5f, 0.75f),
        2 => new AlcoveRide(5, 0.55f, 0.65f),
        3 => new AlcoveRide(5, 0.4f, 0.7f),
        4 => new AlcoveRide(3, 0f, 1f),
        5 => new AlcoveRide(4, 0.05f, 0.18f),
        _ => new AlcoveRide(0, 0f, 0f),
    };

    /// <summary>
    /// The count each ride places on the shipped floor 12 (measured by <c>--test-stop-trap=pockets</c>, Step 1 of Task 8, with nothing claimed):
    /// rides 1-3 have the room (16, 10 and 9 clear pockets), rides 4 and 5 none (the dark, the cavern, the split's and the switches' windows and ride
    /// 5's pieces leave no pocket 30 m clear). 14 in all.
    /// </summary>
    public static IReadOnlyDictionary<int, int> ShippedCounts { get; } = new Dictionary<int, int> { [1] = 4, [2] = 5, [3] = 5, [4] = 0, [5] = 0 };

    /// <summary>Lit pockets: an even index in the final pocket list (DungeonBuilder's lanterns).</summary>
    public static bool LitIndex(int index) => index % 2 == 0;

    /// <summary>How far <paramref name="s"/> lies from the span <paramref name="span"/> (0 inside it).</summary>
    public static float Distance(float s, (float From, float To) span)
    {
        float lo = Math.Min(span.From, span.To), hi = Math.Max(span.From, span.To);
        return s < lo ? lo - s : s > hi ? s - hi : 0f;
    }

    /// <summary>The nearest of <paramref name="spans"/> to <paramref name="s"/> (its index and distance), or (−1, +∞).</summary>
    public static (int Index, float Distance) Nearest(float s, IReadOnlyList<(float From, float To)> spans)
    {
        var best = (-1, float.PositiveInfinity);
        for (var i = 0; i < spans.Count; i++)
        {
            var d = Distance(s, spans[i]);
            if (d < best.Item2) best = (i, d);
        }
        return best;
    }

    /// <summary>
    /// The pocket is a candidate for ride <paramref name="rideFromS"/>..<paramref name="rideToS"/>: on the ride, <see cref="HardClear"/> of both its stops and every
    /// <paramref name="hard"/> span, <see cref="SoftClear"/> of every <paramref name="soft"/> span, and not <paramref name="claimed"/>.
    /// </summary>
    public static bool Clear(Pocket p, float rideFromS, float rideToS, IReadOnlyList<(float From, float To)> hard, IReadOnlyList<(float From, float To)> soft, IReadOnlySet<int>? claimed)
    {
        if (!(p.S > rideFromS && p.S < rideToS)) return false;
        if (p.S - rideFromS < HardClear || rideToS - p.S < HardClear) return false;
        if (claimed != null && claimed.Contains(p.Index)) return false;
        if (Nearest(p.S, hard).Distance < HardClear) return false;
        if (Nearest(p.S, soft).Distance < SoftClear) return false;
        return true;
    }

    /// <summary>
    /// Ride <paramref name="ride"/>'s shooters: the pocket indices picked, at most <see cref="For"/>'s count. The candidates (<see cref="Clear"/>) go in tiers,
    /// enemy lane inside the stretch, enemy lane outside it, party lane inside, party lane outside; within a tier the unlit first, each group shuffled by the
    /// seeded rng (<paramref name="seed"/> ^ 0xA1C0 ^ ride). Picks are at least <see cref="Spacing"/> apart in S.
    /// </summary>
    public static List<int> Place(int ride, float rideFromS, float rideToS, IReadOnlyList<Pocket> pockets, IReadOnlyList<(float From, float To)> hard,
        IReadOnlyList<(float From, float To)> soft, IReadOnlySet<int> claimed, int seed)
    {
        var row = For(ride);
        var picks = new List<int>();
        if (row.Count <= 0) return picks;
        float len = rideToS - rideFromS, inFrom = rideFromS + row.From * len, inTo = rideFromS + row.To * len;
        var rng = new Random(seed ^ 0xA1C0 ^ ride);
        var picked = new List<float>();
        foreach (var (enemyLane, inside) in new[] { (true, true), (true, false), (false, true), (false, false) })
            foreach (var lit in new[] { false, true })
            {
                var group = new List<Pocket>();
                foreach (var p in pockets)
                    if (p.EnemyLane == enemyLane && p.Lit == lit && (p.S >= inFrom && p.S <= inTo) == inside && Clear(p, rideFromS, rideToS, hard, soft, claimed)) group.Add(p);
                group.Sort((a, b) => a.Index.CompareTo(b.Index)); // a stable order before the shuffle: the result is the seed's alone
                for (var i = group.Count - 1; i > 0; i--)
                {
                    var j = rng.Next(i + 1);
                    (group[i], group[j]) = (group[j], group[i]);
                }
                foreach (var p in group)
                {
                    if (picks.Count >= row.Count) return picks;
                    var near = false;
                    foreach (var s in picked) if (Math.Abs(s - p.S) < Spacing) { near = true; break; }
                    if (near) continue;
                    picks.Add(p.Index);
                    picked.Add(p.S);
                }
            }
        return picks;
    }

    /// <summary>Shooter <paramref name="k"/> of ride <paramref name="ride"/>'s kind, seeded 50/50: a forge priest or a bandit archer.</summary>
    public static string Kind(int seed, int ride, int k)
    {
        unchecked
        {
            var h = (uint)seed ^ 0xA1C0u ^ ((uint)ride * 0x9E3779B1u) ^ ((uint)k * 0x85EBCA77u);
            h ^= h >> 16; h *= 0x7FEB352Du; h ^= h >> 15; h *= 0x846CA68Bu; h ^= h >> 16;
            return (h & 1) == 0 ? "enemy.forge_priest" : "enemy.bandit_archer";
        }
    }

    /// <summary>
    /// The phase after <paramref name="p"/>, given how far the train's front is before the post (<paramref name="frontToPost"/>, negative once past) and how far its
    /// back is past it (<paramref name="backPastPost"/>, negative while short): done once the back is <see cref="GoneBehind"/> past, from any phase, and for good;
    /// hidden steps out with the front <see cref="WakeAt"/> or less before it. Step-out's end is its state's own timer (<see cref="StepOutSeconds"/>).
    /// </summary>
    public static AlcovePhase Next(AlcovePhase p, float frontToPost, float backPastPost)
    {
        if (p == AlcovePhase.Done || backPastPost >= GoneBehind) return AlcovePhase.Done;
        if (p == AlcovePhase.Hidden && frontToPost <= WakeAt) return AlcovePhase.StepOut;
        return p;
    }
}
