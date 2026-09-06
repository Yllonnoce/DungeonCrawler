using DungeonCrawler.Adventure;
using Xunit;

namespace DungeonCrawler.Tests.Adventure;

public class MemorySummaryTests
{
    private static FloorRecord Floor(int n, int kills = 6, int downs = 0, float seconds = 300, float exitHp = 0.9f, int sneak = 0, int spells = 0, int weapons = 20)
        => new() { Floor = n, Title = $"Floor {n}", Kills = kills, Downs = downs, Seconds = seconds, ExitHpFraction = exitHp, SneakHits = sneak, SpellCasts = spells, WeaponAttacks = weapons, Completed = true };
    private static FloorRecord With(FloorRecord r, Action<FloorRecord> edit) { edit(r); return r; }

    [Fact]
    public void Empty_WhenNothingHappened()
    {
        Assert.Equal("", MemorySummary.Build(new CampaignMemoryDto(), 2));
        var m = new CampaignMemoryDto { Floors = { new FloorRecord { Floor = 1 } } };
        Assert.Equal("", MemorySummary.Build(m, 2));
    }

    [Fact]
    public void Dominant_And_Struggling_Reads()
    {
        var dominant = new CampaignMemoryDto { Floors = { Floor(1), Floor(2), Floor(3) } };
        Assert.Contains("dominant", MemorySummary.Build(dominant, 4));
        var struggling = new CampaignMemoryDto { Floors = { Floor(1, downs: 3, exitHp: 0.3f), With(Floor(2, downs: 2), r => r.Wipes = 1) } };
        var text = MemorySummary.Build(struggling, 3);
        Assert.Contains("struggling", text);
        Assert.Contains("wiped", text.ToLowerInvariant());
    }

    [Fact]
    public void StyleTags_And_Threads()
    {
        var m = new CampaignMemoryDto
        {
            Floors =
            {
                With(Floor(1, sneak: 5, kills: 6), r => { r.PossessSeconds["Sable"] = 280f; r.NpcFates.Add("Mara rescued"); r.QuestOutcomes.Add("The Relic completed"); }),
                With(Floor(2, sneak: 4, kills: 5), r => { r.PossessSeconds["Sable"] = 250f; r.PossessSeconds["Brand"] = 50f; r.NpcFates.Add("Teodor died"); r.QuestOutcomes.Add("Hold the Line failed"); }),
            },
        };
        var text = MemorySummary.Build(m, 3);
        Assert.Contains("stealthy", text);
        Assert.Contains("mostly plays Sable", text);
        Assert.Contains("rescued and may reappear: Mara", text);
        Assert.Contains("dead: Teodor", text);
        Assert.Contains("failed: Hold the Line", text);
        Assert.Contains("Floor 2 \"Floor 2\"", text);
    }

    [Fact]
    public void OnlyFloorsBefore_AreUsed_And_OlderAreCondensed()
    {
        var m = new CampaignMemoryDto();
        for (var f = 1; f <= 6; f++) m.Floors.Add(Floor(f));
        var text = MemorySummary.Build(m, 5);
        Assert.Contains("Earlier: floor 1", text);
        Assert.DoesNotContain("Floor 5", text);
        Assert.DoesNotContain("Floor 6", text);
        Assert.Contains("Floor 4 \"Floor 4\"", text);
        Assert.True(MemorySummary.Build(m, 7, maxChars: 200).Length <= 200);
    }
}
