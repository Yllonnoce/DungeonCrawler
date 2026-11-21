namespace DungeonCrawler.Party;

/// <summary>Who drives an <see cref="Adventurer"/> body. Engine-free (the live switch back's rule reads it: <c>SwitchBackRules.MayToggle</c>).</summary>
public enum ControlKind
{
    /// <summary>The player at this machine: movement from <see cref="LocalInput"/>, camera current.</summary>
    LocalHuman,
    /// <summary>Companion AI (single-player only).</summary>
    Companion,
    /// <summary>Another player's body: follows the pose the network hands it.</summary>
    Remote,
    /// <summary>Offline player: the body is out of the world until they come back.</summary>
    Parked,
}
