using DungeonCrawler.Adventure;
using DungeonCrawler.Dungeon;
using DungeonCrawler.Dungeon.Generation;
using DungeonCrawler.Enemies;
using Xunit;
using Xunit.Abstractions;

namespace DungeonCrawler.Tests.Adventure;

public static class TestAdventure
{
    public static readonly IReadOnlyDictionary<string, EnemyProfile> Roster = new Dictionary<string, EnemyProfile>
    {
        ["enemy.ghoul"] = new() { Id = "enemy.ghoul", Name = "Ghoul", Kind = EnemyKind.Melee, MaxHp = 30, MinDamage = 6, MaxDamage = 10 },
        ["enemy.skeleton_archer"] = new() { Id = "enemy.skeleton_archer", Name = "Skeleton Archer", Kind = EnemyKind.Ranged, MaxHp = 24, MinDamage = 5, MaxDamage = 9 },
        ["enemy.bone_priest"] = new() { Id = "enemy.bone_priest", Name = "Bone Priest", Kind = EnemyKind.Caster, MaxHp = 40, MinDamage = 8, MaxDamage = 14 },
        ["enemy.crypt_lord"] = new() { Id = "enemy.crypt_lord", Name = "Crypt Lord", Kind = EnemyKind.Melee, MaxHp = 300, MinDamage = 14, MaxDamage = 22, IsBoss = true, Signature = Signature.Slam },
        ["enemy.bandit"] = new() { Id = "enemy.bandit", Name = "Bandit", Kind = EnemyKind.Melee, MaxHp = 28, MinDamage = 5, MaxDamage = 9, Faction = "bandits" },
        ["enemy.bandit_archer"] = new() { Id = "enemy.bandit_archer", Name = "Bandit Archer", Kind = EnemyKind.Ranged, MaxHp = 22, MinDamage = 5, MaxDamage = 8, Faction = "bandits" },
        ["enemy.bandit_chief"] = new() { Id = "enemy.bandit_chief", Name = "Bandit Chief", Kind = EnemyKind.Melee, MaxHp = 60, MinDamage = 9, MaxDamage = 14, Faction = "bandits" },
    };

    public static readonly TierProfile Crypt = new()
    {
        Id = "tier.crypt", DisplayName = "Crypt", Index = 0,
        EnemyPool = new[] { "enemy.ghoul", "enemy.skeleton_archer", "enemy.bone_priest" },
        EnemyWeights = new[] { 5f, 3f, 2f },
        BaseEnemyCount = 4, BossId = "enemy.crypt_lord",
    };

    public static FloorSpec SpecFor(int floor) => FloorSpec.Default with
    {
        EnemyCount = Math.Min(16, 4 + TierRules.FloorInTier(floor)), BossFloor = TierRules.IsBossFloor(floor),
        PitCount = TierRules.IsBossFloor(floor) ? 0 : 1, LockedChestCount = floor >= 2 ? 1 : 0,
    };

    public static (FloorLayout Layout, FloorSpec Spec, FloorMenu Menu) Floor(int floor, int seed)
    {
        var spec = SpecFor(floor);
        var layout = FloorGenerator.Generate(spec, seed);
        var menu = FloorMenu.Build(layout, spec, Crypt, Roster, floor, PartySnapshot.Expected(floor));
        return (layout, menu is null ? throw new InvalidOperationException() : spec, menu);
    }
}

public class OfflineDesignerTests
{
    private readonly ITestOutputHelper _out;
    public OfflineDesignerTests(ITestOutputHelper output) => _out = output;

    public static IEnumerable<object[]> Cases()
    {
        for (var floor = 1; floor <= 20; floor++)
            for (var s = 0; s < 5; s++)
                yield return new object[] { floor, floor * 1000 + s * 7919 + 13 };
    }

