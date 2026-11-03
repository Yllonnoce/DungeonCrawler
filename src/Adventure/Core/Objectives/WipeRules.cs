using DungeonCrawler.Save;

namespace DungeonCrawler.Adventure.Objectives;

/// <summary>
/// What a party wipe does to a floor objective. The floor comes back from its remembered state (the server autosaves
/// it every minute), and the party stands at the up stairs again as if it had just arrived, so the objective's clocks
/// start over with it: the gas is back at the grace period instead of lethal in the start room, the rite is unheld.
/// Everything the party achieved on the floor (freed prisoners, sealed mouths, pulled levers, completion) stays.
/// </summary>
public static class WipeRules
{
    public static void RestartClocks(ObjectiveStateDto? state)
    {
        if (state == null) return;
        state.Seconds = 0f;
        state.RitualProgress = 0f;
    }
}
