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
        if (campaign?.Merchant?.Briefing is { } own && ReachesDeal(own)) return WithClauseNode(own);
        var title = string.IsNullOrWhiteSpace(campaign?.Title) ? "the tunnels" : campaign.Title;
        var villain = string.IsNullOrWhiteSpace(campaign?.Antagonist?.Name) ? "what waits below" : campaign.Antagonist.Name;
        return WithClauseNode(Voice.MerchantLineBank.Generic(title, villain).Briefing);
    }

    /// <summary>The node at which the factor offers the clauses (run-structure), between the deal and the sendoff.</summary>
    public const string ClausesNodeId = "clauses", SendoffNodeId = "sendoff";
    /// <summary>The stock offer, in the merchant's register; a campaign's own briefing may carry its own words.</summary>
    public const string ClausesOffer = "One more thing. The contract has clauses, every one optional and every one paid for. Will you read them?";

    /// <summary>
    /// Splice the clause offer into a briefing that lacks it: a <c>clauses</c> node after the deal with "Not today." (index 0,
    /// so a Choose(0) walk still closes) and "Read them." (the Clauses effect), both leading to the sendoff, and every deal
    /// choice that went to the sendoff repointed at it. Idempotent: the script is returned as is when it has the node or no deal.
    /// </summary>
    public static NpcScript WithClauseNode(NpcScript script)
    {
        if (script.Nodes.Any(n => n.Id == ClausesNodeId)) return script;
        var deal = script.Nodes.FirstOrDefault(n => n.Id == DealNodeId);
        if (deal == null) return script;
        var node = new DialogueNode
        {
            Id = ClausesNodeId,
            Line = new Line(ClausesOffer),
            Choices =
            {
                new ChoicePlan { Text = "Not today.", Next = SendoffNodeId },
                new ChoicePlan { Text = "Read them.", Effect = DialogueEffect.Clauses, Next = SendoffNodeId },
            },
        };
        foreach (var c in deal.Choices) if (c.Next == SendoffNodeId) c.Next = ClausesNodeId;
        script.Nodes.Insert(Math.Min(script.Nodes.IndexOf(deal) + 1, script.Nodes.Count), node);
        return script;
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
