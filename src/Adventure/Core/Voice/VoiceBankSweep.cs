namespace DungeonCrawler.Adventure.Voice;

/// <summary>Every voice line id a campaign and its floors refer to: what a voice bank must keep.</summary>
public static class VoiceRefs
{
    public static HashSet<string> Ids(CampaignPlan? campaign, IReadOnlyDictionary<int, FloorPlan>? floors)
    {
        var ids = new HashSet<string>();
        if (campaign == null) return ids;
        var plans = floors ?? new Dictionary<int, FloorPlan>();
        foreach (var c in VoiceBudget.Candidates(campaign, plans)) Add(ids, c.Line);
        foreach (var c in VoiceBudget.TauntCandidates(campaign, plans)) Add(ids, c.Line);
        foreach (var t in campaign.Taunts.Concat(plans.Values.SelectMany(p => p.Taunts))) { Add(ids, t.Line); Add(ids, t.CleanLine); }
        return ids;
    }

    private static void Add(HashSet<string> ids, Line? line) { if (line?.VoiceId is { Length: > 0 } id) ids.Add(id); }
}

/// <summary>
/// Which files of a voice bank nothing refers to any more. A bank holds NAME.wav (and, in the project, NAME.wav.import);
/// the name is the line's content id. Anything whose id is not in the referenced set is dead weight from an earlier
/// campaign or an earlier style, and a rebake or the cache sweep removes it. Engine-free; the caller deletes.
/// </summary>
public static class VoiceBankSweep
{
    public sealed record Plan(IReadOnlyList<string> Delete, int Kept, long Bytes)
    {
        public string Describe(string dir) => Delete.Count == 0 ? $"nothing to sweep in {dir} ({Kept} files, all referenced)" : $"swept {Delete.Count} old voice files ({Bytes / (1024.0 * 1024.0):0.0} MB) from {dir}, {Kept} kept";
    }

    /// <summary>The id a bank file carries, or null for a file that is not part of the bank.</summary>
    public static string? IdOf(string fileName)
    {
        var name = Path.GetFileName(fileName);
        if (name.EndsWith(".wav.import", StringComparison.OrdinalIgnoreCase)) name = name[..^".wav.import".Length];
        else if (name.EndsWith(".wav", StringComparison.OrdinalIgnoreCase)) name = name[..^".wav".Length];
        else return null;
        return name.Length == 16 && name.All(c => char.IsAsciiHexDigitLower(c) || char.IsAsciiDigit(c)) ? name : null;
    }

    public static Plan Compute(IEnumerable<(string Path, long Bytes)> files, ISet<string> referenced)
    {
        var delete = new List<string>();
        var kept = 0;
        long bytes = 0;
        foreach (var (path, size) in files)
        {
            var id = IdOf(path);
            if (id == null) continue; // not ours to touch
            if (referenced.Contains(id)) { kept++; continue; }
            delete.Add(path);
            bytes += size;
        }
        return new Plan(delete, kept, bytes);
    }
}
