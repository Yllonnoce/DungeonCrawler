using DungeonCrawler.Dungeon.Generation;
using Godot;

namespace DungeonCrawler.Enemies;

/// <summary>The arena change a boss makes at phase two (stub; the arena task fills it).</summary>
public static partial class BossArena
{
    public static void Change(Enemy boss) { }
    public static void Prepare(Enemy boss, FloorLayout layout, Node3D root, int seed) { }
}
