using DungeonCrawler.Dungeon.Generation;

namespace DungeonCrawler.Adventure.Fortune;

/// <summary>Which chest is which: a vault's prize, a secret alcove's or room's, a ledge's, a cellar's, or an ordinary one on the floor.</summary>
public enum ChestKind { Ordinary, Vault, Secret, Ledge, Cellar }

/// <summary>
/// Treasure that differs: every chest kind rolls its own table. A ledge chest is a hard-to-reach chest like a cellar's and
/// rolls <see cref="CellarTable"/> (the spec names no ledge table). The boss rolls <see cref="BossTable"/> two item levels up.
/// </summary>
public static class ChestRules
{
    public const string VaultTable = "table.vault", SecretTable = "table.secret", CellarTable = "table.cellar", BossTable = "table.boss", ChestTable = "table.chest";
    public const int BossItemLevelBonus = 2;

    /// <summary>The kind of the chest at <c>layout.ChestSpawns[spawnIndex]</c>.</summary>
    public static ChestKind KindOf(FloorLayout layout, int spawnIndex)
    {
        if (spawnIndex < 0 || spawnIndex >= layout.ChestSpawns.Count) return ChestKind.Ordinary;
        var cell = layout.ChestSpawns[spawnIndex];
        if (layout.Vaults.Any(v => v.Kind != VaultKind.SafeDoor && v.Room.Center == cell)) return ChestKind.Vault;
        if (layout.Secrets.Any(s => s.Alcove == cell) || layout.SecretRooms.Any(r => r.Chest == cell)) return ChestKind.Secret;
        if (layout.Ledges.Any(l => l.Chest == cell)) return ChestKind.Ledge;
        return ChestKind.Ordinary;
    }

    public static ChestKind CellarKind => ChestKind.Cellar;

    public static string TableFor(ChestKind k) => k switch
    {
        ChestKind.Vault => VaultTable,
        ChestKind.Secret => SecretTable,
        ChestKind.Cellar or ChestKind.Ledge => CellarTable,
        _ => ChestTable,
    };

    /// <summary>The secret table always adds one consumable on top of its rolls.</summary>
    public static bool GuaranteesConsumable(string tableId) => tableId == SecretTable;
}
