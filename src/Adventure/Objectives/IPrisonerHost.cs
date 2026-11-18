using DungeonCrawler.Party;

namespace DungeonCrawler.Adventure.Objectives;

/// <summary>
/// The floor objective that owns its prisoners (the walk-in jailbreak, the railed floor's crew): a Prisoner-mode
/// <see cref="Npc"/> asks it what E on them says and hands it the press.
/// </summary>
public interface IPrisonerHost
{
    /// <summary>What E on <paramref name="npc"/> says; "" offers nothing.</summary>
    string PromptFor(Npc npc);
    /// <summary>E on <paramref name="npc"/> (server). True when it did something.</summary>
    bool OnInteract(Npc npc, PartyController party, Adventurer actor);
}