    [Theory, MemberData(nameof(Cases))]
    public void Designs_Validate_Without_Repairs(int floor, int seed)
    {
        var (layout, _, menu) = TestAdventure.Floor(floor, seed);
        var plan = OfflineDesigner.PlanFloor(menu, layout, seed);
        var before = PlanFormat.Serialize(plan);
        var report = PlanValidator.Validate(layout, plan, menu);
        if (report.Repairs.Count > 0) _out.WriteLine(string.Join("\n", report.Repairs));
        Assert.True(report.Ok, report.ToString());
        Assert.Empty(report.Repairs);
        Assert.Equal(before, PlanFormat.Serialize(plan));
        Assert.InRange(plan.EnemyTotal, menu.MinEnemies, menu.MaxEnemies);
        Assert.Equal(layout.Rooms.Count, plan.Rooms.Count);
        Assert.All(plan.Rooms, r => Assert.False(string.IsNullOrWhiteSpace(r.Name)));
        Assert.NotEmpty(plan.Collectives);
        Assert.Equal(TierRules.IsBossFloor(floor), plan.Boss != null);
        Assert.False(string.IsNullOrWhiteSpace(plan.IntroLine.Text));
    }

    [Fact]
    public void PlanFloor_OnFloorsWithHazards_NeedsNoRepairs()
    {
        // the designer must not put its extra chests and traps where the generator reserved a Press or a Spike Bed
        var failures = new List<string>();
        for (var floor = 3; floor <= TierRules.FinalFloor; floor++)
            for (var seed = 1; seed <= 12; seed++)
            {
                var spec = TestAdventure.SpecFor(floor) with { Floor = floor };
                var layout = FloorGenerator.Generate(spec, seed);
                if (layout.Hazards.Count == 0) continue;
                var menu = FloorMenu.Build(layout, spec, TestAdventure.Crypt, TestAdventure.Roster, floor, PartySnapshot.Expected(floor));
                var plan = OfflineDesigner.PlanFloor(menu, layout, seed);
                var report = PlanValidator.Validate(layout, plan, menu);
                if (!report.Ok || report.Repairs.Count > 0) failures.Add($"floor {floor} seed {seed}: ok={report.Ok} repairs=[{string.Join("; ", report.Repairs)}]");
            }
        Assert.True(failures.Count == 0, string.Join("\n", failures.Take(6)));
    }

    [Fact]
    public void PlanFloor_NeverThrows_AcrossFloorsAndSeeds()
    {
        // every floor of a run, many seeds: a goal that runs out of rooms to claim must fall back, not crash the designer
        var failures = new List<string>();
        foreach (var kind in Enum.GetValues<LayoutKind>())
            for (var floor = 1; floor <= TierRules.FinalFloor; floor++)
                for (var seed = 1; seed <= 12; seed++)
                {
                    var spec = TestAdventure.SpecFor(floor) with { Floor = floor, Layout = kind };
                    var layout = FloorGenerator.Generate(spec, seed);
                    var menu = FloorMenu.Build(layout, spec, TestAdventure.Crypt, TestAdventure.Roster, floor, PartySnapshot.Expected(floor));
                    var plan = OfflineDesigner.PlanFloor(menu, layout, seed);
                    Assert.All(plan.Collectives, c => Assert.True(c.Goal.Room >= 0, $"{kind} floor {floor} seed {seed}: {c.Id} has no room"));
                    var report = PlanValidator.Validate(layout, plan, menu);
                    if (!report.Ok || report.Repairs.Count > 0) failures.Add($"{kind} floor {floor} seed {seed}: ok={report.Ok} repairs=[{string.Join("; ", report.Repairs)}]");
                }
        Assert.True(failures.Count == 0, $"{failures.Count} plans needed repair:\n{string.Join("\n", failures.Take(8))}");
    }

