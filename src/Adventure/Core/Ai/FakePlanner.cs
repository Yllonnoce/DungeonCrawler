namespace DungeonCrawler.Adventure.Ai;

/// <summary>
/// Stands in for the Claude planner without a network: the offline designer's plans, delivered after a delay and
/// stamped with a distinct source so the asynchronous flow (loading screen, prefetch, wait and fallback) can be tested.
/// </summary>
public sealed class FakePlanner : IPlanner
{
    public const string Source = "fake";
    private readonly OfflinePlanner _inner = new();
    public float DelaySeconds { get; set; }

    public FakePlanner(float delaySeconds) => DelaySeconds = delaySeconds;

    private async Task Wait(CancellationToken ct)
    {
        if (DelaySeconds > 0) await Task.Delay(TimeSpan.FromSeconds(DelaySeconds), ct);
    }

    public async Task<OutlineResult> OutlineAsync(OutlineRequest req, CancellationToken ct)
    {
        await Wait(ct);
        var r = await _inner.OutlineAsync(req, ct);
        r.Campaign.Source = Source;
        r.Campaign.Title = "The Pretend Descent";
        r.Campaign.Synopsis = "A campaign written by a stand-in planner, for testing the flow without a key.";
        r.Floor1.Source = Source;
        return r;
    }

    public async Task<FloorResult> PlanFloorAsync(FloorRequest req, CancellationToken ct)
    {
        await Wait(ct);
        var r = await _inner.PlanFloorAsync(req, ct);
        r.Plan.Source = Source;
        return r;
    }

    public async Task<List<BarkSet>> BarksAsync(CampaignPlan campaign, IReadOnlyList<PartyMemberInfo> party, CancellationToken ct)
    {
        await Wait(ct);
        return await _inner.BarksAsync(campaign, party, ct);
    }
}
