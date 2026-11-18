namespace DungeonCrawler.Dungeon.Rails;

/// <summary>
/// The cart's fixed-step clock: <see cref="CartPhysics"/> always steps 1/60 s, whatever the physics rate (a headset sets
/// it to its own refresh). Banks each tick's time, says how many steps are due, and where between the last two steps a
/// rendered frame sits, so the pose moves evenly at any rate. Engine-free, no allocation.
/// </summary>
public struct CartClock
{
    public const float FixedStep = 1f / 60f;
    /// <summary>At most this many steps per tick; a longer stall drops the rest rather than catch up.</summary>
    public const int MaxSubSteps = 8;

    /// <summary>Time banked toward the next step, in [0, FixedStep).</summary>
    public float Accumulated { get; private set; }

    public void Reset() => Accumulated = 0f;

    /// <summary>Bank <paramref name="dt"/> and return how many fixed steps to take now.</summary>
    public int Advance(float dt)
    {
        Accumulated += dt;
        var steps = (int)(Accumulated / FixedStep + 1e-4f);
        if (steps > MaxSubSteps) { Accumulated = 0f; return MaxSubSteps; }
        Accumulated = Math.Max(0f, Accumulated - steps * FixedStep);
        return steps;
    }

    /// <summary>
    /// How far a frame <paramref name="frac"/> of the way through a physics tick of <paramref name="tickDt"/> sits between the
    /// state before the last step (0) and the state now (1). Rendering trails the simulation by one fixed step, evenly.
    /// </summary>
    public readonly float Alpha(float frac, float tickDt) => Math.Clamp((Accumulated + frac * tickDt) / FixedStep, 0f, 1f);

    /// <summary>The S to render: <paramref name="from"/> (before the last step) to <paramref name="to"/> (now) by <paramref name="alpha"/>.</summary>
    public static float RenderS(float from, float to, float alpha) => from + (to - from) * alpha;
}
