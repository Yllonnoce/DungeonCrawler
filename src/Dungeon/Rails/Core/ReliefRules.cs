namespace DungeonCrawler.Dungeon.Rails;

/// <summary>
/// Where a relief cart is in its life: rolling down to the player left behind (<see cref="Coming"/>), standing beside them
/// (<see cref="Waiting"/>, up to <see cref="ReliefRules.WaitBeside"/>), carrying them up behind the train (<see cref="Riding"/>: its
/// countdown, its run, a stand at a heap face or a lip), or rolling on empty after the train (<see cref="Leaving"/>).
/// </summary>
public enum ReliefPhase : byte { Coming, Waiting, Riding, Leaving }

/// <summary>
/// What the left-behind rule reads (<see cref="ReliefRules.LeftBehind"/>): single player; the line still runs (<see cref="ReliefRules.LineLive"/>);
/// the local player is up (not downed, not parked) and seated in no cart; the party cart stands (not rolling) and has met them since it
/// stopped (<see cref="ReliefRules.Met"/>); and how far the train's last body's back is ahead of them along the line (m, negative behind them).
/// </summary>
public readonly record struct ReliefView(bool Solo, bool LineLive, bool PlayerUp, bool PlayerSeated, bool CartStanding, bool Met, float Behind);

/// <summary>
/// The relief cart (plan 7, the spec's "The relief cart"): single player, a player left behind by the train gets the co-op chaser sent
/// to them <see cref="FirstWait"/> later, from the stop behind them. It stops beside them (or past them, having run them over on the
/// rails), waits <see cref="WaitBeside"/>, and rolls on after the train empty if nobody boards; the next comes <see cref="NextWait"/>
/// after that one set off. Boarded, it runs up behind the train as the chaser does. Engine-free; RailController.Relief calls it.
/// </summary>
public static class ReliefRules
{
    /// <summary>A relief comes this long after the player is left behind.</summary>
    public const float FirstWait = 15f;
    /// <summary>An unboarded relief waits beside the player this long, then rolls on.</summary>
    public const float WaitBeside = 30f;
    /// <summary>The next relief comes this long after the last one rolled on without the player.</summary>
    public const float NextWait = 15f;
    /// <summary>The player is left behind once the train's last body's back is more than this far ahead of them (m).</summary>
    public const float LeftBehindRange = 12f;
    /// <summary>A player this close past the relief's spawn (m along the line) has it wait where it was placed.</summary>
    public const float WaitAtStopRange = 6f;
    /// <summary>A relief that ran a player on the rails over stops with its back this far past them (m).</summary>
    public const float PastRunOver = 1.5f;
    /// <summary>A relief is placed with its front at least this far short of the player's body (m).</summary>
    public const float SpawnClear = 1f;
    /// <summary>A body's radius on the line (m). It mirrors <c>RunOverSweep.BodyRadius</c> on the Godot side.</summary>
    public const float BodyRadius = 0.4f;

    /// <summary>
    /// The line still runs for a relief to chase: at least two stops, the party cart not hidden (a wreck no spare replaced) and not
    /// Wrecked, and not Held, unhalted, at the last stop (the line is done).
    /// </summary>
    public static bool LineLive(int stopCount, int stopIndex, CartMode mode, bool halted, bool hidden)
        => stopCount >= 2 && !hidden && mode != CartMode.Wrecked && !(mode == CartMode.Held && !halted && stopIndex >= stopCount - 1);

    /// <summary>How far the train's last body's back (<paramref name="lastBackS"/>) is ahead of the player (<paramref name="playerS"/>).</summary>
    public static float Behind(float lastBackS, float playerS) => lastBackS - playerS;

    /// <summary>
    /// The standing cart has met the player since it stopped: it waits for them. Cleared while the cart rolls; set while it stands
    /// with the player seated in it or within <see cref="LeftBehindRange"/> of the train's back; kept while it stands.
    /// </summary>
    public static bool Met(bool met, bool cartRolling, bool seatedInParty, float behind)
        => !cartRolling && (met || seatedInParty || behind <= LeftBehindRange);

    /// <summary>
    /// The player is left behind: solo, the line live, up on foot (seated in no cart), the train's back more than
    /// <see cref="LeftBehindRange"/> ahead of them, and not a standing cart that has met them (it waits where they left it).
    /// </summary>
    public static bool LeftBehind(in ReliefView v)
        => v.Solo && v.LineLive && v.PlayerUp && !v.PlayerSeated && !(v.CartStanding && v.Met) && v.Behind > LeftBehindRange;

    /// <summary>The relief's stop: the last stop at or behind the player (the first stop when they stand before it). No allocation.</summary>
    public static int SpawnStop(IReadOnlyList<float> stopS, float playerS)
    {
        var stop = 0;
        for (var i = 1; i < stopS.Count; i++)
            if (stopS[i] <= playerS) stop = i;
        return stop;
    }

