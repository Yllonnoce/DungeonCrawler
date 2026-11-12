namespace DungeonCrawler.Adventure.Voice;

/// <summary>
/// Which of a floor's narration entries are due now: the ones for this trigger (and room), whose flags allow them,
/// that have not been said yet this run. The said set rides in the save, so a reload or a second visit stays quiet,
/// while an entry held back by a flag stays unsaid and can still play on a later visit. Engine-free.
/// </summary>
public static class NarrationRules
{
    private const string Prefix = "narr:";

    /// <summary>The key an entry is remembered under: floor, trigger, room and the text, so it survives a save and never collides between floors.</summary>
    public static string Key(int floor, NarrationPlan n)
        => $"{Prefix}{floor}:{n.Trigger}:{n.Room?.ToString() ?? "-"}:{n.Line.Text.Trim().ToLowerInvariant()}";

    /// <summary>The entries to play for a trigger (for <see cref="NarrationTrigger.RoomEnter"/>, that room), in plan order.</summary>
    public static IReadOnlyList<NarrationPlan> Due(FloorPlan plan, NarrationTrigger trigger, int? room, IReadOnlyCollection<string> flags, IReadOnlySet<string> said, int floor)
        => plan.Narration
            .Where(n => n.Trigger == trigger
                && (trigger != NarrationTrigger.RoomEnter || (room != null && n.Room == room))
                && n.Line.Text.Trim().Length > 0
                && FlagRules.Allowed(n.RequiresFlag, n.ForbidsFlag, flags)
                && !said.Contains(Key(floor, n)))
            .ToList();

    /// <summary>Whether a boss is the campaign's antagonist (the same name): then it speaks in the villain's voice, not the stock boss's.</summary>
    public static bool BossIsAntagonist(string bossName, string antagonistName)
        => bossName.Trim().Length > 0 && string.Equals(bossName.Trim(), antagonistName.Trim(), StringComparison.OrdinalIgnoreCase);
}
