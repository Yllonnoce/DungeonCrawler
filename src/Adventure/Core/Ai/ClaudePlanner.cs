using DungeonCrawler.Dungeon;
using DungeonCrawler.Dungeon.Generation;
using DungeonCrawler.Enemies;

namespace DungeonCrawler.Adventure.Ai;

public sealed record OutlineRequest(ulong GameSeed, IReadOnlyList<TierProfile> Tiers, IReadOnlyDictionary<string, EnemyProfile> Roster, IReadOnlyList<PartyMemberInfo> Party,
    FloorLayout Floor1Layout, FloorSpec Floor1Spec, int Floor1Seed, PartySnapshot Snapshot, IReadOnlyList<ThemeProfile>? Themes = null);

public sealed record OutlineResult(CampaignPlan Campaign, FloorPlan Floor1, ValidationReport Floor1Report);

public sealed record FloorRequest(CampaignPlan Campaign, FloorLayout Layout, FloorSpec Spec, TierProfile? Tier, IReadOnlyDictionary<string, EnemyProfile> Roster,
    int Floor, int Seed, PartySnapshot Snapshot, string MemorySummary, float Difficulty = 1f);

public sealed record FloorResult(FloorPlan Plan, ValidationReport Report);

/// <summary>Anything that can author a campaign and its floors. Both implementations produce the same plan format.</summary>
public interface IPlanner
{
    Task<OutlineResult> OutlineAsync(OutlineRequest req, CancellationToken ct);
    Task<FloorResult> PlanFloorAsync(FloorRequest req, CancellationToken ct);
    Task<List<BarkSet>> BarksAsync(CampaignPlan campaign, IReadOnlyList<PartyMemberInfo> party, CancellationToken ct);
}

/// <summary>The offline designer behind the planner interface; also the fallback for the Claude planner.</summary>
public sealed class OfflinePlanner : IPlanner
{
    public Task<OutlineResult> OutlineAsync(OutlineRequest req, CancellationToken ct)
    {
        var campaign = OfflineDesigner.Outline(req.GameSeed, req.Tiers, req.Themes);
        var (plan, report) = AdventurePlanning.DesignOffline(req.Floor1Layout, req.Floor1Spec, req.Tiers.Count > 0 ? req.Tiers[0] : null, req.Roster, 1, req.Floor1Seed, req.Snapshot);
        return Task.FromResult(new OutlineResult(campaign, plan, report));
    }

    public Task<FloorResult> PlanFloorAsync(FloorRequest req, CancellationToken ct)
    {
        var (plan, report) = AdventurePlanning.DesignOffline(req.Layout, req.Spec, req.Tier, req.Roster, req.Floor, req.Seed, req.Snapshot, req.Difficulty);
        return Task.FromResult(new FloorResult(plan, report));
    }

    public Task<List<BarkSet>> BarksAsync(CampaignPlan campaign, IReadOnlyList<PartyMemberInfo> party, CancellationToken ct)
        => Task.FromResult(party.Select(p => OfflineDesigner.DefaultBarks(p.Name, p.ClassName)).ToList());
}

/// <summary>
/// Plans with Claude: builds the prompt, checks the spend cap, calls, parses, fills the fields the model must not set,
/// validates, and falls back to the offline designer on any failure. Every outcome is reported through <c>Log</c>.
/// </summary>
public sealed class ClaudePlanner : IPlanner
{
    private readonly IClaudeClient _client;
    private readonly CostMeter _meter;
    private readonly PromptTemplates _templates;
    private readonly OfflinePlanner _fallback = new();
    public string Model { get; init; } = Pricing.DefaultModel;
    /// <summary>A replay with no recording for a call throws instead of falling back to the offline designer (a bake authored by hand wants to stop and ask).</summary>
    public static bool StrictReplay { get; set; }
    public Action<string> Log { get; init; } = _ => { };
    /// <summary>Set when a call was refused, failed or hit the cap; the director shows it and may stop using the AI.</summary>
    public string? LastFailure { get; private set; }

    public ClaudePlanner(IClaudeClient client, CostMeter meter, PromptTemplates? templates = null)
    {
        _client = client;
        _meter = meter;
        _templates = templates ?? PromptTemplates.Default;
    }

