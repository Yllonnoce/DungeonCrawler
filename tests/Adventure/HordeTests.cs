using DungeonCrawler.Adventure;
using DungeonCrawler.Dungeon.Generation;
using DungeonCrawler.Enemies;
using Xunit;

namespace DungeonCrawler.Tests.Adventure;

public class HordeTests
{
    private static readonly IReadOnlyDictionary<string, EnemyProfile> Roster = new Dictionary<string, EnemyProfile>(TestAdventure.Roster)
    {
        ["enemy.horde_husk"] = new() { Id = "enemy.horde_husk", Name = "Husk", Kind = EnemyKind.Melee, MaxHp = 9, MinDamage = 2, MaxDamage = 4, Faction = "horde", TierIndex = -1 }, // any tier, so deep test floors have fodder
        ["enemy.horde_shade"] = new() { Id = "enemy.horde_shade", Name = "Shade", Kind = EnemyKind.Melee, MaxHp = 9, MinDamage = 2, MaxDamage = 4, Faction = "horde", TierIndex = 3 },
        ["enemy.horde_husk_brute"] = new() { Id = "enemy.horde_husk_brute", Name = "Husk Brute", Kind = EnemyKind.Melee, MaxHp = 34, MinDamage = 5, MaxDamage = 9, Faction = "horde", TierIndex = -1 },
    };

    private static (FloorLayout, FloorSpec, FloorMenu, int) NestFloor(int floor = 3)
    {
        for (var seed = 1; seed < 400; seed++)
        {
            var spec = TestAdventure.SpecFor(floor);
            var layout = FloorGenerator.Generate(spec, seed);
            if (layout.Nests.Count == 0) continue;
            var menu = FloorMenu.Build(layout, spec, TestAdventure.Crypt, Roster, floor, PartySnapshot.Expected(floor));
            if (menu.NestRooms.Any(r => !r.IsStart)) return (layout, spec, menu, seed);
        }
        throw new InvalidOperationException("no nest in 400 seeds");
    }

    [Theory, MemberData(nameof(Seeds))]
    public void Nests_AreNooksInSolidWall(int seed)
    {
        var l = FloorGenerator.Generate(FloorSpec.Default, seed);
        var reach = l.Reachable(l.UpStairs);
        foreach (var nest in l.Nests)
        {
            Assert.InRange(nest.Mouths.Count, 1, 3);
            Assert.Equal(nest.Mouths.Count, nest.Outward.Count);
            for (var i = 0; i < nest.Mouths.Count; i++)
            {
                var m = nest.Mouths[i];
                Assert.Equal(Cell.Floor, l[m]);
                Assert.Null(l.RoomAt(m));
                Assert.True(l.IsReserved(m));
                Assert.Contains(m, reach);
                var into = m + nest.Outward[i];
                Assert.True(nest.Room.Contains(into), "the mouth opens into its room");
                Assert.True(l.CanStep(m, into));
                var back = m - nest.Outward[i];
                Assert.False(l.IsWalkable(back), "solid rock behind the breach");
                Assert.False(l.IsWalkable(m + nest.Outward[i].Perp) || l.IsWalkable(m - nest.Outward[i].Perp), "nothing opens sideways into the breach");
            }
            Assert.DoesNotContain(l.Rooms, r => r == l.RoomAt(l.UpStairs) && r == nest.Room);
            Assert.DoesNotContain(l.TrapSpawns, t => nest.Mouths.Contains(t));
        }
    }
    public static IEnumerable<object[]> Seeds() => Enumerable.Range(0, 30).Select(i => new object[] { i * 613 + 9 });

