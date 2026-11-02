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
    /// The merchant's briefing in the tavern: the campaign's own when the deal can be reached from its start, else the bank's generic one
    /// (which always does), so the seed money can always be paid at the deal.
    /// </summary>
    public static NpcScript BriefingFor(CampaignPlan? campaign)
    {
        if (campaign?.Merchant?.Briefing is { } own && ReachesDeal(own)) return own;
        var title = string.IsNullOrWhiteSpace(campaign?.Title) ? "the tunnels" : campaign.Title;
        var villain = string.IsNullOrWhiteSpace(campaign?.Antagonist?.Name) ? "what waits below" : campaign.Antagonist.Name;
        return Voice.MerchantLineBank.Generic(title, villain).Briefing;
    }

    /// <summary>Whether the deal node can be reached from the script's start by following its choices' <c>Next</c> ids.</summary>
    public static bool ReachesDeal(NpcScript script)
    {
        if (string.IsNullOrEmpty(script.Start)) return false;
        var byId = new Dictionary<string, DialogueNode>();
        foreach (var n in script.Nodes) if (n.Id != null) byId.TryAdd(n.Id, n);
        var seen = new HashSet<string>();
        var queue = new Queue<string>();
        queue.Enqueue(script.Start);
        while (queue.Count > 0)
        {
            var id = queue.Dequeue();
            if (!seen.Add(id) || !byId.TryGetValue(id, out var node)) continue;
            if (id == DealNodeId) return true;
            foreach (var c in node.Choices) if (!string.IsNullOrEmpty(c.Next)) queue.Enqueue(c.Next);
        }
        return false;
    }
}