    public async Task<OutlineResult> OutlineAsync(OutlineRequest req, CancellationToken ct)
    {
        var tier1 = req.Tiers.Count > 0 ? req.Tiers[0] : null;
        var menu = FloorMenu.Build(req.Floor1Layout, req.Floor1Spec, tier1, req.Roster, 1, req.Snapshot);
        var request = PromptBuilder.Outline(_templates, Model, req.GameSeed, req.Tiers, req.Roster, req.Party, menu, req.Themes);
        var parsed = await CallAsync<OutlineWithFirstFloor>(request, ct);
        if (parsed == null) return await _fallback.OutlineAsync(req, ct);

        SchemaSentinels.Normalize(parsed);
        var campaign = parsed.Campaign;
        campaign.Source = PlanSources.Claude;
        campaign.GameSeed = req.GameSeed;
        campaign.SchemaVersion = PlanFormat.CurrentVersion;
        for (var f = 1; f <= TierRules.FinalFloor; f++)
            if (campaign.OutlineFor(f) == null) campaign.Floors.Add(new FloorOutline { Floor = f });
        campaign.Floors = campaign.Floors.Where(f => f.Floor is >= 1 and <= TierRules.FinalFloor).GroupBy(f => f.Floor).Select(g => g.First()).OrderBy(f => f.Floor).ToList();
        foreach (var f in campaign.Floors)
            if (f.Theme != null && (req.Themes == null || !ThemeProfile.ForTier(req.Themes, TierRules.TierIndex(f.Floor)).Any(t => t.Id == f.Theme)))
            { Log($"floor {f.Floor}: unknown theme {f.Theme} dropped"); f.Theme = null; }
        for (var t = 0; t < TierRules.TierCount; t++)
            if (campaign.TierFor(t) == null) campaign.Tiers.Add(new TierPlan { Index = t, Theme = t < req.Tiers.Count ? req.Tiers[t].DisplayName : $"Tier {t + 1}" });

        var floor1 = parsed.Floor1;
        floor1.Floor = 1;
        floor1.Source = PlanSources.Claude;
        floor1.SchemaVersion = PlanFormat.CurrentVersion;
        floor1.LayoutHash = 0;
        var report = PlanValidator.Validate(req.Floor1Layout, floor1, menu, campaign: campaign);
        if (!report.Ok)
        {
            Log($"floor 1 from Claude rejected ({report}); using the offline designer for floor 1");
            var (plan, offlineReport) = AdventurePlanning.DesignOffline(req.Floor1Layout, req.Floor1Spec, tier1, req.Roster, 1, req.Floor1Seed, req.Snapshot);
            return new OutlineResult(campaign, plan, offlineReport);
        }
        if (report.Repairs.Count > 0) Log($"floor 1 repaired: {string.Join("; ", report.Repairs)}");
        return new OutlineResult(campaign, floor1, report);
    }

    public async Task<FloorResult> PlanFloorAsync(FloorRequest req, CancellationToken ct)
    {
        var menu = FloorMenu.Build(req.Layout, req.Spec, req.Tier, req.Roster, req.Floor, req.Snapshot);
        var request = PromptBuilder.Floor(_templates, Model, req.Campaign, menu, req.MemorySummary);
        var plan = await CallAsync<FloorPlan>(request, ct);
        if (plan == null) return await _fallback.PlanFloorAsync(req, ct);
        SchemaSentinels.Normalize(plan);
        plan.Floor = req.Floor;
        plan.Source = PlanSources.Claude;
        plan.SchemaVersion = PlanFormat.CurrentVersion;
        plan.LayoutHash = 0;
        var report = PlanValidator.Validate(req.Layout, plan, menu, campaign: req.Campaign);
        if (!report.Ok)
        {
            Log($"floor {req.Floor} from Claude rejected ({report}); using the offline designer");
            LastFailure = report.ToString();
            return await _fallback.PlanFloorAsync(req, ct);
        }
        if (report.Repairs.Count > 0) Log($"floor {req.Floor} repaired: {string.Join("; ", report.Repairs)}");
        return new FloorResult(plan, report);
    }

    public async Task<List<BarkSet>> BarksAsync(CampaignPlan campaign, IReadOnlyList<PartyMemberInfo> party, CancellationToken ct)
    {
        var request = PromptBuilder.Barks(_templates, Model, campaign, party);
        var bank = await CallAsync<BarkBank>(request, ct);
        if (bank == null) return await _fallback.BarksAsync(campaign, party, ct);
        var result = new List<BarkSet>();
        foreach (var p in party)
        {
            var set = bank.Members.FirstOrDefault(m => string.Equals(m.Member, p.Name, StringComparison.OrdinalIgnoreCase)) ?? OfflineDesigner.DefaultBarks(p.Name, p.ClassName);
            set.Member = p.Name;
            foreach (var line in set.Lines) line.Line.Text = line.Line.Text.Length > 120 ? line.Line.Text[..120] : line.Line.Text;
            result.Add(set);
        }
        return result;
    }

    /// <summary>One call with the cap check, a single retry on truncation, and parsing. Null means "use the fallback".</summary>
    private async Task<T?> CallAsync<T>(ClaudeRequest request, CancellationToken ct) where T : class
    {
        if (!_meter.CanAfford(request))
        {
            LastFailure = $"spend cap reached (${_meter.Spend.Usd:0.00} of ${_meter.CapUsd:0.00})";
            Log($"{request.Kind}: {LastFailure}; using the offline designer");
            return null;
        }
        for (var attempt = 0; attempt < 2; attempt++)
        {
            ClaudeResponse response;
            try { response = await _client.CreateAsync(request, ct); }
            catch (Exception e) { response = ClaudeResponse.Failed(e.Message); }
            _meter.Record(response.Usage);
            if (StrictReplay && response.Error is { } missing && missing.StartsWith("no recording")) throw new InvalidOperationException(missing);
            var parsed = ResponseParser.Parse<T>(response);
            if (parsed.Ok)
            {
                Log($"{request.Kind}: ok in {response.Usage.InputTokens}/{response.Usage.OutputTokens} tokens (cache read {response.Usage.CacheReadTokens}), run spend ${_meter.Spend.Usd:0.000}");
                LastFailure = null;
                return parsed.Value;
            }
            LastFailure = parsed.Error;
            Log($"{request.Kind}: {parsed.Error}");
            if (response.StopReason == "max_tokens" && attempt == 0) { request = request with { MaxTokens = (int)(request.MaxTokens * 1.5) }; continue; }
            if (parsed.Error != null && parsed.Error.StartsWith("invalid JSON") && attempt == 0) { Log($"{request.Kind}: asking again"); continue; } // a slip of the pen, not a refusal
            break;
        }
        return null;
    }
}
