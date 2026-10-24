using DungeonCrawler.Adventure;
using DungeonCrawler.Adventure.Objectives;
using DungeonCrawler.Adventure.Voice;
using DungeonCrawler.Dungeon.Generation;
using Xunit;

namespace DungeonCrawler.Tests.Adventure;

public class TauntTests
{
    private static List<TauntPlan> Bank() => TauntBank.All();
    private static readonly Dictionary<string, int> Fresh = new();

    [Fact]
    public void Stock_HasCrushedTaunts_WithCleanTwins()
    {
        var campaign = new CampaignPlan(); TauntBank.Fill(campaign);
        var crushed = campaign.Taunts.Where(t => t.Trigger == nameof(TauntTrigger.Crushed)).ToList();
        Assert.True(crushed.Count >= 2);
        Assert.All(crushed, t => Assert.False(string.IsNullOrWhiteSpace(t.CleanLine?.Text)));
    }

    [Fact]
    public void Stock_CoversEveryTrigger_WithCleanTwins()
    {
        var bank = Bank();
        foreach (var t in TauntTriggers.All) Assert.Contains(bank, b => b.Trigger == TauntTriggers.Id(t));
        foreach (var t in bank)
        {
            Assert.NotNull(t.CleanLine);
            Assert.True(t.Line.Text.Length <= CampaignRepair.MaxLine, t.Line.Text);
            Assert.True(t.CleanLine!.Text.Length <= CampaignRepair.MaxLine);
            Assert.DoesNotContain("fuck", t.CleanLine.Text, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("shit", t.CleanLine.Text, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("bastard", t.CleanLine.Text, StringComparison.OrdinalIgnoreCase);
        }
        foreach (var kind in FloorObjectives.All) Assert.Contains(bank, b => b.Trigger == "FloorEnter" && b.Objective == FloorObjectives.Id(kind));
    }

    [Fact]
    public void Pick_HonoursCooldowns_AndRepeats()
    {
        var d = new TauntDirector();
        var rng = new Random(3);
        var said = new Dictionary<string, int>();
        var first = d.Pick(Bank(), TauntTrigger.Capture, FloorObjectiveKind.Jailbreak, said, null, 3, now: 100, rng);
        Assert.NotNull(first);
        BarkRepeat.Said(said, first!.Line.Text, 3);
        Assert.Null(d.Pick(Bank(), TauntTrigger.Freed, FloorObjectiveKind.Jailbreak, said, null, 3, now: 105, rng)); // global cooldown
        Assert.NotNull(d.Pick(Bank(), TauntTrigger.Freed, FloorObjectiveKind.Jailbreak, said, null, 3, now: 113, rng));
        Assert.Null(d.Pick(Bank(), TauntTrigger.Capture, FloorObjectiveKind.Jailbreak, said, null, 3, now: 130, rng)); // per-trigger cooldown
        var again = d.Pick(Bank(), TauntTrigger.Capture, FloorObjectiveKind.Jailbreak, said, null, 3, now: 150, rng);
        Assert.NotNull(again);
        Assert.NotEqual(first.Line.Text, again!.Line.Text); // a line said this floor stays out
        BarkRepeat.Said(said, again.Line.Text, 3);
        Assert.Null(d.Pick(Bank(), TauntTrigger.Capture, FloorObjectiveKind.Jailbreak, said, null, 4, now: 200, rng)); // both said within two floors
        Assert.NotNull(d.Pick(Bank(), TauntTrigger.Capture, FloorObjectiveKind.Jailbreak, said, null, 5, now: 260, rng));
    }

    [Fact]
    public void Pick_WaitsForOtherSpeakers_UnlessUrgent()
    {
        var d = new TauntDirector();
        d.NoteNarration(50);
        Assert.Null(d.Pick(Bank(), TauntTrigger.Descend, FloorObjectiveKind.None, Fresh, null, 2, 52, new Random(1)));
        Assert.NotNull(d.Pick(Bank(), TauntTrigger.Descend, FloorObjectiveKind.None, Fresh, null, 2, 52, new Random(1), urgent: true));
    }

    [Fact]
    public void Pick_PrefersTheKindsOwnLines_AndFiltersByKindAndFlags()
    {
        var d = new TauntDirector();
        var pick = d.Pick(Bank(), TauntTrigger.FloorEnter, FloorObjectiveKind.Toxic, Fresh, null, 2, 10, new Random(2));
        Assert.Equal("toxic", pick!.Objective);
        var bank = new List<TauntPlan>
        {
            new() { Trigger = "Idle", Objective = "ritual", Line = new Line("ritual only") },
            new() { Trigger = "Idle", Line = new Line("needs flag"), RequiresFlag = "brave" },
        };
        var d2 = new TauntDirector();
        Assert.Null(d2.Pick(bank, TauntTrigger.Idle, FloorObjectiveKind.Toxic, Fresh, Array.Empty<string>(), 2, 10, new Random(2)));
        Assert.Equal("needs flag", d2.Pick(bank, TauntTrigger.Idle, FloorObjectiveKind.Toxic, Fresh, new[] { "brave" }, 2, 10, new Random(2))!.Line.Text);
    }

    [Fact]
    public void Render_FillsTokens()
    {
        Assert.Equal("Run, Mara! Floor 7 has Tobit.", TauntDirector.Render("Run, {party}! Floor {floor} has {prisoner}.", "Mara", 7, "Tobit"));
        Assert.Equal("Run, little rats! that one", TauntDirector.Render("Run, {party}! {prisoner}", null, 1, null));
    }

    [Fact]
    public void Soften_TakesTheOathsOut()
    {
        Assert.Equal("Wrong lever, you wretches.", TauntBank.Soften("Wrong bloody lever, you bastards."));
        Assert.Equal("Where were you? Looting a barrel?", TauntBank.Soften("Where were you? Looting a fucking barrel?"));
    }

    [Fact]
    public void Repair_FillsGaps_FixesTriggers_AndTwins()
    {
        var campaign = new CampaignPlan();
        campaign.Taunts.Add(new TauntPlan { Trigger = "capture", Objective = "JAILBREAK", Line = new Line("Got one, you damned fools.") });
        campaign.Taunts.Add(new TauntPlan { Trigger = "nonsense", Line = new Line("x") });
        campaign.Antagonist.Persona = new Persona { VoiceStyle = new string('a', 300), DefaultDelivery = "growly", Voice = "puck" };
        var repairs = CampaignRepair.Apply(campaign);
        Assert.Contains(campaign.Taunts, t => t.Trigger == "Capture" && t.Objective == "jailbreak" && t.CleanLine!.Text == "Got one, you fools.");
        Assert.DoesNotContain(campaign.Taunts, t => t.Trigger == "nonsense");
        foreach (var t in TauntTriggers.All) Assert.Contains(campaign.Taunts, b => b.Trigger == TauntTriggers.Id(t));
        Assert.Equal(Persona.MaxStyle, campaign.Antagonist.Persona.VoiceStyle.Length);
        Assert.Null(campaign.Antagonist.Persona.DefaultDelivery);
        Assert.Equal("Puck", campaign.Antagonist.Persona.Voice);
        Assert.NotEmpty(repairs);
        // a second pass adds nothing
        var before = campaign.Taunts.Count;
        CampaignRepair.Apply(campaign);
        Assert.Equal(before, campaign.Taunts.Count);
    }

    [Fact]
    public void Budget_GivesTauntsTheirOwnAllowance()
    {
        var campaign = new CampaignPlan();
        campaign.Tiers.Add(new TierPlan { Index = 0, EnterLine = new Line("welcome") });
        CampaignRepair.Apply(campaign);
        var picks = VoiceBudget.Select(campaign, new Dictionary<int, FloorPlan>(), max: 1, tauntMax: 6);
        Assert.Equal(7, picks.Count);
        var taunts = picks.Where(p => p.Reason.StartsWith("taunt")).ToList();
        Assert.Equal(6, taunts.Count);
        Assert.StartsWith("taunt FloorEnter", taunts[0].Reason); // the entrances first, one per objective
        Assert.Equal(4, taunts.Take(4).Count(t => t.Reason == "taunt FloorEnter"));
        Assert.Contains("taunts 6", VoiceBudget.Describe(picks, 100));
    }
}
