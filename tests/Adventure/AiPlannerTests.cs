using System.Text.Json;
using System.Text.Json.Nodes;
using DungeonCrawler.Adventure;
using DungeonCrawler.Adventure.Ai;
using DungeonCrawler.Dungeon;
using DungeonCrawler.Dungeon.Generation;
using Xunit;

namespace DungeonCrawler.Tests.Adventure;

public class PlanSchemasTests
{
    [Fact]
    public void FloorPlanSchema_IsStrict_And_OmitsGameOwnedFields()
    {
        var json = PlanSchemas.For<FloorPlan>();
        var root = JsonNode.Parse(json)!.AsObject();
        Assert.Equal("object", root["type"]!.GetValue<string>());
        Assert.False(root["additionalProperties"]!.GetValue<bool>());
        var props = root["properties"]!.AsObject();
        Assert.Contains("Collectives", props.Select(p => p.Key));
        Assert.DoesNotContain("LayoutHash", props.Select(p => p.Key));
        Assert.DoesNotContain("SchemaVersion", props.Select(p => p.Key));
        Assert.DoesNotContain("Floor", props.Select(p => p.Key));
        var required = root["required"]!.AsArray().Select(n => n!.GetValue<string>()).ToList();
        Assert.Equal(props.Count, required.Count);
        var goalKind = props["Collectives"]!["items"]!["properties"]!["Goal"]!["properties"]!["Kind"]!.AsObject();
        Assert.Equal(Enum.GetNames<GoalKind>(), goalKind["enum"]!.AsArray().Select(n => n!.GetValue<string>()));
        // optional fields are plain in the schema (the API allows few unions); the model writes "" or -1 for none
        var alarm = props["Collectives"]!["items"]!["properties"]!["Goal"]!["properties"]!["AlarmTarget"]!.AsObject();
        Assert.Equal("string", alarm["type"]!.GetValue<string>());
        Assert.Contains("empty string", alarm["description"]!.GetValue<string>());
        var rally = props["Collectives"]!["items"]!["properties"]!["Goal"]!["properties"]!["RallyRoom"]!.AsObject();
        Assert.Equal("integer", rally["type"]!.GetValue<string>());
        Assert.Contains("-1", rally["description"]!.GetValue<string>());
        Assert.DoesNotContain("VoiceId", json);
        Assert.Contains("\"description\"", json);
    }

    [Fact]
    public void OutlineAndBarkSchemas_Build()
    {
        var outline = JsonNode.Parse(PlanSchemas.For<OutlineWithFirstFloor>())!.AsObject();
        Assert.Contains("Campaign", outline["properties"]!.AsObject().Select(p => p.Key));
        Assert.DoesNotContain("GameSeed", PlanSchemas.For<OutlineWithFirstFloor>());
        var barks = JsonNode.Parse(PlanSchemas.For<BarkBank>())!.AsObject();
        Assert.Equal("array", barks["properties"]!["Members"]!["type"]!.GetValue<string>());
    }

    [Fact]
    public void DesignerOutput_MatchesSchemaShape()
    {
        var (layout, _, menu) = TestAdventure.Floor(4, 17);
        var plan = OfflineDesigner.PlanFloor(menu, layout, 17);
        var schema = JsonNode.Parse(PlanSchemas.For<FloorPlan>())!.AsObject();
        var doc = JsonNode.Parse(PlanFormat.Serialize(plan))!.AsObject();
        Check(schema, doc, "$");
    }

    /// <summary>A small structural check: every property in the document is in the schema and enums are valid.</summary>
    private static void Check(JsonObject schema, JsonNode? node, string path)
    {
        var types = schema["type"] is JsonArray arr ? arr.Select(t => t!.GetValue<string>()).ToList() : new List<string> { schema["type"]!.GetValue<string>() };
        if (node == null) return; // the designer writes null where the model would write "" or -1
        if (types.Contains("object") && node is JsonObject obj)
        {
            var props = schema["properties"]!.AsObject();
            foreach (var kv in obj)
            {
                if (!props.ContainsKey(kv.Key)) continue; // game-owned fields are serialized but not requested
                Check(props[kv.Key]!.AsObject(), kv.Value, $"{path}.{kv.Key}");
            }
        }
        else if (types.Contains("array") && node is JsonArray items)
        {
            foreach (var item in items) Check(schema["items"]!.AsObject(), item, path + "[]");
        }
        else if (schema["enum"] is JsonArray allowed)
        {
            Assert.Contains(node!.GetValue<string>(), allowed.Select(a => a!.GetValue<string>()));
        }
    }
}

