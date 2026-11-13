using DungeonCrawler.Combat;

namespace DungeonCrawler.Items;

public enum MoveEase { Linear, In, Out, InOut }

/// <summary>
/// One pose of a first-person move, as an offset from where the weapon rests, reached over <see cref="Seconds"/>.
/// <see cref="Up"/> raises the blade (degrees of elevation: + toward upright and on over the shoulder, − down toward
/// the floor ahead); <see cref="Left"/> swings where it points (+ to the left, − to the right); <see cref="Twist"/> turns
/// it about its own length. X is right, Y up, Z back toward the eye (−Z reaches forward), in metres.
/// <see cref="Impact"/> marks the moment the blow lands: the view jolts there for heavy weapons.
/// </summary>
public readonly record struct MoveKey(float Up, float Left, float Twist, float X, float Y, float Z, float Seconds, MoveEase Ease = MoveEase.Out, bool Impact = false)
{
    public bool IsRest => Up == 0 && Left == 0 && Twist == 0 && X == 0 && Y == 0 && Z == 0;
}

/// <summary>Where the hand is at one moment of a move (the same offsets as a <see cref="MoveKey"/>).</summary>
public readonly record struct MovePose(float Up, float Left, float Twist, float X, float Y, float Z)
{
    public static readonly MovePose Rest = new(0, 0, 0, 0, 0, 0);
    public static MovePose Of(MoveKey k) => new(k.Up, k.Left, k.Twist, k.X, k.Y, k.Z);
    public static MovePose Lerp(MovePose a, MovePose b, float f) => new(
        a.Up + (b.Up - a.Up) * f, a.Left + (b.Left - a.Left) * f, a.Twist + (b.Twist - a.Twist) * f,
        a.X + (b.X - a.X) * f, a.Y + (b.Y - a.Y) * f, a.Z + (b.Z - a.Z) * f);
}

/// <summary>
/// A first-person weapon move: poses played in order, the last one back at rest. <see cref="Jolt"/> (0..1) shakes the
/// view at each impact; <see cref="Trail"/> draws a streak behind the blade (skills always, else only legendaries and
/// perfect blows).
/// </summary>
public sealed record WeaponMove(string Name, MoveKey[] Keys, float Jolt = 0f, bool Trail = false)
{
    public float Seconds => Keys.Sum(k => k.Seconds);

    /// <summary>The same move, played faster if it would outlast <paramref name="budget"/> seconds (the time until the next swing).</summary>
    public WeaponMove FitTo(float budget)
    {
        var total = Seconds;
        if (budget <= 0f || total <= budget) return this;
        var f = budget / total;
        return this with { Keys = Keys.Select(k => k with { Seconds = k.Seconds * f }).ToArray() };
    }

    /// <summary>
    /// The pose <paramref name="t"/> seconds in: eased between the pose before and the key being reached, so a sweep
    /// travels through every angle between its two ends (a 0 s key is a jump). Rest before the start and after the end.
    /// </summary>
    public MovePose Sample(float t)
    {
        var prev = MovePose.Rest;
        var at = 0f;
        if (t <= 0f) return prev;
        foreach (var k in Keys)
        {
            if (k.Seconds > 0f && t < at + k.Seconds) return MovePose.Lerp(prev, MovePose.Of(k), WeaponMoves.Ease(k.Ease, (t - at) / k.Seconds));
            at += k.Seconds;
            prev = MovePose.Of(k);
        }
        return prev;
    }

    /// <summary>The moments (seconds in) at which blows land.</summary>
    public IEnumerable<float> ImpactTimes()
    {
        var at = 0f;
        foreach (var k in Keys)
        {
            at += k.Seconds;
            if (k.Impact) yield return at;
        }
    }
}

