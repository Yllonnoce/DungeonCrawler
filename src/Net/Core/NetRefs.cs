namespace DungeonCrawler.Net;

/// <summary>Stable names for things a player can interact with, the same on every peer because floors build identically.</summary>
public static class NetRefs
{
    public const string ChestKind = "chest", DoorKind = "door", LeverKind = "lever", SecretKind = "secret", TrapKind = "trap",
        StairsKind = "stairs", NpcKind = "npc", LootKind = "loot", BarrelKind = "barrel", PlaqueKind = "plaque", HazardKind = "hazard", ShrineKind = "shrine";

    /// <summary>A railed floor's props (plan 5). A stop's are named by its stop index; a set piece's by its plan's index in <c>FloorContents.RailSetPieces</c> (the same on every peer).</summary>
    public const string GateKind = "gate", WinchKind = "winch", WagonKind = "wagon", SpareKind = "spare", SwitchKind = "switch", SpurKind = "spur",
        HeapKind = "heap", ValveKind = "valve", TrestleKind = "trestle", BufferKind = "buffer", KeyringKind = "keyring", CartKind = "cart";

    public static string Chest(int index) => $"{ChestKind}:{index}";
    public static string Door(int vault) => $"{DoorKind}:{vault}";
    public static string Lever(int vault) => $"{LeverKind}:{vault}";
    public static string Secret(int index) => $"{SecretKind}:{index}";
    public static string Trap(int index) => $"{TrapKind}:{index}";
    public static string Barrel(int index) => $"{BarrelKind}:{index}";
    public static string Plaque(int index) => $"{PlaqueKind}:{index}";
    public static string Hazard(int index) => $"{HazardKind}:{index}";
    public static string Shrine(int index) => $"{ShrineKind}:{index}";
    public static string StairsDown => $"{StairsKind}:down";
    public static string StairsUp => $"{StairsKind}:up";
    public static string Gate(int stop) => $"{GateKind}:{stop}";
    public static string Winch(int stop) => $"{WinchKind}:{stop}";
    public static string Wagon(int stop) => $"{WagonKind}:{stop}";
    public static string Spare(int stop) => $"{SpareKind}:{stop}";
    public static string Switch(int piece) => $"{SwitchKind}:{piece}";
    public static string Spur(int piece) => $"{SpurKind}:{piece}";
    public static string Heap(int piece) => $"{HeapKind}:{piece}";
    public static string Valve(int piece) => $"{ValveKind}:{piece}";
    public static string Trestle(int piece) => $"{TrestleKind}:{piece}";
    public static string Buffer(int piece) => $"{BufferKind}:{piece}";
    /// <summary>The orc's keyring at dead Grisel's belt (there is one at most).</summary>
    public static string Keyring => $"{KeyringKind}:0";
    /// <summary>The party's cart (there is one).</summary>
    public static string Cart => $"{CartKind}:0";
    /// <summary>
    /// A cart's wire name: the party's own cart (the one the rail controller rides) is <see cref="Cart"/>; a stop's spare (<paramref name="spareStop"/>
    /// its stop, −1 for none) is <see cref="Spare"/>; any other cart (a runaway wagon, a slag cart, a chaser) has none.
    /// </summary>
    public static string? CartRef(Net.CartKind kind, bool isThePartysCart, int spareStop = -1)
        => kind != Net.CartKind.Party ? null : isThePartysCart ? Cart : spareStop >= 0 ? Spare(spareStop) : null;
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
