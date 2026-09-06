using DungeonCrawler.Adventure;
using DungeonCrawler.Dungeon.Generation;
using Xunit;

namespace DungeonCrawler.Tests.Adventure;

public class PlanValidatorTests
{
    private static (FloorLayout Layout, FloorMenu Menu, FloorPlan Plan) Fresh(int floor = 3, int seed = 21)
    {
        var (layout, _, menu) = TestAdventure.Floor(floor, seed);
        var plan = OfflineDesigner.PlanFloor(menu, layout, seed);
        return (layout, menu, plan);
    }

    [Fact]
    public void WrongFloor_IsRejected()
    {
        var (layout, menu, plan) = Fresh();
        plan.Floor = 9;
        Assert.False(PlanValidator.Validate(layout, plan, menu).Ok);
    }

    [Fact]
    public void OlderLayout_IsAdaptedNotRejected()
    {
        var (layout, menu, plan) = Fresh();
        plan.LayoutHash ^= 1;
        var report = PlanValidator.Validate(layout, plan, menu);
        Assert.True(report.Ok);
        Assert.Contains(report.Repairs, e => e.Contains("older layout"));
        Assert.Equal(layout.ContentHash(), plan.LayoutHash);
    }

    [Fact]
    public void ZeroHash_IsFilledIn()
    {
        var (layout, menu, plan) = Fresh();
        plan.LayoutHash = 0;
        Assert.True(PlanValidator.Validate(layout, plan, menu).Ok);
        Assert.Equal(layout.ContentHash(), plan.LayoutHash);
    }

    [Fact]
    public void UnknownRoom_And_StartRoom_Collectives_AreDropped()
    {
        var (layout, menu, plan) = Fresh();
        plan.Collectives.Add(new CollectivePlan { Id = "ghosts", Members = { new MemberPlan { EnemyId = "enemy.ghoul" } }, Goal = new GoalPlan { Room = 999 } });
        plan.Collectives.Add(new CollectivePlan { Id = "campers", Members = { new MemberPlan { EnemyId = "enemy.ghoul" } }, Goal = new GoalPlan { Room = menu.StartRoom!.Index } });
        var report = PlanValidator.Validate(layout, plan, menu);
        Assert.True(report.Ok);
        Assert.DoesNotContain(plan.Collectives, c => c.Id is "ghosts" or "campers");
        Assert.Contains(report.Repairs, r => r.Contains("ghosts"));
        Assert.Contains(report.Repairs, r => r.Contains("campers"));
    }

    [Fact]
    public void UnknownEnemy_IsDropped_EmptyGroupRemoved()
    {
        var (layout, menu, plan) = Fresh();
        var room = menu.Rooms.First(r => !r.IsStart && !r.IsVault).Index;
        plan.Collectives.Add(new CollectivePlan { Id = "dragons", Members = { new MemberPlan { EnemyId = "enemy.dragon", Count = 3 } }, Goal = new GoalPlan { Room = room } });
        var report = PlanValidator.Validate(layout, plan, menu);
        Assert.DoesNotContain(plan.Collectives, c => c.Id == "dragons");
        Assert.Contains(report.Repairs, r => r.Contains("enemy.dragon"));
    }

    [Fact]
    public void Patrol_WithDisconnectedRoute_BecomesGuard()
    {
        var (layout, menu, plan) = Fresh();
        var a = menu.Rooms.First(r => !r.IsStart && !r.IsVault && !r.IsArena);
        var far = menu.Rooms.FirstOrDefault(r => r.Index != a.Index && !a.Neighbours.Contains(r.Index) && !r.IsStart && !r.IsVault);
        if (far == null) return; // fully connected tiny floor
        plan.Collectives.Clear(); plan.Loners.Clear();
        plan.Collectives.Add(new CollectivePlan { Id = "walkers", Members = { new MemberPlan { EnemyId = "enemy.ghoul", Count = 3 } }, Goal = new GoalPlan { Kind = GoalKind.Patrol, Room = a.Index, Route = { a.Index, far.Index } } });
        var report = PlanValidator.Validate(layout, plan, menu);
        Assert.True(report.Ok);
        Assert.Equal(GoalKind.Guard, plan.Collectives[0].Goal.Kind);
        Assert.Empty(plan.Collectives[0].Goal.Route);
    }

