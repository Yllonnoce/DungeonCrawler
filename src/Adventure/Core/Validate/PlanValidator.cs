using DungeonCrawler.Enemies;
using DungeonCrawler.Dungeon;
using DungeonCrawler.Dungeon.Generation;

namespace DungeonCrawler.Adventure;

public sealed class ValidationReport
{
    public List<string> Errors { get; } = new();
    public List<string> Repairs { get; } = new();
    public bool Ok => Errors.Count == 0;
    public override string ToString() => Ok ? $"ok ({Repairs.Count} repairs)" : $"rejected: {string.Join("; ", Errors)}";
}

/// <summary>
/// Checks a <see cref="FloorPlan"/> against the layout it claims to describe. Anything that can be fixed is fixed in
/// place and recorded as a repair; structural problems (wrong floor, wrong layout) are errors and the plan is rejected.
/// </summary>
public static class PlanValidator
{
    public const int MaxLineLength = 220;
    public const int MaxCollectives = 5;
    public const int MaxMembersPerCollective = 6;
    public const int MaxWaves = 3;
    public const int MaxHordes = 8;

    /// <param name="enforceBudget">False when re-checking a stored plan at build time: the budget was judged against the party as it was when the plan was written, and must not shift on reload.</param>
    public static ValidationReport Validate(FloorLayout layout, FloorPlan plan, FloorMenu menu, bool enforceBudget = true, CampaignPlan? campaign = null)
    {
        var report = new ValidationReport();
        if (plan.Floor != menu.Floor) report.Errors.Add($"plan is for floor {plan.Floor}, layout is floor {menu.Floor}");
        if (!report.Ok) return report;
        // a plan written against an older build of the same seed (the generator has changed since) is adapted, not thrown away:
        // rooms keep their indices, and every cell reference below is checked against the layout as it is now
        if (plan.LayoutHash != 0 && plan.LayoutHash != layout.ContentHash()) report.Repairs.Add("plan was written for an older layout; adapted");
        plan.LayoutHash = layout.ContentHash();

        var start = menu.StartRoom?.Index ?? -1;
        bool RoomOk(int i) => menu.Room(i) != null;
        CampaignRepair.RepairLine(plan.IntroLine, report.Repairs, "intro");
        foreach (var n in plan.Narration) CampaignRepair.RepairLine(n.Line, report.Repairs, "narration");
        plan.Taunts.RemoveAll(t =>
        {
            if (!Objectives.TauntTriggers.TryParse(t.Trigger, out var trigger) || string.IsNullOrWhiteSpace(t.Line.Text)) { report.Repairs.Add("dropped a floor taunt with no trigger or no words"); return true; }
            t.Trigger = Objectives.TauntTriggers.Id(trigger);
            t.Line.Text = Clip(t.Line.Text, CampaignRepair.MaxLine);
            if (t.CleanLine != null && string.IsNullOrWhiteSpace(t.CleanLine.Text)) t.CleanLine = null;
            t.CleanLine ??= new Line(Objectives.TauntBank.Soften(t.Line.Text));
            CampaignRepair.RepairLine(t.Line, report.Repairs, "taunt");
            return false;
        });
        bool IsVault(int i) => menu.Room(i)?.IsVault == true;
        bool IsSafe(int i) => menu.Room(i)?.IsSafeRoom == true; // the safe room: nothing of a plan's goes in it

        // rooms: dedupe, drop unknown, fill roles the designer left out
        var seenRooms = new HashSet<int>();
        plan.Rooms.RemoveAll(r =>
        {
            if (!RoomOk(r.Index)) { report.Repairs.Add($"dropped role for unknown room {r.Index}"); return true; }
            if (!seenRooms.Add(r.Index)) { report.Repairs.Add($"dropped duplicate role for room {r.Index}"); return true; }
            return false;
        });
        foreach (var info in menu.Rooms)
        {
            var rp = plan.RoomPlanFor(info.Index);
            if (rp == null) { rp = new RoomPlan { Index = info.Index }; plan.Rooms.Add(rp); }
            var forced = info.IsStart ? RoomRole.Start : info.IsExit && !info.IsArena ? RoomRole.Exit : info.IsArena ? RoomRole.Arena : info.IsJail ? RoomRole.Jail : info.IsShrine ? RoomRole.Shrine : (RoomRole?)null;
            if (forced != null && rp.Role != forced) { rp.Role = forced.Value; report.Repairs.Add($"room {info.Index} role forced to {forced}"); }
            if (forced == null && rp.Role is RoomRole.Start or RoomRole.Exit or RoomRole.Arena or RoomRole.Jail) { rp.Role = RoomRole.Passage; report.Repairs.Add($"room {info.Index} cannot be {rp.Role}"); }
            rp.Name = Clip(rp.Name, 40);
        }
        plan.Rooms.Sort((a, b) => a.Index.CompareTo(b.Index));

        // collectives
        var ids = new HashSet<string>();
        for (var ci = 0; ci < plan.Collectives.Count; ci++)
        {
            var c = plan.Collectives[ci];
            if (string.IsNullOrWhiteSpace(c.Id) || !ids.Add(c.Id)) { c.Id = $"group{ci + 1}"; while (!ids.Add(c.Id)) c.Id += "x"; report.Repairs.Add($"collective {ci} renamed to {c.Id}"); }
            c.Name = Clip(c.Name, 40);
        }
        plan.Collectives.RemoveAll(c =>
        {
            c.Members.RemoveAll(m =>
            {
                if (menu.Enemy(m.EnemyId) == null) { report.Repairs.Add($"{c.Id}: dropped unknown enemy {m.EnemyId}"); return true; }
                if (m.Count < 1) { report.Repairs.Add($"{c.Id}: dropped empty member entry"); return true; }
                return false;
            });
            while (c.Members.Sum(m => m.Count) > MaxMembersPerCollective)
            {
                var last = c.Members[^1];
                if (last.Count > 1) last.Count--; else c.Members.RemoveAt(c.Members.Count - 1);
                report.Repairs.Add($"{c.Id}: trimmed to {MaxMembersPerCollective} members");
            }
            if (c.Members.Count == 0) { report.Repairs.Add($"{c.Id}: dropped, no members"); return true; }
            if (!RoomOk(c.Goal.Room)) { report.Repairs.Add($"{c.Id}: dropped, unknown room {c.Goal.Room}"); return true; }
            if (c.Goal.Room == start) { report.Repairs.Add($"{c.Id}: dropped, posted in the start room"); return true; }
            if (IsSafe(c.Goal.Room)) { report.Repairs.Add($"{c.Id}: dropped, posted in the safe room"); return true; }
            if (IsVault(c.Goal.Room) && c.Goal.Kind is not (GoalKind.Guard or GoalKind.Defend)) { c.Goal.Kind = GoalKind.Guard; report.Repairs.Add($"{c.Id}: vault group changed to Guard"); }
            return false;
        });
        // a railed floor may post one more group at each intermediate stop
        var maxCollectives = MaxCollectives + (layout.Rail is { } rl ? Math.Max(0, rl.Stops.Count - 2) : 0);
        if (plan.Collectives.Count > maxCollectives)
        {
            plan.Collectives.RemoveRange(maxCollectives, plan.Collectives.Count - maxCollectives);
            report.Repairs.Add($"kept only {maxCollectives} collectives");
        }
        ids = plan.Collectives.Select(c => c.Id).ToHashSet();
        foreach (var c in plan.Collectives)
        {
            var g = c.Goal;
            switch (g.Kind)
            {
                case GoalKind.Patrol:
                    var route = new List<int>();
                    if (!g.Route.Contains(g.Room)) g.Route.Insert(0, g.Room);
                    foreach (var r in g.Route)
                    {
                        if (!RoomOk(r) || r == start || IsVault(r) || IsSafe(r) || menu.Room(r)!.IsJail) { report.Repairs.Add($"{c.Id}: dropped route room {r}"); continue; }
                        if (route.Count > 0 && route[^1] == r) continue;
                        if (route.Count > 0 && !menu.Rooms[route[^1]].Neighbours.Contains(r)) { report.Repairs.Add($"{c.Id}: route hop {route[^1]}->{r} is not connected"); continue; }
                        route.Add(r);
                    }
                    if (route.Distinct().Count() < 2) { g.Kind = GoalKind.Guard; g.Route.Clear(); report.Repairs.Add($"{c.Id}: patrol needs two rooms, changed to Guard"); }
                    else { g.Route = route; g.Room = route[0]; }
                    break;
                case GoalKind.Alarm:
                    if (g.AlarmTarget == null || g.AlarmTarget == c.Id || !ids.Contains(g.AlarmTarget))
                    {
                        // the tier's creatures run to their own kind before they run to bandits
                        var other = plan.Collectives.FirstOrDefault(o => o.Id != c.Id && !o.Members.Any(m => menu.IsBandit(m.EnemyId))) ?? plan.Collectives.FirstOrDefault(o => o.Id != c.Id);
                        if (other == null) { g.Kind = GoalKind.Guard; g.AlarmTarget = null; report.Repairs.Add($"{c.Id}: nobody to alarm, changed to Guard"); }
                        else { g.AlarmTarget = other.Id; report.Repairs.Add($"{c.Id}: alarm target set to {other.Id}"); }
                    }
                    break;
                case GoalKind.Defend:
                    if (!ObjectiveOk(layout, menu, plan, g)) { g.Kind = GoalKind.Guard; g.Objective = null; report.Repairs.Add($"{c.Id}: objective missing or invalid, changed to Guard"); }
                    break;
            }
            if (g.Kind != GoalKind.Patrol) g.Route.Clear();
            if (g.Kind != GoalKind.Alarm) g.AlarmTarget = null;
            if (g.Kind != GoalKind.Defend) g.Objective = null;
            if (g.RallyRoom is { } rally && (!RoomOk(rally) || rally == start || IsSafe(rally))) { g.RallyRoom = null; report.Repairs.Add($"{c.Id}: dropped invalid rally room"); }
        }

        // loners
        plan.Loners.RemoveAll(l =>
        {
            if (menu.Enemy(l.EnemyId) == null) { report.Repairs.Add($"dropped loner of unknown enemy {l.EnemyId}"); return true; }
            if (!RoomOk(l.Room) || l.Room == start || IsVault(l.Room) || IsSafe(l.Room)) { report.Repairs.Add($"dropped loner in room {l.Room}"); return true; }
            return false;
        });

        // capacity per room
        foreach (var info in menu.Rooms)
        {
            var wanted = plan.Collectives.Where(c => c.Goal.Room == info.Index).Sum(c => c.Members.Sum(m => m.Count)) + plan.Loners.Count(l => l.Room == info.Index);
            var free = info.FreeCells - plan.Chests.Count(ch => ch.Room == info.Index) - plan.Traps.Count(t => t.Room == info.Index);
            while (wanted > free)
            {
                var loner = plan.Loners.FindLastIndex(l => l.Room == info.Index);
                if (loner >= 0) { plan.Loners.RemoveAt(loner); wanted--; report.Repairs.Add($"room {info.Index} full, dropped a loner"); continue; }
                var c = plan.Collectives.LastOrDefault(c => c.Goal.Room == info.Index && c.Members.Count > 0);
                if (c == null) break;
                var m = c.Members[^1];
                if (m.Count > 1) m.Count--; else c.Members.RemoveAt(c.Members.Count - 1);
                wanted--;
                report.Repairs.Add($"room {info.Index} full, trimmed {c.Id}");
            }
        }
        plan.Collectives.RemoveAll(c => c.Members.Count == 0);

        // bandits live in camps and nowhere else; every camp has its band
        foreach (var c in plan.Collectives)
        {
            if (menu.Room(c.Goal.Room)?.IsCamp == true) continue;
            var dropped = c.Members.RemoveAll(m => menu.IsBandit(m.EnemyId));
            if (dropped > 0) report.Repairs.Add($"{c.Id}: bandits only live in camps, dropped {dropped}");
        }
        plan.Collectives.RemoveAll(c => { if (c.Members.Count > 0) return false; report.Repairs.Add($"{c.Id}: dropped, only bandits outside a camp"); return true; });
        plan.Loners.RemoveAll(l => { if (!menu.IsBandit(l.EnemyId) || menu.Room(l.Room)?.IsCamp == true) return false; report.Repairs.Add("dropped a bandit loner outside a camp"); return true; });
        var bandits = menu.Roster.Where(e => e.IsBandit).ToList();
        if (bandits.Count > 0)
            foreach (var camp in menu.CampRooms.Where(r => r.Index != start))
            {
                if (plan.Collectives.Any(c => c.Goal.Room == camp.Index && c.Members.Any(m => menu.IsBandit(m.EnemyId)))) continue;
                var posted = plan.Collectives.FirstOrDefault(c => c.Goal.Room == camp.Index);
                if (posted != null)
                {
                    var count = posted.Members.Sum(m => m.Count);
                    posted.Members = BanditBand(bandits, count);
                    report.Repairs.Add($"{posted.Id}: the camp's group became bandits");
                }
                else
                {
                    var id = "bandits";
                    while (plan.Collectives.Any(c => c.Id == id)) id += "x";
                    plan.Collectives.Add(new CollectivePlan { Id = id, Name = "Camp Bandits", FactionId = FloorMenu.BanditFaction, Members = BanditBand(bandits, 3), Goal = new GoalPlan { Kind = GoalKind.Guard, Room = camp.Index } });
                    report.Repairs.Add($"camp room {camp.Index} had no bandits, added a band");
                }
            }

        // hordes: one per nest room, swarm creatures only, sensible counts; they sit outside the budget
        var hordeIds = new HashSet<string>();
        plan.Hordes.RemoveAll(h =>
        {
            if (string.IsNullOrWhiteSpace(h.Id) || !hordeIds.Add(h.Id)) { h.Id = $"horde{hordeIds.Count + 1}"; hordeIds.Add(h.Id); }
            var room = menu.Room(h.Room);
            if (room == null || !room.HasNest || room.Index == start) { report.Repairs.Add($"horde {h.Id}: room {h.Room} has no nest, dropped"); return true; }
            if (room.IsShrine) { report.Repairs.Add($"horde {h.Id}: room {h.Room} is the shrine, whose breaches belong to the ritual; dropped"); return true; }
            // older plans: one creature and a count
            if (h.Members.Count == 0 && !string.IsNullOrEmpty(h.EnemyId)) h.Members.Add(new MemberPlan { EnemyId = h.EnemyId, Count = Math.Max(1, h.Count ?? 20) });
            h.EnemyId = null; h.Count = null;
            var fodder = menu.Roster.FirstOrDefault(e => e.IsHorde);
            if (fodder == null) { report.Repairs.Add($"horde {h.Id}: no swarm creature on the roster, dropped"); return true; }
            h.Members.RemoveAll(m =>
            {
                if (m.Count < 1) return true;
                if (menu.Enemy(m.EnemyId) == null) { report.Repairs.Add($"horde {h.Id}: dropped unknown creature {m.EnemyId}"); return true; }
                if (menu.IsBandit(m.EnemyId) || menu.Enemy(m.EnemyId)!.Id == menu.BossId) { report.Repairs.Add($"horde {h.Id}: {m.EnemyId} does not march with a horde, dropped"); return true; }
                return false;
            });
            foreach (var m in h.Members.Where(m => m.Elite != null)) { m.Elite = null; report.Repairs.Add($"horde {h.Id}: {m.EnemyId} cannot be an elite"); }
            // merge duplicates
            h.Members = h.Members.GroupBy(m => m.EnemyId).Select(g => new MemberPlan { EnemyId = g.Key, Count = g.Sum(m => m.Count) }).ToList();
            if (h.Members.All(m => !menu.IsHorde(m.EnemyId))) { h.Members.Insert(0, new MemberPlan { EnemyId = fodder.Id, Count = Math.Max(FloorMenu.MinHorde, h.Members.Sum(m => m.Count) * 3) }); report.Repairs.Add($"horde {h.Id}: no swarm creatures, added {fodder.Id}"); }
            // the big ones: only from a certain depth, and never more than a quarter
            var bigAllowed = menu.Floor >= FloorMenu.BigMembersFromFloor ? Math.Min(FloorMenu.MaxBigMembers, Math.Max(1, h.Total / 4)) : 0;
            var big = h.Members.Where(m => !menu.IsHorde(m.EnemyId)).Sum(m => m.Count);
            while (big > bigAllowed)
            {
                var m = h.Members.Where(x => !menu.IsHorde(x.EnemyId)).OrderByDescending(x => menu.CostOf(x.EnemyId)).First();
                if (m.Count > 1) m.Count--; else h.Members.Remove(m);
                big--;
                report.Repairs.Add($"horde {h.Id}: too many big creatures, trimmed {m.EnemyId}");
            }
            while (h.Total > FloorMenu.MaxHorde) { var m = h.Members.MaxBy(x => x.Count)!; m.Count--; if (m.Count == 0) h.Members.Remove(m); }
            while (h.Total < FloorMenu.MinHorde) { var m = h.Members.First(x => menu.IsHorde(x.EnemyId)); m.Count++; }
            if (plan.Hordes.Any(o => o != h && o.Room == h.Room && hordeIds.Contains(o.Id) && plan.Hordes.IndexOf(o) < plan.Hordes.IndexOf(h))) { report.Repairs.Add($"horde {h.Id}: room {h.Room} already has a horde, dropped"); return true; }
            if (h.Trigger == HordeTrigger.Alarm && plan.Collectives.All(c => c.Goal.Kind != GoalKind.Alarm)) { h.Trigger = HordeTrigger.Enter; report.Repairs.Add($"horde {h.Id}: nobody to raise an alarm, triggers on entry"); }
            if (h.Trigger == HordeTrigger.Chest && room.Chests.Count == 0 && plan.Chests.All(c => c.Room != room.Index)) { h.Trigger = HordeTrigger.Enter; report.Repairs.Add($"horde {h.Id}: no chest in its room, triggers on entry"); }
            h.Name = Clip(h.Name, 40);
            h.Line.Text = Clip(h.Line.Text, MaxLineLength);
            return false;
        });
        if (plan.Hordes.Count > MaxHordes) { plan.Hordes.RemoveRange(MaxHordes, plan.Hordes.Count - MaxHordes); report.Repairs.Add($"kept only {MaxHordes} hordes"); }

        // elites: the planner's champions sanitised, never on a horde creature, never more than the floor allows
        var eliteBudget = EliteRules.CountFor(menu.Floor, 0);
        foreach (var c in plan.Collectives)
            foreach (var m in c.Members)
            {
                if (m.Elite == null) continue;
                if (menu.IsHorde(m.EnemyId)) { m.Elite = null; report.Repairs.Add($"{c.Id}: {m.EnemyId} cannot be an elite (horde creature)"); continue; }
                if (eliteBudget-- <= 0) { m.Elite = null; report.Repairs.Add($"floor allows {EliteRules.CountFor(menu.Floor, 0)} elites, dropped the rest"); continue; }
                SanitizeElite(m.Elite, menu.Floor);
            }
        foreach (var l in plan.Loners)
        {
            if (l.Elite == null) continue;
            if (menu.IsHorde(l.EnemyId)) { l.Elite = null; report.Repairs.Add($"loner {l.EnemyId} cannot be an elite (horde creature)"); continue; }
            if (eliteBudget-- <= 0) { l.Elite = null; report.Repairs.Add($"floor allows {EliteRules.CountFor(menu.Floor, 0)} elites, dropped the rest"); continue; }
            SanitizeElite(l.Elite, menu.Floor);
        }

        // budget and counts
        var total = plan.EnemyTotal;
        while (total > menu.MaxEnemies && TrimOne(plan, menu, report, "over the enemy cap")) total = plan.EnemyTotal;
        var cost = Cost(plan, menu);
        while (enforceBudget && cost > menu.Budget * FloorMenu.BudgetTolerance && total > menu.MinEnemies && TrimOne(plan, menu, report, "over budget")) { total = plan.EnemyTotal; cost = Cost(plan, menu); }
        if (enforceBudget && total < menu.MinEnemies && menu.Roster.Count > 0)
        {
            var cheapest = menu.Roster.MinBy(e => e.Cost)!;
            var rooms = menu.Rooms.Where(r => !r.IsStart && !r.IsVault && !r.IsSafeRoom && !r.IsArena && r.FreeCells > 0).OrderByDescending(r => r.HopsFromStart).ToList();
            for (var i = 0; total < menu.MinEnemies && rooms.Count > 0; i++)
            {
                plan.Loners.Add(new LonerPlan { EnemyId = cheapest.Id, Room = rooms[i % rooms.Count].Index });
                total++;
            }
            report.Repairs.Add($"too few enemies, added loners up to {menu.MinEnemies}");
        }

        // boss
        if (plan.Boss != null)
        {
            if (!menu.BossFloor) { plan.Boss = null; report.Repairs.Add("dropped boss on a non-boss floor"); }
            else
            {
                plan.Boss.Name = Clip(plan.Boss.Name, 40);
                if (plan.Boss.SignatureOverride != null && !Enum.TryParse<Enemies.Signature>(plan.Boss.SignatureOverride, true, out var sig) || plan.Boss.SignatureOverride?.Equals("None", StringComparison.OrdinalIgnoreCase) == true)
                { plan.Boss.SignatureOverride = null; report.Repairs.Add("dropped unknown boss signature"); }
                ClipLine(plan.Boss.IntroLine); ClipLine(plan.Boss.DeathLine);
            }
        }

        // chests and traps
        plan.Chests.RemoveAll(ch =>
        {
            if (!RoomOk(ch.Room) || ch.Room == start || IsSafe(ch.Room)) { report.Repairs.Add($"dropped chest in room {ch.Room}"); return true; }
            return false;
        });
        if (plan.Chests.Count > FloorMenu.MaxExtraChests) { plan.Chests.RemoveRange(FloorMenu.MaxExtraChests, plan.Chests.Count - FloorMenu.MaxExtraChests); report.Repairs.Add("too many extra chests"); }
        plan.Traps.RemoveAll(t =>
        {
            if (!RoomOk(t.Room) || t.Room == start || IsSafe(t.Room)) { report.Repairs.Add($"dropped trap in room {t.Room}"); return true; }
            if (!Enum.TryParse<TrapKind>(t.Kind, true, out var kind)) { report.Repairs.Add($"dropped trap of unknown kind {t.Kind}"); return true; }
            t.Kind = kind.ToString();
            return false;
        });
        if (plan.Traps.Count > FloorMenu.MaxExtraTraps) { plan.Traps.RemoveRange(FloorMenu.MaxExtraTraps, plan.Traps.Count - FloorMenu.MaxExtraTraps); report.Repairs.Add("too many extra traps"); }

        ValidateNpcsAndQuests(plan, menu, start, report, campaign);
        ValidateCrew(plan, layout, report, campaign);
        ValidateRailPlans(plan, layout, menu, report);

        // text
        plan.Title = Clip(plan.Title, 60);
        ClipLine(plan.IntroLine);
        foreach (var n in plan.Narration) ClipLine(n.Line);
        plan.Narration.RemoveAll(n =>
        {
            if (n.Trigger == NarrationTrigger.RoomEnter && (n.Room == null || !RoomOk(n.Room.Value))) { report.Repairs.Add("dropped narration for unknown room"); return true; }
            return false;
        });
        return report;
    }

