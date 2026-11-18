using Godot;

namespace DungeonCrawler.Dungeon.Rails;

/// <summary>
/// An empty ore wagon running loose down the party's line: the party cart's iron tub with nobody offered a seat in it (it is
/// not an interactable). Posed and judged by the <see cref="RailEncounterController"/>, which runs it on the party track with
/// its own <see cref="CartRun"/>. Server only.
/// </summary>
public partial class RunawayWagon : MineCart
{
    /// <summary>A body mangled by a runaway wagon carries this as MangledBy (no hazard has it, so it always counts as armed).</summary>
    public const int HazardIndex = 1002;
    public const string SourceName = "a runaway wagon";

    /// <summary>When each body was last run over (<see cref="RunOverSweep.Judge"/> keeps it).</summary>
    public Dictionary<ulong, double> HitAt { get; } = new();

    public RunawayWagon()
    {
        Name = "RunawayWagon";
        CollisionLayer = 1; // solid, but not on the interact layer: nothing to board
    }
}
