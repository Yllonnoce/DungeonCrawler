using System.Text.Json;

namespace DungeonCrawler.Save;

/// <summary>
/// A wipe on a railed floor rises at the last stop the party reached, with the train as it was when it arrived there (ruling 14):
/// the wipe's own capture of the floor, with the train taken from the arrival's snapshot. Engine-free.
/// </summary>
public static class RailArrival
{
    /// <summary>
    /// A new floor state (a copy of <paramref name="live"/>; neither input changes) whose train is <paramref name="arrival"/>'s: the
    /// stop (<see cref="FloorStateDto.RailStop"/>), nobody seated (<see cref="FloorStateDto.RailRiders"/> null), and the train's people:
    /// the crew (<paramref name="crewIds"/>) as they were at the arrival, and each escort (<paramref name="escortIds"/>: Grisel) as she was
    /// then too, unless the wipe found her dead or fled: a fall never kills an escort (she is caught at the lip), so that came from a fight
    /// or her betrayal, whose quest state lives in the run and stands. An entry the live capture lacks is added. Only on a floor that runs
    /// the crew trial (<paramref name="crewTrial"/>) does the objective come from the arrival too: its open cages, its lost wagons, its
    /// home, lost and freed crew, and its completion (the crew come back with the train, so a win since the arrival goes too). Everything
    /// else is the wipe's: the dead stay dead, the spares, the gates and the orc's keys stand as they were, and another objective's
    /// progress (a ritual, the gas, a walk-in jailbreak) is untouched.
    /// </summary>
    public static FloorStateDto Merge(FloorStateDto live, FloorStateDto arrival, IReadOnlySet<string> crewIds, IReadOnlySet<string> escortIds, bool crewTrial)
    {
        var merged = Copy(live);
        var from = Copy(arrival);
        merged.RailStop = from.RailStop;
        merged.RailRiders = null;
        foreach (var npc in from.NpcStates)
        {
            var crew = crewIds.Contains(npc.Id);
            if (!crew && !escortIds.Contains(npc.Id)) continue;
            var at = merged.NpcStates.FindIndex(n => n.Id == npc.Id);
            if (at < 0) { merged.NpcStates.Add(npc); continue; }
            if (!crew && merged.NpcStates[at] is { Dead: true } or { Fled: true }) continue; // the escort died or turned since: that stands
            merged.NpcStates[at] = npc;
        }
        if (crewTrial && merged.Objective is { } o && from.Objective is { } a)
        {
            o.OpenCages = a.OpenCages;
            o.HomeCrew = a.HomeCrew;
            o.LostCrew = a.LostCrew;
            o.FreedNpcs = a.FreedNpcs;
            o.LostWagons = a.LostWagons;
            o.Completed = a.Completed;
        }
        return merged;
    }

    private static FloorStateDto Copy(FloorStateDto dto)
        => JsonSerializer.Deserialize<FloorStateDto>(JsonSerializer.Serialize(dto, SaveFormat.Options), SaveFormat.Options)!;
}
