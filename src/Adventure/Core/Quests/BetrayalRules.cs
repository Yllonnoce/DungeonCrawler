namespace DungeonCrawler.Adventure;

/// <summary>
/// What a Betrayal says and who its creatures are. A turncoat (the default) vanishes and their creatures stand in their
/// place, the first wearing their name. An escort (<see cref="BetrayalPlan.Escort"/>) is an NPC who runs to the enemy
/// sent for them: they leave the floor alive, the creatures keep their own names, and every line says they fled.
/// Engine-free.
/// </summary>
public static class BetrayalRules
{
    /// <summary>The name the creature at <paramref name="index"/> wears, or null to keep its own.</summary>
    public static string? CreatureName(BetrayalPlan betrayal, string npcName, int index)
        => !betrayal.Escort && index == 0 ? npcName : null;

    /// <summary>The run log's line as the NPC goes.</summary>
    public static string TurnLog(bool escort, string npcName)
        => escort ? $"{npcName} runs, and the enemy comes for you." : $"{npcName} drops the act.";

    /// <summary>What the floor's record keeps of the NPC (neither a death nor a rescue for an escort).</summary>
    public static string Fate(bool escort, string npcName)
        => escort ? $"{npcName} fled to the enemy" : $"{npcName} turned on the party";

    /// <summary>The quest tracker's line for a Betrayal quest in progress.</summary>
    public static string Objective(bool escort, string giverName, int progress, int goal)
        => escort ? $"{giverName} has fled. Put down the escort ({progress}/{goal})." : $"{giverName} has turned. Put them down ({progress}/{goal}).";

    /// <summary>The extra log line when the betrayal carries no Betrayal quest, or null for none.</summary>
    public static string? NoQuestLog(bool escort, string npcName)
        => escort ? null : $"{npcName} was never on your side.";
}
