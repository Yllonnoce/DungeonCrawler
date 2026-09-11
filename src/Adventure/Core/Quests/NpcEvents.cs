namespace DungeonCrawler.Adventure;

public enum NpcEventKind { Died, ReachedStairs, StartedFollowing, Downed, Revived, Captured, Jailed, Freed }

public static class QuestKeys
{
    /// <summary>Quest ids repeat across floors ("quest1"), so live quests are keyed by floor as well.</summary>
    public static string For(int floor, string planQuestId) => $"{floor}.{planQuestId}";
    /// <summary>A quest item's key: the quest key plus which of its items this is (Investigate places several).</summary>
    public static string Item(int floor, string planQuestId, int ordinal) => $"{For(floor, planQuestId)}#{ordinal}";
    /// <summary>The quest key an item key belongs to.</summary>
    public static string QuestOf(string itemKey) { var i = itemKey.IndexOf('#'); return i < 0 ? itemKey : itemKey[..i]; }
}
