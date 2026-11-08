namespace DungeonCrawler.Characters;

public static class Leveling
{
    public const int StatPointsPerLevel = 3;
    public const int SkillPointsPerLevel = 1;
    /// <summary>A campaign ends around level 18-20; the cap leaves room for a long run.</summary>
    public const int MaxLevel = 30;

    /// <summary>XP needed to go from <paramref name="level"/> to the next one: 25L² + 50L (level 18 at 52,275 total, 20 at 71,250).</summary>
    public static int XpToNext(int level) => 25 * level * level + 50 * level;

    /// <summary>Total XP from level 1 to reach <paramref name="level"/>.</summary>
    public static int TotalXpTo(int level) => Enumerable.Range(1, Math.Max(0, level - 1)).Sum(XpToNext);

    /// <summary>Add XP, applying as many level-ups as earned. Returns levels gained.</summary>
    public static int AddXp(CharacterState c, int xp)
    {
        if (!c.IsAlive || xp <= 0 || c.Level >= MaxLevel) return 0;
        c.Xp += xp;
        var gained = 0;
        while (c.Level < MaxLevel && c.Xp >= XpToNext(c.Level))
        {
            c.Xp -= XpToNext(c.Level);
            c.Level++;
            c.StatPoints += StatPointsPerLevel;
            c.SkillPoints += SkillPointsPerLevel;
            gained++;
        }
        if (gained > 0)
        {
            c.Recalculate();
            c.Heal(c.Stats[Stat.MaxHp] * 0.25f * gained);
            c.Mana = Math.Min(c.Stats[Stat.MaxMana], c.Mana + c.Stats[Stat.MaxMana] * 0.25f * gained);
        }
        return gained;
    }
}