/// <summary>
/// The library of first-person moves. Each weapon style has its own light swings (two that alternate while you keep
/// swinging), its own heavy swing, and its own way of shooting; each skill has a signature move of its own; spells
/// and shouts lift, thrust or slam the weapon. Light weapons snap, heavy ones wind up long and land with a jolt.
/// A blade resting up and a little to the left: Up −50 lays it level ahead, Up +50 cocks it back past upright,
/// Left −100 lays it out to the right, Left +60 across to the left. Blows end in view above the party bar: sweeps
/// raise the hand, chops stop with the head ahead, thrusts come in on a slant so the shaft shows.
/// </summary>
public static class WeaponMoves
{
    /// <summary>Light swings in a row alternate while each comes within this many seconds of the last one ending.</summary>
    public const float ComboWindow = 0.6f;

    private static MoveKey K(float up, float left, float twist, float x, float y, float z, float s, MoveEase e = MoveEase.Out, bool hit = false)
        => new(up, left, twist, x, y, z, s, e, hit);
    private static MoveKey Rest(float s) => new(0, 0, 0, 0, 0, 0, s, MoveEase.InOut);

    public static float Ease(MoveEase e, float f)
    {
        f = Math.Clamp(f, 0f, 1f);
        return e switch
        {
            MoveEase.Linear => f,
            MoveEase.In => f * f,
            MoveEase.InOut => 0.5f - 0.5f * MathF.Cos(MathF.PI * f),
            _ => 1f - (1f - f) * (1f - f),
        };
    }

