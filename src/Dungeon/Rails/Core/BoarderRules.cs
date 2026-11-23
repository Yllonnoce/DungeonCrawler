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
    /// --test-boarders=reach on ride 2 (it prints it): from a hero's body in a wagon's seat 0 its own rim spot is 1.82 m away on the
    /// straight and 1.82 m in a bend (the sharpest met 0.334/m: the spot rides the same body as the seat); the car ahead's spot comes as
    /// near as 1.75 m and the car behind's 3.96 m, so the distance alone never keeps a blow off the car ahead (the own-car rule does).
    /// This is the own distance + 0.15 (1.97 m), rounded up to 0.05, and under <see cref="TrainRules.Spacing"/> (2.6 m).
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
}
