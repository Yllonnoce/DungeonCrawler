using DungeonCrawler.Enemies;
using DungeonCrawler.Dungeon;
using DungeonCrawler.Dungeon.Generation;
using DungeonCrawler.Items;

namespace DungeonCrawler.Adventure;

/// <summary>
/// Rule-following designer that needs no network: names rooms, forms enemy collectives with goals, adds a few
/// chests and traps, and writes template narration. Produces the same plan format as the AI planner so everything
/// downstream is shared, and serves as the fallback whenever the AI is unavailable.
/// </summary>
public static class OfflineDesigner
{
    private static readonly string[][] Adjectives =
    {
        new[] { "Sunken", "Hollow", "Silent", "Weeping", "Broken", "Forgotten", "Blackened", "Shrouded" },
        new[] { "Dripping", "Glimmering", "Narrow", "Echoing", "Fungal", "Crystal", "Cold", "Yawning" },
        new[] { "Smoldering", "Iron", "Ashen", "Molten", "Rusted", "Soot-Black", "Ringing", "Brazen" },
        new[] { "Void-Touched", "Whispering", "Bleeding", "Endless", "Unlit", "Screaming", "Faded", "Nameless" },
    };
    private static readonly Dictionary<RoomRole, string[]> Nouns = new()
    {
        [RoomRole.Passage] = new[] { "Passage", "Crossing", "Gallery", "Walk" },
        [RoomRole.Start] = new[] { "Landing", "Threshold", "Descent" },
        [RoomRole.Exit] = new[] { "Stairwell", "Deepening", "Way Down" },
        [RoomRole.Arena] = new[] { "Court", "Sanctum", "Pit" },
        [RoomRole.Treasury] = new[] { "Treasury", "Reliquary", "Hoard" },
        [RoomRole.Gate] = new[] { "Gate", "Watch", "Bastion" },
        [RoomRole.Shrine] = new[] { "Shrine", "Altar", "Chapel" },
        [RoomRole.Cell] = new[] { "Cell", "Oubliette", "Holding" },
        [RoomRole.Barracks] = new[] { "Barracks", "Muster", "Quarters" },
        [RoomRole.Kennel] = new[] { "Kennel", "Warren", "Den" },
        [RoomRole.Lair] = new[] { "Lair", "Nest", "Roost" },
        [RoomRole.Hall] = new[] { "Hall", "Nave", "Chamber" },
        [RoomRole.Camp] = new[] { "Camp", "Squat", "Hideout" },
        [RoomRole.Ruin] = new[] { "Ruin", "Colonnade", "Wreck" },
    };
    private static readonly string[][] GroupNouns =
    {
        new[] { "Pack", "Vigil", "Congregation", "Wardens" },
        new[] { "Brood", "Clutch", "Tribe", "Lurkers" },
        new[] { "Forge-Guard", "Foundry", "Cohort", "Stokers" },
        new[] { "Choir", "Host", "Circle", "Hunters" },
    };
    private static readonly string[] IntroLines =
    {
        "The {tier} swallows your torchlight. Something here has been waiting.",
        "Dust stirs as you descend into the {tier}. The {group} keep their posts.",
        "Cold air rises from the {tier}. Whatever guards the {room} does not sleep.",
        "The stair ends in the {tier}. Footprints, none of them yours, lead toward the {room}.",
    };
    /// <summary>Tests: make the next NPC quest use this template.</summary>
    public static QuestTemplate? ForcedTemplate { get; set; }
    private static readonly string[] ClueNames = { "torn ledger pages", "wax seals", "scratched tally marks", "bloodied prayer slips", "broken message tubes" };