    /// <summary>A plain attack: light (<paramref name="combo"/> picks the forehand or the backhand) or heavy. Null for fists.</summary>
    public static WeaponMove? Attack(WeaponStyle style, bool heavy, int combo = 0)
    {
        var b = (combo & 1) == 1;
        return style switch
        {
            WeaponStyle.Dagger => heavy
                ? new("plunge", [K(40, -10, 0, 0, 0.14f, 0.02f, 0.12f), K(-64, -20, 0, -0.05f, 0.05f, -0.2f, 0.07f, MoveEase.In, true), Rest(0.25f)])
                : b ? new("backhand flick", [K(20, -70, 0, 0.05f, 0.05f, 0, 0.06f), K(-50, 40, 0, -0.16f, 0.05f, -0.08f, 0.07f, MoveEase.Out, true), Rest(0.2f)])
                    : new("jab", [K(-40, -10, 0, -0.02f, 0.05f, 0.08f, 0.05f), K(-42, -8, 0, -0.08f, 0.08f, -0.22f, 0.06f, MoveEase.Out, true), Rest(0.2f)]),

            WeaponStyle.Sword => heavy
                ? new("overhead", [K(45, -15, 0, 0.02f, 0.16f, 0.02f, 0.16f), K(-63, -20, 0, -0.04f, 0.05f, -0.14f, 0.09f, MoveEase.In, true), Rest(0.3f)], 0.25f)
                : b ? new("backhand", [K(20, 60, 0, -0.12f, 0.08f, 0, 0.08f), K(-60, -90, 0, 0.1f, 0.05f, -0.1f, 0.09f, MoveEase.Out, true), Rest(0.24f)])
                    : new("forehand", [K(25, -80, 0, 0.08f, 0.08f, 0.02f, 0.08f), K(-60, 55, 0, -0.16f, 0.05f, -0.1f, 0.09f, MoveEase.Out, true), Rest(0.24f)]),

            WeaponStyle.Sabre => heavy
                ? new("double cut", [K(15, -60, 0, 0.05f, 0.04f, 0, 0.06f), K(-60, 45, 0, -0.12f, 0.05f, -0.06f, 0.07f, MoveEase.Out, true), K(-60, -85, 0, 0.08f, 0.05f, -0.1f, 0.08f, MoveEase.Out, true), Rest(0.22f)], 0f, true)
                : b ? new("riposte", [K(-42, -10, 0, 0.02f, 0.04f, 0.06f, 0.05f), K(-44, -8, 0, -0.08f, 0.08f, -0.22f, 0.06f, MoveEase.Out, true), Rest(0.18f)])
                    : new("flick cut", [K(15, -60, 0, 0.05f, 0.04f, 0, 0.05f), K(-60, 45, 0, -0.13f, 0.05f, -0.08f, 0.07f, MoveEase.Out, true), Rest(0.18f)]),

            WeaponStyle.Greatsword => heavy
                ? new("cleaving overhead", [K(50, -15, 0, 0.02f, 0.2f, 0.02f, 0.28f), K(-63, -20, 0, -0.05f, 0.05f, -0.16f, 0.1f, MoveEase.In, true), K(-63, -20, 0, -0.05f, 0.04f, -0.16f, 0.08f, MoveEase.Linear), Rest(0.35f)], 1f)
                : b ? new("return sweep", [K(-32, 75, 0, -0.22f, 0.14f, 0.05f, 0.16f), K(-36, -110, 0, 0.14f, 0.16f, -0.1f, 0.12f, MoveEase.Out, true), Rest(0.35f)], 0.35f)
                    : new("sweep", [K(-32, -110, 0, 0.14f, 0.14f, 0.05f, 0.16f), K(-36, 70, 0, -0.26f, 0.16f, -0.1f, 0.12f, MoveEase.Out, true), Rest(0.35f)], 0.35f),

            WeaponStyle.Axe => heavy
                ? new("wrenching chop", [K(50, -20, 0, 0.02f, 0.17f, 0.02f, 0.18f), K(-63, 0, 0, -0.06f, 0.05f, -0.14f, 0.09f, MoveEase.In, true), K(-62, 0, 40, -0.03f, 0.04f, -0.1f, 0.1f), Rest(0.3f)], 0.5f)
                : b ? new("side hack", [K(-20, -100, 0, 0.12f, 0.1f, 0, 0.1f), K(-45, 50, 0, -0.15f, 0.06f, -0.08f, 0.08f, MoveEase.In, true), Rest(0.26f)], 0.15f)
                    : new("hack", [K(40, -30, 0, 0.04f, 0.14f, 0.02f, 0.1f), K(-63, 0, 0, -0.08f, 0.05f, -0.12f, 0.08f, MoveEase.In, true), Rest(0.26f)], 0.15f),

            WeaponStyle.BattleAxe => heavy
                ? new("whirl", [K(-32, -120, 0, 0.16f, 0.14f, 0.06f, 0.22f), K(-36, 90, 0, -0.3f, 0.16f, -0.12f, 0.16f, MoveEase.InOut, true), Rest(0.4f)], 1f)
                : b ? new("rising cut", [K(-80, 30, 0, -0.06f, -0.04f, 0, 0.16f), K(30, -70, 0, 0.08f, 0.14f, -0.08f, 0.1f, MoveEase.Out, true), Rest(0.34f)], 0.4f)
                    : new("great chop", [K(50, -40, 0, 0.08f, 0.18f, 0.02f, 0.18f), K(-63, 20, 0, -0.12f, 0.05f, -0.14f, 0.1f, MoveEase.In, true), Rest(0.34f)], 0.45f),

            WeaponStyle.Mace => heavy
                ? new("crusher", [K(55, -15, 0, 0.02f, 0.2f, 0.02f, 0.24f), K(-63, -10, 0, -0.03f, 0.05f, -0.14f, 0.08f, MoveEase.In, true), K(-63, -10, 0, -0.03f, 0.04f, -0.13f, 0.08f, MoveEase.Linear), Rest(0.3f)], 0.7f)
                : b ? new("hook", [K(-25, -100, 0, 0.13f, 0.1f, 0.02f, 0.12f), K(-45, 55, 0, -0.16f, 0.06f, -0.08f, 0.08f, MoveEase.In, true), Rest(0.28f)], 0.2f)
                    : new("smash", [K(45, -20, 0, 0.03f, 0.14f, 0.02f, 0.12f), K(-62, 0, 0, -0.05f, 0.05f, -0.12f, 0.07f, MoveEase.In, true), Rest(0.28f)], 0.2f),

            WeaponStyle.WarHammer => heavy
                ? new("earthshaker", [K(55, -10, 0, 0.02f, 0.24f, 0.02f, 0.32f), K(-64, -15, 0, -0.02f, 0.05f, -0.16f, 0.09f, MoveEase.In, true), K(-64, -15, 0, -0.02f, 0.04f, -0.16f, 0.12f, MoveEase.Linear), Rest(0.4f)], 1f)
                : b ? new("side swing", [K(-32, -115, 0, 0.15f, 0.12f, 0.05f, 0.18f), K(-36, 70, 0, -0.24f, 0.14f, -0.1f, 0.12f, MoveEase.In, true), Rest(0.38f)], 0.55f)
                    : new("overhand", [K(50, -15, 0, 0.02f, 0.2f, 0.02f, 0.2f), K(-63, -10, 0, -0.04f, 0.05f, -0.14f, 0.1f, MoveEase.In, true), Rest(0.38f)], 0.6f),

            WeaponStyle.Spear => heavy
                ? new("lunge and twist", [K(-38, -8, 0, 0.05f, 0.04f, 0.2f, 0.16f), K(-40, -6, 0, -0.08f, 0.1f, -0.4f, 0.08f, MoveEase.Out, true), K(-40, -6, 90, -0.08f, 0.1f, -0.38f, 0.08f), Rest(0.3f)], 0.3f)
                : b ? new("high thrust", [K(-30, -6, 0, 0.06f, 0.1f, 0.1f, 0.08f), K(-33, -4, 0, -0.1f, 0.16f, -0.28f, 0.07f, MoveEase.Out, true), Rest(0.24f)])
                    : new("thrust", [K(-38, -8, 0, 0.03f, 0.05f, 0.12f, 0.08f), K(-40, -6, 0, -0.07f, 0.1f, -0.3f, 0.07f, MoveEase.Out, true), Rest(0.24f)]),

            WeaponStyle.Glaive => heavy
                ? new("reaping arc", [K(50, -50, 0, 0.06f, 0.2f, 0.02f, 0.24f), K(-63, 30, 0, -0.15f, 0.05f, -0.15f, 0.12f, MoveEase.In, true), Rest(0.38f)], 0.8f)
                : b ? new("long thrust", [K(-38, -8, 0, 0.05f, 0.04f, 0.16f, 0.12f), K(-40, -6, 0, -0.08f, 0.1f, -0.36f, 0.08f, MoveEase.Out, true), Rest(0.3f)], 0.2f)
                    : new("sweep", [K(-30, -115, 0, 0.16f, 0.14f, 0.05f, 0.14f), K(-36, 70, 0, -0.26f, 0.16f, -0.12f, 0.12f, MoveEase.Out, true), Rest(0.32f)], 0.3f),

            WeaponStyle.Staff => heavy
                ? new("sweep and bash", [K(-30, -100, 0, 0.12f, 0.12f, 0.04f, 0.12f), K(-35, 60, 0, -0.2f, 0.14f, -0.08f, 0.1f, MoveEase.Out, true), K(45, -10, 0, 0, 0.16f, 0.02f, 0.12f), K(-62, -10, 0, -0.03f, 0.05f, -0.12f, 0.08f, MoveEase.In, true), Rest(0.25f)], 0.3f)
                : b ? new("swat", [K(-20, -90, 0, 0.1f, 0.08f, 0, 0.1f), K(-45, 50, 0, -0.15f, 0.06f, -0.08f, 0.09f, MoveEase.Out, true), Rest(0.25f)])
                    : new("poke", [K(-38, -8, 0, 0, 0.03f, 0.07f, 0.08f), K(-40, -6, 0, -0.04f, 0.08f, -0.22f, 0.08f, MoveEase.Out, true), Rest(0.24f)]),

            WeaponStyle.Wand => heavy
                ? new("lunge", [K(-38, -8, 0, 0, 0.02f, 0.08f, 0.1f), K(-42, -6, 0, -0.04f, 0.08f, -0.24f, 0.07f, MoveEase.Out, true), Rest(0.2f)])
                : b ? new("flick back", [K(10, -60, 0, 0.04f, 0.03f, 0, 0.05f), K(-50, 30, 0, -0.06f, 0.05f, -0.1f, 0.06f, MoveEase.Out, true), Rest(0.16f)])
                    : new("flick", [K(20, -20, 0, 0, 0.02f, 0.02f, 0.05f), K(-60, -10, 0, -0.04f, 0.03f, -0.12f, 0.06f, MoveEase.Out, true), Rest(0.16f)]),

            WeaponStyle.Bow => heavy
                ? new("full draw", [K(0, 0, 0, 0.06f, 0.02f, 0.16f, 0.24f), K(1, 0, 1, 0.06f, 0.025f, 0.17f, 0.06f, MoveEase.Linear), K(-2, 0, 0, -0.02f, 0, -0.05f, 0.04f, MoveEase.Out, true), Rest(0.25f)], 0.15f)
                : new("loose", [K(0, 0, 0, 0.05f, 0.02f, 0.13f, 0.12f), K(0, 0, 0, -0.01f, 0, -0.03f, 0.04f, MoveEase.Out, true), Rest(0.22f)]),

            WeaponStyle.Crossbow => heavy
                ? new("braced shot", [K(-3, 0, 0, 0, -0.02f, 0.03f, 0.1f), K(20, 0, 0, 0, 0.04f, 0.14f, 0.04f, MoveEase.Out, true), Rest(0.36f)], 0.6f)
                : new("bolt", [K(-3, 0, 0, 0, 0, 0, 0.04f), K(14, 0, 0, 0, 0.03f, 0.1f, 0.04f, MoveEase.Out, true), Rest(0.3f)], 0.4f),

            _ => null,
        };
    }

