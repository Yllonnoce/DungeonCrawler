namespace DungeonCrawler.Dungeon.Generation;

/// <summary>A secret alcove that opened into a carved room (fortune): its doorway is the old alcove cell; a chest, a plaque and, once a tier, a merchant's stall inside.</summary>
public sealed record SecretRoom(int SecretIndex, Room Room, GridPos Doorway, GridPos Chest, GridPos Plaque, GridPos? Merchant);