    private static void ValidateCrew(FloorPlan plan, FloorLayout layout, ValidationReport report, CampaignPlan? campaign)
    {
        var ids = new HashSet<string>();
        var cast = campaign?.Cast.Select(c => c.Name.Trim().ToLowerInvariant()).ToHashSet() ?? new HashSet<string>();
        // a crew member is spawned as an NPC of the floor: an id another NPC (or dead Grisel's body) holds is taken
        var taken = plan.Npcs.Select(n => n.Id).Append(Objectives.CrewRules.GriselBodyId).ToHashSet();
        plan.Crew.RemoveAll(c =>
        {
            if (string.IsNullOrWhiteSpace(c.Id) || string.IsNullOrWhiteSpace(c.Name) || !ids.Add(c.Id)) { report.Repairs.Add($"crew {c.Id}: dropped (no name or a duplicate id)"); return true; }
            if (taken.Contains(c.Id)) { report.Repairs.Add($"crew {c.Id}: dropped (id taken)"); return true; }
            c.Name = Clip(c.Name, 40);
            if (cast.Contains(c.Name.Trim().ToLowerInvariant())) { report.Repairs.Add($"crew {c.Id}: dropped (a cast name)"); return true; }
            c.Sex = string.Equals(c.Sex, "woman", StringComparison.OrdinalIgnoreCase) ? "woman" : "man";
            return false;
        });
        var cap = layout.Rail is { } rail ? Objectives.CrewRules.PerWagon * Math.Max(0, rail.Stops.Count - 2) : 6;
        if (plan.Crew.Count > cap) { report.Repairs.Add($"crew: dropped {plan.Crew.Count - cap} over the cap of {cap}"); plan.Crew.RemoveRange(cap, plan.Crew.Count - cap); }
    }