    [Fact]
    public void NpcQuests_AppearAndValidate_AcrossTemplates()
    {
        var templates = new HashSet<QuestTemplate>();
        var withNpc = 0;
        for (var seed = 1; seed < 60; seed++)
        {
            var (layout, _, menu) = TestAdventure.Floor(2 + seed % 5, seed);
            var plan = OfflineDesigner.PlanFloor(menu, layout, seed);
            var report = PlanValidator.Validate(layout, plan, menu);
            Assert.True(report.Ok);
            Assert.Empty(report.Repairs);
            if (plan.Npcs.Count == 0) continue;
            withNpc++;
            var npc = Assert.Single(plan.Npcs);
            var quest = Assert.Single(plan.Quests);
            Assert.Equal(quest.Id, npc.QuestId);
            Assert.Equal(npc.Id, quest.GiverNpc);
            templates.Add(quest.Template);
            var state = QuestMachine.Create(quest, plan, menu.Floor, 3);
            Assert.False(string.IsNullOrWhiteSpace(state.ObjectiveText));
            if (quest.Template == QuestTemplate.Decide)
                Assert.True(npc.Script.Nodes.SelectMany(n => n.Choices).Count(c => c.Effect == DialogueEffect.Choose && c.Flag != null) >= 2);
            else
            {
                Assert.Contains(npc.Script.Nodes, n => n.Choices.Any(c => c.Effect == DialogueEffect.AcceptQuest));
                Assert.Contains(npc.Script.Nodes, n => n.Choices.Any(c => c.Effect == DialogueEffect.TurnIn));
            }
            var contents = PlanResolver.Resolve(layout, plan, TestAdventure.Crypt, TestAdventure.Roster, menu.Floor, seed);
            var placed = Assert.Single(contents.Npcs);
            Assert.True(layout.Rooms[npc.Room].Contains(placed.Cell));
            if (quest.Template == QuestTemplate.Recover)
            {
                var item = Assert.Single(contents.QuestItems);
                Assert.True(layout.Rooms[quest.Params.ItemRoom!.Value].Contains(item.Cell));
                Assert.DoesNotContain(contents.Enemies, e => e.Cell == item.Cell);
            }
        }
        Assert.True(withNpc >= 20, $"only {withNpc} floors had an NPC");
        Assert.True(templates.Count >= 4, $"only {templates.Count} templates seen");
    }

    [Fact]
    public void Deterministic()
    {
        var (layout, _, menu) = TestAdventure.Floor(4, 99);
        var a = PlanFormat.Serialize(OfflineDesigner.PlanFloor(menu, layout, 99));
        var b = PlanFormat.Serialize(OfflineDesigner.PlanFloor(menu, layout, 99));
        Assert.Equal(a, b);
        Assert.NotEqual(a, PlanFormat.Serialize(OfflineDesigner.PlanFloor(menu, layout, 100)));
    }

    [Fact]
    public void UsesEveryGoalKind_AcrossSeeds()
    {
        var kinds = new HashSet<GoalKind>();
        for (var seed = 1; seed < 30; seed++)
        {
            var (layout, _, menu) = TestAdventure.Floor(8, seed);
            foreach (var c in OfflineDesigner.PlanFloor(menu, layout, seed).Collectives) kinds.Add(c.Goal.Kind);
        }
        Assert.Superset(new HashSet<GoalKind> { GoalKind.Guard, GoalKind.Defend, GoalKind.Patrol, GoalKind.Alarm }, kinds);
    }

    [Fact]
    public void Difficulty_ScalesEnemyCount()
    {
        var (layout, _, menu) = TestAdventure.Floor(6, 5);
        var easy = OfflineDesigner.PlanFloor(menu, layout, 5, 0.5f).EnemyTotal;
        var hard = OfflineDesigner.PlanFloor(menu, layout, 5, 1.5f).EnemyTotal;
        Assert.True(hard > easy, $"hard {hard} should exceed easy {easy}");
    }

    [Fact]
    public void Outline_CoversEveryFloorAndTier()
    {
        var outline = OfflineDesigner.Outline(12345, new[] { TestAdventure.Crypt });
        Assert.Equal(TierRules.FinalFloor, outline.Floors.Count);
        Assert.Equal(TierRules.TierCount, outline.Tiers.Count);
        Assert.All(outline.Floors, f => Assert.True(f.SpecOverrides.IsEmpty));
        var json = PlanFormat.Serialize(outline);
        var back = PlanFormat.Deserialize<CampaignPlan>(json);
        Assert.Equal(json, PlanFormat.Serialize(back));
    }
}
