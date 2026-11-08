using Godot;

namespace DungeonCrawler.Save.Profile;

/// <summary>user://profile.json on disk: loaded at start, written atomically (temp + .bak + rename) like the saves; an unreadable file falls back to its .bak, then to an empty profile.</summary>
public static class ProfileStore
{
    public static ProfileDto Current { get; private set; } = new();
    /// <summary>Tests point this at "profile-test.json" so a headless flow never touches the player's profile.</summary>
    public static string FileName { get; set; } = "profile.json";
    public static string Path => ProjectSettings.GlobalizePath("user://" + FileName);

    public static void Load()
    {
        Current = ReadFile(Path) ?? ReadFile(Path + ".bak") ?? new ProfileDto();
        GD.Print($"[Profile] {FileName}: {Current.Records.Runs} runs, {Current.Deeds.Count} deeds, best depth {Current.Records.BestDepth}");
    }

    private static ProfileDto? ReadFile(string path)
    {
        if (!System.IO.File.Exists(path)) return null;
        try
        {
            var p = ProfileFormat.Deserialize(System.IO.File.ReadAllText(path));
            if (p == null) GD.PushError($"[Profile] {path} is not a profile");
            return p;
        }
        catch (Exception e) { GD.PushError($"[Profile] failed to read {path}: {e.Message}"); return null; }
    }

    public static void Save()
    {
        var path = Path;
        System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
        var temp = path + ".tmp";
        System.IO.File.WriteAllText(temp, ProfileFormat.Serialize(Current));
        if (System.IO.File.Exists(path)) System.IO.File.Copy(path, path + ".bak", overwrite: true);
        System.IO.File.Move(temp, path, overwrite: true);
    }

    /// <summary>What the deeds unlocked, applied to the live game: legendaries into the catalog's pool, banter sets, the hidden stall's lines and its extra rolls.</summary>
    public static void ApplyUnlocks(Items.ItemCatalog cat)
    {
        cat.Unlocked.Clear();
        Adventure.Voice.BanterRules.UnlockedSets.Clear();
        Adventure.Voice.MerchantLineBank.UnlockedLines.Clear();
        foreach (var u in Current.Unlocks)
        {
            var sep = u.IndexOf(':');
            if (sep < 0) continue;
            var (kind, id) = (u[..sep], u[(sep + 1)..]);
            switch (kind)
            {
                case "legendary": cat.Unlocked.Add(id); break;
                case "banter": Adventure.Voice.BanterRules.UnlockedSets.Add(id); break;
                case "merchantline": Adventure.Voice.MerchantLineBank.UnlockedLines.Add(id); break;
            }
        }
        Items.ShopRules.HiddenStallExtraRolls = Adventure.Voice.MerchantLineBank.UnlockedLines.Count;
    }

    /// <summary>Forget the profile on disk and in memory (tests).</summary>
    public static void Delete()
    {
        foreach (var p in new[] { Path, Path + ".bak", Path + ".tmp" }) if (System.IO.File.Exists(p)) System.IO.File.Delete(p);
        Current = new ProfileDto();
    }
}