public class PromptBuilderTests
{
    [Fact]
    public void FloorRequests_ShareAStablePrefix()
    {
        var campaign = OfflineDesigner.Outline(5, new[] { TestAdventure.Crypt });
        var (_, _, menu2) = TestAdventure.Floor(2, 3);
        var (_, _, menu3) = TestAdventure.Floor(3, 4);
        var a = PromptBuilder.Floor(PromptTemplates.Default, "claude-opus-5", campaign, menu2, "");
        var b = PromptBuilder.Floor(PromptTemplates.Default, "claude-opus-5", campaign, menu3, "The party died once.");
        Assert.Equal(a.System.Select(s => s.Text), b.System.Select(s => s.Text));
        Assert.All(a.System, s => Assert.True(s.Cache));
        Assert.NotEqual(a.User, b.User);
        Assert.NotEqual(a.Hash, b.Hash);
        Assert.Contains("Floor 3 menu", b.User);
        Assert.Contains("The party died once.", b.User);
        Assert.Contains("Rooms (index", a.User);
        Assert.True(a.ApproxInputTokens > 800, "the rules alone exceed the cache minimum");
    }

    [Fact]
    public void StoryBrief_RidesEveryCallAsACachedBlock_AndIsAbsentByDefault()
    {
        var campaign = OfflineDesigner.Outline(5, new[] { TestAdventure.Crypt });
        var (_, _, menu) = TestAdventure.Floor(2, 3);
        var party = new[] { new PartyMemberInfo("Brand", "Warrior") };
        var withBrief = new PromptTemplates { Brief = "# The Gullet of Gorm\nA god sleeps under Dunmarrow." };

        var plain = PromptBuilder.Floor(PromptTemplates.Default, "claude-opus-5", campaign, menu, "");
        Assert.DoesNotContain(plain.System, s => s.Text.Contains("Gullet of Gorm"));
        Assert.DoesNotContain("bible", plain.User);

        var floor = PromptBuilder.Floor(withBrief, "claude-opus-5", campaign, menu, "");
        var bible = Assert.Single(floor.System, s => s.Text.Contains("Gullet of Gorm"));
        Assert.True(bible.Cache);
        Assert.StartsWith(PromptTemplates.BriefPreamble.Trim(), bible.Text);
        Assert.Contains("bible", floor.User);

        var outline = PromptBuilder.Outline(withBrief, "claude-opus-5", 5, new[] { TestAdventure.Crypt }, TestAdventure.Roster, party, menu);
        Assert.Contains(outline.System, s => s.Text == bible.Text);
        Assert.Contains("Follow the campaign bible", outline.User);

        var barks = PromptBuilder.Barks(withBrief, "claude-opus-5", campaign, party);
        Assert.Contains(barks.System, s => s.Text == bible.Text);
    }

    [Fact]
    public void OutlineRequest_ListsTiersAndParty()
    {
        var (_, _, menu) = TestAdventure.Floor(1, 9);
        var r = PromptBuilder.Outline(PromptTemplates.Default, "claude-opus-5", 42, new[] { TestAdventure.Crypt }, TestAdventure.Roster, new[] { new PartyMemberInfo("Brand", "Warrior") }, menu);
        Assert.Equal("outline", r.Kind);
        Assert.Contains("Crypt", r.User);
        Assert.Contains("Brand the Warrior", r.User);
        Assert.Contains("Floor1", r.SchemaJson);
        Assert.Equal("xhigh", r.Effort);
    }
}

public class ResponseParserTests
{
    [Fact]
    public void Parses_PlainAndFenced_Json()
    {
        var (layout, _, menu) = TestAdventure.Floor(3, 8);
        var plan = OfflineDesigner.PlanFloor(menu, layout, 8);
        var json = PlanFormat.Serialize(plan);
        var plain = ResponseParser.Parse<FloorPlan>(new ClaudeResponse(json, "end_turn", ClaudeUsage.Zero));
        Assert.True(plain.Ok, plain.Error);
        Assert.Equal(plan.Collectives.Count, plain.Value!.Collectives.Count);
        var fenced = ResponseParser.Parse<FloorPlan>(new ClaudeResponse("Here is the plan:\n```json\n" + json + "\n```", "end_turn", ClaudeUsage.Zero));
        Assert.True(fenced.Ok, fenced.Error);
    }