    private static readonly string[][] NpcNames =
    {
        new[] { "Brother Aldous", "Mara the Digger", "Old Teodor", "Sister Wren" },
        new[] { "Pell the Lost", "Hesk Lampwright", "Ada Ferrin", "Corvin Shale" },
        new[] { "Ilse the Smith", "Bram Coalhand", "Oren Tallow", "Vesna Ashe" },
        new[] { "The Pale Pilgrim", "Nyx", "Halvard the Broken", "Sera Voidwalker" },
    };
    private static readonly string[] BandNames = { "Gallows Company", "Knifegrinders", "Red Hoods", "Ditchmen", "Crowfeeders", "Saltless Men", "Cellar Rats" };
    private static readonly string[] HordeNames = { "The Tide", "The Gnawing", "The Crawl", "The Rot", "The Spill", "The Seethe", "The Boil", "The Scurry", "The Welter" };
    private static readonly string[] HordeLines =
    {
        "The walls give way. Something has been breeding in the dark, and all of it is hungry.",
        "A sound like gravel poured from a sack: the breaches spill their teeming filth.",
        "They come out of the stone itself, more of them than there are heartbeats to count.",
    };
    private static readonly string[] RelicNames = { "Ember Reliquary", "Saint's Knucklebone", "Lantern of Hours", "Iron Psalter", "Weeping Idol", "Bell of the Deep" };
    private static readonly string[] Greetings =
    {
        "You... you are alive. Thank the depths. Do not come closer with that torch, my eyes are not used to it.",
        "Another descent, another fool. Or perhaps a friend? Speak.",
        "Hush. They do not know I am here. If you can help me, I can help you.",
    };

    private static readonly string[] BossIntro = { "{boss} rises to meet you. \"You should have stayed above.\"", "\"So the thieves come at last.\" {boss} bars the way down." };
    private static readonly string[] BossDeath = { "{boss} falls, and the way down opens.", "\"Deeper... it waits... deeper...\" {boss} is still." };

    public static CampaignPlan Outline(ulong gameSeed, IReadOnlyList<TierProfile> tiers, IReadOnlyList<ThemeProfile>? themes = null)
    {
        var rng = new Random((int)(gameSeed & 0x7FFFFFFF) ^ 0x0A1C);
        var plan = new CampaignPlan
        {
            Source = PlanSources.Offline, GameSeed = gameSeed,
            Title = "The Long Descent",
            Synopsis = "Twenty floors down, something old keeps the dead moving. Reach it, and end it.",
            Antagonist = new VoicedCharacter { Name = "The Hollow King", Epithet = "who was buried twice" },
        };
        for (var t = 0; t < TierRules.TierCount; t++)
        {
            var tier = t < tiers.Count ? tiers[t] : null;
            plan.Tiers.Add(new TierPlan { Index = t, Theme = tier?.DisplayName ?? $"Tier {t + 1}", Beat = "", BossName = "" });
            plan.Factions.Add(new FactionPlan { Id = $"faction{t}", Name = $"{Pick(rng, Adjectives[t])} {Pick(rng, GroupNouns[t])}", EnemyIds = tier?.EnemyPool.ToList() ?? new List<string>() });
        }
        for (var f = 1; f <= TierRules.FinalFloor; f++)
            plan.Floors.Add(new FloorOutline { Floor = f, Theme = themes == null ? null : ThemeProfile.Pick(themes, TierRules.TierIndex(f), gameSeed, f)?.Id });
        return plan;
    }