    [Fact]
    public void Alarm_WithoutTarget_PicksAnotherGroup_OrGuards()
    {
        var (layout, menu, plan) = Fresh();
        var room = menu.Rooms.First(r => !r.IsStart && !r.IsVault && !r.IsArena).Index;
        plan.Collectives.Clear(); plan.Loners.Clear();
        plan.Collectives.Add(new CollectivePlan { Id = "lone", Members = { new MemberPlan { EnemyId = "enemy.ghoul", Count = 3 } }, Goal = new GoalPlan { Kind = GoalKind.Alarm, Room = room, AlarmTarget = "nobody" } });
        PlanValidator.Validate(layout, plan, menu);
        Assert.Equal(GoalKind.Guard, plan.Collectives[0].Goal.Kind);

        plan.Collectives[0].Goal = new GoalPlan { Kind = GoalKind.Alarm, Room = room, AlarmTarget = "nobody" };
        plan.Collectives.Add(new CollectivePlan { Id = "other", Members = { new MemberPlan { EnemyId = "enemy.ghoul" } }, Goal = new GoalPlan { Room = room } });
        PlanValidator.Validate(layout, plan, menu);
        Assert.Equal(GoalKind.Alarm, plan.Collectives[0].Goal.Kind);
        Assert.Equal("other", plan.Collectives[0].Goal.AlarmTarget);
    }

    [Fact]
    public void Defend_WithBadObjective_BecomesGuard()
    {
        var (layout, menu, plan) = Fresh();
        var room = menu.Rooms.First(r => !r.IsStart && !r.IsVault && !r.IsArena).Index;
        plan.Collectives.Clear(); plan.Loners.Clear();
        plan.Collectives.Add(new CollectivePlan { Id = "d", Members = { new MemberPlan { EnemyId = "enemy.ghoul", Count = 3 } }, Goal = new GoalPlan { Kind = GoalKind.Defend, Room = room, Objective = new ObjectivePlan { Kind = ObjectiveKind.Chest, Ref = "77" } } });
        PlanValidator.Validate(layout, plan, menu);
        Assert.Equal(GoalKind.Guard, plan.Collectives[0].Goal.Kind);
        Assert.Null(plan.Collectives[0].Goal.Objective);
    }

    [Fact]
    public void OverBudget_IsTrimmed_ToTolerance()
    {
        var (layout, menu, plan) = Fresh(floor: 2);
        var room = menu.Rooms.First(r => !r.IsStart && !r.IsVault && !r.IsArena && r.FreeCells >= 12).Index;
        plan.Collectives.Clear(); plan.Loners.Clear();
        plan.Collectives.Add(new CollectivePlan { Id = "horde", Members = { new MemberPlan { EnemyId = "enemy.bone_priest", Count = 6 } }, Goal = new GoalPlan { Room = room } });
        plan.Collectives.Add(new CollectivePlan { Id = "horde2", Members = { new MemberPlan { EnemyId = "enemy.bone_priest", Count = 6 } }, Goal = new GoalPlan { Room = room } });
        var report = PlanValidator.Validate(layout, plan, menu);
        Assert.True(report.Ok);
        var cost = plan.Collectives.Sum(c => c.Members.Sum(m => m.Count * menu.CostOf(m.EnemyId)));
        Assert.True(cost <= menu.Budget * FloorMenu.BudgetTolerance || plan.EnemyTotal <= menu.MinEnemies, $"cost {cost} budget {menu.Budget}");
        Assert.Contains(report.Repairs, r => r.Contains("over budget"));
    }

    [Fact]
    public void TooFewEnemies_GetsLoners()
    {
        var (layout, menu, plan) = Fresh();
        plan.Collectives.Clear(); plan.Loners.Clear();
        var report = PlanValidator.Validate(layout, plan, menu);
        Assert.True(report.Ok);
        Assert.True(plan.EnemyTotal >= menu.MinEnemies);
        Assert.All(plan.Loners, l => Assert.NotEqual(menu.StartRoom!.Index, l.Room));
    }