    /// <summary>
    /// A skill or a spell: melee skills have a signature move whatever the weapon; bow skills draw and loose (a
    /// crossbow kicks instead); spells thrust, call down or slam the weapon; shouts and blessings lift it.
    /// </summary>
    public static WeaponMove? Skill(string abilityId, AbilityKind kind, WeaponStyle style)
    {
        if (style == WeaponStyle.Fists) return null;
        if (kind == AbilityKind.WeaponAttack) return Attack(style, false);
        if (kind == AbilityKind.Projectile && WeaponStyles.IsRanged(style)) return Shot(abilityId, style);
        return abilityId switch
        {
            "ability.cleave" => new("cleave", [K(-32, -120, 0, 0.2f, 0.14f, 0.05f, 0.14f), K(-36, 95, 0, -0.35f, 0.16f, -0.12f, 0.14f, MoveEase.InOut, true), Rest(0.32f)], 0.6f, true),
            "ability.backstab" => new("backstab", [K(-50, -8, 0, 0.05f, -0.12f, 0.08f, 0.12f), K(-30, -6, 0, -0.06f, 0.1f, -0.3f, 0.07f, MoveEase.Out, true), K(-30, -6, 90, -0.06f, 0.1f, -0.28f, 0.08f), Rest(0.25f)], 0.3f, true),
            "ability.venom_strike" => new("venom strike", [K(-40, -6, 0, -0.04f, 0.06f, -0.2f, 0.05f, MoveEase.Out, true), K(-38, -10, 0, 0.02f, 0.03f, 0.02f, 0.05f), K(-42, -14, 0, -0.08f, 0.08f, -0.22f, 0.05f, MoveEase.Out, true), Rest(0.2f)], 0f, true),
            "ability.shield_bash" => new("shield bash", [K(-30, -80, 0, 0.2f, -0.14f, 0.12f, 0.1f), K(-30, -80, 0, 0.14f, -0.12f, -0.04f, 0.07f, MoveEase.Out, true), Rest(0.3f)], 1f, false),
            "ability.fan_of_knives" => new("fan of knives", [K(-60, 45, 0, -0.1f, 0.05f, -0.06f, 0.06f, MoveEase.Out, true), K(-60, -85, 0, 0.08f, 0.05f, -0.06f, 0.07f, MoveEase.Out, true), K(-50, -24, 0, 0, 0.05f, -0.12f, 0.05f, MoveEase.Out, true), Rest(0.22f)], 0f, true),
            "ability.smoke_bomb" => new("smoke bomb", [K(-20, 0, 0, 0, -0.15f, 0.05f, 0.1f), K(-40, 0, 0, 0, 0.05f, -0.1f, 0.08f, MoveEase.Out, true), Rest(0.2f)]),
            "ability.meteor" => new("call down", [K(35, -20, 0, 0, 0.25f, 0, 0.2f), K(37, -20, 0, 0, 0.27f, 0, 0.1f, MoveEase.Linear), K(-60, -20, 0, 0, 0.05f, -0.15f, 0.1f, MoveEase.In, true), Rest(0.3f)], 0.8f, true),
            "ability.war_cry" or "ability.rally" => new("rallying pump", [K(35, -20, 0, 0, 0.22f, 0, 0.12f, MoveEase.Out, true), K(20, -20, 0, 0, 0.12f, 0, 0.08f), K(35, -20, 0, 0, 0.24f, 0, 0.1f, MoveEase.Out, true), Rest(0.3f)], 0.3f),
            "ability.iron_skin" => new("brace", [K(-35, 60, 0, -0.08f, 0.05f, 0.05f, 0.12f, MoveEase.Out, true), K(-35, 60, 0, -0.08f, 0.04f, 0.05f, 0.3f, MoveEase.Linear), Rest(0.25f)], 0.2f),
            _ => kind switch
            {
                AbilityKind.MeleeStrike => Attack(style, true) is { } h ? h with { Trail = true } : null,
                AbilityKind.Projectile => new("cast", [K(15, -20, 0, 0, 0.04f, 0.08f, 0.1f), K(-45, -24, 0, -0.05f, 0.08f, -0.2f, 0.08f, MoveEase.Out, true), K(-45, -24, 0, -0.05f, 0.08f, -0.2f, 0.06f, MoveEase.Linear), Rest(0.25f)], 0f, true),
                AbilityKind.Nova => new("slam", [K(30, -20, 0, 0, 0.2f, 0, 0.14f), K(-30, -20, 0, 0, 0.05f, -0.1f, 0.08f, MoveEase.In, true), Rest(0.3f)], 0.5f, true),
                _ => new("lift", [K(30, -20, 0, 0, 0.18f, 0.02f, 0.18f, MoveEase.Out, true), K(30, -20, 0, 0, 0.19f, 0.02f, 0.15f, MoveEase.Linear), Rest(0.3f)]),
            },
        };
    }