    public const int MaxRailEncounters = 12, MaxRailSetPieces = 20, MaxSlagRiders = 3;
    /// <summary>The encounters a line of <paramref name="rides"/> rides keeps: three a ride, never fewer than today's cap.</summary>
    public static int RailEncounterCap(int rides) => Math.Max(MaxRailEncounters, 3 * rides);
    /// <summary>The set pieces a line of <paramref name="rides"/> rides keeps: five a ride, never fewer than today's cap.</summary>
    public static int RailSetPieceCap(int rides) => Math.Max(MaxRailSetPieces, 5 * rides);
    private static readonly string[] EncounterKinds = { "slag_cart", "lone", "thrower", "golem" };
    private static readonly string[] SetPieceKinds = { "beam", "buckets", "switch_out", "switch_back", "heap", "hammers", "pour", "trestle", "jump", "spur_switch", "buffer", "barricade", "slag_barricade", "dark", "runaway", "split" };

    private static void ValidateRailPlans(FloorPlan plan, FloorLayout layout, FloorMenu menu, ValidationReport report)
    {
        if (plan.RailEncounters.Count == 0 && plan.RailSetPieces.Count == 0) return;
        if (layout.Rail is not { } rail)
        {
            plan.RailEncounters.Clear();
            plan.RailSetPieces.Clear();
            report.Repairs.Add("rail encounters dropped (no rails)");
            return;
        }
        var rides = rail.Stops.Count - 1;
        var ids = new HashSet<string>();
        float Clamp(string what, string id, float at)
        {
            var c = Math.Clamp(at, 0.05f, 0.95f);
            if (c != at) report.Repairs.Add($"{what} {id}: at clamped to {c}");
            return c;
        }
        plan.RailEncounters.RemoveAll(e =>
        {
            if (string.IsNullOrWhiteSpace(e.Id) || !ids.Add(e.Id)) { report.Repairs.Add($"rail encounter {e.Id}: dropped (no id or a duplicate)"); return true; }
            if (!EncounterKinds.Contains(e.Kind)) { report.Repairs.Add($"rail encounter {e.Id}: dropped (unknown kind {e.Kind})"); return true; }
            if (e.Ride < 1 || e.Ride > rides) { report.Repairs.Add($"rail encounter {e.Id}: dropped (ride {e.Ride} out of range)"); return true; }
            e.Riders ??= new();
            e.Riders.RemoveAll(m => m.Count < 1);
            if (e.Riders.Count == 0) { report.Repairs.Add($"rail encounter {e.Id}: dropped (no riders)"); return true; }
            foreach (var m in e.Riders)
            {
                if (m.EnemyId is null || !menu.KnownEnemies.TryGetValue(m.EnemyId, out var isBoss)) { report.Repairs.Add($"rail encounter {e.Id}: dropped (unknown enemy {m.EnemyId})"); return true; }
                if (isBoss) { report.Repairs.Add($"rail encounter {e.Id}: no bosses"); return true; }
            }
            e.At = Clamp("rail encounter", e.Id, e.At);
            if (e.Line != null) { CampaignRepair.RepairLine(e.Line, report.Repairs, $"rail encounter {e.Id} line"); ClipLine(e.Line); }
            if (e.Kind == "slag_cart")
            {
                var total = e.Riders.Sum(m => m.Count);
                if (total > MaxSlagRiders)
                {
                    report.Repairs.Add($"rail encounter {e.Id}: riders trimmed to {MaxSlagRiders}");
                    var left = MaxSlagRiders;
                    foreach (var m in e.Riders) { m.Count = Math.Min(m.Count, left); left -= m.Count; }
                    e.Riders.RemoveAll(m => m.Count < 1);
                }
            }
            return false;
        });
        var encounterCap = RailEncounterCap(rides);
        if (plan.RailEncounters.Count > encounterCap) { report.Repairs.Add($"rail encounters: dropped {plan.RailEncounters.Count - encounterCap} over the cap of {encounterCap}"); plan.RailEncounters.RemoveRange(encounterCap, plan.RailEncounters.Count - encounterCap); }
        var pieceIds = new HashSet<string>();
        plan.RailSetPieces.RemoveAll(p =>
        {
            if (string.IsNullOrWhiteSpace(p.Id) || !pieceIds.Add(p.Id)) { report.Repairs.Add($"rail set piece {p.Id}: dropped (no id or a duplicate)"); return true; }
            if (!SetPieceKinds.Contains(p.Kind)) { report.Repairs.Add($"rail set piece {p.Id}: dropped (unknown kind {p.Kind})"); return true; }
            if (p.Ride < 1 || p.Ride > rides) { report.Repairs.Add($"rail set piece {p.Id}: dropped (ride {p.Ride} out of range)"); return true; }
            p.At = Clamp("rail set piece", p.Id, p.At);
            return false;
        });
        var pieceCap = RailSetPieceCap(rides);
        if (plan.RailSetPieces.Count > pieceCap) { report.Repairs.Add($"rail set pieces: dropped {plan.RailSetPieces.Count - pieceCap} over the cap of {pieceCap}"); plan.RailSetPieces.RemoveRange(pieceCap, plan.RailSetPieces.Count - pieceCap); }
    }

