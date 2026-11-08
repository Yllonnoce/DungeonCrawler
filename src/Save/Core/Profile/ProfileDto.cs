namespace DungeonCrawler.Save.Profile;

/// <summary>user://profile.json: records, the last twenty runs, the deeds and what they unlocked, the dailies. Engine-free.</summary>
public sealed class ProfileDto
{
    public const int CurrentVersion = 1;
    public int Version { get; set; } = CurrentVersion;
    public RecordsDto Records { get; set; } = new();
    /// <summary>The last twenty runs, newest first, upserted by RunId.</summary>
    public List<RunSummaryDto> Runs { get; set; } = new();
    /// <summary>Deed id to the UTC time it was earned.</summary>
    public Dictionary<string, string> Deeds { get; set; } = new();
    /// <summary>All-time counters the deeds read across runs (elites_total, rescues_total, gerald_total, dailies_total).</summary>
    public Dictionary<string, int> Counters { get; set; } = new();
    /// <summary>Daily results by date key (yyyy-MM-dd).</summary>
    public Dictionary<string, DailyResultDto> Daily { get; set; } = new();
    /// <summary>What the deeds unlocked: "clauserank:key:rank", "legendary:id", "banter:set", "merchantline:id".</summary>
    public List<string> Unlocks { get; set; } = new();
}

public sealed class RecordsDto
{
    public int BestDepth { get; set; }
    public double? FastestStorySeconds { get; set; }
    /// <summary>The first clear at each heat per mode, keyed "{run}:{heat}".</summary>
    public Dictionary<string, HeatClearDto> HeatClears { get; set; } = new();
    public int ElitesSlain { get; set; }
    /// <summary>Distinct runs ever recorded.</summary>
    public int Runs { get; set; }
    public int Victories { get; set; }
}

public sealed class HeatClearDto
{
    public double Seconds { get; set; }
    public string Utc { get; set; } = "";
}

/// <summary>One run as the profile remembers it; <c>Main.RunSummaryOf()</c> builds it from the adventure and the ledger.</summary>
public sealed class RunSummaryDto
{
    public string RunId { get; set; } = "";
    public string Run { get; set; } = "Story";
    public string StartedUtc { get; set; } = "";
    public string UpdatedUtc { get; set; } = "";
    public ulong Seed { get; set; }
    public int DeepestFloor { get; set; }
    public int Heat { get; set; }
    public int ElitesSlain { get; set; }
    public int SecretsFound { get; set; }
    public int Wipes { get; set; }
    public int Score { get; set; }
    public double Seconds { get; set; }
    public bool Victory { get; set; }
    public bool Fallen { get; set; }
    public string? Epitaph { get; set; }
    public List<string> Clauses { get; set; } = new();
}

public sealed class DailyResultDto
{
    public int Depth { get; set; }
    public int Score { get; set; }
    public double Seconds { get; set; }
    public bool Victory { get; set; }
    public bool Fallen { get; set; }
}