    /// <summary>A bow skill: power shot draws long, multishot looses three, poison arrow dips the head first.</summary>
    private static WeaponMove Shot(string abilityId, WeaponStyle style)
    {
        if (style == WeaponStyle.Crossbow)
            return abilityId == "ability.multishot"
                ? new("bolt volley", [K(14, -6, 0, 0, 0.03f, 0.1f, 0.04f, MoveEase.Out, true), K(0, 0, 0, 0, 0, 0, 0.08f), K(14, 0, 0, 0, 0.03f, 0.1f, 0.04f, MoveEase.Out, true), K(0, 0, 0, 0, 0, 0, 0.08f), K(14, 6, 0, 0, 0.03f, 0.1f, 0.04f, MoveEase.Out, true), Rest(0.3f)], 0.35f, true)
                : (Attack(style, abilityId == "ability.power_shot")! with { Trail = true });
        return abilityId switch
        {
            "ability.power_shot" => new("power shot", [K(0, 0, 0, 0.07f, 0.02f, 0.18f, 0.28f), K(1, 0, 1, 0.07f, 0.025f, 0.19f, 0.08f, MoveEase.Linear), K(-4, 0, 0, -0.03f, 0, -0.07f, 0.04f, MoveEase.Out, true), Rest(0.25f)], 0.35f, true),
            "ability.multishot" => new("multishot", [K(0, 0, 0, 0.05f, 0.02f, 0.13f, 0.12f), K(0, -8, 0, -0.01f, 0, -0.03f, 0.04f, MoveEase.Out, true), K(0, 0, 0, 0.03f, 0.01f, 0.06f, 0.05f), K(0, 0, 0, -0.01f, 0, -0.03f, 0.04f, MoveEase.Out, true), K(0, 0, 0, 0.03f, 0.01f, 0.06f, 0.05f), K(0, 8, 0, -0.01f, 0, -0.03f, 0.04f, MoveEase.Out, true), Rest(0.22f)], 0f, true),
            "ability.poison_arrow" => new("poisoned shot", [K(-10, 0, 0, 0, -0.08f, 0.05f, 0.1f), K(0, 0, 0, 0.05f, 0.02f, 0.13f, 0.12f), K(0, 0, 0, -0.01f, 0, -0.03f, 0.04f, MoveEase.Out, true), Rest(0.22f)], 0f, true),
            _ => new("quick shot", [K(0, 0, 0, 0.04f, -0.02f, 0.1f, 0.08f), K(0, 0, 0, -0.01f, -0.02f, -0.03f, 0.04f, MoveEase.Out, true), Rest(0.2f)], 0f, true),
        };
    }

