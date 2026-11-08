namespace DungeonCrawler.Enemies;

/// <summary>How an enemy's attack announces itself: the wind-up's length under Frost or Haste, and (Task 6) the clip, tint and decal numbers.</summary>
public static class TelegraphRules
{
    /// <summary>A wind-up or recovery scaled by the attacker's attack-speed factor; the floor keeps a frozen giant from winding up forever.</summary>
    public static float Scaled(float seconds, float attackSpeed) => seconds / Math.Max(0.2f, attackSpeed);
}