    [Fact]
    public void Reports_Refusal_Truncation_Garbage()
    {
        Assert.Contains("refused", ResponseParser.Parse<FloorPlan>(new ClaudeResponse("", "refusal", ClaudeUsage.Zero, "cyber")).Error);
        Assert.Contains("truncated", ResponseParser.Parse<FloorPlan>(new ClaudeResponse("{\"Title\":", "max_tokens", ClaudeUsage.Zero)).Error);
        Assert.Contains("invalid JSON", ResponseParser.Parse<FloorPlan>(new ClaudeResponse("{not json", "end_turn", ClaudeUsage.Zero)).Error);
        Assert.Contains("boom", ResponseParser.Parse<FloorPlan>(ClaudeResponse.Failed("boom")).Error);
    }
}

public class CostMeterTests
{
    [Fact]
    public void Prices_And_Cap()
    {
        var meter = new CostMeter(new SpendDto(), capUsd: 1.0);
        meter.Record(new ClaudeUsage(1_000_000, 100_000, 0, 0));
        Assert.Equal(5.0 + 2.5, meter.Spend.Usd, 6);
        Assert.Equal(1, meter.Spend.Calls);
        var small = new ClaudeRequest("x", "m", Array.Empty<PromptBlock>(), "hi", "{}", "low", 100);
        Assert.False(meter.CanAfford(small));
        var free = new CostMeter(new SpendDto(), capUsd: 0);
        Assert.True(free.CanAfford(small));
        Assert.True(CostMeter.Estimate(small) > 0);
    }
}

public class ClaudePlannerTests
{
    private static FloorRequest Request(int floor, int seed, CampaignPlan? campaign = null)
    {
        var (layout, spec, _) = TestAdventure.Floor(floor, seed);
        return new FloorRequest(campaign ?? OfflineDesigner.Outline(1, new[] { TestAdventure.Crypt }), layout, spec, TestAdventure.Crypt, TestAdventure.Roster, floor, seed, PartySnapshot.Expected(floor), "");
    }

    private static string DesignerJson(FloorRequest req)
    {
        var menu = FloorMenu.Build(req.Layout, req.Spec, req.Tier, req.Roster, req.Floor, req.Snapshot);
        var plan = OfflineDesigner.PlanFloor(menu, req.Layout, req.Seed);
        plan.LayoutHash = 12345; // the model does not know it; the planner must reset it
        plan.Source = "should-be-overwritten";
        return PlanFormat.Serialize(plan);
    }

    [Fact]
    public async Task GoodAnswer_IsValidated_And_MarkedClaude()
    {
        var req = Request(4, 21);
        var fake = new FakeClaudeClient(_ => new ClaudeResponse(DesignerJson(req), "end_turn", new ClaudeUsage(5000, 2000, 4000, 0)));
        var meter = new CostMeter(new SpendDto(), 3.0);
        var planner = new ClaudePlanner(fake, meter);
        var result = await planner.PlanFloorAsync(req, CancellationToken.None);
        Assert.Equal(PlanSources.Claude, result.Plan.Source);
        Assert.Equal(4, result.Plan.Floor);
        Assert.Equal(req.Layout.ContentHash(), result.Plan.LayoutHash);
        Assert.True(result.Report.Ok);
        Assert.Single(fake.Requests);
        Assert.Equal("floor4", fake.Requests[0].Kind);
        Assert.True(meter.Spend.Usd > 0);
        Assert.Null(planner.LastFailure);
    }

    [Fact]
    public async Task Garbage_FallsBackToOffline()
    {
        var req = Request(3, 5);
        var fake = new FakeClaudeClient(_ => new ClaudeResponse("{\"Rooms\": \"nope\"", "end_turn", ClaudeUsage.Zero));
        var planner = new ClaudePlanner(fake, new CostMeter(new SpendDto(), 3.0));
        var result = await planner.PlanFloorAsync(req, CancellationToken.None);
        Assert.Equal(PlanSources.Offline, result.Plan.Source);
        Assert.NotNull(planner.LastFailure);
        Assert.True(result.Report.Ok);
    }

    [Fact]
    public async Task Refusal_FallsBack_WithoutRetry()
    {
        var req = Request(3, 5);
        var fake = new FakeClaudeClient(_ => new ClaudeResponse("", "refusal", ClaudeUsage.Zero, "unspecified"));
        var planner = new ClaudePlanner(fake, new CostMeter(new SpendDto(), 3.0));
        var result = await planner.PlanFloorAsync(req, CancellationToken.None);
        Assert.Equal(PlanSources.Offline, result.Plan.Source);
        Assert.Single(fake.Requests);
        Assert.Contains("refused", planner.LastFailure);
    }

