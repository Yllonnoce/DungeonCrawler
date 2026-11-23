namespace DungeonCrawler.Dungeon.Rails;

/// <summary>What a wagon's <see cref="ThrowClock"/> tells this tick: nothing, the call (an alone stretch begins), the glow before a throw, or the throw.</summary>
public enum ThrowStep { None, Call, Glow, Throw }

/// <summary>
/// A boarder alone with the prisoners (plan 14 Task 5, spec A.4): one clock per wagon. It counts only while the wagon is alone
/// (<see cref="BoarderRules.Alone"/>) and moves are not refused for everyone; refused (<paramref name="paused"/> in <see cref="Step"/>) it keeps
/// its count. An alone stretch starts with <see cref="ThrowStep.Call"/>, which counts no time; <see cref="BoarderRules.ThrowGlow"/> before each throw
/// it gives <see cref="ThrowStep.Glow"/>; the throws come at <see cref="BoarderRules.ThrowFirst"/> and each <see cref="BoarderRules.ThrowEvery"/> after.
/// Leaving alone (a hero up in the wagon, the boarder gone, the crew gone, the train held) resets it to nought, so the next stretch has the full
/// <see cref="BoarderRules.ThrowFirst"/> again. Engine-free.
/// </summary>
public sealed class ThrowClock
{
    /// <summary>The seconds counted in this alone stretch (paused time not counted).</summary>
    public float Running { get; private set; }
    /// <summary>The prisoners thrown in this alone stretch.</summary>
    public int Thrown { get; private set; }
    /// <summary>The wagon was alone at the last step.</summary>
    public bool Alone { get; private set; }
    /// <summary>The seconds this stretch has been paused (moves refused while alone): the throw's print gives them.</summary>
    public float Paused { get; private set; }

    /// <summary>The count at which the next throw comes: <see cref="BoarderRules.ThrowFirst"/> + <see cref="Thrown"/> × <see cref="BoarderRules.ThrowEvery"/>.</summary>
    public float NextThrowAt => BoarderRules.ThrowFirst + Thrown * BoarderRules.ThrowEvery;

    /// <summary>One tick of <paramref name="dt"/> seconds: what to do now (see the class).</summary>
    public ThrowStep Step(float dt, bool alone, bool paused)
    {
        if (!alone)
        {
            Alone = false;
            Running = 0f;
            Thrown = 0;
            Paused = 0f;
            return ThrowStep.None;
        }
        if (!Alone)
        {
            Alone = true;
            Running = 0f;
            Thrown = 0;
            Paused = 0f;
            return ThrowStep.Call;
        }
        if (paused)
        {
            Paused += dt;
            return ThrowStep.None;
        }
        var was = Running;
        Running += dt;
        var throwAt = NextThrowAt;
        if (was < throwAt && Running >= throwAt)
        {
            Thrown++;
            return ThrowStep.Throw;
        }
        var glowAt = throwAt - BoarderRules.ThrowGlow;
        return was < glowAt && Running >= glowAt ? ThrowStep.Glow : ThrowStep.None;
    }
}
