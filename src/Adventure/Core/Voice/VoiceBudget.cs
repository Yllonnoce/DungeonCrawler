namespace DungeonCrawler.Adventure.Voice;

/// <summary>
/// Which lines of a baked campaign get audio when only so many may be rendered. Priority: the tier entrances and boss
/// lines, then the story floors' intros and NPC dialogue in floor order, then the remaining narration, then quest lines,
/// and companion barks last. Everything else stays a subtitle.
/// </summary>
public static class VoiceBudget
{
    public const int Default = 80;

    public sealed record Pick(Line Line, string Reason, int Floor);

    public static List<Pick> Select(CampaignPlan campaign, IReadOnlyDictionary<int, FloorPlan> floors, int max)
    {
        var ordered = Candidates(campaign, floors);
        var seen = new HashSet<Line>(ReferenceEqualityComparer.Instance);
        var picks = new List<Pick>();
        foreach (var c in ordered)
        {
            if (picks.Count >= max) break;
            if (string.IsNullOrWhiteSpace(c.Line.Text) || !seen.Add(c.Line)) continue;
            picks.Add(c);
        }
        return picks;
    }

    /// <summary>Every voiceable line in priority order (before the cap).</summary>
    public static List<Pick> Candidates(CampaignPlan campaign, IReadOnlyDictionary<int, FloorPlan> floors)
    {
        var list = new List<Pick>();
        foreach (var t in campaign.Tiers.OrderBy(t => t.Index)) list.Add(new Pick(t.EnterLine, "tier entrance", t.Index * 5 + 1));
        var byFloor = floors.OrderBy(f => f.Key).ToList();
        foreach (var (floor, plan) in byFloor)
            if (plan.Boss != null) { list.Add(new Pick(plan.Boss.IntroLine, "boss intro", floor)); list.Add(new Pick(plan.Boss.DeathLine, "boss death", floor)); }
        // story floors carry the plot: their intro, then what their people say
        var story = byFloor.Where(f => campaign.IsStoryFloor(f.Key)).ToList();
        foreach (var (floor, plan) in story) list.Add(new Pick(plan.IntroLine, "story intro", floor));
        foreach (var (floor, plan) in story)
            foreach (var npc in plan.Npcs)
                foreach (var node in plan.Npcs.Count == 0 ? Enumerable.Empty<DialogueNode>() : npc.Script.Nodes)
                    list.Add(new Pick(node.Line, $"{npc.Name}", floor));
        foreach (var (floor, plan) in story)
            foreach (var n in plan.Narration.Where(n => n.Trigger != NarrationTrigger.Enter)) list.Add(new Pick(n.Line, "story narration", floor));
        foreach (var (floor, plan) in story)
            foreach (var q in plan.Quests) { list.Add(new Pick(q.Lines.Offer, "quest offer", floor)); list.Add(new Pick(q.Lines.Complete, "quest complete", floor)); }
        // quiet floors: their intro only, then whatever else they have
        var quiet = byFloor.Where(f => !campaign.IsStoryFloor(f.Key)).ToList();
        foreach (var (floor, plan) in quiet) list.Add(new Pick(plan.IntroLine, "quiet intro", floor));
        foreach (var (floor, plan) in quiet)
            foreach (var npc in plan.Npcs) foreach (var node in npc.Script.Nodes) list.Add(new Pick(node.Line, $"{npc.Name}", floor));
        foreach (var (floor, plan) in quiet)
            foreach (var q in plan.Quests) { list.Add(new Pick(q.Lines.Offer, "quest offer", floor)); list.Add(new Pick(q.Lines.Complete, "quest complete", floor)); }
        foreach (var (floor, plan) in byFloor)
            foreach (var q in plan.Quests) { list.Add(new Pick(q.Lines.Accept, "quest accept", floor)); list.Add(new Pick(q.Lines.Decline, "quest decline", floor)); list.Add(new Pick(q.Lines.Fail, "quest fail", floor)); }
        foreach (var (floor, plan) in quiet)
            foreach (var n in plan.Narration.Where(n => n.Trigger != NarrationTrigger.Enter)) list.Add(new Pick(n.Line, "narration", floor));
        foreach (var (floor, plan) in byFloor) foreach (var h in plan.Hordes) list.Add(new Pick(h.Line, "horde", floor));
        foreach (var set in campaign.Barks) foreach (var b in set.Lines) list.Add(new Pick(b.Line, $"bark {set.Member}", 0));
        return list;
    }

    public static string Describe(IReadOnlyList<Pick> picks, int candidates)
    {
        var groups = picks.GroupBy(p => p.Reason.StartsWith("bark") ? "barks" : p.Reason).Select(g => $"{g.Key} {g.Count()}");
        return $"{picks.Count} of {candidates} lines voiced: {string.Join(", ", groups)}";
    }
}