    private static void ValidateNpcsAndQuests(FloorPlan plan, FloorMenu menu, int start, ValidationReport report, CampaignPlan? campaign = null)
    {
        var npcIds = new HashSet<string>();
        var rosterIds = menu.Roster.Select(r => r.Id).ToHashSet();
        plan.Npcs.RemoveAll(n =>
        {
            if (string.IsNullOrWhiteSpace(n.Id) || !npcIds.Add(n.Id)) { report.Repairs.Add("dropped NPC with missing or duplicate id"); return true; }
            if (n.Ritualist && !menu.Rooms.Any(r => r.IsShrine)) { n.Ritualist = false; report.Repairs.Add($"NPC {n.Id}: no ritual on this floor to lead"); }
            // the rite's leader stands beside the altar wherever the plan put them; everyone else needs a room people may stand in
            if (!n.Ritualist && (menu.Room(n.Room) == null || menu.Room(n.Room)!.IsVault || menu.Room(n.Room)!.IsSafeRoom || menu.Room(n.Room)!.IsArena || menu.Room(n.Room)!.IsJail || menu.Room(n.Room)!.IsShrine)) { report.Repairs.Add($"dropped NPC {n.Id} in room {n.Room}"); return true; }
            if (n.CastId != null && campaign?.CastFor(n.CastId) is { Merchant: true }) { report.Repairs.Add($"dropped NPC {n.Id}: the merchant is never a floor NPC"); return true; }
            n.Name = Clip(n.Name, 40);
            if (n.CastId != null)
            {
                if (campaign?.CastFor(n.CastId) is { } cast) n.Name = Clip(cast.Name, 40);
                else if (campaign != null) { n.CastId = null; report.Repairs.Add($"NPC {n.Id}: unknown cast id cleared"); }
            }
            n.RequiresFlag = FlagRules.Clean(n.RequiresFlag); n.ForbidsFlag = FlagRules.Clean(n.ForbidsFlag);
            if (n.RequiresFlag != null && n.RequiresFlag == n.ForbidsFlag) { n.ForbidsFlag = null; report.Repairs.Add($"NPC {n.Id}: contradictory flags"); }
            CampaignRepair.RepairPersona(n.Persona, report.Repairs, $"NPC {n.Id}");
            if (n.Persona is { IsEmpty: true }) n.Persona = null;
            foreach (var node in n.Script.Nodes) CampaignRepair.RepairLine(node.Line, report.Repairs, $"NPC {n.Id} line");
            n.Calls ??= new();
            n.Calls.RemoveAll(c => c?.Line == null);
            foreach (var c in n.Calls) { CampaignRepair.RepairLine(c.Line, report.Repairs, $"NPC {n.Id} call {c.Key}"); ClipLine(c.Line); }
            if (n.Betrayal != null)
            {
                if (!rosterIds.Contains(n.Betrayal.EnemyId)) { n.Betrayal = null; report.Repairs.Add($"NPC {n.Id}: betrayal creature not in the roster, dropped"); }
                else n.Betrayal.Count = Math.Clamp(n.Betrayal.Count, 1, 4);
            }
            var nodeIds = n.Script.Nodes.Select(x => x.Id).ToHashSet();
            if (n.Script.Nodes.Count == 0 || !nodeIds.Contains(n.Script.Start))
            {
                if (n.Script.Nodes.Count > 0) { n.Script.Start = n.Script.Nodes[0].Id; report.Repairs.Add($"NPC {n.Id}: start node fixed"); }
                else if (!n.Ritualist) { report.Repairs.Add($"dropped NPC {n.Id} with no dialogue"); return true; } // the ritualist speaks through the rite
            }
            foreach (var node in n.Script.Nodes)
            {
                ClipLine(node.Line);
                foreach (var ch in node.Choices)
                {
                    ch.Text = Clip(ch.Text, 120);
                    if (ch.Next != null && !nodeIds.Contains(ch.Next)) { ch.Next = null; report.Repairs.Add($"NPC {n.Id}: choice pointed at unknown node"); }
                    ch.Flag = FlagRules.Clean(ch.Flag);
                    ch.RequiresFlag = FlagRules.Clean(ch.RequiresFlag); ch.ForbidsFlag = FlagRules.Clean(ch.ForbidsFlag);
                    if (ch.Effect == DialogueEffect.Choose && ch.Flag == null) { ch.Effect = DialogueEffect.None; report.Repairs.Add($"NPC {n.Id}: a Choose choice had no flag"); }
                    if (ch.Effect != DialogueEffect.Choose) ch.Flag = null;
                    if (ch.Effect == DialogueEffect.Betray && n.Betrayal == null) { ch.Effect = DialogueEffect.Farewell; report.Repairs.Add($"NPC {n.Id}: a Betray choice on an NPC with no Betrayal, made a farewell"); }
                }
                if (node.Choices.Count == 0) node.Choices.Add(new ChoicePlan { Text = "Farewell.", Effect = DialogueEffect.Farewell });
            }
            return false;
        });
        // an NPC dropped above was counted before its room was checked: only the ones kept may give or join a quest
        npcIds.IntersectWith(plan.Npcs.Select(n => n.Id));
        var questIds = new HashSet<string>();
        plan.Quests.RemoveAll(q =>
        {
            if (string.IsNullOrWhiteSpace(q.Id) || !questIds.Add(q.Id)) { report.Repairs.Add("dropped quest with missing or duplicate id"); return true; }
            if (!npcIds.Contains(q.GiverNpc)) { report.Repairs.Add($"dropped quest {q.Id}: giver NPC missing"); return true; }
            q.Title = Clip(q.Title, 60); q.Objective = Clip(q.Objective, 120);
            foreach (var l in new[] { q.Lines.Offer, q.Lines.Accept, q.Lines.Decline, q.Lines.Complete, q.Lines.Fail }) ClipLine(l);
            q.Reward.Xp = Math.Clamp(q.Reward.Xp, 0, 500 * Math.Max(1, menu.Floor));
            q.RequiresFlag = FlagRules.Clean(q.RequiresFlag); q.ForbidsFlag = FlagRules.Clean(q.ForbidsFlag);
            if (q.RequiresFlag != null && q.RequiresFlag == q.ForbidsFlag) q.ForbidsFlag = null;
            var giver = plan.Npcs.First(n => n.Id == q.GiverNpc);
            switch (q.Template)
            {
                case QuestTemplate.Investigate:
                    var unusable = q.Params.ClueRooms.Distinct().Where(r => !(menu.Room(r) is { IsVault: false, IsSafeRoom: false, IsArena: false, FreeCells: > 0 } && r != start)).ToList();
                    if (unusable.Count > 0) report.Repairs.Add($"quest {q.Id}: dropped clue room {string.Join(", ", unusable)}");
                    q.Params.ClueRooms = q.Params.ClueRooms.Distinct().Where(r => menu.Room(r) is { IsVault: false, IsSafeRoom: false, IsArena: false, FreeCells: > 0 } && r != start).Take(4).ToList();
                    if (q.Params.ClueRooms.Count == 0) { report.Repairs.Add($"dropped quest {q.Id}: no usable clue rooms"); return true; }
                    q.Params.ClueName = Clip(string.IsNullOrWhiteSpace(q.Params.ClueName) ? "clue" : q.Params.ClueName!, 40);
                    break;
                case QuestTemplate.Decide:
                {
                    var flags = giver.Script.Nodes.SelectMany(x => x.Choices).Where(c => c.Effect == DialogueEffect.Choose && c.Flag != null).Select(c => c.Flag!).Distinct().Count();
                    if (flags < 2) { report.Repairs.Add($"dropped quest {q.Id}: a Decide quest needs two Choose choices with different flags on its giver"); return true; }
                    break;
                }
                case QuestTemplate.Betrayal:
                    if (giver.Betrayal == null || !giver.Script.Nodes.Any(x => x.Choices.Any(c => c.Effect == DialogueEffect.Betray))) { report.Repairs.Add($"dropped quest {q.Id}: the giver has no Betrayal or no Betray choice"); return true; }
                    break;
                case QuestTemplate.KillTarget:
                    if (q.Params.TargetCollective == null || plan.Collectives.All(c => c.Id != q.Params.TargetCollective)) { report.Repairs.Add($"dropped quest {q.Id}: target collective missing"); return true; }
                    break;
                case QuestTemplate.Recover:
                    if (q.Params.ItemRoom is not { } room || menu.Room(room) == null || menu.Room(room)!.IsSafeRoom || room == start) { report.Repairs.Add($"dropped quest {q.Id}: item room invalid"); return true; }
                    q.Params.ItemName = Clip(string.IsNullOrWhiteSpace(q.Params.ItemName) ? "Relic" : q.Params.ItemName!, 40);
                    break;
                case QuestTemplate.Escort:
                    if (q.Params.EscortNpc == null || !npcIds.Contains(q.Params.EscortNpc)) { report.Repairs.Add($"dropped quest {q.Id}: escort NPC missing"); return true; }
                    if (plan.Npcs.First(n => n.Id == q.Params.EscortNpc).Room == menu.ExitRoom?.Index) { report.Repairs.Add($"dropped quest {q.Id}: escort NPC already at the exit"); return true; }
                    break;
                case QuestTemplate.HoldOut:
                    if (q.Params.WaveCollective == null || plan.Collectives.All(c => c.Id != q.Params.WaveCollective)) { report.Repairs.Add($"dropped quest {q.Id}: wave collective missing"); return true; }
                    q.Params.Waves = Math.Clamp(q.Params.Waves ?? 2, 1, MaxWaves);
                    break;
            }
            return false;
        });
        questIds = plan.Quests.Select(q => q.Id).ToHashSet(); // dropped quests are gone for good
        foreach (var n in plan.Npcs)
            if (n.QuestId != null && !questIds.Contains(n.QuestId)) { n.QuestId = null; report.Repairs.Add($"NPC {n.Id}: quest reference cleared"); }
        foreach (var n in plan.Npcs)
        {
            var quest = n.QuestId == null ? null : plan.Quests.FirstOrDefault(q => q.Id == n.QuestId);
            var hasAccept = n.Script.Nodes.Any(x => x.Choices.Any(c => c.Effect == DialogueEffect.AcceptQuest));
            var needsAccept = quest != null && quest.Template is not (QuestTemplate.Decide or QuestTemplate.Betrayal);
            if (needsAccept && !hasAccept) { n.QuestId = null; report.Repairs.Add($"NPC {n.Id}: no accept choice, quest unlinked"); }
            // the finished quest must be hand-in-able from the greeting; model-written scripts often park the turn-in on a node nothing links to
            if (n.QuestId != null && quest != null)
                foreach (var fix in DialogueRepair.EnsureReachable(n.Script, quest.Template, quest.Lines.Complete.Text, quest.Id)) report.Repairs.Add($"NPC {n.Id}: {fix}");
        }
        // a quest nobody gives is unreachable
        plan.Quests.RemoveAll(q => { var linked = plan.Npcs.Any(n => n.QuestId == q.Id); if (!linked) report.Repairs.Add($"dropped quest {q.Id}: no NPC offers it"); return !linked; });
        foreach (var nar in plan.Narration) { nar.RequiresFlag = FlagRules.Clean(nar.RequiresFlag); nar.ForbidsFlag = FlagRules.Clean(nar.ForbidsFlag); }
    }


