using DungeonCrawler.Adventure.Run;
using DungeonCrawler.Characters;
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
    /// <summary>Run-structure's stakes: a wipe costs a fifth of the gold (floored, so the party keeps the odd coin) and leaves the Scar curse until the next floor.</summary>
    public const float GoldLoss = 0.20f;
    public const string ScarId = Fortune.Curses.Scar;

    public static int GoldAfterWipe(int gold) => gold - (int)Math.Floor(gold * (double)GoldLoss);

    /// <summary>What killed the party: the latest harm any member took (its cause id and the name of what did it); ("generic", "the dark") when nothing is known.</summary>
    public static (string Cause, string Killer) CauseOf(IEnumerable<CharacterState> members)
    {
        var last = members.Select(m => m.LastHarm).Where(h => h != null).OrderByDescending(h => h!.At).FirstOrDefault();
        return last == null ? (HarmCauses.Generic, "the dark") : (last.CauseId, last.Name);
    }

    public static void RestartClocks(ObjectiveStateDto? state)
    {
        if (state == null) return;
        state.Seconds = 0f;
        state.RitualProgress = 0f;
    }
}