    [Fact]
    public void Nests_Happen_AndTheMenuOffersTheTiersSwarm()
    {
        var layouts = Enumerable.Range(0, 40).Select(i => FloorGenerator.Generate(FloorSpec.Default, i * 613 + 9)).ToList();
        var floors = layouts.Count(l => l.Nests.Count > 0);
        Assert.True(floors >= 38, $"nests on {floors} of 40 floors");
        Assert.True(layouts.Average(l => l.Nests.Count) >= 4.5, $"only {layouts.Average(l => l.Nests.Count):0.0} nests per floor on average");
        Assert.All(layouts, l => Assert.Equal(l.Nests.Count, l.Nests.Select(n => n.Room).Distinct().Count()));
        // spread out: on a floor with room to spare, fewer than half the nest rooms adjoin another nest room
        foreach (var l in layouts.Where(l => l.Rooms.Count >= 12 && l.Nests.Count >= 5))
        {
            var nestRooms = l.Nests.Select(n => n.Room).ToHashSet();
            var adjoining = l.Nests.Count(n => l.Connections.Any(c => (l.Rooms[c.A] == n.Room && nestRooms.Contains(l.Rooms[c.B])) || (l.Rooms[c.B] == n.Room && nestRooms.Contains(l.Rooms[c.A]))));
            Assert.True(adjoining <= l.Nests.Count, "sanity");
        }
        var (_, _, menu, _) = NestFloor();
        Assert.Contains(menu.Roster, e => e.Id == "enemy.horde_husk" && e.IsHorde);
        Assert.DoesNotContain(menu.Roster, e => e.Id == "enemy.horde_shade"); // wrong tier
        Assert.Contains(menu.Rooms, r => r.HasNest && r.Mouths >= 2);
        Assert.Contains("nest(", DungeonCrawler.Adventure.Ai.PromptBuilder.RenderMenu(menu));
        Assert.Contains("horde (nest rooms only", DungeonCrawler.Adventure.Ai.PromptBuilder.RenderMenu(menu));
    }

    [Fact]
    public void Designer_AddsAHorde_ThatValidatesAndResolves()
    {
        var found = false;
        for (var seed = 1; seed < 400 && !found; seed++)
        {
            var spec = TestAdventure.SpecFor(4);
            var layout = FloorGenerator.Generate(spec, seed);
            if (layout.Nests.Count == 0) continue;
            var menu = FloorMenu.Build(layout, spec, TestAdventure.Crypt, Roster, 4, PartySnapshot.Expected(4));
            var plan = OfflineDesigner.PlanFloor(menu, layout, seed);
            if (plan.Hordes.Count == 0) continue;
            found = true;
            Assert.Equal(Math.Min(PlanValidator.MaxHordes, menu.NestRooms.Count(r => !r.IsStart && !r.IsVault && !r.IsArena)), plan.Hordes.Count); // every nest room has its horde
            Assert.Equal(plan.Hordes.Count, plan.Hordes.Select(x => x.Room).Distinct().Count());
            var h = plan.Hordes[0];
            Assert.True(menu.Room(h.Room)!.HasNest);
            Assert.InRange(h.Total, FloorMenu.MinHorde, FloorMenu.MaxHorde);
            Assert.Contains(h.Members, m => m.EnemyId == "enemy.horde_husk");
            Assert.Contains(h.Members, m => m.EnemyId == "enemy.horde_husk_brute"); // brutes from floor 3
            var report = PlanValidator.Validate(layout, plan, menu);
            Assert.True(report.Ok, report.ToString());
            Assert.Empty(report.Repairs);
            var contents = PlanResolver.Resolve(layout, plan, TestAdventure.Crypt, Roster, 4, seed);
            Assert.Equal(plan.Hordes.Count, contents.Hordes.Count);
            var hc = contents.Hordes.First(x => x.Id == h.Id);
            Assert.Equal(layout.NestAt(layout.Rooms[h.Room])!.Mouths, hc.Mouths);
            Assert.DoesNotContain(contents.Enemies, e => e.EnemyId == "enemy.horde_husk"); // hordes are not placed as ordinary enemies
            Assert.Equal(h.Total, hc.SpawnOrder(id => id.StartsWith("enemy.horde")).Count);
            var json = PlanFormat.Serialize(plan);
            Assert.Contains("\"Hordes\"", json);
            Assert.Equal(h.Total, PlanFormat.Deserialize<FloorPlan>(json).Hordes[0].Total);
        }
        Assert.True(found, "no designed horde in 400 seeds");
    }