    [Fact]
    public async Task Truncation_RetriesOnce_WithMoreTokens()
    {
        var req = Request(3, 5);
        var calls = 0;
        var fake = new FakeClaudeClient(_ => ++calls == 1 ? new ClaudeResponse("{\"Ti", "max_tokens", ClaudeUsage.Zero) : new ClaudeResponse(DesignerJson(req), "end_turn", ClaudeUsage.Zero));
        var planner = new ClaudePlanner(fake, new CostMeter(new SpendDto(), 3.0));
        var result = await planner.PlanFloorAsync(req, CancellationToken.None);
        Assert.Equal(PlanSources.Claude, result.Plan.Source);
        Assert.Equal(2, fake.Requests.Count);
        Assert.True(fake.Requests[1].MaxTokens > fake.Requests[0].MaxTokens);
    }

    [Fact]
    public async Task CapReached_SkipsTheCall()
    {
        var req = Request(3, 5);
        var fake = new FakeClaudeClient(_ => throw new InvalidOperationException("should not be called"));
        var planner = new ClaudePlanner(fake, new CostMeter(new SpendDto { Usd = 2.99 }, 3.0));
        var result = await planner.PlanFloorAsync(req, CancellationToken.None);
        Assert.Equal(PlanSources.Offline, result.Plan.Source);
        Assert.Empty(fake.Requests);
        Assert.Contains("cap", planner.LastFailure);
    }

    [Fact]
    public async Task InvalidPlan_FallsBack()
    {
        var req = Request(3, 5);
        var wrongFloorMenu = FloorMenu.Build(req.Layout, req.Spec, req.Tier, req.Roster, req.Floor, req.Snapshot);
        var plan = OfflineDesigner.PlanFloor(wrongFloorMenu, req.Layout, req.Seed);
        plan.Collectives.Clear(); plan.Loners.Clear();
        // a plan can always be repaired and enums parse leniently, so force a structural failure with broken JSON
        var json = PlanFormat.Serialize(plan).Replace("\"Rooms\": [", "\"Rooms\": {");
        var fake = new FakeClaudeClient(_ => new ClaudeResponse(json, "end_turn", ClaudeUsage.Zero));
        var planner = new ClaudePlanner(fake, new CostMeter(new SpendDto(), 3.0));
        var result = await planner.PlanFloorAsync(req, CancellationToken.None);
        Assert.Equal(PlanSources.Offline, result.Plan.Source);
    }

    [Fact]
    public async Task Outline_FillsMissingFloors_AndUsesFloor1()
    {
        var (layout, spec, menu) = TestAdventure.Floor(1, 77);
        var floor1 = OfflineDesigner.PlanFloor(menu, layout, 77);
        var campaign = new CampaignPlan { Title = "Test Arc", Synopsis = "Down.", Floors = { new FloorOutline { Floor = 1, Title = "The Landing" } } };
        var payload = PlanFormat.Serialize(new OutlineWithFirstFloor { Campaign = campaign, Floor1 = floor1 });
        var fake = new FakeClaudeClient(_ => new ClaudeResponse(payload, "end_turn", new ClaudeUsage(9000, 6000, 0, 9000)));
        var planner = new ClaudePlanner(fake, new CostMeter(new SpendDto(), 3.0));
        var req = new OutlineRequest(77, new[] { TestAdventure.Crypt }, TestAdventure.Roster, new[] { new PartyMemberInfo("Brand", "Warrior") }, layout, spec, 77, PartySnapshot.Expected(1));
        var result = await planner.OutlineAsync(req, CancellationToken.None);
        Assert.Equal("Test Arc", result.Campaign.Title);
        Assert.Equal(PlanSources.Claude, result.Campaign.Source);
        Assert.Equal(77UL, result.Campaign.GameSeed);
        Assert.Equal(TierRules.FinalFloor, result.Campaign.Floors.Count);
        Assert.Equal(TierRules.TierCount, result.Campaign.Tiers.Count);
        Assert.Equal(PlanSources.Claude, result.Floor1.Source);
        Assert.Equal(1, result.Floor1.Floor);
        Assert.True(result.Floor1Report.Ok);
    }

