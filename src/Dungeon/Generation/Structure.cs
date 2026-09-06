namespace DungeonCrawler.Dungeon.Generation;

public enum StructureKind { Gate, Camp, Ruin }

/// <summary>
/// A built feature inside a big room.
/// Gate: a wall across the room (<see cref="Cells"/> are its wall cells) with one arched opening (<see cref="Anchor"/>);
/// <see cref="Openings"/> lists every gap, including breaches corridors forced; <see cref="Facing"/> is the axis through the opening.
/// Camp: a dilapidated hut whose interior is <see cref="Cells"/>, entered through <see cref="Openings"/>[0] (the doorway cell)
/// toward <see cref="Anchor"/>, the campfire cell outside; <see cref="Facing"/> points from the doorway to the fire. The hut's
/// walls are blocked edges on the layout; <see cref="Broken"/> lists the ones rendered as collapsed.
/// Ruin: a collapsed colonnade; <see cref="Cells"/> are the column cells (standing or fallen, the builder decides which),
/// nothing else is placed on them, and <see cref="Anchor"/> is the room's middle.
/// </summary>
public sealed record Structure(StructureKind Kind, Room Room, List<GridPos> Cells, GridPos Anchor, GridPos Facing, List<GridPos> Openings, List<(GridPos A, GridPos B)> Broken)
{
    public bool IsInterior(GridPos p) => Kind == StructureKind.Camp && Cells.Contains(p);
}

public enum WaterKind : byte { None = 0, Pond = 1, Stream = 2 }

/// <summary>
/// Water sunk into a room's floor: a pond blob, or a stream channel with a <see cref="Flow"/> direction (ponds have none)
/// crossed by a plank bridge at <see cref="Bridge"/>.
/// </summary>
public sealed record WaterFeature(WaterKind Kind, Room Room, List<GridPos> Cells, GridPos Flow, GridPos? Bridge = null);

/// <summary>
/// Breaches in a room's walls that a horde pours out of: each mouth is a one-cell nook carved into the wall (walkable,
/// reserved), <see cref="Outward"/> is the direction from the mouth into the room.
/// </summary>
public sealed record Nest(Room Room, List<GridPos> Mouths, List<GridPos> Outward);