    public static FloorPlan PlanFloor(FloorMenu menu, FloorLayout layout, int seed, float difficulty = 1f)
    {
        var rng = new Random(seed ^ 0x0FF1);
        var t = Math.Clamp(menu.TierIndex, 0, Adjectives.Length - 1);
        var plan = new FloorPlan { Floor = menu.Floor, Source = PlanSources.Offline, LayoutHash = layout.ContentHash() };

        // room roles and names
        foreach (var r in menu.Rooms)
        {
            var role = r.IsStart ? RoomRole.Start : r.IsArena ? RoomRole.Arena : r.IsExit ? RoomRole.Exit : r.IsVault ? RoomRole.Treasury
                : r.IsCamp ? RoomRole.Camp : r.IsRuin ? RoomRole.Ruin : r.HasGate || r.HasLedge ? RoomRole.Gate : r.HasCellar ? RoomRole.Kennel : r.Area >= 49 ? RoomRole.Hall
                : r.DeadEnd ? Pick(rng, new[] { RoomRole.Cell, RoomRole.Shrine, RoomRole.Lair }) : Pick(rng, new[] { RoomRole.Passage, RoomRole.Barracks, RoomRole.Passage });
            plan.Rooms.Add(new RoomPlan { Index = r.Index, Role = role, Name = $"{Pick(rng, Adjectives[t])} {Pick(rng, Nouns[role])}" });
        }
        string RoomName(int i) => plan.RoomPlanFor(i)?.Name ?? "chamber";

        var candidates = menu.Rooms.Where(r => !r.IsStart && !r.IsVault && !r.IsArena && r.FreeCells >= 2).ToList();
        if (candidates.Count == 0) candidates = menu.Rooms.Where(r => !r.IsStart && r.FreeCells >= 1).ToList();
        if (candidates.Count == 0 || menu.Roster.Count == 0) { plan.IntroLine = new Line(Intro(rng, menu, plan, "no one")); return plan; }

        var target = Math.Clamp((int)MathF.Round(menu.TargetEnemies * difficulty), menu.MinEnemies, menu.MaxEnemies);
        var used = new HashSet<int>();

        // every camp gets its band of bandits first; they hold the hut and its fire
        var bandits = menu.Roster.Where(e => e.IsBandit).ToList();
        foreach (var camp in menu.CampRooms.Where(r => candidates.Any(c => c.Index == r.Index)))
        {
            if (bandits.Count == 0) break;
            var size = Math.Min(PlanValidator.MaxMembersPerCollective, Math.Min(camp.FreeCells, 3 + (menu.Floor >= 5 ? 1 : 0) + (menu.Floor >= 12 ? 1 : 0)));
            var band = new CollectivePlan { Id = $"band{plan.Collectives.Count + 1}", Name = Pick(rng, BandNames), FactionId = FloorMenu.BanditFaction, Members = PlanValidator.BanditBand(bandits, size), Goal = new GoalPlan { Kind = GoalKind.Guard, Room = camp.Index } };
            if (menu.Floor >= 6 && bandits.FirstOrDefault(b => b.Kind == EnemyKind.Melee && b.Cost == bandits.Where(x => x.Kind == EnemyKind.Melee).Max(x => x.Cost)) is { } chief && band.Members.Sum(m => m.Count) >= 3 && band.Members.All(m => m.EnemyId != chief.Id))
            {
                var grunt = band.Members.First(m => m.Count == band.Members.Max(x => x.Count));
                grunt.Count--;
                if (grunt.Count == 0) band.Members.Remove(grunt);
                band.Members.Add(new MemberPlan { EnemyId = chief.Id, Count = 1 });
            }
            plan.Collectives.Add(band);
            used.Add(camp.Index);
        }
        target = Math.Max(2, target - plan.Collectives.Sum(c => c.Members.Sum(m => m.Count)));
        var loners = target >= 6 ? 1 : 0;
        var groupCount = Math.Clamp((target - loners) / 3, 1, Math.Min(4, Math.Max(1, candidates.Count - used.Count)));
        var sizes = new int[groupCount];
        for (var i = 0; i < target - loners; i++) sizes[i % groupCount]++;

        var weights = menu.Roster.Select(e => e.Weight).ToArray();
        string PickEnemy() => menu.Roster[LootRoller.WeightedIndex(weights, rng)].Id;
        RoomInfo? Claim(Func<RoomInfo, bool> pred, Func<RoomInfo, float>? prefer = null)
        {
            var pool = candidates.Where(r => !used.Contains(r.Index) && pred(r));
            var pick = prefer == null ? pool.OrderBy(_ => rng.Next()).FirstOrDefault() : pool.OrderByDescending(prefer).ThenBy(_ => rng.Next()).FirstOrDefault();
            if (pick != null) used.Add(pick.Index);
            return pick;
        }

        // which goals this floor uses: every kind gets a turn across floors; Alarm needs someone to run to
        var kinds = new List<GoalKind> { GoalKind.Defend, GoalKind.Patrol, GoalKind.Alarm, GoalKind.Guard };
        for (var i = kinds.Count - 1; i > 0; i--) { var j = rng.Next(i + 1); (kinds[i], kinds[j]) = (kinds[j], kinds[i]); }
        if (groupCount == 1) kinds.Remove(GoalKind.Alarm);
        // a gatehouse is always held
        if (candidates.Any(r => r.HasGate && !used.Contains(r.Index))) { kinds.Remove(GoalKind.Defend); kinds.Insert(0, GoalKind.Defend); }
        var alarmGroups = new List<CollectivePlan>();

        for (var gi = 0; gi < groupCount; gi++)
        {
            var c = new CollectivePlan { Id = $"group{gi + 1}", Name = $"{Pick(rng, Adjectives[t])} {Pick(rng, GroupNouns[t])}", FactionId = $"faction{t}" };
            for (var k = 0; k < sizes[gi]; k++)
            {
                var id = PickEnemy();
                var m = c.Members.FirstOrDefault(x => x.EnemyId == id);
                if (m == null) c.Members.Add(new MemberPlan { EnemyId = id, Count = 1 }); else m.Count++;
            }
            var g = c.Goal;
            switch (kinds[gi % kinds.Count])
            {
                case GoalKind.Defend:
                {
                    var gateRoom = Claim(r => r.HasGate);
                    if (gateRoom != null) { g.Kind = GoalKind.Defend; g.Room = gateRoom.Index; g.Objective = new ObjectivePlan { Kind = ObjectiveKind.Gate, Ref = gateRoom.Index.ToString() }; break; }
                    var chestRoom = Claim(r => r.Chests.Count > 0, r => r.HopsFromStart);
                    if (chestRoom != null) { g.Kind = GoalKind.Defend; g.Room = chestRoom.Index; g.Objective = new ObjectivePlan { Kind = ObjectiveKind.Chest, Ref = chestRoom.Chests[0].ToString() }; break; }
                    var vault = menu.Rooms.FirstOrDefault(r => r.IsVault);
                    var doorRoom = vault == null ? null : Claim(r => vault.Neighbours.Contains(r.Index));
                    if (vault != null && doorRoom != null) { g.Kind = GoalKind.Defend; g.Room = doorRoom.Index; g.Objective = new ObjectivePlan { Kind = ObjectiveKind.Door, Ref = layout.Vaults.First(v => v.Room == layout.Rooms[vault.Index]).Index.ToString() }; break; }
                    var far = Claim(_ => true, r => r.HopsFromStart)!;
                    g.Kind = GoalKind.Guard; g.Room = far.Index;
                    break;
                }
                case GoalKind.Patrol:
                {
                    var a = Claim(r => r.Neighbours.Any(n => candidates.Any(c2 => c2.Index == n && !used.Contains(n))));
                    if (a != null)
                    {
                        var b = candidates.Where(r => a.Neighbours.Contains(r.Index) && !used.Contains(r.Index)).OrderBy(_ => rng.Next()).First();
                        used.Add(b.Index);
                        g.Kind = GoalKind.Patrol; g.Room = a.Index; g.Route = new List<int> { a.Index, b.Index };
                        var c3 = candidates.FirstOrDefault(r => b.Neighbours.Contains(r.Index) && r.Index != a.Index && !used.Contains(r.Index));
                        if (c3 != null && rng.Next(2) == 0) { g.Route.Add(c3.Index); used.Add(c3.Index); }
                        break;
                    }
                    var any = Claim(_ => true) ?? candidates[rng.Next(candidates.Count)];
                    g.Kind = GoalKind.Guard; g.Room = any.Index;
                    break;
                }
                case GoalKind.Alarm:
                {
                    var near = Claim(_ => true, r => -r.HopsFromStart) ?? candidates[rng.Next(candidates.Count)];
                    g.Kind = GoalKind.Alarm; g.Room = near.Index;
                    alarmGroups.Add(c); // target chosen once every group exists
                    break;
                }
                default:
                {
                    var room = Claim(r => r.IsExit) ?? Claim(_ => true) ?? candidates[rng.Next(candidates.Count)];
                    g.Kind = GoalKind.Guard; g.Room = room.Index;
                    break;
                }
            }
            plan.Collectives.Add(c);
        }
        foreach (var c in alarmGroups)
        {
            var runTo = plan.Collectives.Where(o => o != c && o.Goal.Kind != GoalKind.Alarm).OrderByDescending(o => o.Members.Sum(m => m.Count)).FirstOrDefault()
                        ?? plan.Collectives.FirstOrDefault(o => o != c);
            if (runTo == null) { c.Goal.Kind = GoalKind.Guard; continue; }
            c.Goal.AlarmTarget = runTo.Id;
            c.Goal.RallyRoom = runTo.Goal.Room;
        }
        // keep within budget with a little headroom for the validator's tolerance
        float Cost() => plan.Collectives.Sum(c => c.Members.Sum(m => m.Count * menu.CostOf(m.EnemyId))) + plan.Loners.Sum(l => menu.CostOf(l.EnemyId));
        while (Cost() > menu.Budget * 1.05f && plan.EnemyTotal > menu.MinEnemies)
        {
            var big = plan.Collectives.Where(c => c.Members.Sum(m => m.Count) > 1).MaxBy(c => c.Members.Sum(m => m.Count));
            if (big == null) break;
            var m = big.Members.MaxBy(m => menu.CostOf(m.EnemyId))!;
            if (m.Count > 1) m.Count--; else big.Members.Remove(m);
        }
        for (var i = 0; i < loners; i++)
        {
            var room = candidates.Where(r => !used.Contains(r.Index)).OrderBy(_ => rng.Next()).FirstOrDefault() ?? candidates[rng.Next(candidates.Count)];
            var id = PickEnemy();
            if (Cost() + menu.CostOf(id) > menu.Budget * 1.1f) break;
            plan.Loners.Add(new LonerPlan { EnemyId = id, Room = room.Index });
        }

        // a horde in every nest room: a handful of minions per hideaway on the first floor, growing every floor toward
        // fifty or sixty at the bottom, with the tier's brutes joining from the third floor and its full-size creatures
        // marching among them from the sixth. Triggers vary so they break out of different parts of the floor.
        if (menu.Roster.Any(e => e.IsHorde))
        {
            var hasAlarm = plan.Collectives.Any(c => c.Goal.Kind == GoalKind.Alarm);
            var n = 0;
            foreach (var nestRoom in menu.NestRooms.Where(r => !r.IsStart && !r.IsVault && !r.IsArena).OrderBy(r => r.HopsFromStart))
            {
                if (n >= PlanValidator.MaxHordes) break;
                n++;
                var trigger = HordeTrigger.Enter;
                if (nestRoom.Chests.Count > 0 && rng.Next(100) < 55) trigger = HordeTrigger.Chest;
                else if (hasAlarm && rng.Next(100) < 30) trigger = HordeTrigger.Alarm;
                plan.Hordes.Add(new HordePlan { Id = $"horde{n}", Name = Pick(rng, HordeNames), Room = nestRoom.Index, Members = HordeMembers(rng, menu), Trigger = trigger, Line = new Line(Pick(rng, HordeLines)) });
            }
        }

        if (menu.BossFloor && menu.BossId != null)
        {
            var boss = menu.BossName ?? "The Guardian";
            plan.Boss = new BossPlan { Name = boss, IntroLine = new Line(Pick(rng, BossIntro).Replace("{boss}", boss)), DeathLine = new Line(Pick(rng, BossDeath).Replace("{boss}", boss)) };
        }
        if (rng.Next(100) < 30)
        {
            var deadEnd = candidates.Where(r => r.DeadEnd && r.FreeCells > 3).OrderBy(_ => rng.Next()).FirstOrDefault();
            if (deadEnd != null) plan.Chests.Add(new ChestPlan { Room = deadEnd.Index, Locked = rng.Next(3) == 0 });
        }
        var trapCount = rng.Next(0, 3);
        var deep = candidates.Where(r => r.HopsFromStart >= 2 && r.FreeCells > 4).ToList();
        for (var i = 0; i < trapCount && deep.Count > 0; i++)
            plan.Traps.Add(new TrapPlan { Room = deep[rng.Next(deep.Count)].Index, Kind = Pick(rng, menu.TrapKinds) });

        if (menu.Floor >= 2 && candidates.Count > 1 && rng.Next(100) < 70) AddNpcQuest(rng, menu, plan, candidates, used, t, RoomName);

        var focus = (plan.Collectives.FirstOrDefault(c => c.FactionId != FloorMenu.BanditFaction) ?? plan.Collectives[0]).Goal.Room;
        plan.Title = RoomName(focus);
        plan.IntroLine = new Line(Intro(rng, menu, plan, plan.Collectives[0].Name).Replace("{room}", RoomName(focus)));
        plan.Narration.Add(new NarrationPlan { Trigger = NarrationTrigger.Enter, Line = plan.IntroLine });
        return plan;
    }

