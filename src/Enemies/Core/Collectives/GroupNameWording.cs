namespace DungeonCrawler.Enemies.Collectives;

/// <summary>How a group's name reads in the combat log: a name that already opens with "The" is not given a second article.</summary>
public static class GroupNameWording
{
    public static string WithArticle(string? name)
    {
        var n = (name ?? "").Trim();
        if (n.Length == 0) return "The group";
        if (n.StartsWith("The ", System.StringComparison.OrdinalIgnoreCase)) return n;
        return "The " + n;
    }
}
