using DungeonCrawler.Dungeon.Generation;

namespace DungeonCrawler.Adventure.Objectives;

/// <summary>A villager on a Jailbreak floor: who they are, where they start, whether they start behind bars.</summary>
public sealed record PrisonerContents(string Id, string Name, GridPos Cell, bool Jailed);

/// <summary>The ritualist on a Ritual floor.</summary>
public sealed record RitualistContents(string Id, string Name, GridPos Cell);

/// <summary>A clue plaque: where it stands and what it says.</summary>
public sealed record PlaqueContents(int Index, GridPos Cell, string Text);

/// <summary>
/// Everything the builder needs for a floor's objective on top of the layout: names, prisoners, the ritualist, plaque
/// texts. Derived deterministically from the layout and the floor seed by <see cref="ObjectivePlanner"/>, never from
/// a plan, so every peer and every kind of campaign gets the same. Engine-free and never saved.
/// </summary>
public sealed class ObjectiveContents
{
    public FloorObjectiveKind Kind { get; init; }
    public List<PrisonerContents> Prisoners { get; } = new();
    public RitualistContents? Ritualist { get; set; }
    public List<PlaqueContents> Plaques { get; } = new();
}
