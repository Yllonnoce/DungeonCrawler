using DungeonCrawler.Adventure;
using DungeonCrawler.Adventure.Voice;
using Xunit;

namespace DungeonCrawler.Tests.Adventure;

public class VoiceBankSweepTests
{
    [Fact]
    public void ReferencedIds_IncludeTheBarkLines_SoASweepKeepsTheirAudio()
    {
        var campaign = new CampaignPlan();
        var set = new BarkSet { Member = "Brand" };
        set.Lines.Add(new BarkLine { Trigger = BarkTrigger.Kill, Line = new Line("Stay down.") { VoiceId = "feedfacefeedface" } });
        campaign.Barks.Add(set);
        Assert.Contains("feedfacefeedface", VoiceRefs.Ids(campaign, new Dictionary<int, FloorPlan>()));
    }

    [Fact]
    public void IdOf_ReadsBankFilesOnly()
    {
        Assert.Equal("01d2d1e5adc53bb5", VoiceBankSweep.IdOf("/bank/01d2d1e5adc53bb5.wav"));
        Assert.Equal("01d2d1e5adc53bb5", VoiceBankSweep.IdOf("01d2d1e5adc53bb5.wav.import"));
        Assert.Null(VoiceBankSweep.IdOf("readme.txt"));
        Assert.Null(VoiceBankSweep.IdOf("notes.wav"));
        Assert.Null(VoiceBankSweep.IdOf(".gdignore"));
    }

    [Fact]
    public void Compute_DeletesOnlyUnreferencedBankFiles()
    {
        var files = new[] { ("a/0000000000000001.wav", 100L), ("a/0000000000000001.wav.import", 10L), ("a/0000000000000002.wav", 200L), ("a/0000000000000002.wav.import", 10L), ("a/keep.txt", 5L) };
        var plan = VoiceBankSweep.Compute(files, new HashSet<string> { "0000000000000001" });
        Assert.Equal(new[] { "a/0000000000000002.wav", "a/0000000000000002.wav.import" }, plan.Delete);
        Assert.Equal(2, plan.Kept);
        Assert.Equal(210, plan.Bytes);
        Assert.Contains("swept 2", plan.Describe("a"));
        Assert.Contains("nothing to sweep", VoiceBankSweep.Compute(files, new HashSet<string> { "0000000000000001", "0000000000000002" }).Describe("a"));
    }

    [Fact]
    public void Refs_CoverEveryLineKind()
    {
        var campaign = new CampaignPlan();
        campaign.Tiers.Add(new TierPlan { Index = 0, EnterLine = new Line("tier") { VoiceId = "tier000000000000" } });
        campaign.Taunts.Add(new TauntPlan { Trigger = "Idle", Line = new Line("coarse") { VoiceId = "taunt00000000000" }, CleanLine = new Line("clean") { VoiceId = "clean00000000000" } });
        campaign.Barks.Add(new BarkSet { Member = "Brand", Lines = { new BarkLine { Trigger = BarkTrigger.Kill, Line = new Line("bark") { VoiceId = "bark000000000000" } } } });
        var floor = new FloorPlan { Floor = 1, IntroLine = new Line("intro") { VoiceId = "intro00000000000" } };
        floor.Npcs.Add(new NpcPlan { Id = "n", Name = "N", Script = new NpcScript { Start = "a", Nodes = { new DialogueNode { Id = "a", Line = new Line("hi") { VoiceId = "npc0000000000000" } } } } });
        var ids = VoiceRefs.Ids(campaign, new Dictionary<int, FloorPlan> { [1] = floor });
        Assert.Superset(new HashSet<string> { "tier000000000000", "taunt00000000000", "clean00000000000", "bark000000000000", "intro00000000000", "npc0000000000000" }, ids);
        Assert.Empty(VoiceRefs.Ids(null, null));
    }
}
