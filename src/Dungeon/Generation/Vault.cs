namespace DungeonCrawler.Dungeon.Generation;

public enum VaultKind { KeyDoor, LeverGate, SafeDoor }

/// <summary>A dead-end treasure room sealed by a door. KeyDoor: a key lies elsewhere on the floor. LeverGate: a lever elsewhere opens it. SafeDoor: the floor's safe room,
/// no treasure inside, its door opening for the party and never for a creature.</summary>
/// <param name="Revealed">The door shows from the start (an objective's gate), instead of hiding in the wall until someone notices it.</param>
public sealed record Vault(int Index, Room Room, GridPos Door, VaultKind Kind, GridPos? Lever, GridPos? Key, bool Revealed = false);