    [Fact]
    public async Task Barks_FillMissingMembers()
    {
        var bank = new BarkBank { Members = { new BarkSet { Member = "brand", Lines = { new BarkLine { Trigger = BarkTrigger.Kill, Line = new Line("Ha.") } } } } };
        var fake = new FakeClaudeClient(_ => new ClaudeResponse(PlanFormat.Serialize(bank), "end_turn", ClaudeUsage.Zero));
        var planner = new ClaudePlanner(fake, new CostMeter(new SpendDto(), 3.0));
        var party = new[] { new PartyMemberInfo("Brand", "Warrior"), new PartyMemberInfo("Sable", "Rogue") };
        var result = await planner.BarksAsync(OfflineDesigner.Outline(1, new[] { TestAdventure.Crypt }), party, CancellationToken.None);
        Assert.Equal(2, result.Count);
        Assert.Equal("Brand", result[0].Member);
        Assert.Single(result[0].Lines);
        Assert.Equal("Sable", result[1].Member);
        Assert.True(result[1].Lines.Count >= 20, "missing members get the default bank");
    }

    [Fact]
    public async Task Replay_And_Recording_RoundTrip()
    {
        var dir = Path.Combine(Path.GetTempPath(), "dc-replay-" + Guid.NewGuid().ToString("N"));
        try
        {
            var req = Request(2, 11);
            var live = new FakeClaudeClient(_ => new ClaudeResponse(DesignerJson(req), "end_turn", new ClaudeUsage(10, 20, 30, 40)));
            var recording = new RecordingClaudeClient(live, dir);
            var first = await new ClaudePlanner(recording, new CostMeter(new SpendDto(), 0)).PlanFloorAsync(req, CancellationToken.None);
            Assert.Equal(PlanSources.Claude, first.Plan.Source);
            Assert.True(File.Exists(Path.Combine(dir, "log.txt")));

            var replay = new ReplayClaudeClient(dir);
            var second = await new ClaudePlanner(replay, new CostMeter(new SpendDto(), 0)).PlanFloorAsync(req, CancellationToken.None);
            Assert.Equal(PlanSources.Claude, second.Plan.Source);
            Assert.Equal(PlanFormat.Serialize(first.Plan), PlanFormat.Serialize(second.Plan));

            var other = Request(3, 12);
            var miss = await new ClaudePlanner(replay, new CostMeter(new SpendDto(), 0)).PlanFloorAsync(other, CancellationToken.None);
            Assert.Equal(PlanSources.Offline, miss.Plan.Source);
        }
        finally { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
    }
}

public class SchemaUnionTests
{
    private static int Unions(string json) => System.Text.RegularExpressions.Regex.Matches(json, "\"type\":\\[").Count + System.Text.RegularExpressions.Regex.Matches(json, "\"anyOf\"").Count;

    [Xunit.Fact]
    public void Schemas_StayUnderTheApiLimitOfSixteenNullableParameters()
    {
        Xunit.Assert.True(Unions(DungeonCrawler.Adventure.Ai.PlanSchemas.For<DungeonCrawler.Adventure.FloorPlan>()) <= 16, "floor schema");
        Xunit.Assert.True(Unions(DungeonCrawler.Adventure.Ai.PlanSchemas.For<DungeonCrawler.Adventure.Ai.OutlineWithFirstFloor>()) <= 16, "outline schema");
        Xunit.Assert.True(Unions(DungeonCrawler.Adventure.Ai.PlanSchemas.For<DungeonCrawler.Adventure.Ai.BarkBank>()) <= 16, "barks schema");
    }