    /// <summary>
    /// What a horde is made of at this depth: total 3-5 on floor 1 rising to 50-60 by floor 20; the cheapest swarm
    /// creature is the fodder, the dearer swarm creature (the brute) takes a growing share from floor 3, and from
    /// floor 6 a few of the tier's own creatures march with them (never more than a quarter, never more than eight).
    /// </summary>
    public static List<MemberPlan> HordeMembers(Random rng, FloorMenu menu)
    {
        var floor = menu.Floor;
        var total = Math.Clamp((int)MathF.Round(2 + floor * 2.6f) + rng.Next(0, 2 + floor / 2), FloorMenu.MinHorde, FloorMenu.MaxHorde);
        var swarm = menu.Roster.Where(e => e.IsHorde).OrderBy(e => e.Cost).ToList();
        if (swarm.Count == 0) return new List<MemberPlan>();
        var fodder = swarm[0];
        var brute = swarm.Count > 1 ? swarm[^1] : null;
        var members = new List<MemberPlan>();
        var brutes = brute == null || floor < 3 ? 0 : (int)MathF.Round(total * Math.Min(0.35f, 0.04f * floor));
        var bigAllowed = floor >= FloorMenu.BigMembersFromFloor ? Math.Min(FloorMenu.MaxBigMembers, Math.Max(1, total / 4)) : 0;
        var big = floor < 6 ? 0 : Math.Min(bigAllowed, 1 + (floor - 6) / 3);
        var regulars = menu.Roster.Where(e => !e.IsHorde && !e.IsBandit && e.Id != menu.BossId).ToList();
        if (regulars.Count == 0) big = 0;
        var fodderCount = Math.Max(1, total - brutes - big);
        members.Add(new MemberPlan { EnemyId = fodder.Id, Count = fodderCount });
        if (brutes > 0) members.Add(new MemberPlan { EnemyId = brute!.Id, Count = brutes });
        for (var i = 0; i < big; i++)
        {
            var pick = regulars[rng.Next(regulars.Count)];
            var m = members.FirstOrDefault(x => x.EnemyId == pick.Id);
            if (m == null) members.Add(new MemberPlan { EnemyId = pick.Id, Count = 1 }); else m.Count++;
        }
        return members;
    }

