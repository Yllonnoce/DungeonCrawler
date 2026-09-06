namespace DungeonCrawler.Characters;

public static class Leveling
{
    public const int StatPointsPerLevel = 3;
    public const int SkillPointsPerLevel = 1;
    public const int MaxLevel = 50;

    /// <summary>XP needed to go from <paramref name="level"/> to the next one.</summary>
    public static int XpToNext(int level) => 40 * level * level + 60 * level;

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
