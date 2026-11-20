using DungeonCrawler.Adventure;

namespace DungeonCrawler.Dungeon.Rails;

/// <summary>
/// What a railed floor loads ahead of its rides, so nothing is read from disk as the cart reaches it: the creatures its
/// encounters spawn (their models are read on a thread when the floor is built, <c>EnemyModels</c>) and the recorded lines a
/// ride's encounters speak (read on a thread when the ride arms). Engine-free.
/// </summary>
public static class RailWarmup
{
    /// <summary>Every enemy id the encounters spawn (on ride <paramref name="ride"/> only, when given), each once, in plan order.</summary>
    public static IReadOnlyList<string> EnemyIds(IEnumerable<RailEncounterPlan> encounters, int? ride = null)
    {
        var ids = new List<string>();
        foreach (var enc in encounters)
        {
            if (ride is { } r && enc.Ride != r) continue;
            foreach (var member in enc.Riders)
                if (!string.IsNullOrWhiteSpace(member.EnemyId) && !ids.Contains(member.EnemyId)) ids.Add(member.EnemyId);
        }
        return ids;
    }

    /// <summary>The voice ids of ride <paramref name="ride"/>'s encounter lines that have one, each once, in plan order.</summary>
    public static IReadOnlyList<string> VoiceIds(IEnumerable<RailEncounterPlan> encounters, int ride)
    {
        var ids = new List<string>();
        foreach (var enc in encounters)
            if (enc.Ride == ride && enc.Line?.VoiceId is { Length: > 0 } id && !ids.Contains(id)) ids.Add(id);
        return ids;
    }
}