    [Xunit.Fact]
    public void Sentinels_BecomeNullAgain()
    {
        var plan = new DungeonCrawler.Adventure.FloorPlan();
        plan.Collectives.Add(new DungeonCrawler.Adventure.CollectivePlan { FactionId = "", Goal = new DungeonCrawler.Adventure.GoalPlan { RallyRoom = -1, AlarmTarget = "", Objective = new DungeonCrawler.Adventure.ObjectivePlan { Ref = "" } } });
        plan.Boss = new DungeonCrawler.Adventure.BossPlan();
        var npc = new DungeonCrawler.Adventure.NpcPlan { Id = "n", CastId = "", QuestId = "", RequiresFlag = " ", Betrayal = new DungeonCrawler.Adventure.BetrayalPlan { EnemyId = "" } };
        npc.Script.Nodes.Add(new DungeonCrawler.Adventure.DialogueNode { Id = "a", Choices = { new DungeonCrawler.Adventure.ChoicePlan { Next = "", Flag = "" } } });
        plan.Npcs.Add(npc);
        plan.Quests.Add(new DungeonCrawler.Adventure.QuestPlan { Id = "q", Params = new DungeonCrawler.Adventure.QuestParams { ItemRoom = -1, Waves = -1, ItemName = "", ClueName = "seal" }, Reward = new DungeonCrawler.Adventure.QuestReward { Table = "" } });
        plan.Narration.Add(new DungeonCrawler.Adventure.NarrationPlan { Room = -1 });
        var campaign = new DungeonCrawler.Adventure.CampaignPlan();
        campaign.Floors.Add(new DungeonCrawler.Adventure.FloorOutline { Theme = "", SpecOverrides = new DungeonCrawler.Adventure.SpecOverrides { EnemyCount = -1, ChestCount = 3, HighRoomChance = -1f } });
        DungeonCrawler.Adventure.Ai.SchemaSentinels.Normalize(new DungeonCrawler.Adventure.Ai.OutlineWithFirstFloor { Campaign = campaign, Floor1 = plan });
        var c = plan.Collectives[0];
        Xunit.Assert.Null(c.FactionId); Xunit.Assert.Null(c.Goal.RallyRoom); Xunit.Assert.Null(c.Goal.AlarmTarget); Xunit.Assert.Null(c.Goal.Objective);
        Xunit.Assert.Null(plan.Boss);
        Xunit.Assert.Null(npc.CastId); Xunit.Assert.Null(npc.QuestId); Xunit.Assert.Null(npc.RequiresFlag); Xunit.Assert.Null(npc.Betrayal);
        Xunit.Assert.Null(npc.Script.Nodes[0].Choices[0].Next); Xunit.Assert.Null(npc.Script.Nodes[0].Choices[0].Flag);
        var q = plan.Quests[0];
        Xunit.Assert.Null(q.Params.ItemRoom); Xunit.Assert.Null(q.Params.Waves); Xunit.Assert.Null(q.Params.ItemName); Xunit.Assert.Equal("seal", q.Params.ClueName); Xunit.Assert.Null(q.Reward.Table);
        Xunit.Assert.Null(plan.Narration[0].Room);
        var o = campaign.Floors[0];
        Xunit.Assert.Null(o.Theme); Xunit.Assert.Null(o.SpecOverrides.EnemyCount); Xunit.Assert.Equal(3, o.SpecOverrides.ChestCount); Xunit.Assert.Null(o.SpecOverrides.HighRoomChance);
    }
}

public class LenientParseTests
{
    [Xunit.Fact]
    public void UnknownEnum_FallsBackInsteadOfFailing()
    {
        var text = "{\"Title\":\"x\",\"Quests\":[{\"Id\":\"q\",\"Template\":\"Investigation\",\"Lines\":{}}],\"Npcs\":[{\"Id\":\"n\",\"Script\":{\"Nodes\":[{\"Id\":\"a\",\"Choices\":[{\"Text\":\"t\",\"Effect\":\"chooses\"}]}]}}]}";
        var r = DungeonCrawler.Adventure.Ai.ResponseParser.Parse<DungeonCrawler.Adventure.FloorPlan>(new DungeonCrawler.Adventure.Ai.ClaudeResponse(text, "end_turn", DungeonCrawler.Adventure.Ai.ClaudeUsage.Zero));
        Xunit.Assert.True(r.Ok, r.Error);
        Xunit.Assert.Equal(DungeonCrawler.Adventure.QuestTemplate.KillTarget, r.Value!.Quests[0].Template);
        Xunit.Assert.Equal(DungeonCrawler.Adventure.DialogueEffect.None, r.Value.Npcs[0].Script.Nodes[0].Choices[0].Effect);
        var ok = DungeonCrawler.Adventure.Ai.ResponseParser.Parse<DungeonCrawler.Adventure.FloorPlan>(new DungeonCrawler.Adventure.Ai.ClaudeResponse("```json\n{\"Quests\":[{\"Id\":\"q\",\"Template\":\"decide\"}]}\n```", "end_turn", DungeonCrawler.Adventure.Ai.ClaudeUsage.Zero));
        Xunit.Assert.Equal(DungeonCrawler.Adventure.QuestTemplate.Decide, ok.Value!.Quests[0].Template);
    }
}