    /// <summary>One NPC who offers a quest the floor can actually support, with a four-node dialogue script.</summary>
    private static void AddNpcQuest(Random rng, FloorMenu menu, FloorPlan plan, List<RoomInfo> candidates, HashSet<int> used, int tier, Func<int, string> roomName)
    {
        var npcRoom = candidates.Where(r => !used.Contains(r.Index) && !r.IsExit).OrderBy(_ => rng.Next()).FirstOrDefault()
                      ?? candidates.Where(r => !r.IsExit).OrderBy(_ => rng.Next()).FirstOrDefault();
        if (npcRoom == null) return;
        var npcId = "npc1";
        var name = Pick(rng, NpcNames[tier]);
        var options = new List<QuestTemplate>();
        if (plan.Collectives.Count > 0) { options.Add(QuestTemplate.KillTarget); options.Add(QuestTemplate.HoldOut); }
        if (menu.Rooms.Any(r => r.Index != npcRoom.Index && !r.IsStart && !r.IsVault && !r.IsArena && r.HopsFromStart >= 2 && r.FreeCells > 2)) options.Add(QuestTemplate.Recover);
        options.Add(QuestTemplate.Escort);
        var clueRooms = menu.Rooms.Where(r => r.Index != npcRoom.Index && !r.IsStart && !r.IsVault && !r.IsArena && r.FreeCells > 2).ToList();
        if (clueRooms.Count >= 2) options.Add(QuestTemplate.Investigate);
        options.Add(QuestTemplate.Decide);
        var template = ForcedTemplate ?? Pick(rng, options);
        var q = new QuestPlan { Id = "quest1", Template = template, GiverNpc = npcId, Reward = new QuestReward { Xp = 40 * menu.Floor + 60, Table = "table.chest" } };
        string offer, accept, complete, fail, title, objective;
        NpcScript? customScript = null;
        switch (template)
        {
            case QuestTemplate.Investigate:
            {
                var rooms = clueRooms.OrderBy(_ => rng.Next()).Take(3).ToList();
                if (rooms.Count < 2) rooms = clueRooms.Take(2).ToList();
                q.Params.ClueRooms = rooms.Select(r => r.Index).ToList();
                q.Params.ClueName = Pick(rng, ClueNames);
                title = "What Happened Here";
                objective = $"Find the {q.Params.ClueName} scattered through {rooms.Count} rooms and bring word to {name}.";
                offer = $"Something went wrong on this floor before you came, and nobody will say what. There are {rooms.Count} {q.Params.ClueName} lying about that would tell it. I would look myself, but I am not brave, only curious.";
                accept = "Bring me what you find. I will do the thinking; that part is safe.";
                complete = "So that is how it was. I almost wish you had not told me. Almost. Here, this was going to be my burial money.";
                fail = "Some things are better left in the dark. I keep telling myself that.";
                break;
            }
            case QuestTemplate.Decide:
            {
                var flagA = $"f{menu.Floor}_told_truth";
                var flagB = $"f{menu.Floor}_kept_silent";
                title = $"{name}'s Question";
                objective = $"{name} wants an answer. Give one.";
                offer = "They are coming for me, one way or another. Tell me straight: do I run deeper, or do I stay and let them find me?";
                accept = ""; complete = "Then it is decided. Whatever comes of it is on both of us now."; fail = "";
                customScript = new NpcScript
                {
                    Start = "greet",
                    Nodes =
                    {
                        new DialogueNode { Id = "greet", Line = new Line(offer), Choices =
                        {
                            new ChoicePlan { Text = "Run. Deeper is safer than here.", Effect = DialogueEffect.Choose, Flag = flagA, Next = "done" },
                            new ChoicePlan { Text = "Stay. Face it.", Effect = DialogueEffect.Choose, Flag = flagB, Next = "done" },
                            new ChoicePlan { Text = "Not my business.", Effect = DialogueEffect.Farewell },
                        } },
                        new DialogueNode { Id = "done", Line = new Line(complete), Choices = { new ChoicePlan { Text = "Farewell.", Effect = DialogueEffect.Farewell } } },
                    },
                };
                break;
            }
            case QuestTemplate.KillTarget:
            {
                var target = plan.Collectives.OrderByDescending(c => c.Members.Sum(m => m.Count)).First();
                q.Params.TargetCollective = target.Id;
                title = $"The {target.Name}";
                objective = $"Slay the {target.Name} in the {roomName(target.Goal.Room)}.";
                offer = $"The {target.Name} took everything I had and left me here to rot. Kill every last one of them in the {roomName(target.Goal.Room)} and I will make it worth your while.";
                accept = "Then go. And do not be gentle.";
                complete = "They are dead? All of them? Then perhaps there is justice down here after all. Take this.";
                fail = "So they live on. Of course they do.";
                break;
            }
            case QuestTemplate.Recover:
            {
                var place = menu.Rooms.Where(r => r.Index != npcRoom.Index && !r.IsStart && !r.IsVault && !r.IsArena && r.HopsFromStart >= 2 && r.FreeCells > 2).OrderByDescending(r => r.HopsFromStart).ThenBy(_ => rng.Next()).First();
                q.Params.ItemRoom = place.Index;
                q.Params.ItemName = Pick(rng, RelicNames);
                title = $"The {q.Params.ItemName}";
                objective = $"Bring the {q.Params.ItemName} back from the {roomName(place.Index)}.";
                offer = $"I dropped the {q.Params.ItemName} when I fled the {roomName(place.Index)}. It is worth more than my life, which is why I am not going back for it. Would you?";
                accept = "Bless you. It should still be lying where I fell.";
                complete = $"The {q.Params.ItemName}! I never thought I would hold it again. Here, this is yours now.";
                fail = "Lost, then. Like everything else down here.";
                break;
            }
            case QuestTemplate.HoldOut:
            {
                var waves = plan.Collectives.OrderByDescending(c => c.Members.Sum(m => m.Count)).First();
                q.Params.WaveCollective = waves.Id;
                q.Params.Waves = 2;
                title = "Hold the Line";
                objective = $"Stand with {name} against two waves of the {waves.Name}.";
                offer = $"They know I am here. The {waves.Name} come in waves whenever I make a sound, and I cannot keep quiet forever. Stand with me through the next two and I will pay you from what I have hidden.";
                accept = "Then here they come. Do not let them reach me.";
                complete = "It is quiet. Truly quiet. I had forgotten what that felt like. Take what I promised.";
                fail = "You left me. I will not forget that.";
                break;
            }
            default:
            {
                q.Template = QuestTemplate.Escort;
                q.Params.EscortNpc = npcId;
                title = $"{name}'s Way Down";
                objective = $"Get {name} to the stairs down alive.";
                offer = "I cannot go on alone; every corridor is watched. Take me as far as the stairs down and I will give you everything I carried out of the levels above.";
                accept = "I will stay close. Please, do not leave me behind.";
                complete = "The stairs. I can manage from here. Thank you, stranger. Take this.";
                fail = "...";
                break;
            }
        }
        q.Title = title; q.Objective = objective;
        q.Lines = new QuestLines { Offer = new Line(offer), Accept = new Line(accept), Decline = new Line("Then leave me. Others will come, or they will not."), Complete = new Line(complete), Fail = new Line(fail) };
        var script = customScript ?? StandardScript(Pick(rng, Greetings), offer, accept, complete);
        plan.Npcs.Add(new NpcPlan { Id = npcId, Name = name, Room = npcRoom.Index, Script = script, QuestId = q.Id });
        plan.Quests.Add(q);
        used.Add(npcRoom.Index);
    }

