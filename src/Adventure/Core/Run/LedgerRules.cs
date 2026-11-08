namespace DungeonCrawler.Adventure.Run;

/// <summary>The floor card's "worst moment", read off the floor's ledger: the wipe, else a hazard death, else any fall, else the hardest hit, else nothing.</summary>
public static class LedgerRules
{
    public const string NothingWorse = "Nothing worse than a stubbed toe";
    private static readonly string[] HazardCauses = { HarmCauses.Press, HarmCauses.Spikes, HarmCauses.Gas, HarmCauses.Fall };

    public static string WorstMoment(FloorRecord r)
    {
        if (r.WipeCauses.Count > 0)
        {
            var cause = r.WipeCauses[^1];
            var killer = r.Falls.LastOrDefault(f => f.Cause == cause)?.Killer ?? "";
            return $"The party fell to {HarmCauses.Describe(cause, killer)}";
        }
        if (r.Falls.FirstOrDefault(f => HazardCauses.Contains(f.Cause)) is { } hazard)
            return $"{hazard.Member} fell to {HarmCauses.Describe(hazard.Cause, hazard.Killer)}";
        if (r.Falls.Count > 0)
            return $"{r.Falls[0].Member} went down to {HarmCauses.Describe(r.Falls[0].Cause, r.Falls[0].Killer)}";
        if (r.BiggestHit > 0f)
            return $"{r.BiggestHitOn} took {r.BiggestHit:0} from {r.BiggestHitBy}";
        return NothingWorse;
    }
}
