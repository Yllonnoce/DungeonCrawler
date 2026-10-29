namespace DungeonCrawler.Adventure.Voice;

/// <summary>
/// Keeps the companions from saying the same thing over and over: a bark that has been said stays out of the draw
/// until the party is <see cref="EveryFloors"/> floors deeper. When every line for a trigger has been said that
/// recently, the companion says nothing. The record is kept in the save. Engine-free.
/// </summary>
public static class BarkRepeat
{
    public const int EveryFloors = 2;

    /// <summary>The key a line is remembered under: the text, trimmed and case-folded.</summary>
    public static string Key(string text) => text.Trim().ToLowerInvariant();

    /// <summary>Whether a line may be said on <paramref name="floor"/>, given the floor it was last said on.</summary>
    public static bool CanSay(IReadOnlyDictionary<string, int> saidOnFloor, string text, int floor, int everyFloors = EveryFloors)
        => !saidOnFloor.TryGetValue(Key(text), out var last) || floor - last >= everyFloors;

    /// <summary>Pick one of the lines not said in the last <paramref name="everyFloors"/> floors; null when there is none.</summary>
    public static string? Choose(IReadOnlyDictionary<string, int> saidOnFloor, IReadOnlyList<string> lines, int floor, Random rng, int everyFloors = EveryFloors)
    {
        var fresh = lines.Where(l => l.Trim().Length > 0 && CanSay(saidOnFloor, l, floor, everyFloors)).ToList();
        return fresh.Count == 0 ? null : fresh[rng.Next(fresh.Count)];
    }

    /// <summary>A fresh line when there is one, else the line said longest ago: for a character who should never fall silent.</summary>
    public static string? ChooseOrOldest(IReadOnlyDictionary<string, int> saidOnFloor, IReadOnlyList<string> lines, int floor, Random rng, int everyFloors = EveryFloors)
    {
        if (Choose(saidOnFloor, lines, floor, rng, everyFloors) is { } fresh) return fresh;
        var usable = lines.Where(l => l.Trim().Length > 0).ToList();
        if (usable.Count == 0) return null;
        var oldest = usable.Min(l => saidOnFloor.TryGetValue(Key(l), out var f) ? f : int.MinValue);
        var candidates = usable.Where(l => (saidOnFloor.TryGetValue(Key(l), out var f) ? f : int.MinValue) == oldest).ToList();
        return candidates[rng.Next(candidates.Count)];
    }

    /// <summary>Remember that a line was said on a floor.</summary>
    public static void Said(IDictionary<string, int> saidOnFloor, string text, int floor) => saidOnFloor[Key(text)] = floor;
}