    [Fact]
    public void RoomFull_DropsExtras()
    {
        var (layout, menu, plan) = Fresh();
        var small = menu.Rooms.Where(r => !r.IsStart && !r.IsVault && !r.IsArena).MinBy(r => r.FreeCells)!;
        plan.Collectives.Clear(); plan.Loners.Clear();
        plan.Collectives.Add(new CollectivePlan { Id = "crowd", Members = { new MemberPlan { EnemyId = "enemy.ghoul", Count = 6 } }, Goal = new GoalPlan { Room = small.Index } });
        plan.Collectives.Add(new CollectivePlan { Id = "crowd2", Members = { new MemberPlan { EnemyId = "enemy.ghoul", Count = 6 } }, Goal = new GoalPlan { Room = small.Index } });
        for (var i = 0; i < 6; i++) plan.Loners.Add(new LonerPlan { EnemyId = "enemy.ghoul", Room = small.Index });
        PlanValidator.Validate(layout, plan, menu);
        var wanted = plan.Collectives.Where(c => c.Goal.Room == small.Index).Sum(c => c.Members.Sum(m => m.Count)) + plan.Loners.Count(l => l.Room == small.Index);
        Assert.True(wanted <= small.FreeCells, $"{wanted} placed in a room with {small.FreeCells} free cells");
    }

    [Fact]
    public void Boss_OnNonBossFloor_IsDropped_And_BadSignatureCleared()
    {
        var (layout, menu, plan) = Fresh(floor: 3);
        plan.Boss = new BossPlan { Name = "Impostor" };
        PlanValidator.Validate(layout, plan, menu);
        Assert.Null(plan.Boss);

        var (layout5, menu5, plan5) = Fresh(floor: 5, seed: 8);
        plan5.Boss!.SignatureOverride = "Tickle";
        PlanValidator.Validate(layout5, plan5, menu5);
        Assert.Null(plan5.Boss!.SignatureOverride);
        plan5.Boss.SignatureOverride = "volley";
        PlanValidator.Validate(layout5, plan5, menu5);
        Assert.Equal("volley", plan5.Boss.SignatureOverride);
    }

    [Fact]
    public void Chests_And_Traps_AreCapped_And_Checked()
    {
        var (layout, menu, plan) = Fresh();
        var room = menu.Rooms.First(r => !r.IsStart).Index;
        plan.Chests.Clear(); plan.Traps.Clear();
        for (var i = 0; i < 5; i++) plan.Chests.Add(new ChestPlan { Room = room });
        plan.Chests.Add(new ChestPlan { Room = menu.StartRoom!.Index });
        for (var i = 0; i < 5; i++) plan.Traps.Add(new TrapPlan { Room = room, Kind = i == 0 ? "lava" : "gas" });
        PlanValidator.Validate(layout, plan, menu);
        Assert.Equal(FloorMenu.MaxExtraChests, plan.Chests.Count);
        Assert.Equal(FloorMenu.MaxExtraTraps, plan.Traps.Count);
        Assert.All(plan.Traps, t => Assert.Equal("Gas", t.Kind));
    }

    [Fact]
    public void LongText_IsClipped()
    {
        var (layout, menu, plan) = Fresh();
        plan.IntroLine.Text = new string('x', 500);
        PlanValidator.Validate(layout, plan, menu);
        Assert.Equal(PlanValidator.MaxLineLength, plan.IntroLine.Text.Length);
    }

    [Fact]
    public void Npc_Script_IsRepaired_And_Quests_Checked()
    {
        var (layout, menu, plan) = Fresh();
        plan.Npcs.Clear(); plan.Quests.Clear(); // replace whatever the designer authored
        var room = menu.Rooms.First(r => !r.IsStart && !r.IsVault && !r.IsArena).Index;
        var npc = new NpcPlan
        {
            Id = "hermit", Name = "Hermit", Room = room, QuestId = "q1",
            Script = new NpcScript
            {
                Start = "missing",
                Nodes =
                {
                    new DialogueNode { Id = "hello", Line = new Line("Hello."), Choices = { new ChoicePlan { Text = "Help?", Next = "nowhere", Effect = DialogueEffect.AcceptQuest } } },
                    new DialogueNode { Id = "bye", Line = new Line("Bye.") },
                },
            },
        };
        plan.Npcs.Add(npc);
        plan.Quests.Add(new QuestPlan { Id = "q1", Template = QuestTemplate.KillTarget, GiverNpc = "hermit", Params = new QuestParams { TargetCollective = plan.Collectives[0].Id } });
        plan.Quests.Add(new QuestPlan { Id = "q2", Template = QuestTemplate.Recover, GiverNpc = "nobody" });
        plan.Quests.Add(new QuestPlan { Id = "q3", Template = QuestTemplate.Escort, GiverNpc = "hermit", Params = new QuestParams { EscortNpc = "ghost" } });
        var report = PlanValidator.Validate(layout, plan, menu);
        Assert.True(report.Ok);
        Assert.Equal("hello", npc.Script.Start);
        Assert.Null(npc.Script.Nodes[0].Choices[0].Next);
        Assert.Single(npc.Script.Nodes[1].Choices);
        Assert.Single(plan.Quests);
        Assert.Equal("q1", plan.Quests[0].Id);
        Assert.Equal("q1", npc.QuestId);
    }
}

