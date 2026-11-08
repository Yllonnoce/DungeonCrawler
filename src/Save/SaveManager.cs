using Godot;

namespace DungeonCrawler.Save;

/// <summary>Reads and writes save files under user://saves. Writes are atomic (temp + rename) and keep one .bak.</summary>
public static class SaveManager
{
    public const string QuickSlot = "quick";
    /// <summary>Solo autosave (run-structure): every floor change and every three minutes; the Death Clause's memorial is written here too.</summary>
    public const string AutoSlot = "auto";
    public static readonly string[] ManualSlots = { "slot1", "slot2", "slot3" };
    public static IEnumerable<string> AllSlots => new[] { AutoSlot, QuickSlot }.Concat(ManualSlots);

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
        return new SlotInfo(slot, true, h.label, h.floor, h.savedAt, h.playSeconds, h.members, false, h.fallen, h.epitaph ?? "", h.run);
    }

    public static string DisplayName(string slot) => slot == QuickSlot ? "Quicksave" : slot == AutoSlot ? "Autosave" : slot.StartsWith("slot") ? $"Slot {slot[4..]}" : slot;

    /// <summary>The Death Clause ended the run: every slot holding this game (by seed) becomes a memorial that Load shows greyed with the epitaph.</summary>
    public static int MarkFallen(ulong gameSeed, string epitaph)
    {
        var marked = 0;
        foreach (var slot in AllSlots)
        {
            if (!Exists(slot)) continue;
            var header = SaveFormat.ReadHeader(System.IO.File.ReadAllText(PathFor(slot)));
            if (header is not { } h || h.gameSeed != gameSeed || h.fallen) continue;
            if (Read(slot) is not { Adventure: { } adv } data) continue;
            adv.Fallen = true;
            adv.Epitaph = epitaph;
            Write(slot, data);
            marked++;
        }
        return marked;
    }
}