    /// <summary>
    /// Where the relief is placed (its centre S): at its stop, but with its front at least <see cref="SpawnClear"/> short of the player's
    /// body, never past the chase limit (behind the train), and never before the line's start.
    /// </summary>
    public static float SpawnS(float stopS, float playerS, float chaseLimit)
        => Math.Max(0f, Math.Min(Math.Min(stopS, playerS - CartRules.HalfLength - BodyRadius - SpawnClear), chaseLimit));

    /// <summary>
    /// Where the relief stops for the player (its centre S): where it was placed when they stand within <see cref="WaitAtStopRange"/>
    /// of it; else beside them, or with its back <see cref="PastRunOver"/> past them when they stand on the rails (it ran them over).
    /// </summary>
    public static float SeatS(float spawnS, float playerS, bool onRails)
        => playerS - spawnS <= WaitAtStopRange ? spawnS
            : onRails ? playerS + CartRules.HalfLength + BodyRadius + PastRunOver : playerS;

    /// <summary>The relief's stop point only moves forward: a player walking back never pulls it back.</summary>
    public static float Target(float current, float seatS) => Math.Max(current, seatS);

    /// <summary>The coming relief's limit: its seat, never past the chase limit, and pulled back short of a gap or trestle span it would stand over (its front at the near lip).</summary>
    public static float SeatLimit(float limit, float target, IReadOnlyList<(float From, float To)> holes)
        => SpareRules.HoleLimit(Math.Min(limit, target), holes);

    /// <summary>
    /// The relief's next phase: Coming waits once it stands; Waiting rides once the player is aboard, or leaves after
    /// <see cref="WaitBeside"/>; Riding waits again when it stands with no player in it; Leaving stays Leaving.
    /// </summary>
    public static ReliefPhase Next(ReliefPhase phase, bool held, bool playerAboard, float waited) => phase switch
    {
        ReliefPhase.Coming => held ? ReliefPhase.Waiting : ReliefPhase.Coming,
        ReliefPhase.Waiting => playerAboard ? ReliefPhase.Riding : waited >= WaitBeside ? ReliefPhase.Leaving : ReliefPhase.Waiting,
        ReliefPhase.Riding => held && !playerAboard ? ReliefPhase.Waiting : ReliefPhase.Riding,
        _ => ReliefPhase.Leaving,
    };

    /// <summary>The relief rolls: Coming and Leaving while their limit lies ahead; Riding once its countdown ran out; Waiting never.</summary>
    public static bool Rolls(ReliefPhase phase, bool boarded, float s, float limit) => phase switch
    {
        ReliefPhase.Coming or ReliefPhase.Leaving => SpareRules.ChaseRolls(true, s, limit),
        ReliefPhase.Riding => SpareRules.ChaseRolls(boarded, s, limit),
        _ => false,
    };

    /// <summary>
    /// The relief's prompt: the chaser's ("Get out" while it stands, never in the countdown's first two seconds, never downed) for a hero
    /// seated in it; "Board the cart" for an on-foot player within reach while it waits; else nothing.
    /// </summary>
    public static string Prompt(ReliefPhase phase, bool held, bool seatedHere, bool onFootPlayer, bool inReach, bool downed, float boardingLeft)
    {
        if (seatedHere) return SpareRules.ChaserPrompt(true, held, downed, boardingLeft);
        if (phase == ReliefPhase.Waiting && onFootPlayer && inReach)
            return RideInputRules.BoardPrompt(seatedHere: false, held: true, lastStop: false, heroSeatFree: true, inReach: true);
        return "";
    }
}

/// <summary>
/// The relief's clock: how long the player has been left behind while no relief is out (<see cref="Behind"/>), and how many reliefs
/// it has sent (<see cref="Sent"/>). It sends one at <see cref="ReliefRules.FirstWait"/>, or <see cref="ReliefRules.NextWait"/> after
/// one rolled on without the player (<see cref="Went"/>). Not saved: a load resets it.
/// </summary>
public sealed class ReliefClock
{
    /// <summary>Seconds left behind with no relief out, toward the next send.</summary>
    public float Behind { get; private set; }
    /// <summary>Reliefs sent since the floor was built or loaded.</summary>
    public int Sent { get; private set; }
    private float _due = ReliefRules.FirstWait;

    /// <summary>
    /// One tick: no longer left behind starts the wait over; left behind, it counts while <paramref name="counting"/> (no relief out).
    /// True once when the wait is up: send a relief.
    /// </summary>
    public bool Tick(float dt, bool leftBehind, bool counting)
    {
        if (!leftBehind)
        {
            Behind = 0f;
            _due = ReliefRules.FirstWait;
            return false;
        }
        if (counting) Behind += dt;
        if (Behind < _due - 1e-4f) return false;
        Behind = 0f;
        _due = ReliefRules.FirstWait;
        Sent++;
        return true;
    }

    /// <summary>The relief rolled on without the player: the next comes <see cref="ReliefRules.NextWait"/> after this.</summary>
    public void Went()
    {
        Behind = 0f;
        _due = ReliefRules.NextWait;
    }

    /// <summary>Back to a clean floor (a load): nothing counted, nothing sent.</summary>
    public void Reset()
    {
        Behind = 0f;
        Sent = 0;
        _due = ReliefRules.FirstWait;
    }
}
