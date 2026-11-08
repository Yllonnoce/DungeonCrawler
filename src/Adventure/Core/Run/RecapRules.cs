namespace DungeonCrawler.Adventure;

/// <summary>
/// The recap's role lines (the co-op scoreboard): roles in a fixed order, each to the best player who holds none yet,
/// a role nobody qualifies for skipped, stopping once every player has one. Praise, never a damage rank. Engine-free;
/// co-op's plan reads it as <c>RecapRules.RoleLines(floors, players)</c>.
/// </summary>
public static class RecapRules
{
    public const string MostRescues = "Most rescues", Vanguard = "Vanguard", Steadiest = "Steadiest", LongestWatch = "Longest watch";

    public static IReadOnlyList<(string Role, string Player)> RoleLines(IReadOnlyList<FloorRecord> floors, IReadOnlyList<string> players)
    {
        var lines = new List<(string Role, string Player)>();
        if (players.Count == 0 || floors.Count == 0) return lines;
        var taken = new HashSet<string>();
        var order = players.ToList();
        int Sum(Func<FloorRecord, Dictionary<string, int>> pick, string who) => floors.Sum(f => pick(f).GetValueOrDefault(who));
        float SumF(Func<FloorRecord, Dictionary<string, float>> pick, string who) => floors.Sum(f => pick(f).GetValueOrDefault(who));
        var free = () => players.Where(p => !taken.Contains(p)).ToList();
        void Award(string role, string? who) { if (who == null) return; taken.Add(who); lines.Add((role, who)); }

        var rescuers = free().Where(p => Sum(f => f.RescuesBy, p) > 0).OrderByDescending(p => Sum(f => f.RescuesBy, p)).ThenBy(p => order.IndexOf(p)).ToList();
        Award(MostRescues, rescuers.FirstOrDefault());
        if (taken.Count >= players.Count) return lines;
        var killers = free().Where(p => Sum(f => f.KillsBy, p) > 0).OrderByDescending(p => Sum(f => f.KillsBy, p)).ThenBy(p => order.IndexOf(p)).ToList();
        Award(Vanguard, killers.FirstOrDefault());
        if (taken.Count >= players.Count) return lines;
        var steady = free().OrderBy(p => Sum(f => f.DownsBy, p)).ThenBy(p => order.IndexOf(p)).ToList();
        Award(Steadiest, steady.FirstOrDefault());
        if (taken.Count >= players.Count) return lines;
        var watch = free().Where(p => SumF(f => f.PossessSeconds, p) > 0f).OrderByDescending(p => SumF(f => f.PossessSeconds, p)).ThenBy(p => order.IndexOf(p)).ToList();
        Award(LongestWatch, watch.FirstOrDefault());
        return lines;
    }
}
