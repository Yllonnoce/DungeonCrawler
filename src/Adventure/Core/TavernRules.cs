namespace DungeonCrawler.Adventure;

/// <summary>The tavern above the dungeon: the seed money the party is paid once, and the numbers its scene shares.</summary>
public static class TavernRules
{
    /// <summary>The gold the party is staked with, once, when the deal is struck or the party first descends.</summary>
    public const int SeedGold = 150;
    /// <summary>The story node at which the deal is struck.</summary>
    public const string DealNodeId = "deal";
    /// <summary>How close (in cells) the party must come for the tavern folk to greet it.</summary>
    public const float GreetRadius = 4f;

    /// <summary>The seed money is paid exactly once: at the deal, or failing that on the way down.</summary>
    public static bool ShouldPaySeed(bool alreadyPaid, string? reachedNodeId, bool descending)
        => !alreadyPaid && (reachedNodeId == DealNodeId || descending);
}
