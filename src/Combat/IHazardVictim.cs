using DungeonCrawler.Dungeon;

namespace DungeonCrawler.Combat;

/// <summary>
/// Anything a lethal hazard (the Press, a Spike Bed) can catch: party members, allies and NPCs, enemies and bosses.
/// The outcome is decided by <see cref="HazardRules.Outcome"/> from the victim's kind and the dungeon's lethality;
/// the victim only applies it.
/// </summary>
public interface IHazardVictim
{
    VictimKind HazardVictimKind { get; }
    /// <param name="sourceName">What did it, for the log: "The press", "The spikes".</param>
    /// <param name="hazardIndex">The hazard's placement index on the floor, so a Mangled body knows what it is waiting for.</param>
    void ReceiveHazard(HazardDamage kind, HazardOutcome outcome, string sourceName, int hazardIndex);
}
