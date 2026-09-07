using DungeonCrawler.Adventure;
using Xunit;

namespace DungeonCrawler.Tests.Adventure;

public class DialogueRepairTests
{
    private static NpcScript Node(string id, string text, params ChoicePlan[] choices)
    {
        var s = new NpcScript { Start = id };
        s.Nodes.Add(new DialogueNode { Id = id, Line = new Line(text) });
        s.Nodes[0].Choices.AddRange(choices);
        return s;
    }

    [Fact]
    public void Investigate_WithNoTurnInAnywhere_GetsCompletionNode()
    {
        // floor 1 of the 2026-09-07 Runware rebake: greet -> offer (accept / decline) or decline; nothing turns the quest in
        var s = new NpcScript { Start = "n_greet" };
        s.Nodes.Add(new DialogueNode { Id = "n_greet", Line = new Line("Lost markers."), Choices = { new ChoicePlan { Text = "What trade?", Next = "n_offer" }, new ChoicePlan { Text = "Busy.", Next = "n_decline" } } });
        s.Nodes.Add(new DialogueNode { Id = "n_offer", Line = new Line("Copper cylinders."), Choices = { new ChoicePlan { Text = "All right.", Effect = DialogueEffect.AcceptQuest }, new ChoicePlan { Text = "No.", Effect = DialogueEffect.DeclineQuest, Next = "n_decline" } } });
        s.Nodes.Add(new DialogueNode { Id = "n_decline", Line = new Line("Suit yourselves."), Choices = { new ChoicePlan { Text = "Noted.", Effect = DialogueEffect.Farewell } } });
        var fixes = DialogueRepair.EnsureReachable(s, QuestTemplate.Investigate, "Three for three. Good.", "quest_markers_f1");
        Assert.Single(fixes);
        var turnIn = s.Nodes[0].Choices.Single(c => c.Effect == DialogueEffect.TurnIn);
        var done = s.Nodes.Single(n => n.Id == turnIn.Next);
        Assert.Equal("Three for three. Good.", done.Line.Text);
        Assert.Equal(DialogueEffect.Farewell, done.Choices.Single().Effect);
        Assert.Empty(DialogueRepair.EnsureReachable(s, QuestTemplate.Investigate, "x", "quest_markers_f1")); // idempotent
    }

    [Fact]
    public void Investigate_WithUnlinkedTurnInNode_GetsLinkFromGreeting()
    {
        // floor 3: a node_turnin exists whose own choice is TurnIn, but nothing leads there
        var s = new NpcScript { Start = "meet" };
        s.Nodes.Add(new DialogueNode { Id = "meet", Line = new Line("Mislaid markers."), Choices = { new ChoicePlan { Text = "We will look.", Effect = DialogueEffect.AcceptQuest, Next = "accept" }, new ChoicePlan { Text = "No.", Effect = DialogueEffect.DeclineQuest } } });
        s.Nodes.Add(new DialogueNode { Id = "accept", Line = new Line("Excellent."), Choices = { new ChoicePlan { Text = "Right.", Effect = DialogueEffect.Farewell } } });
        s.Nodes.Add(new DialogueNode { Id = "node_turnin", Line = new Line("That seal is not mine."), Choices = { new ChoicePlan { Text = "Curious.", Effect = DialogueEffect.TurnIn } } });
        var fixes = DialogueRepair.EnsureReachable(s, QuestTemplate.Investigate, "complete", "q");
        Assert.Single(fixes);
        var turnIn = s.Nodes[0].Choices.Single(c => c.Effect == DialogueEffect.TurnIn);
        Assert.Equal("node_turnin", turnIn.Next);
        Assert.Equal(3, s.Nodes.Count); // no new node needed
    }

    [Fact]
    public void Decide_WithUnreachableChoices_GetsPlainLink()
    {
        // floor 9: greet -> accused -> quest offer (accept/decline); the Choose node hangs loose
        var s = new NpcScript { Start = "greet" };
        s.Nodes.Add(new DialogueNode { Id = "greet", Line = new Line("Structural notations."), Choices = { new ChoicePlan { Text = "Tell us.", Next = "accused" }, new ChoicePlan { Text = "Help us look.", Next = "offer" } } });
        s.Nodes.Add(new DialogueNode { Id = "accused", Line = new Line("I was paid."), Choices = { new ChoicePlan { Text = "Where are they?", Next = "offer" }, new ChoicePlan { Text = "Done talking.", Effect = DialogueEffect.Farewell } } });
        s.Nodes.Add(new DialogueNode { Id = "offer", Line = new Line("Three rooms."), Choices = { new ChoicePlan { Text = "We will.", Effect = DialogueEffect.AcceptQuest }, new ChoicePlan { Text = "No.", Effect = DialogueEffect.DeclineQuest } } });
        s.Nodes.Add(new DialogueNode { Id = "node_turnin", Line = new Line("Every mark is a trap."), Choices = { new ChoicePlan { Text = "Expose him.", Effect = DialogueEffect.Choose, Flag = "pellory_exposed" }, new ChoicePlan { Text = "Spare him.", Effect = DialogueEffect.Choose, Flag = "pellory_spared" } } });
        var fixes = DialogueRepair.EnsureReachable(s, QuestTemplate.Decide, null, "q");
        Assert.Single(fixes);
        var link = s.Nodes[0].Choices.Last();
        Assert.Equal(DialogueEffect.None, link.Effect);
        Assert.Equal("node_turnin", link.Next);
        Assert.Empty(DialogueRepair.EnsureReachable(s, QuestTemplate.Decide, null, "q"));
    }

    [Fact]
    public void StandardScript_AndBetrayal_NeedNothing()
    {
        var standard = OfflineDesigner.StandardScript("g", "o", "a", "c");
        Assert.Empty(DialogueRepair.EnsureReachable(standard, QuestTemplate.Investigate, "c", "q"));
        Assert.Equal(4, standard.Nodes.Count);
        var betrayal = Node("greet", "Trust me.", new ChoicePlan { Text = "Follow", Effect = DialogueEffect.Betray });
        Assert.Empty(DialogueRepair.EnsureReachable(betrayal, QuestTemplate.Betrayal, null, "q"));
        // a Decide script whose choices sit on the greeting is already fine
        var decide = Node("greet", "Choose.", new ChoicePlan { Text = "A", Effect = DialogueEffect.Choose, Flag = "a" }, new ChoicePlan { Text = "B", Effect = DialogueEffect.Choose, Flag = "b" });
        Assert.Empty(DialogueRepair.EnsureReachable(decide, QuestTemplate.Decide, null, "q"));
    }
}
