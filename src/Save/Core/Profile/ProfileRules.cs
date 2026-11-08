namespace DungeonCrawler.Save.Profile;

/// <summary>What the profile keeps and how it is updated: the last twenty runs, the records, the first clear per heat per mode, the dailies, the cosmetics. Engine-free.</summary>
public static class ProfileRules
{
    public const int KeepRuns = 20;
    /// <summary>Heat 5 and 10 unlock a title and a border on the records page.</summary>
    public const int StandingHeat = 5, WholeContractHeat = 10;
    public const string StandingTitle = "Tenant in Good Standing", WholeContractTitle = "Whole Contract";

    /// <summary>Upsert by RunId (newest first, trimmed to twenty); the records follow: best depth, elites (summed by upsert), first clear per heat and mode, victories, the fastest story clear.</summary>
    public static void RecordRun(ProfileDto p, RunSummaryDto run)
    {
        var existing = p.Runs.FirstOrDefault(r => r.RunId == run.RunId);
        var copy = Clone(run);
        copy.UpdatedUtc = Now();
        if (existing == null)
        {
            copy.StartedUtc = string.IsNullOrEmpty(copy.StartedUtc) ? copy.UpdatedUtc : copy.StartedUtc;
            p.Records.Runs++;
        }
        else
        {
            copy.StartedUtc = existing.StartedUtc;
            p.Records.ElitesSlain -= existing.ElitesSlain;
            if (existing.Victory) p.Records.Victories--; // re-counted below if still a victory
            p.Runs.Remove(existing);
        }
        p.Runs.Insert(0, copy);
        while (p.Runs.Count > KeepRuns) p.Runs.RemoveAt(p.Runs.Count - 1);
        p.Records.ElitesSlain += copy.ElitesSlain;
        p.Records.BestDepth = Math.Max(p.Records.BestDepth, copy.DeepestFloor);
        if (copy.Victory)
        {
            p.Records.Victories++;
            p.Records.HeatClears.TryAdd($"{copy.Run}:{copy.Heat}", new HeatClearDto { Seconds = copy.Seconds, Utc = copy.UpdatedUtc });
            if (copy.Run == "Story") p.Records.FastestStorySeconds = p.Records.FastestStorySeconds is { } best ? Math.Min(best, copy.Seconds) : copy.Seconds;
        }
    }

    /// <summary>The day's result: written once, overwritten only by a deeper (or, as deep, a higher-scoring) attempt.</summary>
    public static void RecordDaily(ProfileDto p, string date, RunSummaryDto run)
    {
        var next = new DailyResultDto { Depth = run.DeepestFloor, Score = run.Score, Seconds = run.Seconds, Victory = run.Victory, Fallen = run.Fallen };
        if (p.Daily.TryGetValue(date, out var had) && (had.Depth > next.Depth || (had.Depth == next.Depth && had.Score > next.Score))) return;
        p.Daily[date] = next;
    }

    public static bool DailyDone(ProfileDto p, string date) => p.Daily.ContainsKey(date);

    /// <summary>The highest heat any mode has been cleared at; −1 when nothing was cleared.</summary>
    public static int HighestHeatClear(ProfileDto p)
    {
        var best = -1;
        foreach (var key in p.Records.HeatClears.Keys)
        {
            var sep = key.LastIndexOf(':');
            if (sep >= 0 && int.TryParse(key[(sep + 1)..], out var heat)) best = Math.Max(best, heat);
        }
        return best;
    }

    public static string? Title(ProfileDto p)
    {
        var heat = HighestHeatClear(p);
        return heat >= WholeContractHeat ? WholeContractTitle : heat >= StandingHeat ? StandingTitle : null;
    }

    /// <summary>0 plain, 1 at a heat-5 clear, 2 at a heat-10 clear.</summary>
    public static int BorderTier(ProfileDto p)
    {
        var heat = HighestHeatClear(p);
        return heat >= WholeContractHeat ? 2 : heat >= StandingHeat ? 1 : 0;
    }

    private static string Now() => DateTime.UtcNow.ToString("o");

    private static RunSummaryDto Clone(RunSummaryDto r) => new()
    {
        RunId = r.RunId, Run = r.Run, StartedUtc = r.StartedUtc, UpdatedUtc = r.UpdatedUtc, Seed = r.Seed, DeepestFloor = r.DeepestFloor, Heat = r.Heat,
        ElitesSlain = r.ElitesSlain, SecretsFound = r.SecretsFound, Wipes = r.Wipes, Score = r.Score, Seconds = r.Seconds, Victory = r.Victory, Fallen = r.Fallen,
        Epitaph = r.Epitaph, Clauses = new List<string>(r.Clauses),
    };
}
