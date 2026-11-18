namespace DungeonCrawler.Dungeon.Rails;

/// <summary>
/// The trackside thrower encounters that have stood once on this floor: the first time an encounter sends its thrower it is a
/// full enemy (it stays after the cart passes, for loot and XP); one sent again (a re-armed ride after it died, a wreck or a load)
/// is a reinforcement, so a ride replayed is no loot farm. The floor's save keeps it (<c>FloorStateDto.ThrowersSent</c>; null reads
/// as nothing sent). Engine-free.
/// </summary>
public sealed class ThrowerMemory
{
    private readonly HashSet<string> _sent = new();

    /// <summary>The encounter <paramref name="id"/> sends its thrower now: true when it had sent one before (this one is a reinforcement).</summary>
    public bool Send(string id) => !_sent.Add(id);

    /// <summary>What the save keeps: the encounters that have sent their thrower, sorted.</summary>
    public List<string> Capture() => _sent.OrderBy(id => id, StringComparer.Ordinal).ToList();

    /// <summary>Back from a save: exactly the saved encounters have sent theirs (null, an older save: none has).</summary>
    public void Restore(IEnumerable<string>? sent)
    {
        _sent.Clear();
        if (sent != null) foreach (var id in sent) _sent.Add(id);
    }
}
