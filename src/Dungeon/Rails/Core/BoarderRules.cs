using DungeonCrawler.Combat;

namespace DungeonCrawler.Dungeon.Rails;

/// <summary>
/// Fighting from the train (plan 14, Rulings 13-15). Cars are numbered as <see cref="RailController.CarOf"/> numbers them: 0 the party's
/// cart, k the coupled wagon Wk, −1 none. A seated hero's melee lands on a train boarder (a hound carried by the cart or a coupled wagon)
/// only when that boarder rides the hero's own car (<see cref="Strikes"/>), out to <see cref="WagonReach"/>; on foot, and for shots and
/// spells, nothing changes. A wagon boarder has a grip, as a barricade has its toughness: <see cref="Grip"/> of the party's hits, weighed
/// as <see cref="BarricadeRules.HitWeight"/> weighs them (a spell all of it), knock it off alive (<see cref="KnockedOff"/>); only a
/// party member's hit counts (<see cref="Counts"/>). A wagon with a boarder, crew and no hero up needs a companion's answer
/// (<see cref="NeedsAnswer"/>). Engine-free.
/// </summary>
public static class BoarderRules
{
    /// <summary>
    /// A seated blow reaches its own car's rim boarder this far (body to body), and the brain's melee stretches to it. Measured by
    /// --test-boarders=reach on ride 2 (it prints both): a wagon's seat 0 to its own rim spot, read off the wagon's pose, is 1.79 m and never
    /// changes (straight, or the sharpest bend met, 0.334/m: the spot rides the same body as the seat). The seated body and its hound sampled in
    /// a rendered frame span 1.80–1.85 m: one of the two is already posed for the frame while the other still stands where the physics tick
    /// left it (the 1.82 and 1.84 m earlier runs printed). The car ahead's spot comes as near as 1.75–1.77 m and the car behind's 3.66–3.96 m,
    /// so the distance alone never keeps a blow off the car ahead (the own-car rule does). The own spot + 0.15 (1.94 m) rounds up to 1.95; it is
    /// kept at 2.0 so the widest body sample + 0.15 (2.00 m) is covered too, and it is under <see cref="TrainRules.Spacing"/> (2.6 m).
    /// </summary>
    public const float WagonReach = 2.0f;

    /// <summary>A wagon boarder's grip: the hits that knock it off (a spell's one is worth all of them).</summary>
    public const int Grip = BarricadeRules.Toughness;

    /// <summary>How much of <see cref="Grip"/> one hit of <paramref name="kind"/> takes: a spell all of it, anything else one.</summary>
    public static int GripTaken(AttackKind kind) => BarricadeRules.HitWeight(kind);

    /// <summary>The grip is gone: <paramref name="taken"/> has reached <see cref="Grip"/>.</summary>
    public static bool KnockedOff(int taken) => taken >= Grip;

    /// <summary>
    /// A blow by a striker in car <paramref name="strikerCar"/> lands on a boarder in car <paramref name="boarderCar"/> (−1: not a train boarder):
    /// always on foot, always on a body no car of the train carries, else only on its own car's.
    /// </summary>
    public static bool Strikes(bool strikerSeatedInTrain, int strikerCar, int boarderCar)
        => !strikerSeatedInTrain || boarderCar < 0 || strikerCar == boarderCar;

    /// <summary>A hit counts toward the grip: a party member struck it (a player, a companion, a remote player), not thorns or a burn's tick.</summary>
    public static bool Counts(int byMember) => byMember >= 0;

    // ---- a boarder alone with the prisoners (plan 14 Task 5, spec A.4, Rulings 16-17) ----------------------------------------------

    /// <summary>A boarder alone in a wagon throws its first prisoner off after this long counted alone (<see cref="ThrowClock"/>).</summary>
    public const float ThrowFirst = 6f;
    /// <summary>And one more after each of these.</summary>
    public const float ThrowEvery = 4f;
    /// <summary>It crouches over the crew with a glow (the existing hazard warning) this long before each throw.</summary>
    public const float ThrowGlow = 1.5f;
    /// <summary>A thrown prisoner tumbles beside the lane this long, then is gone (hidden, never a corpse).</summary>
    public const float ThrownHide = 1.2f;

