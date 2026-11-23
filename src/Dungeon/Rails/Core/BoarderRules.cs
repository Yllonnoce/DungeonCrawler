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

    /// <summary>A wagon needs an answer (Ruling 15): a boarder aboard, crew seated in it, and no hero up in it.</summary>
    public static bool NeedsAnswer(bool boarderAboard, int crewAboard, int upHeroesAboard) => boarderAboard && crewAboard > 0 && upHeroesAboard == 0;

    /// <summary>
    /// One seated, up companion (or hero bot) not already answering, as the answer's choice sees it: its party order, its car, whether it fights
    /// with a blade (no shot or spell of its own), whether it is a hero bot, and whether it has a clear shot at the wagon's boarder now
    /// (range and line of sight, as the brain's own test for a ranged target).
    /// </summary>
    public readonly record struct AnswerCandidate(int Order, int Car, bool Blade, bool Bot, bool HasShot);

    /// <summary>
    /// Who answers wagon <paramref name="wagon"/> (Ruling 15 as amended in Task 4's fix round): a candidate with a clear shot answers from its
    /// seat (the nearest by cars, then the nearer the cart, then party order); only when none has one does an answerer move: the nearest blade or
    /// hero bot, a tie by cars going to a blade, then the nearer the cart, then party order. Null when nobody may.
    /// </summary>
    public static (int Order, bool FromSeat)? PickAnswerer(int wagon, IReadOnlyList<AnswerCandidate> candidates)
    {
        AnswerCandidate? shot = null, mover = null;
        foreach (var c in candidates)
        {
            if (c.HasShot && (shot is not { } s || Nearer(c, s, wagon, blades: false))) shot = c;
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