public class StoryValidationTests
{
    private static (FloorLayout Layout, FloorMenu Menu, FloorPlan Plan) Fresh(int floor = 3, int seed = 21)
    {
        var (layout, _, menu) = TestAdventure.Floor(floor, seed);
        var plan = OfflineDesigner.PlanFloor(menu, layout, seed);
        return (layout, menu, plan);
    }

    private static NpcPlan Giver(FloorMenu menu, string id = "cast1")
    {
        var room = menu.Rooms.First(r => !r.IsStart && !r.IsVault && !r.IsArena);
        var npc = new NpcPlan { Id = id, Name = "Someone", Room = room.Index, CastId = "cast_guide" };
        npc.Script.Start = "a";
        npc.Script.Nodes.Add(new DialogueNode { Id = "a", Line = new Line("Well?"), Choices =
        {
            new ChoicePlan { Text = "Run.", Effect = DialogueEffect.Choose, Flag = "Told To Run" },
            new ChoicePlan { Text = "Stay.", Effect = DialogueEffect.Choose, Flag = "told_to_stay", RequiresFlag = "  " },
            new ChoicePlan { Text = "Bye.", Effect = DialogueEffect.Farewell },
        } });
        return npc;
    }

    [Fact]
    public void CastId_TakesTheCastName_OrIsCleared()
    {
        var (layout, menu, plan) = Fresh();
        plan.Npcs.Clear(); plan.Quests.Clear();
        plan.Npcs.Add(Giver(menu));
        var campaign = new CampaignPlan { Cast = { new CastMember { Id = "cast_guide", Name = "Ossian Vell" } } };
        Assert.True(PlanValidator.Validate(layout, plan, menu, campaign: campaign).Ok);
        Assert.Equal("Ossian Vell", plan.Npcs[0].Name);
        var (layout2, menu2, plan2) = Fresh();
        plan2.Npcs.Clear(); plan2.Quests.Clear();
        plan2.Npcs.Add(Giver(menu2));
        var report = PlanValidator.Validate(layout2, plan2, menu2, campaign: new CampaignPlan());
        Assert.Null(plan2.Npcs[0].CastId);
        Assert.Contains(report.Repairs, r => r.Contains("unknown cast id"));
    }

    [Fact]
    public void Decide_NeedsTwoFlags_AndFlagsAreCleaned()
    {
        var (layout, menu, plan) = Fresh();
        plan.Npcs.Clear(); plan.Quests.Clear();
        var npc = Giver(menu);
        plan.Npcs.Add(npc);
        plan.Quests.Add(new QuestPlan { Id = "q", Template = QuestTemplate.Decide, GiverNpc = npc.Id });
        npc.QuestId = "q";
        Assert.True(PlanValidator.Validate(layout, plan, menu).Ok);
        Assert.Single(plan.Quests);
        Assert.Equal("told_to_run", npc.Script.Nodes[0].Choices[0].Flag);
        Assert.Null(npc.Script.Nodes[0].Choices[1].RequiresFlag);
        // one flag only: the quest is dropped and the NPC unlinked
        npc.Script.Nodes[0].Choices[1].Effect = DialogueEffect.None;
        var report = PlanValidator.Validate(layout, plan, menu);
        Assert.Empty(plan.Quests);
        Assert.Null(npc.QuestId);
        Assert.Contains(report.Repairs, r => r.Contains("Decide"));
    }