    /// <summary>Members for a band of <paramref name="count"/>: two melee for every ranged one, cheapest first.</summary>
    public static List<MemberPlan> BanditBand(IReadOnlyList<EnemyOption> bandits, int count)
    {
        var melee = bandits.Where(b => b.Kind == EnemyKind.Melee).OrderBy(b => b.Cost).ToList();
        var ranged = bandits.Where(b => b.Kind != EnemyKind.Melee).OrderBy(b => b.Cost).ToList();
        var members = new List<MemberPlan>();
        void Add(string id) { var m = members.FirstOrDefault(x => x.EnemyId == id); if (m == null) members.Add(new MemberPlan { EnemyId = id, Count = 1 }); else m.Count++; }
        for (var i = 0; i < Math.Max(1, count); i++)
        {
            var pool = i % 3 == 2 && ranged.Count > 0 ? ranged : melee.Count > 0 ? melee : ranged;
            if (pool.Count == 0) break;
            Add(pool[0].Id);
        }
        return members;
    }

    private static bool ObjectiveOk(FloorLayout layout, FloorMenu menu, FloorPlan plan, GoalPlan g)
    {
        if (g.Objective == null) return false;
        switch (g.Objective.Kind)
        {
            case ObjectiveKind.Gate:
                return int.TryParse(g.Objective.Ref, out var gate) && menu.Room(gate)?.HasGate == true;
            case ObjectiveKind.Chest:
                if (!int.TryParse(g.Objective.Ref, out var chest)) return false;
                if (chest >= FloorContents.ExtraIndexBase) return chest - FloorContents.ExtraIndexBase < plan.Chests.Count;
                return chest >= 0 && chest < layout.ChestSpawns.Count;
            case ObjectiveKind.Lever:
                return int.TryParse(g.Objective.Ref, out var lever) && layout.Vaults.Any(v => v.Index == lever && v.Lever != null);
            case ObjectiveKind.Door:
                return int.TryParse(g.Objective.Ref, out var door) && layout.Vaults.Any(v => v.Index == door);
            case ObjectiveKind.Stairs:
                g.Objective.Ref = g.Objective.Ref.Equals("up", StringComparison.OrdinalIgnoreCase) ? "up" : "down";
                return true;
            case ObjectiveKind.Npc:
                return plan.Npcs.Any(n => n.Id == g.Objective.Ref);
        }
        return false;
    }

