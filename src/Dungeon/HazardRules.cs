namespace DungeonCrawler.Dungeon;

/// <summary>The lethal hazards: a Press (a corridor whose walls close) and a Spike Bed (floor patches that impale).</summary>
public enum HazardKind : byte { Press, SpikeBed }

/// <summary>How a spike bed fires: when stepped on, or on a metronome from the floor's start tick.</summary>
public enum HazardVariant : byte { Pressure, Periodic }

/// <summary>The Press's state machine. Crush is the instant the kill check runs.</summary>
public enum PressPhase : byte { Armed, Warning, Closing, Crush, Hold, Retract, Cooldown }

/// <summary>The spike bed's state machine. The kill check runs on the Click to Up transition.</summary>
public enum SpikePhase : byte { Down, Click, Up, Retract, Rearm }

public enum HazardDamage : byte { Crush, Impale }

/// <summary>Who a hazard hit, which decides what "lethal" means for them.</summary>
public enum VictimKind : byte { Member, Ally, QuestNpc, Enemy, Boss }

/// <param name="Lethal">The victim is killed (enemies) or put down Mangled (members, allies).</param>
/// <param name="HpFraction">When not lethal, the fraction of max HP taken.</param>
/// <param name="StunSeconds">Bosses stagger instead of dying.</param>
public readonly record struct HazardOutcome(bool Lethal, float HpFraction, float StunSeconds);

/// <summary>Every number for the lethal hazards. Engine-free; the Godot nodes only read it.</summary>
public static class HazardRules
{
    /// <summary>At this <see cref="TierRules.Lethality"/> and above, a hazard kills players and allies outright.</summary>
    public const float LethalFrom = 2.5f;
    public const float JamGap = 1.0f;
    public const float SpikePreRaiseMetres = 0.10f;
    private const float PressOpenGap = 3.5f;
    private const float PressClosedGap = 0.8f;

    public static bool IsLethal(float lethality) => lethality >= LethalFrom;

    /// <summary>Below the lethal threshold: half of max HP at lethality 1, rising linearly.</summary>
    public static float DamageFraction(float lethality) => 0.5f + 0.25f * (lethality - 1f);

    public static HazardOutcome Outcome(VictimKind victim, float lethality) => victim switch
    {
        VictimKind.Enemy => new HazardOutcome(true, 1f, 0f),
        VictimKind.Boss => new HazardOutcome(false, 0.35f, 2f),
        VictimKind.QuestNpc => new HazardOutcome(false, 1f, 0f),
        _ => IsLethal(lethality) ? new HazardOutcome(true, 1f, 0f) : new HazardOutcome(false, DamageFraction(lethality), 0f),
    };

    public static float PressDuration(PressPhase phase) => phase switch
    {
        PressPhase.Armed => float.PositiveInfinity,
        PressPhase.Warning => 1.2f,
        PressPhase.Closing => 2.4f,
        PressPhase.Crush => 0f,
        PressPhase.Hold => 1.5f,
        PressPhase.Retract => 4.0f,
        _ => 6.0f,
    };

    public static PressPhase NextPress(PressPhase phase) => phase == PressPhase.Cooldown ? PressPhase.Armed : (PressPhase)((byte)phase + 1);

    /// <summary>The gap between the slabs during Closing, eased in so the squeeze starts slow and ends fast.</summary>
    public static float PressGap(float t01)
    {
        var t = Math.Clamp(t01, 0f, 1f);
        return PressOpenGap + (PressClosedGap - PressOpenGap) * t * t;
    }

    public static float SpikeDuration(SpikePhase phase, HazardVariant variant) => phase switch
    {
        SpikePhase.Down => variant == HazardVariant.Periodic ? 3.0f : float.PositiveInfinity,
        SpikePhase.Click => 0.5f,
        SpikePhase.Up => 1.0f,
        SpikePhase.Retract => 1.5f,
        _ => variant == HazardVariant.Periodic ? 0f : 4.0f,
    };

    public static SpikePhase NextSpike(SpikePhase phase) => phase == SpikePhase.Rearm ? SpikePhase.Down : (SpikePhase)((byte)phase + 1);

    /// <summary>A periodic bed's cycle in physics ticks at 60 Hz: Down 180, Click 30, Up 60.</summary>
    public const int PeriodicPeriodTicks = 270;
    private const int PeriodicDownTicks = 180;
    private const int PeriodicClickTicks = 30;

    /// <summary>Where a periodic bed is in its cycle at a server tick; beds on one floor are offset so they fire out of step.</summary>
    public static SpikePhase PeriodicPhaseAt(int serverTick, int patchIndex, out float secondsIntoPhase)
    {
        var offset = (patchIndex * 37) % PeriodicPeriodTicks;
        var t = ((serverTick + offset) % PeriodicPeriodTicks + PeriodicPeriodTicks) % PeriodicPeriodTicks;
        int into;
        SpikePhase phase;
        if (t < PeriodicDownTicks) { phase = SpikePhase.Down; into = t; }
        else if (t < PeriodicDownTicks + PeriodicClickTicks) { phase = SpikePhase.Click; into = t - PeriodicDownTicks; }
        else { phase = SpikePhase.Up; into = t - PeriodicDownTicks - PeriodicClickTicks; }
        secondsIntoPhase = into / 60f;
        return phase;
    }

    /// <summary>A body with its feet off the ground when the spikes reach full height is not hit.</summary>
    public static bool CanDodgeSpikes(bool onFloor) => !onFloor;
}
