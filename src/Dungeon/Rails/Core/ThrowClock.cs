namespace DungeonCrawler.Dungeon.Rails;

/// <summary>What a wagon's <see cref="ThrowClock"/> tells this tick: nothing, the call (an alone stretch begins), the glow before a throw, or the throw.</summary>
public enum ThrowStep { None, Call, Glow, Throw }

/// <summary>
/// A boarder alone with the prisoners (plan 14 Task 5, spec A.4): one clock per wagon. It counts only while the wagon is alone
/// (<see cref="BoarderRules.Alone"/>) and moves are not refused for everyone; refused (<paramref name="paused"/> in <see cref="Step"/>) it keeps
/// its count. An alone stretch starts with <see cref="ThrowStep.Call"/>, which counts no time; <see cref="BoarderRules.ThrowGlow"/> before each throw
/// it gives <see cref="ThrowStep.Glow"/>; the throws come at <see cref="BoarderRules.ThrowFirst"/> and each <see cref="BoarderRules.ThrowEvery"/> after.
/// Leaving alone (a hero up in the wagon, the boarder gone, the crew gone, the train held) resets it to nought, so the next stretch has the full
/// <see cref="BoarderRules.ThrowFirst"/> again. A throw never comes without a glow since the clock last resumed (the final review's M4, the owner's
/// "a warning first"): a throw that falls due after a pause with its glow before that pause gives <see cref="ThrowStep.Glow"/> instead, and the throw
/// follows <see cref="BoarderRules.ThrowGlow"/> later (the throws after it keep <see cref="BoarderRules.ThrowEvery"/> from it). Engine-free.
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

    /// <summary>The glow told again before a throw due after a pause put the throws back this many seconds of count (M4).</summary>
    public float PutBack { get; private set; }
    /// <summary>The last <see cref="ThrowStep.Glow"/> was told again for a throw that fell due after a pause (M4), not at its own time.</summary>
    public bool GlowRetold { get; private set; }

    /// <summary>The clock was paused at its last step (a resume follows).</summary>
    private bool _wasPaused;
    /// <summary>The clock resumed since the last glow: the next throw due glows first (M4).</summary>
    private bool _glowStale;

    /// <summary>
    /// The count at which the next throw comes: <see cref="BoarderRules.ThrowFirst"/> + <see cref="Thrown"/> × <see cref="BoarderRules.ThrowEvery"/>,
    /// plus <see cref="PutBack"/>.
    /// </summary>
    public float NextThrowAt => BoarderRules.ThrowFirst + Thrown * BoarderRules.ThrowEvery + PutBack;

    /// <summary>One tick of <paramref name="dt"/> seconds: what to do now (see the class).</summary>
    public ThrowStep Step(float dt, bool alone, bool paused)
    {
        if (!alone)
        {
            Alone = false;
            Reset();
            return ThrowStep.None;
        }
        if (!Alone)
        {
            Alone = true;
            Reset();
            return ThrowStep.Call;
        }
        if (paused)
        {
            Paused += dt;
            _wasPaused = true;
            return ThrowStep.None;
        }
        if (_wasPaused)
        {
            _wasPaused = false;
            _glowStale = true; // a glow before the pause has gone out: the next throw needs one of its own
        }
        var was = Running;
        Running += dt;
        var throwAt = NextThrowAt;
        if (was < throwAt && Running >= throwAt)
        {
            if (_glowStale)
            {
                _glowStale = false;
                PutBack += Running - throwAt + BoarderRules.ThrowGlow; // the throw comes ThrowGlow after this glow
                GlowRetold = true;
                return ThrowStep.Glow;
            }
            Thrown++;
            return ThrowStep.Throw;
        }
        var glowAt = throwAt - BoarderRules.ThrowGlow;
        if (was >= glowAt || Running < glowAt) return ThrowStep.None;
        _glowStale = false;
        GlowRetold = false;
        return ThrowStep.Glow;
    }

    private void Reset()
    {
        Running = 0f;
        Thrown = 0;
        Paused = 0f;
        PutBack = 0f;
        GlowRetold = false;
        _wasPaused = false;
        _glowStale = false;
    }
}
