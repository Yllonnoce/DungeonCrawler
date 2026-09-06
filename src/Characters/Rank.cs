using DungeonCrawler.Combat;

namespace DungeonCrawler.Characters;

public enum Rank { Front, Back }

public static class RankRules
{
    /// <summary>Since characters have their own bodies, rank only decides the formation slot; anyone may melee.</summary>
    public static bool CanMelee(Rank rank, WeaponKind weapon) => true;

    public static Rank Other(Rank r) => r == Rank.Front ? Rank.Back : Rank.Front;
}
