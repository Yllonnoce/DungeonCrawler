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
    /// stop (<see cref="FloorStateDto.RailStop"/>), nobody seated (<see cref="FloorStateDto.RailRiders"/> null), the
    /// <see cref="FloorStateDto.NpcStates"/> entries whose id is in <paramref name="trainNpcIds"/> (added when the live capture lacks
    /// them), and the objective's open, coupled and lost cages, home, lost and freed crew, and its completion (the trial's crew come back
    /// with the train, so a completion won after the arrival goes too). Everything else is the wipe's: the dead stay dead, the spares,
    /// the gates and the orc's keys stand as they were.
    /// </summary>
    public static FloorStateDto Merge(FloorStateDto live, FloorStateDto arrival, IReadOnlySet<string> trainNpcIds)
    {
        var merged = Copy(live);
        var from = Copy(arrival);
        merged.RailStop = from.RailStop;
        merged.RailRiders = null;
        foreach (var npc in from.NpcStates)
        {
            if (!trainNpcIds.Contains(npc.Id)) continue;
            var at = merged.NpcStates.FindIndex(n => n.Id == npc.Id);
            if (at >= 0) merged.NpcStates[at] = npc;
            else merged.NpcStates.Add(npc);
        }
        if (merged.Objective is { } o && from.Objective is { } a)
        {
            o.OpenCages = a.OpenCages;
            o.CoupledCages = a.CoupledCages;
            o.HomeCrew = a.HomeCrew;
            o.LostCrew = a.LostCrew;
            o.FreedNpcs = a.FreedNpcs;
            o.LostCages = a.LostCages;
            o.Completed = a.Completed;
        }
        return merged;
    }

    private static FloorStateDto Copy(FloorStateDto dto)
        => JsonSerializer.Deserialize<FloorStateDto>(JsonSerializer.Serialize(dto, SaveFormat.Options), SaveFormat.Options)!;
}
