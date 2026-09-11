namespace DungeonCrawler.Dungeon.Generation;

/// <summary>
/// What the generator laid down for a floor's objective: the rooms and cells the runtime builds on. Filled by
/// <see cref="FloorGenerator"/> for the kind in question; the fields another kind does not use stay empty.
/// </summary>
public sealed class ObjectiveLayout
{
    public FloorObjectiveKind Kind { get; init; }

    // Jailbreak
    public Room? JailRoom { get; set; }
    /// <summary>Index into <see cref="FloorLayout.Vaults"/> of the jail's door, or -1.</summary>
    public int JailVault { get; set; } = -1;
    /// <summary>Where the prisoners start; the first <see cref="JailedAtStart"/> of them inside the jail.</summary>
    public List<GridPos> PrisonerCells { get; } = new();
    public int JailedAtStart { get; set; }
    /// <summary>Spare jail keys lying about the floor (also appended to <see cref="FloorLayout.KeySpawns"/>).</summary>
    public List<GridPos> ExtraKeyCells { get; } = new();
    /// <summary>The treasure vault that opens once every prisoner is free, or -1.</summary>
    public int RewardVault { get; set; } = -1;

    // Ritual
    public Room? ShrineRoom { get; set; }
    public GridPos? Altar { get; set; }
    /// <summary>One lever inside the shrine per breach mouth it can seal.</summary>
    public List<(GridPos Lever, GridPos Mouth)> BreachLevers { get; } = new();

    // Toxic
    /// <summary>The lever gate on the exit room, or -1 when the exit could not be gated.</summary>
    public int ExitGateVault { get; set; } = -1;

    // Shifting Vault
    /// <summary>The vault around the stairs, or -1.</summary>
    public int StairsVault { get; set; } = -1;
    public VaultPuzzle? Puzzle { get; set; }
    /// <summary>Clue plaques: where each stands and which puzzle clue it carries (-1 = the vault's own).</summary>
    public List<(GridPos Cell, int Clue)> Plaques { get; } = new();

    /// <summary>Cells the objective owns: nothing else is placed on them and paths treat them like any reserved cell.</summary>
    public HashSet<GridPos> Reserved { get; } = new();
}

/// <summary>
/// A lever-and-gate puzzle: pulling lever i flips every gate whose bit is set in <see cref="Toggles"/>[i]. The vault
/// opens when the gates stand as <see cref="Target"/> says. Built and verified by <c>VaultPuzzle.Generate</c>.
/// </summary>
public sealed record VaultPuzzle(IReadOnlyList<GridPos> Levers, IReadOnlyList<GridPos> Gates, IReadOnlyList<int> Toggles, int Initial, int Target)
{
    public int LeverCount => Levers.Count;
    public int GateCount => Gates.Count;
    /// <summary>The gate state after the levers in <paramref name="parity"/> (bit i = lever i pulled an odd number of times) from the initial one.</summary>
    public int StateFor(int parity)
    {
        var state = Initial;
        for (var i = 0; i < LeverCount; i++) if ((parity & (1 << i)) != 0) state ^= Toggles[i];
        return state;
    }
    public bool IsOpen(int state, int gate) => (state & (1 << gate)) != 0;
}
