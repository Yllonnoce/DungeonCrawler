namespace DungeonCrawler.Adventure;

/// <summary>The tavern above the dungeon: the seed money the party is paid once, and the numbers its scene shares.</summary>
public static class TavernRules
{
    /// <summary>The gold the party is staked with, once, when the deal is struck or the party first descends.</summary>
    public const int SeedGold = 150;
    /// <summary>The story node at which the deal is struck.</summary>
    public const string DealNodeId = "deal";
    /// <summary>How close (metres, as <c>Npc.GreetRadius</c> measures) the party must come for the tavern merchant to greet it.</summary>
    public const float GreetRadius = 4f;

    /// <summary>The seed money is paid exactly once: at the deal, or failing that on the way down.</summary>
    public static bool ShouldPaySeed(bool alreadyPaid, string? reachedNodeId, bool descending)
        => !alreadyPaid && (reachedNodeId == DealNodeId || descending);

    /// <summary>
    /// The merchant's briefing in the tavern: the campaign's own when it reaches the deal, else the bank's generic one
    /// (which always does), so the seed money can always be paid at the deal.
    /// </summary>
    public static NpcScript BriefingFor(CampaignPlan? campaign)
    {
        if (campaign?.Merchant?.Briefing is { } own && own.Nodes.Any(n => n.Id == DealNodeId)) return own;
        var title = string.IsNullOrWhiteSpace(campaign?.Title) ? "the tunnels" : campaign.Title;
        var villain = string.IsNullOrWhiteSpace(campaign?.Antagonist?.Name) ? "what waits below" : campaign.Antagonist.Name;
        return Voice.MerchantLineBank.Generic(title, villain).Briefing;
    }
}
