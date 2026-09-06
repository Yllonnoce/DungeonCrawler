using System.Text;

namespace DungeonCrawler.Adventure;

/// <summary>What happened on one floor, as the planner will hear about it.</summary>
public sealed class FloorRecord
{
    public int Floor { get; set; }
    public string Title { get; set; } = "";
    public string Source { get; set; } = "";
    public float Seconds { get; set; }
    public int Kills { get; set; }
    public int SneakHits { get; set; }
    public int SpellCasts { get; set; }
    public int WeaponAttacks { get; set; }
    public int Downs { get; set; }
    public int Wipes { get; set; }
    public float DamageTaken { get; set; }
    public float DamageDealt { get; set; }
    public float BossSeconds { get; set; }
    public float ExitHpFraction { get; set; } = 1f;
    public List<string> QuestOutcomes { get; set; } = new();
    public List<string> NpcFates { get; set; } = new();
    /// <summary>Flags set on this floor by dialogue choices, and clues found.</summary>
    public List<string> Choices { get; set; } = new();
    public List<string> Repairs { get; set; } = new();
    public Dictionary<string, float> PossessSeconds { get; set; } = new();
    public bool Completed { get; set; }
}

public sealed class CampaignMemoryDto
{
    public List<FloorRecord> Floors { get; set; } = new();
    /// <summary>Every flag set so far, in order. NPCs, quests, choices and narration gate on these.</summary>
    public List<string> Flags { get; set; } = new();
    public FloorRecord For(int floor)
    {
        var r = Floors.FirstOrDefault(f => f.Floor == floor);
        if (r == null) { r = new FloorRecord { Floor = floor }; Floors.Add(r); }
        return r;
    }
}

/// <summary>Condenses the run so far into a short block for the floor prompt: style, difficulty, threads, recent floors.</summary>
public static class MemorySummary
{
    public const int RecentFloors = 3;

    public static string Build(CampaignMemoryDto memory, int upToFloor, int maxChars = 1600)
    {
        var floors = memory.Floors.Where(f => f.Floor < upToFloor && (f.Completed || f.Kills > 0 || f.Seconds > 5f)).OrderBy(f => f.Floor).ToList();
        if (floors.Count == 0) return "";
        var sb = new StringBuilder();

        var kills = floors.Sum(f => f.Kills);
        var sneak = floors.Sum(f => f.SneakHits);
        var spells = floors.Sum(f => f.SpellCasts);
        var weapons = floors.Sum(f => f.WeaponAttacks);
        var tags = new List<string>();
        if (kills > 0 && sneak >= kills * 0.4f) tags.Add("stealthy: many kills open with sneak attacks");
        if (spells > weapons * 1.2f && spells > 10) tags.Add("caster-heavy");
        else if (weapons > spells * 2f && weapons > 10) tags.Add("brawler: fights up close");
        var possess = floors.SelectMany(f => f.PossessSeconds).GroupBy(kv => kv.Key).Select(g => (Name: g.Key, Seconds: g.Sum(kv => kv.Value))).OrderByDescending(x => x.Seconds).ToList();
        if (possess.Count > 0 && possess[0].Seconds > floors.Sum(f => f.Seconds) * 0.5f) tags.Add($"the player mostly plays {possess[0].Name}");
        var avgSeconds = floors.Average(f => f.Seconds);
        if (avgSeconds > 0 && avgSeconds < 120) tags.Add("moves fast through floors");
        sb.Append("Play style: ").Append(tags.Count == 0 ? "no strong pattern yet" : string.Join("; ", tags)).Append(".\n");

        var downs = floors.Sum(f => f.Downs);
        var wipes = floors.Sum(f => f.Wipes);
        var avgExit = floors.Average(f => f.ExitHpFraction);
        var difficulty = wipes > 0 || downs > floors.Count * 1.5f ? "struggling: ease the next floor (fewer, weaker groups, a kinder quest)"
            : downs == 0 && avgExit > 0.8f ? "dominant: tighten the next floor (alarms, a defended treasury, tougher mix)"
            : "comfortable: keep the pressure steady";
        sb.Append($"Difficulty read: {difficulty} ({downs} downs, {wipes} wipes, leaves floors at {avgExit:P0} health on average).\n");

        if (memory.Flags.Count > 0) sb.Append("Flags set by the party's choices (gate later content on these): ").Append(string.Join(", ", memory.Flags)).Append(".\n");
        var chosen = floors.SelectMany(f => f.Choices).ToList();
        if (chosen.Count > 0) sb.Append("Choices and discoveries: ").Append(string.Join("; ", chosen)).Append(".\n");
        var saved = floors.SelectMany(f => f.NpcFates).Where(x => x.EndsWith("rescued")).Select(x => x[..x.LastIndexOf(' ')]).Distinct().ToList();
        var lost = floors.SelectMany(f => f.NpcFates).Where(x => x.EndsWith("died")).Select(x => x[..x.LastIndexOf(' ')]).Distinct().ToList();
        var failed = floors.SelectMany(f => f.QuestOutcomes).Where(x => x.EndsWith("failed")).ToList();
        if (saved.Count + lost.Count + failed.Count > 0)
        {
            sb.Append("Threads: ");
            var parts = new List<string>();
            if (saved.Count > 0) parts.Add($"rescued and may reappear: {string.Join(", ", saved)}");
            if (lost.Count > 0) parts.Add($"dead: {string.Join(", ", lost)}");
            if (failed.Count > 0) parts.Add($"failed: {string.Join(", ", failed.Select(x => x[..x.LastIndexOf(' ')]))}");
            sb.Append(string.Join("; ", parts)).Append(".\n");
        }

        var recent = floors.TakeLast(RecentFloors).ToList();
        var older = floors.Take(Math.Max(0, floors.Count - RecentFloors)).ToList();
        if (older.Count > 0)
            sb.Append("Earlier: ").Append(string.Join("; ", older.Select(f => $"floor {f.Floor} {Short(f)}"))).Append(".\n");
        foreach (var f in recent)
        {
            sb.Append($"Floor {f.Floor}{(f.Title != "" ? $" \"{f.Title}\"" : "")}: {f.Kills} kills, {f.Downs} downs, {Math.Round(f.Seconds / 60f)} min");
            if (f.Wipes > 0) sb.Append(f.Wipes == 1 ? ", the party wiped once" : $", the party wiped {f.Wipes} times");
            if (f.BossSeconds > 0) sb.Append($", boss down after {Math.Round(f.BossSeconds)} s");
            foreach (var q in f.QuestOutcomes) sb.Append(", quest ").Append(q);
            foreach (var n in f.NpcFates) sb.Append(", ").Append(n);
            if (f.Repairs.Count > 0) sb.Append($", the game had to fix your plan ({string.Join("; ", f.Repairs.Take(3))})");
            sb.Append(".\n");
        }
        var text = sb.ToString().TrimEnd();
        return text.Length <= maxChars ? text : text[..maxChars];
    }

    private static string Short(FloorRecord f)
    {
        var bits = new List<string> { $"{f.Kills} kills" };
        if (f.Downs > 0) bits.Add($"{f.Downs} downs");
        if (f.Wipes > 0) bits.Add("wiped");
        bits.AddRange(f.QuestOutcomes.Select(q => "quest " + q));
        return string.Join(", ", bits);
    }
}
