using DungeonCrawler.Adventure;
using DungeonCrawler.Dungeon.Generation;
using Xunit;

namespace DungeonCrawler.Tests.Adventure;

public class BanditTests
{
    private static (FloorLayout, FloorMenu, int seed) CampFloor(StructureKind kind)
    {
        for (var seed = 1; seed < 400; seed++)
        {
            var (layout, _, menu) = TestAdventure.Floor(3, seed);
            if (layout.Structures.Any(s => s.Kind == kind) && menu.Rooms.Any(r => (kind == StructureKind.Camp ? r.IsCamp : r.HasGate) && !r.IsStart)) return (layout, menu, seed);
        }
        throw new InvalidOperationException("no floor with that structure in 400 seeds");
    }

    [Fact]
    public void Menu_OffersBandits_OnlyWithACamp()
    {
        var (_, camp, _) = CampFloor(StructureKind.Camp);
        Assert.Contains(camp.Roster, e => e.IsBandit);
        Assert.Contains(camp.Rooms, r => r.IsCamp);
        var (_, gate, _) = CampFloor(StructureKind.Gate);
        if (!gate.Rooms.Any(r => r.IsCamp)) Assert.DoesNotContain(gate.Roster, e => e.IsBandit);
    }

    [Fact]
    public void OfflineDesigner_PostsABand_AtEveryCamp_AndDefendsGates()
    {
        var (layout, menu, seed) = CampFloor(StructureKind.Camp);
        var plan = OfflineDesigner.PlanFloor(menu, layout, seed);
        foreach (var camp in menu.CampRooms)
            Assert.Contains(plan.Collectives, c => c.Goal.Room == camp.Index && c.Goal.Kind == GoalKind.Guard && c.Members.All(m => menu.IsBandit(m.EnemyId)));
        Assert.All(plan.Collectives.Where(c => menu.Room(c.Goal.Room)?.IsCamp != true), c => Assert.DoesNotContain(c.Members, m => menu.IsBandit(m.EnemyId)));
        Assert.Contains(plan.Rooms, r => r.Role == RoomRole.Camp);
        Assert.True(PlanValidator.Validate(layout, plan, menu).Ok);

        var (gl, gm, gs) = CampFloor(StructureKind.Gate);
        var gplan = OfflineDesigner.PlanFloor(gm, gl, gs);
        Assert.Contains(gplan.Collectives, c => c.Goal.Kind == GoalKind.Defend && c.Goal.Objective?.Kind == ObjectiveKind.Gate);
        var contents = PlanResolver.Resolve(gl, gplan, TestAdventure.Crypt, TestAdventure.Roster, 3, gs);
        var gate = gl.Structures.First(s => s.Kind == StructureKind.Gate);
        Assert.Contains(contents.Collectives, c => c.ObjectiveCell == gate.Anchor);
    }

    [Fact]
    public void Validator_KeepsBanditsInCamps_AndFillsEmptyCamps()
    {
        var (layout, menu, seed) = CampFloor(StructureKind.Camp);
        var plan = OfflineDesigner.PlanFloor(menu, layout, seed);
        var camp = menu.CampRooms.First();
        var band = plan.Collectives.First(c => c.Goal.Room == camp.Index);
        var elsewhere = menu.Rooms.First(r => !r.IsStart && !r.IsCamp && !r.IsVault && !r.IsArena && r.FreeCells > 3);
        band.Goal.Room = elsewhere.Index; // bandits wandered off
        plan.Loners.Add(new LonerPlan { EnemyId = "enemy.bandit", Room = elsewhere.Index });
        var report = PlanValidator.Validate(layout, plan, menu);
        Assert.True(report.Ok, report.ToString());
        Assert.Contains(report.Repairs, r => r.Contains("bandits only live in camps"));
        Assert.Contains(report.Repairs, r => r.Contains("had no bandits") || r.Contains("became bandits"));
        Assert.Contains(plan.Collectives, c => c.Goal.Room == camp.Index && c.Members.All(m => menu.IsBandit(m.EnemyId)));
        Assert.DoesNotContain(plan.Loners, l => menu.IsBandit(l.EnemyId));
        Assert.All(plan.Collectives.Where(c => c.Goal.Room != camp.Index), c => Assert.DoesNotContain(c.Members, m => menu.IsBandit(m.EnemyId)));

        // a gate objective must point at a gate room
        var gplan = OfflineDesigner.PlanFloor(menu, layout, seed);
        var g = gplan.Collectives[^1];
        g.Goal.Kind = GoalKind.Defend;
        g.Goal.Objective = new ObjectivePlan { Kind = ObjectiveKind.Gate, Ref = camp.Index.ToString() };
        var r2 = PlanValidator.Validate(layout, gplan, menu);
        Assert.Contains(r2.Repairs, r => r.Contains("objective missing"));
    }

    [Fact]
    public void Resolver_ClustersBandits_AroundTheFire()
    {
        var (layout, menu, seed) = CampFloor(StructureKind.Camp);
        var plan = OfflineDesigner.PlanFloor(menu, layout, seed);
        var contents = PlanResolver.Resolve(layout, plan, TestAdventure.Crypt, TestAdventure.Roster, 3, seed);
        var camp = layout.Structures.First(s => s.Kind == StructureKind.Camp);
        var band = contents.Enemies.Where(e => menu.IsBandit(e.EnemyId)).ToList();
        Assert.NotEmpty(band);
        Assert.All(band, e => Assert.True(e.Cell.ManhattanDistance(camp.Anchor) <= 3, $"bandit at {e.Cell} far from the fire {camp.Anchor}"));
        Assert.DoesNotContain(band, e => e.Cell == camp.Anchor);
    }

    [Fact]
    public void Prompt_FlagsStructuresAndWater()
    {
        var (layout, menu, _) = CampFloor(StructureKind.Camp);
        var text = DungeonCrawler.Adventure.Ai.PromptBuilder.RenderMenu(menu);
        Assert.Contains("bandit-camp", text);
        Assert.Contains("bandit (camp rooms only)", text);
        if (layout.Water.Count > 0) Assert.True(text.Contains("pond") || text.Contains("stream"));
    }
}
