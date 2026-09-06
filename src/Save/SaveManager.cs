using Godot;

namespace DungeonCrawler.Save;

public sealed record SlotInfo(string Slot, bool Exists, string Label, int Floor, string SavedAt, double PlaySeconds, List<string> Members, bool Corrupt)
{
    public string Summary => !Exists ? "Empty" : Corrupt ? "Unreadable save" : $"Floor {Floor}   {string.Join(", ", Members)}   {FormatTime(PlaySeconds)}";
    public static string FormatTime(double s) => s >= 3600 ? $"{(int)(s / 3600)}h {(int)(s % 3600 / 60)}m" : $"{(int)(s / 60)}m {(int)(s % 60)}s";
}

/// <summary>Reads and writes save files under user://saves. Writes are atomic (temp + rename) and keep one .bak.</summary>
public static class SaveManager
{
    public const string QuickSlot = "quick";
    public static readonly string[] ManualSlots = { "slot1", "slot2", "slot3" };
    public static IEnumerable<string> AllSlots => new[] { QuickSlot }.Concat(ManualSlots);

    private static string Dir => ProjectSettings.GlobalizePath("user://saves");
    public static string PathFor(string slot) => System.IO.Path.Combine(Dir, slot + ".json");

    public static void Write(string slot, SaveData data)
    {
        System.IO.Directory.CreateDirectory(Dir);
        var path = PathFor(slot);
        var temp = path + ".tmp";
        System.IO.File.WriteAllText(temp, SaveFormat.Serialize(data));
        if (System.IO.File.Exists(path)) System.IO.File.Copy(path, path + ".bak", overwrite: true);
        System.IO.File.Move(temp, path, overwrite: true);
        GD.Print($"[Save] wrote {path}");
    }

    public static SaveData? Read(string slot)
    {
        var path = PathFor(slot);
        if (!System.IO.File.Exists(path)) return null;
        try
        {
            return SaveFormat.Deserialize(System.IO.File.ReadAllText(path));
        }
        catch (Exception e)
        {
            GD.PushError($"[Save] failed to read {path}: {e.Message}");
            var bak = path + ".bak";
            if (!System.IO.File.Exists(bak)) return null;
            try { return SaveFormat.Deserialize(System.IO.File.ReadAllText(bak)); }
            catch (Exception e2) { GD.PushError($"[Save] backup also unreadable: {e2.Message}"); return null; }
        }
    }

    public static bool Exists(string slot) => System.IO.File.Exists(PathFor(slot));

    public static SlotInfo Info(string slot)
    {
        var path = PathFor(slot);
        if (!System.IO.File.Exists(path)) return new SlotInfo(slot, false, "", 0, "", 0, new List<string>(), false);
        var header = SaveFormat.ReadHeader(System.IO.File.ReadAllText(path));
        if (header == null) return new SlotInfo(slot, true, "", 0, "", 0, new List<string>(), true);
        var h = header.Value;
        return new SlotInfo(slot, true, h.label, h.floor, h.savedAt, h.playSeconds, h.members, false);
    }

    public static string DisplayName(string slot) => slot == QuickSlot ? "Quicksave" : slot.StartsWith("slot") ? $"Slot {slot[4..]}" : slot;
}
