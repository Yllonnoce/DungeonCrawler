namespace DungeonCrawler.Net;

/// <summary>Stable names for things a player can interact with, the same on every peer because floors build identically.</summary>
public static class NetRefs
{
    public const string ChestKind = "chest", DoorKind = "door", LeverKind = "lever", SecretKind = "secret", TrapKind = "trap",
        StairsKind = "stairs", NpcKind = "npc", LootKind = "loot", BarrelKind = "barrel";

    public static string Chest(int index) => $"{ChestKind}:{index}";
    public static string Door(int vault) => $"{DoorKind}:{vault}";
    public static string Lever(int vault) => $"{LeverKind}:{vault}";
    public static string Secret(int index) => $"{SecretKind}:{index}";
    public static string Trap(int index) => $"{TrapKind}:{index}";
    public static string Barrel(int index) => $"{BarrelKind}:{index}";
    public static string StairsDown => $"{StairsKind}:down";
    public static string StairsUp => $"{StairsKind}:up";
    public static string Npc(string id) => $"{NpcKind}:{id}";
    public static string Loot(Guid id) => $"{LootKind}:{id:N}";

    /// <summary>Split "kind:id"; false for anything malformed.</summary>
    public static bool TryParse(string? text, out string kind, out string id)
    {
        kind = ""; id = "";
        if (string.IsNullOrEmpty(text) || text.Length > 80) return false;
        var colon = text.IndexOf(':');
        if (colon <= 0 || colon == text.Length - 1) return false;
        kind = text[..colon];
        id = text[(colon + 1)..];
        return kind.All(char.IsLetter);
    }

    public static bool TryIndex(string id, out int index) => int.TryParse(id, out index) && index >= 0;
}
