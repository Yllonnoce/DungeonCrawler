namespace DungeonCrawler.Adventure.Ai;

/// <summary>Running spend for one campaign; stored in the save.</summary>
public sealed class SpendDto
{
    public int Calls { get; set; }
    public long InputTokens { get; set; }
    public long OutputTokens { get; set; }
    public long CacheReadTokens { get; set; }
    public long CacheWriteTokens { get; set; }
    public double Usd { get; set; }
}

/// <summary>Claude Opus 5 list prices in dollars per million tokens (verify against the current price list when changing models).</summary>
public static class Pricing
{
    public const string DefaultModel = "claude-opus-5";
    public const double InputPerMillion = 5.0;
    public const double OutputPerMillion = 25.0;
    public const double CacheReadPerMillion = 0.50;
    public const double CacheWritePerMillion = 6.25;

    public static double Cost(ClaudeUsage u)
        => (u.InputTokens * InputPerMillion + u.OutputTokens * OutputPerMillion + u.CacheReadTokens * CacheReadPerMillion + u.CacheWriteTokens * CacheWritePerMillion) / 1_000_000.0;
}

/// <summary>Tracks spend against a per-run cap and estimates a call before it is made.</summary>
public sealed class CostMeter
{
    public SpendDto Spend { get; }
    public double CapUsd { get; set; }
    public CostMeter(SpendDto spend, double capUsd) { Spend = spend; CapUsd = capUsd; }

    /// <summary>Worst-case-ish dollars for a request: all input uncached plus half the output allowance.</summary>
    public static double Estimate(ClaudeRequest r)
        => (r.ApproxInputTokens * Pricing.InputPerMillion + r.MaxTokens * 0.5 * Pricing.OutputPerMillion) / 1_000_000.0;

    public bool CanAfford(ClaudeRequest r) => CapUsd <= 0 || Spend.Usd + Estimate(r) <= CapUsd;
    public double Remaining => CapUsd <= 0 ? double.PositiveInfinity : Math.Max(0, CapUsd - Spend.Usd);

    public void Record(ClaudeUsage u)
    {
        Spend.Calls++;
        Spend.InputTokens += u.InputTokens;
        Spend.OutputTokens += u.OutputTokens;
        Spend.CacheReadTokens += u.CacheReadTokens;
        Spend.CacheWriteTokens += u.CacheWriteTokens;
        Spend.Usd += Pricing.Cost(u);
    }
}
