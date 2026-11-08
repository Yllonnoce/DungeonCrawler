using DungeonCrawler.Adventure.Voice;

namespace DungeonCrawler.Adventure.Run;

/// <summary>The last thing that hurt a member: the cause id (a <see cref="WipeCause"/> id), who or what it was, and when (play seconds).</summary>
public sealed record HarmSource(string CauseId, string Name, double At);

/// <summary>
/// The wipe's causes as the death card and the ledger key them: the same ids writing's <see cref="WipeCause"/> uses for
/// the epitaph bank, with the mapping from an enemy's flags and a hazard's source name. Engine-free.
/// </summary>
public static class HarmCauses
{
    public const string Gas = WipeCause.Gas, Press = WipeCause.Press, Spikes = WipeCause.Spikes, Boss = WipeCause.Boss, Horde = WipeCause.Horde,
        Elite = WipeCause.Elite, Fall = WipeCause.Fall, Mimic = WipeCause.Mimic, Bomber = WipeCause.Bomber, Generic = WipeCause.Generic;
    public static readonly string[] All = WipeCause.All;

    /// <summary>Boss over mimic over elite; a bomber is a horde creature, so bomber beats horde.</summary>
    public static string ForEnemy(bool isBoss, bool isElite, bool isHorde, bool isBomber, bool isMimic)
        => isBoss ? Boss : isMimic ? Mimic : isElite ? Elite : isBomber ? Bomber : isHorde ? Horde : Generic;

    /// <summary>A non-creature attacker's name ("The gas", "The press", "The spikes", "The fall") to its cause; anything else is generic.</summary>
    public static string FromSource(string sourceName)
    {
        if (sourceName.Contains("gas", StringComparison.OrdinalIgnoreCase)) return Gas;
        if (sourceName.Contains("fall", StringComparison.OrdinalIgnoreCase)) return Fall;
        var hazard = WipeCause.FromHazardSource(sourceName);
        return hazard;
    }

    /// <summary>The cause as a sentence names it: the hazards by name, a horde as "the horde", anything else the killer's own name or "the dark".</summary>
    public static string Describe(string cause, string killer) => cause switch
    {
        Gas => "the gas",
        Press => "the Press",
        Spikes => "the spikes",
        Fall => "the fall",
        Horde => "the horde",
        _ => string.IsNullOrWhiteSpace(killer) ? "the dark" : killer,
    };
}