    /// <summary>A parry: the blade flicked out across the body, the same for every weapon.</summary>
    public static WeaponMove Parry { get; } = new("parry", [K(-30, 70, 0, 0.06f, 0.03f, -0.08f, 0.08f, MoveEase.Out, true), Rest(0.17f)]);

    /// <summary>The guard pose held while blocking: the blade laid across the body.</summary>
    public static MovePose Guard { get; } = new(-35, 60, 0, -0.04f, 0, 0.02f);

    /// <summary>
    /// Which light swing comes next: alternates while each swing starts within <see cref="ComboWindow"/> seconds of the
    /// previous one ending, else starts over on the forehand.
    /// </summary>
    public static int NextCombo(int previous, float secondsSincePreviousEnded)
        => secondsSincePreviousEnded <= ComboWindow ? previous + 1 : 0;
}

/// <summary>Colours of the streak behind a blade, as linear RGB.</summary>
public readonly record struct TrailColor(float R, float G, float B)
{
    public static readonly TrailColor Steel = new(0.85f, 0.88f, 0.95f);
    public static readonly TrailColor Gold = new(1f, 0.8f, 0.35f);

    /// <summary>A skill's streak: fire orange, frost blue, venom green, shadow violet, blood red; steel for the rest.</summary>
    public static TrailColor ForSkill(string abilityId) => abilityId switch
    {
        "ability.fireball" or "ability.meteor" => new(1f, 0.5f, 0.15f),
        "ability.frostbolt" or "ability.frost_nova" or "ability.chain_frost" => new(0.45f, 0.75f, 1f),
        "ability.venom_strike" or "ability.poison_arrow" => new(0.4f, 0.95f, 0.3f),
        "ability.backstab" or "ability.smoke_bomb" or "ability.fan_of_knives" => new(0.7f, 0.45f, 1f),
        "ability.cleave" => new(1f, 0.3f, 0.25f),
        _ => Steel,
    };

    /// <summary>A legendary's glow and streak, from what its blows inflict: gold when they inflict nothing.</summary>
    public static TrailColor ForStatus(StatusKind? status) => status switch
    {
        StatusKind.Burn => new(1f, 0.5f, 0.15f),
        StatusKind.Frost or StatusKind.Slow => new(0.45f, 0.75f, 1f),
        StatusKind.Poison => new(0.4f, 0.95f, 0.3f),
        StatusKind.Bleed => new(0.95f, 0.2f, 0.2f),
        StatusKind.Stun => new(1f, 0.95f, 0.5f),
        _ => Gold,
    };
}