    private static float Cost(FloorPlan plan, FloorMenu menu)
        => plan.Collectives.Sum(c => c.Members.Sum(m => m.Count * menu.CostOf(m.EnemyId))) + plan.Loners.Sum(l => menu.CostOf(l.EnemyId));

    /// <summary>Remove the cheapest single enemy: loners first, then the largest collective's cheapest member.</summary>
    private static bool TrimOne(FloorPlan plan, FloorMenu menu, ValidationReport report, string why)
    {
        if (plan.Loners.Count > 0)
        {
            var idx = plan.Loners.Select((l, i) => (l, i)).MinBy(t => menu.CostOf(t.l.EnemyId)).i;
            plan.Loners.RemoveAt(idx);
            report.Repairs.Add($"{why}: dropped a loner");
            return true;
        }
        var c = plan.Collectives.Where(c => c.Members.Sum(m => m.Count) > 1).MaxBy(c => c.Members.Sum(m => m.Count));
        if (c == null) return false;
        var m = c.Members.MinBy(m => menu.CostOf(m.EnemyId))!;
        if (m.Count > 1) m.Count--; else c.Members.Remove(m);
        report.Repairs.Add($"{why}: trimmed {c.Id}");
        return true;
    }

    private static void SanitizeElite(ElitePlan e, int floor)
    {
        e.Affixes = EliteRules.Sanitize(e.Affixes, floor).Select(a => a.ToString()).ToList();
        e.Name = Clip(e.Name ?? "", EliteRules.MaxNameLength);
    }

    private static string Clip(string s, int max) => s.Length <= max ? s : s[..max];
    private static void ClipLine(Line line) => line.Text = Clip(line.Text, MaxLineLength);
}
