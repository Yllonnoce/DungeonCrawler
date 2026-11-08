namespace DungeonCrawler.Save;

/// <summary>A save slot as the slot lists show it. Engine-free; <c>SaveManager.Info</c> fills it from the file's header.</summary>
public sealed record SlotInfo(string Slot, bool Exists, string Label, int Floor, string SavedAt, double PlaySeconds, List<string> Members, bool Corrupt, bool Fallen = false, string Epitaph = "", string Run = "Story")
{
    public string Summary => !Exists ? "Empty" : Corrupt ? "Unreadable save"
        : Fallen ? $"Fallen on floor {Floor} — {Epitaph}"
        : $"{(Floor == 0 ? "The Tavern" : $"Floor {Floor}")}   {string.Join(", ", Members)}   {FormatTime(PlaySeconds)}";
    public static string FormatTime(double s) => s >= 3600 ? $"{(int)(s / 3600)}h {(int)(s % 3600 / 60)}m" : $"{(int)(s / 60)}m {(int)(s % 60)}s";
}