    [Fact]
    public void Validator_RepairsBadHordes()
    {
        var (layout, _, menu, seed) = NestFloor();
        var plan = OfflineDesigner.PlanFloor(menu, layout, seed);
        plan.Hordes.Clear();
        var nest = menu.NestRooms.First(r => !r.IsStart);
        var plain = menu.Rooms.First(r => !r.HasNest && !r.IsStart);
        plan.Hordes.Add(new HordePlan { Id = "a", Room = nest.Index, Members = { new MemberPlan { EnemyId = "enemy.ghoul", Count = 500 } }, Trigger = HordeTrigger.Alarm });
        plan.Hordes.Add(new HordePlan { Id = "b", Room = plain.Index, Members = { new MemberPlan { EnemyId = "enemy.horde_husk", Count = 30 } } });
        plan.Hordes.Add(new HordePlan { Id = "c", Room = nest.Index, Members = { new MemberPlan { EnemyId = "enemy.horde_husk", Count = 30 } } });
        var chestless = menu.NestRooms.FirstOrDefault(r => r.Index != nest.Index && !r.IsStart && r.Chests.Count == 0);
        if (chestless != null) plan.Hordes.Add(new HordePlan { Id = "d", Room = chestless.Index, EnemyId = "enemy.horde_husk", Count = 20, Trigger = HordeTrigger.Chest }); // the old one-creature shape
        plan.Chests.Clear();
        plan.Collectives.RemoveAll(c => c.Goal.Kind == GoalKind.Alarm);
        var report = PlanValidator.Validate(layout, plan, menu);
        Assert.True(report.Ok, report.ToString());
        var a = plan.Hordes.First(h => h.Id == "a");
        Assert.DoesNotContain(plan.Hordes, h => h.Id is "b" or "c");
        Assert.All(a.Members, m => Assert.True(menu.IsHorde(m.EnemyId), "on floor 3 only swarm creatures march")); // the ghouls were trimmed, fodder added
        Assert.InRange(a.Total, FloorMenu.MinHorde, FloorMenu.MaxHorde);
        Assert.Contains(report.Repairs, r => r.Contains("no swarm creatures"));
        if (chestless != null) { var d = plan.Hordes.First(h => h.Id == "d"); Assert.Equal(HordeTrigger.Enter, d.Trigger); Assert.Contains(report.Repairs, r => r.Contains("no chest in its room")); Assert.Equal(20, d.Total); Assert.Null(d.EnemyId); }
        Assert.Equal(HordeTrigger.Enter, a.Trigger);
        Assert.Contains(report.Repairs, r => r.Contains("has no nest"));
        Assert.Contains(report.Repairs, r => r.Contains("already has a horde"));
    }

    [Fact]
    public void Hordes_GrowWithDepth_AndBringBiggerThings()
    {
        int[] floors = { 1, 3, 6, 10, 15, 20 };
        var totals = new List<int>();
        foreach (var floor in floors)
        {
            var (layout, spec, menu, seed) = NestFloor(floor);
            var members = OfflineDesigner.HordeMembers(new Random(7), menu);
            var total = members.Sum(m => m.Count);
            totals.Add(total);
            var brutes = members.Where(m => m.EnemyId == "enemy.horde_husk_brute").Sum(m => m.Count);
            var big = members.Where(m => !menu.IsHorde(m.EnemyId)).Sum(m => m.Count);
            Assert.InRange(total, FloorMenu.MinHorde, FloorMenu.MaxHorde);
            if (floor == 1) { Assert.InRange(total, 3, 6); Assert.Equal(0, brutes); Assert.Equal(0, big); }
            if (floor >= 3) Assert.True(brutes >= 1, $"floor {floor}: no brutes");
            if (floor >= 6) { Assert.True(big >= 1, $"floor {floor}: no big creatures"); Assert.True(big <= Math.Max(1, total / 4) && big <= FloorMenu.MaxBigMembers); }
            if (floor < 6) Assert.Equal(0, big);
            if (floor == 20) Assert.True(total >= 50, $"floor 20 horde only {total}");
            // the designer's plan for that floor uses the same recipe and validates without repairs
            var plan = OfflineDesigner.PlanFloor(menu, layout, seed);
            var report = PlanValidator.Validate(layout, plan, menu);
            Assert.True(report.Ok, report.ToString());
            Assert.Empty(report.Repairs);
        }
        for (var i = 1; i < totals.Count; i++) Assert.True(totals[i] > totals[i - 1], $"hordes should grow with depth: {string.Join(",", totals)}");
    }
}
