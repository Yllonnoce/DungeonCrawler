namespace DungeonCrawler.Dungeon.Generation;

/// <summary>What waits at the end of a mine drift (fortune): a breakable crate, a locked chest, or something that was waiting.</summary>
public enum CacheKind { Crate, LockedChest, Ambusher }

public sealed record DriftCache(GridPos Cell, CacheKind Kind);