    /// <summary>Generic barks for a member, three per trigger, used when no bank was written.</summary>
    public static BarkSet DefaultBarks(string name, string className)
    {
        var set = new BarkSet { Member = name };
        void Add(BarkTrigger t, params string[] lines) { foreach (var l in lines) set.Lines.Add(new BarkLine { Trigger = t, Line = new Line(l) }); }
        Add(BarkTrigger.Kill, "Down.", "Stay down.", "One less.");
        Add(BarkTrigger.Hurt, "Agh!", "That one landed.", "I'm bleeding.");
        Add(BarkTrigger.Loot, "This is worth carrying.", "Mine now.", "Something useful.");
        Add(BarkTrigger.LevelUp, "I'm getting the measure of this place.", "Stronger. Good.", "That felt like something clicked.");
        Add(BarkTrigger.BossSeen, "There. That's the one.", "Steady. It's big.", "Everyone, on me.");
        Add(BarkTrigger.Possess, $"{name} here.", "I have the lead.", "Follow me.");
        Add(BarkTrigger.Downed, "I can't...", "Get me up...", "Not like this.");
        Add(BarkTrigger.Revived, "Still here.", "I owe you one.", "Back on my feet.");
        return set;
    }

    /// <summary>The four-node quest conversation: greet (ask / turn in / leave), offer (accept / decline), accepted, done.</summary>
    public static NpcScript StandardScript(string greeting, string offer, string accept, string complete) => new()
    {
        Start = "greet",
        Nodes =
        {
            new DialogueNode { Id = "greet", Line = new Line(greeting), Choices =
            {
                new ChoicePlan { Text = "What happened to you?", Next = "offer" },
                new ChoicePlan { Text = "It is done.", Effect = DialogueEffect.TurnIn, Next = "done" },
                new ChoicePlan { Text = "Farewell.", Effect = DialogueEffect.Farewell },
            } },
            new DialogueNode { Id = "offer", Line = new Line(offer), Choices =
            {
                new ChoicePlan { Text = "I will do it.", Effect = DialogueEffect.AcceptQuest, Next = "accepted" },
                new ChoicePlan { Text = "Not now.", Effect = DialogueEffect.DeclineQuest },
            } },
            new DialogueNode { Id = "accepted", Line = new Line(accept), Choices = { new ChoicePlan { Text = "Farewell.", Effect = DialogueEffect.Farewell } } },
            new DialogueNode { Id = "done", Line = new Line(complete), Choices = { new ChoicePlan { Text = "Farewell.", Effect = DialogueEffect.Farewell } } },
        },
    };

    private static string Intro(Random rng, FloorMenu menu, FloorPlan plan, string group)
        => Pick(rng, IntroLines).Replace("{tier}", menu.TierName).Replace("{group}", group).Replace("{room}", plan.Rooms.Count > 0 ? plan.Rooms[^1].Name : "dark");

    private static T Pick<T>(Random rng, IReadOnlyList<T> list) => list[rng.Next(list.Count)];
}