    /// <summary>
    /// A wagon is alone (A.4): a boarder aboard, at least one crew member seated in it, no hero UP in it (a downed hero does not count), and the
    /// train rolling.
    /// </summary>
    public static bool Alone(bool boarderAboard, int crewAboard, int upHeroesAboard, bool rolling) => boarderAboard && crewAboard > 0 && upHeroesAboard == 0 && rolling;

    /// <summary>The crew seat a throw empties: the rear bench seat 5 first, then 4; null when neither holds a crew member.</summary>
    public static int? ThrowSeat(bool seat4Taken, bool seat5Taken) => seat5Taken ? 5 : seat4Taken ? 4 : null;

    /// <summary>A wagon needs an answer (Ruling 15): a boarder aboard, crew seated in it, and no hero up in it.</summary>
    public static bool NeedsAnswer(bool boarderAboard, int crewAboard, int upHeroesAboard) => boarderAboard && crewAboard > 0 && upHeroesAboard == 0;

    /// <summary>
    /// One seated, up companion (or hero bot) not already answering, as the answer's choice sees it: its party order, its car, whether it is a
    /// blade (no shot of its own, a blow to strike with), whether it is a hero bot, whether it has a clear shot at the wagon's boarder now (what
    /// the brain would fire at it alone: <c>CompanionBrain.HasShot</c>), and whether it is pumping the cart (fix round 2: a pumper holds its fire,
    /// so it never answers from its seat).
    /// </summary>
    public readonly record struct AnswerCandidate(int Order, int Car, bool Blade, bool Bot, bool HasShot, bool Pumping = false);

    /// <summary>A seat answerer may be without its shot this long (a line of sight that flickers) before its answer ends (fix round 2).</summary>
    public const float ShotGrace = 0.5f;

    /// <summary>
    /// A seat answer goes on (fix round 2): its answerer is up, still run by the companion AI (no player took it over), not pumping, and has been
    /// without its shot no longer than <see cref="ShotGrace"/> (<paramref name="shotLostFor"/>, 0 while it has one). Else it ends that tick and
    /// the wagon is answered afresh.
    /// </summary>
    public static bool SeatAnswerHolds(bool up, bool companion, bool pumping, double shotLostFor) => up && companion && !pumping && shotLostFor <= ShotGrace;

    /// <summary>
    /// Who answers wagon <paramref name="wagon"/> (Ruling 15 as amended in Task 4's fix rounds): a candidate with a clear shot, not pumping, answers from its
    /// seat (the nearest by cars, then the nearer the cart, then party order); only when none has one does an answerer move: the nearest blade or
    /// hero bot, a tie by cars going to a blade, then the nearer the cart, then party order. Null when nobody may.
    /// </summary>
    public static (int Order, bool FromSeat)? PickAnswerer(int wagon, IReadOnlyList<AnswerCandidate> candidates)
    {
        AnswerCandidate? shot = null, mover = null;
        foreach (var c in candidates)
        {
            if (c.HasShot && !c.Pumping && (shot is not { } s || Nearer(c, s, wagon, blades: false))) shot = c;
            if ((c.Blade || c.Bot) && (mover is not { } m || Nearer(c, m, wagon, blades: true))) mover = c;
        }
        if (shot is { } byShot) return (byShot.Order, true);
        if (mover is { } byMove) return (byMove.Order, false);
        return null;
    }

    private static bool Nearer(AnswerCandidate a, AnswerCandidate b, int wagon, bool blades)
    {
        int da = Math.Abs(a.Car - wagon), db = Math.Abs(b.Car - wagon);
        if (da != db) return da < db;
        if (blades && a.Blade != b.Blade) return a.Blade;
        if (a.Car != b.Car) return a.Car < b.Car;
        return a.Order < b.Order;
    }
}
