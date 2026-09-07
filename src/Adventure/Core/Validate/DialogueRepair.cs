namespace DungeonCrawler.Adventure;

/// <summary>
/// Makes sure a quest giver's conversation can actually finish the quest. The dialogue screen always opens at the
/// script's start node and only shows the choices that fit the quest's state, so a script whose turn-in choice is on
/// a node nothing links to (or that has no turn-in at all) leaves the party with nothing but "Farewell" once the work
/// is done. Model-written scripts do this often; the offline designer's never need repair.
/// </summary>
public static class DialogueRepair
{
    public const string TurnInText = "It is done.";
    public const string DecideText = "We have decided.";
    public const string DefaultCompleteText = "Thank you. That is what I needed.";

    /// <summary>
    /// Ensure the start node can reach the quest's ending: a TurnIn choice for quests that are handed in, or the
    /// Choose choices for a Decide quest. Returns what was changed (empty when nothing was); safe to call again.
    /// </summary>
    public static List<string> EnsureReachable(NpcScript script, QuestTemplate template, string? completeText, string questId = "")
    {
        var repairs = new List<string>();
        var start = script.Nodes.FirstOrDefault(n => n.Id == script.Start) ?? script.Nodes.FirstOrDefault();
        if (start == null || template == QuestTemplate.Betrayal) return repairs;

        if (template == QuestTemplate.Decide)
        {
            // the Choose choices must be reachable through choices that are always shown (Effect None)
            if (ReachableThroughPlainChoices(script, start).Any(n => n.Choices.Any(c => c.Effect == DialogueEffect.Choose))) return repairs;
            var chooser = script.Nodes.FirstOrDefault(n => n.Choices.Any(c => c.Effect == DialogueEffect.Choose));
            if (chooser == null) return repairs; // the validator drops such quests
            start.Choices.Add(new ChoicePlan { Text = DecideText, Effect = DialogueEffect.None, Next = chooser.Id });
            repairs.Add($"added a way from the greeting to the decision ({chooser.Id})");
            return repairs;
        }

        // hand-in quests: the greeting itself must carry the TurnIn choice, since that is the node shown on every visit
        if (start.Choices.Any(c => c.Effect == DialogueEffect.TurnIn)) return repairs;
        var turnIn = script.Nodes.FirstOrDefault(n => n != start && n.Choices.Any(c => c.Effect == DialogueEffect.TurnIn));
        if (turnIn != null)
        {
            start.Choices.Add(new ChoicePlan { Text = TurnInText, Effect = DialogueEffect.TurnIn, Next = turnIn.Id });
            repairs.Add($"added a turn-in choice on the greeting leading to {turnIn.Id}");
            return repairs;
        }
        var doneId = UniqueId(script, string.IsNullOrEmpty(questId) ? "done" : $"done_{questId}");
        var text = string.IsNullOrWhiteSpace(completeText) ? DefaultCompleteText : completeText!.Trim();
        script.Nodes.Add(new DialogueNode { Id = doneId, Line = new Line(text), Choices = { new ChoicePlan { Text = "Farewell.", Effect = DialogueEffect.Farewell } } });
        start.Choices.Add(new ChoicePlan { Text = TurnInText, Effect = DialogueEffect.TurnIn, Next = doneId });
        repairs.Add($"added a turn-in choice on the greeting and a completion node ({doneId})");
        return repairs;
    }

    private static IEnumerable<DialogueNode> ReachableThroughPlainChoices(NpcScript script, DialogueNode start)
    {
        var seen = new HashSet<string> { start.Id };
        var queue = new Queue<DialogueNode>();
        queue.Enqueue(start);
        while (queue.Count > 0)
        {
            var node = queue.Dequeue();
            yield return node;
            foreach (var choice in node.Choices)
            {
                if (choice.Effect != DialogueEffect.None || choice.Next == null || !seen.Add(choice.Next)) continue;
                var next = script.Nodes.FirstOrDefault(n => n.Id == choice.Next);
                if (next != null) queue.Enqueue(next);
            }
        }
    }

    private static string UniqueId(NpcScript script, string wanted)
    {
        var id = wanted;
        for (var i = 2; script.Nodes.Any(n => n.Id == id); i++) id = $"{wanted}_{i}";
        return id;
    }
}