    [Fact]
    public void Investigate_KeepsOnlyUsableClueRooms()
    {
        var (layout, menu, plan) = Fresh();
        plan.Npcs.Clear(); plan.Quests.Clear();
        var npc = Giver(menu);
        npc.Script.Nodes[0].Choices.Insert(0, new ChoicePlan { Text = "I'll look.", Effect = DialogueEffect.AcceptQuest });
        plan.Npcs.Add(npc);
        var start = menu.Rooms.First(r => r.IsStart).Index;
        var good = menu.Rooms.Where(r => !r.IsStart && !r.IsVault && !r.IsArena && r.FreeCells > 0).Select(r => r.Index).Take(2).ToList();
        plan.Quests.Add(new QuestPlan { Id = "q", Template = QuestTemplate.Investigate, GiverNpc = npc.Id, Params = new QuestParams { ClueRooms = { start, good[0], good[0], 999, good[1] }, ClueName = "" } });
        npc.QuestId = "q";
        Assert.True(PlanValidator.Validate(layout, plan, menu).Ok);
        Assert.Equal(good, plan.Quests[0].Params.ClueRooms);
        Assert.Equal("clue", plan.Quests[0].Params.ClueName);
        var contents = PlanResolver.Resolve(layout, plan, null, new Dictionary<string, DungeonCrawler.Enemies.EnemyProfile>(), 3, 21);
        Assert.Equal(2, contents.QuestItems.Count);
        Assert.Equal(new[] { 0, 1 }, contents.QuestItems.Select(i => i.Ordinal));
    }

    [Fact]
    public void Betrayal_NeedsRosterCreatureAndBetrayChoice()
    {
        var (layout, menu, plan) = Fresh();
        plan.Npcs.Clear(); plan.Quests.Clear();
        var npc = Giver(menu);
        npc.Betrayal = new BetrayalPlan { EnemyId = "enemy.nothing", Count = 9 };
        npc.Script.Nodes[0].Choices.Add(new ChoicePlan { Text = "Lead on.", Effect = DialogueEffect.Betray });
        plan.Npcs.Add(npc);
        plan.Quests.Add(new QuestPlan { Id = "q", Template = QuestTemplate.Betrayal, GiverNpc = npc.Id });
        npc.QuestId = "q";
        var report = PlanValidator.Validate(layout, plan, menu);
        Assert.Null(npc.Betrayal);
        Assert.Empty(plan.Quests);
        Assert.Equal(DialogueEffect.Farewell, npc.Script.Nodes[0].Choices[^1].Effect);

        var (layout2, menu2, plan2) = Fresh();
        plan2.Npcs.Clear(); plan2.Quests.Clear();
        var npc2 = Giver(menu2);
        npc2.Betrayal = new BetrayalPlan { EnemyId = menu2.Roster[0].Id, Count = 9 };
        npc2.Script.Nodes[0].Choices.Add(new ChoicePlan { Text = "Lead on.", Effect = DialogueEffect.Betray });
        plan2.Npcs.Add(npc2);
        plan2.Quests.Add(new QuestPlan { Id = "q", Template = QuestTemplate.Betrayal, GiverNpc = npc2.Id });
        npc2.QuestId = "q";
        Assert.True(PlanValidator.Validate(layout2, plan2, menu2).Ok);
        Assert.Single(plan2.Quests);
        Assert.Equal(4, npc2.Betrayal!.Count);
    }

    [Fact]
    public void FlagGatedNpc_IsLeftOutUntilTheFlagIsSet()
    {
        var (layout, menu, plan) = Fresh();
        plan.Npcs.Clear(); plan.Quests.Clear();
        var npc = Giver(menu);
        npc.RequiresFlag = "told_to_run";
        plan.Npcs.Add(npc);
        Assert.True(PlanValidator.Validate(layout, plan, menu).Ok);
        var roster = new Dictionary<string, DungeonCrawler.Enemies.EnemyProfile>();
        Assert.Empty(PlanResolver.Resolve(layout, plan, null, roster, 3, 21).Npcs);
        Assert.Single(PlanResolver.Resolve(layout, plan, null, roster, 3, 21, new[] { "told_to_run" }).Npcs);
    }
}
