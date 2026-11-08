namespace DungeonCrawler.Adventure.Run;

/// <summary>A run's score: depth × 100 × (1 + 0.1 × heat) + 25 × elites + 10 × secrets − 50 × wipes, never below 0. Engine-free.</summary>
public static class ScoreRules
{
    public const int DepthPoints = 100, ElitePoints = 25, SecretPoints = 10, WipePenalty = 50;

    public static int Score(int deepestFloor, int heat, int elitesSlain, int secretsFound, int wipes)
        => Math.Max(0, (int)Math.Round(deepestFloor * DepthPoints * (1 + ClauseRules.HeatStep * (double)heat)) + elitesSlain * ElitePoints + secretsFound * SecretPoints - wipes * WipePenalty);
}
