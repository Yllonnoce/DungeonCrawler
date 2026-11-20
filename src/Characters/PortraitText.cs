namespace DungeonCrawler.Characters;

/// <summary>
/// The HUD portrait's status line and attack button text, and the keys that tell when either has to be built again. The
/// portrait reads them every frame; building the strings only when a key changes keeps it from allocating ~7 KB a frame,
/// and every change in the text changes its key, so it shows exactly what it would rebuilding every frame. Engine-free.
/// </summary>
public static class PortraitText
{
    /// <summary>The statuses with their seconds left (rounded), then unspent stat and skill points.</summary>
    public static string Statuses(CharacterState m)
    {
        var statuses = string.Join("  ", m.Effects.Active.Select(e => e.ToString()));
        if (m.StatPoints > 0) statuses = (statuses.Length > 0 ? statuses + "  " : "") + $"+{m.StatPoints} pts (C)";
        if (m.SkillPoints > 0) statuses = (statuses.Length > 0 ? statuses + "  " : "") + $"+{m.SkillPoints} skill (K)";
        return statuses;
    }

    /// <summary>
    /// Whether <see cref="Statuses"/> may read differently from when <paramref name="key"/> was last written (true the first
    /// time); the key is brought up to date. An effect's seconds show rounded, which changes only as its time left crosses a
    /// half second: the key holds the half seconds, so it changes at least as often as the text. No allocation once warm.
    /// </summary>
    public static bool StatusesChanged(CharacterState m, List<int> key)
    {
        var effects = m.Effects.Active;
        var length = 3 + effects.Count * 3;
        var changed = key.Count != length;
        if (changed) { key.Clear(); for (var i = 0; i < length; i++) key.Add(int.MinValue); }
        changed |= Set(key, 0, m.StatPoints);
        changed |= Set(key, 1, m.SkillPoints);
        changed |= Set(key, 2, effects.Count);
        for (var i = 0; i < effects.Count; i++)
        {
            var e = effects[i];
            changed |= Set(key, 3 + i * 3, (int)e.Kind);
            changed |= Set(key, 4 + i * 3, e.Stacks);
            changed |= Set(key, 5 + i * 3, (int)MathF.Floor(e.Remaining * 2f));
        }
        return changed;
    }

    private static bool Set(List<int> key, int i, int value)
    {
        if (key[i] == value) return false;
        key[i] = value;
        return true;
    }

    /// <summary>The attack button: down (fallen into a crevasse), whose to take control of, stunned, the cooldown in tenths, the swing's recovery; the arrows left when given.</summary>
    public static string Attack(bool alive, bool focused, int index, bool stunned, string ability, float cooldown, float recovery, int? arrows, bool fallen = false)
    {
        var text = !alive ? (fallen ? "Fallen" : "Down")
            : !focused ? $"Control ({index + 1})"
            : stunned ? "Stunned"
            : cooldown > 0 ? $"{ability} ({cooldown:0.0}s)"
            : recovery > 0 ? $"{ability} ..."
            : ability;
        return arrows is { } n ? text + $"  [{n}]" : text;
    }

    /// <summary>A cooldown's key for <see cref="Attack"/>: its twentieths of a second (the text shows tenths, rounded), −1 when ready.</summary>
    public static int CooldownKey(float cooldown) => cooldown > 0 ? (int)MathF.Floor(cooldown * 20f) : -1;
}
