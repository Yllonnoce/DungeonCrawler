namespace DungeonCrawler.Dungeon.Generation;

/// <summary>
/// Which floor slabs are buckled: nearly half of a ruined room, most cells beside a rubble mound, and one in twenty elsewhere.
/// Never stairs, doors, ramps, ledges (their cells and ramp ends), water, the under layer, a vault's door, key or lever, or a camp's
/// cells and anchor, and never a rail lane cell: the track lies on a flat slab (a heave
/// would bury its rails and sleepers). A lane cell still makes both of its draws, so every other cell's pick is the one it always was.
/// Engine-free: no Godot types.
/// </summary>
public static class BuckleRules
{
    /// <summary>How many heaved slab shapes there are (ridges and domes).</summary>
    public const int Variants = 4;
    /// <summary>Mixed into the layout seed for this pick's own rng.</summary>
    public const int Salt = 0xB0C1;

    /// <summary>
    /// The buckled cells and their variants, and <c>SparedRail</c>: the lane cells whose roll came up, laid flat instead.
    /// </summary>
    public static (Dictionary<GridPos, int> Buckled, List<GridPos> SparedRail) Pick(FloorLayout layout)
    {
        var buckled = new Dictionary<GridPos, int>();
        var spared = new List<GridPos>();
        var rng = new Random(layout.Seed ^ Salt);
        for (var y = 0; y < layout.Height; y++)
            for (var x = 0; x < layout.Width; x++)
            {
                var p = new GridPos(x, y);
                if (layout[p] != Cell.Floor || layout.IsWater(p) || layout.IsUnder(p) || p == layout.UpStairs || p == layout.DownStairs) continue;
                if (layout.Ledges.Any(l => l.Cells.Contains(p) || l.RampBottom == p || l.RampTop == p)) continue;
                if (layout.Vaults.Any(v => v.Door == p || v.Key == p || v.Lever == p)) continue;
                if (layout.Structures.Any(s => s.Kind == StructureKind.Camp && (s.Cells.Contains(p) || s.Anchor == p))) continue;
                var room = layout.RoomAt(p);
                var chance = room != null && layout.StructureAt(room)?.Kind == StructureKind.Ruin ? 0.42
                    : layout.Rubble.Any(r => r.ManhattanDistance(p) == 1) ? 0.55
                    : layout.Rubble.Contains(p) ? 0.0 : 0.05;
                if (rng.NextDouble() >= chance) continue;
                var variant = rng.Next(Variants); // drawn for a lane cell too: the draws after it stay where they were
                if (!layout.IsRail(p)) buckled[p] = variant;
                else spared.Add(p);
            }
        return (buckled, spared);
    }
}
