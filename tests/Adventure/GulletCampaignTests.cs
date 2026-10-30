using DungeonCrawler.Adventure;
using Xunit;

namespace DungeonCrawler.Tests.Adventure;

/// <summary>The shipped campaign, The Gullet of Gorm, carries the story beats the bible asks for and the bodies the owner built.</summary>
public class GulletCampaignTests
{
    private static readonly string Root = FindRoot();
    private static string Builtin => Path.Combine(Root, "data", "adventures", "builtin");

    private static string FindRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "DungeonCrawler.csproj"))) dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("repo root not found");
    }

    private static FloorPlan Floor(int n) => PlanFormat.Deserialize<FloorPlan>(File.ReadAllText(Path.Combine(Builtin, $"floor_{n:00}.json")));
    private static CampaignPlan Campaign() => PlanFormat.Deserialize<CampaignPlan>(File.ReadAllText(Path.Combine(Builtin, "campaign.json")));

    [Fact]
    public void EveryArchetypeBody_AndTheVerger_AreInTheViewerFolder()
    {
        foreach (var body in NpcBodies.Archetypes.Append("verger"))
            Assert.True(File.Exists(Path.Combine(Root, "assets", "models", "viewer", $"{body}.glb")), body);
    }

    [Theory]
    [InlineData("cast_maud", "npc_old_woman")]
    [InlineData("cast_crane", "npc_merchant")]
    [InlineData("cast_tobit", "npc_priest")]
    [InlineData("cast_grisel", "npc_prisoner")]
    [InlineData("cast_ulf", "npc_old_man")]
    public void TheCast_WearTheBodiesTheBibleGivesThem(string castId, string body)
    {
        var cast = Campaign().CastFor(castId)!;
        Assert.Equal(body, NpcBodies.For(castId, cast.Name, false, cast.Role, cast.Persona?.VoiceStyle));
    }

    [Fact]
    public void EveryFloorNpc_ResolvesToTheSameBodyAsTheirCastMember()
    {
        var campaign = Campaign();
        for (var f = 1; f <= 20; f++)
            foreach (var n in Floor(f).Npcs.Where(n => n.CastId != null))
            {
                var cast = campaign.CastFor(n.CastId)!;
                var own = NpcBodies.For(n.Id, n.Name, false, n.Persona?.VoiceStyle, cast.Role, cast.Persona?.VoiceStyle);
                var expected = NpcBodies.For(cast.Id, cast.Name, false, cast.Role, cast.Persona?.VoiceStyle);
                Assert.True(expected == own, $"floor {f} {n.Id}: {own}, cast says {expected}");
            }
    }

    [Fact]
    public void TheVerger_HasABody()
    {
        var v = Campaign().Antagonist;
        Assert.Equal("verger", NpcBodies.Villain(v.Name, v.Epithet, v.Persona?.VoiceStyle));
    }

    [Fact]
    public void GriselLeadsTheUnswearing_OnFloorSeven()
    {
        var grisel = Assert.Single(Floor(7).Npcs, n => n.CastId == "cast_grisel");
        Assert.True(grisel.Ritualist);
        Assert.Equal("Grisel Thorn", grisel.Name);
        Assert.Equal("ritual", Campaign().Floors.First(f => f.Floor == 7).Objective);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(6)]
    [InlineData(11)]
    [InlineData(16)]
    [InlineData(18)]
    [InlineData(19)]
    public void Maud_IsOnEveryFloorTheBibleWalksHerThrough(int floor)
    {
        var maud = Assert.Single(Floor(floor).Npcs, n => n.CastId == "cast_maud");
        Assert.NotEmpty(maud.Script.Nodes);
        Assert.Contains(maud.Script.Nodes, n => n.Id == maud.Script.Start);
    }

    [Fact]
    public void TheVergerDies_InDifferentWords_IfGormWakes()
    {
        var boss = Floor(20).Boss!;
        Assert.Equal("wake_gorm", boss.AltDeathFlag);
        Assert.NotNull(boss.AltDeathLine);
        Assert.NotEqual(boss.DeathLine.Text, boss.AltDeathLine!.Text);
        Assert.Contains("thank you", boss.AltDeathLine.Text);
    }

    [Fact]
    public void TheVerger_SpeaksAtHalfHealth_EitherWay()
    {
        var boss = Floor(20).Boss!;
        Assert.Contains("new Verger", boss.HalfLine!.Text);
        Assert.Contains("turning over", boss.AltHalfLine!.Text);
    }

    [Fact]
    public void EveryHero_HasFiveVoicedDanceLines_InTheShippedBank()
    {
        var campaign = Campaign();
        var bank = Path.Combine(Root, "assets", "voice", "builtin");
        foreach (var name in new[] { "Brand", "Sable", "Wren", "Ilex" })
        {
            var set = campaign.Barks.First(b => b.Member == name);
            var dance = set.Lines.Where(l => l.Trigger == BarkTrigger.Dance).ToList();
            Assert.True(dance.Count >= 5, $"{name} has {dance.Count} dance lines");
            foreach (var l in dance)
            {
                Assert.NotNull(l.Line.VoiceId);
                Assert.True(File.Exists(Path.Combine(bank, l.Line.VoiceId + ".wav")), $"{name}: no audio for \"{l.Line.Text}\"");
            }
        }
    }
}
