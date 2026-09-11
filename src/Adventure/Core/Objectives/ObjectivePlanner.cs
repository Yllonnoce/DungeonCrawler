using DungeonCrawler.Dungeon.Generation;

namespace DungeonCrawler.Adventure.Objectives;

/// <summary>Turns what the generator laid down for an objective into the people and texts the floor needs. Deterministic per layout and seed.</summary>
public static class ObjectivePlanner
{
    public static ObjectiveContents? Resolve(FloorLayout layout, int floor, int seed)
    {
        if (layout.Objective is not { } objective || objective.Kind == FloorObjectiveKind.None) return null;
        var rng = new Random(seed ^ 0x0B7EC7);
        var contents = new ObjectiveContents { Kind = objective.Kind };
        switch (objective.Kind)
        {
            case FloorObjectiveKind.Ritual when objective.ShrineRoom is { } shrine && objective.Altar is { } altar:
                contents.Ritualist = new RitualistContents("ritualist", Pick(rng, RitualistNames), AltarSide(layout, shrine, altar));
                break;
            case FloorObjectiveKind.Jailbreak:
                var names = PrisonerNames.OrderBy(_ => rng.Next()).ToList();
                for (var i = 0; i < objective.PrisonerCells.Count; i++)
                    contents.Prisoners.Add(new PrisonerContents($"prisoner{i + 1}", names[i % names.Count], objective.PrisonerCells[i], i < objective.JailedAtStart));
                break;
            default: break;
        }
        return contents;
    }

    /// <summary>Attach the objective's contents to a populated floor (both the plan resolver and the legacy populator call this).</summary>
    public static void Attach(FloorContents contents, FloorLayout layout, int floor, int seed) => contents.Objective = Resolve(layout, floor, seed);

    internal static T Pick<T>(Random rng, IReadOnlyList<T> list) => list[rng.Next(list.Count)];

    private static readonly string[] PrisonerNames = { "Mara", "Tobit", "Elsbeth", "Garrick", "Nell", "Piran", "Odile", "Wat", "Sabine", "Cuthbert", "Ysolde", "Bram" };

    private static readonly string[] RitualistNames = { "Sister Maud", "Brother Anselm", "Old Wenna", "Father Corvin", "Idris the Pale", "Mother Hesper", "Tamsin Vey", "Deacon Rolf" };

    /// <summary>A free cell beside the altar for the ritualist to stand on.</summary>
    private static GridPos AltarSide(FloorLayout layout, Room shrine, GridPos altar)
    {
        foreach (var d in new[] { new GridPos(0, 1), new GridPos(1, 0), new GridPos(0, -1), new GridPos(-1, 0) })
        {
            var q = altar + d;
            if (shrine.Contains(q) && layout[q] == Cell.Floor && !layout.IsReserved(q) && !layout.EnemySpawns.Contains(q) && !layout.ChestSpawns.Contains(q) && !layout.TrapSpawns.Contains(q)) return q;
        }
        return altar + new GridPos(0, 1);
    }
}
