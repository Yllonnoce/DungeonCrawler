namespace DungeonCrawler.Items;

/// <summary>How many coins chests, enemies and bosses drop. Engine-free; all randomness comes from the caller's Random.</summary>
public static class CoinRules
{
    /// <summary>Uniform in [10 + 6 x floor, 25 + 10 x floor], inclusive.</summary>
    public static int ChestCoins(int floor, Random rng) => rng.Next(10 + 6 * floor, 25 + 10 * floor + 1);

    /// <summary>Nothing three times in four, else uniform in [2 + floor, 6 + 2 x floor], inclusive.</summary>
    public static int EnemyCoins(int floor, Random rng)
    {
        if (rng.NextDouble() >= 0.25) return 0;
        return rng.Next(2 + floor, 6 + 2 * floor + 1);
    }

    public static int BossCoins(int tier) => 150 * tier;
}
