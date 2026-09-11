namespace DungeonCrawler.Adventure.Objectives;

/// <summary>
/// The HUD's single objective line, composed from everything that wants to be on it in a fixed order: the floor's
/// objective (the gate) first, then a horde loose on the floor, then the open quest, then the reinforcement count.
/// One writer, no races. Engine-free.
/// </summary>
public static class ObjectiveLine
{
    public const string Gap = "   ";
    public const int MaxLength = 160;

    public static string Compose(string? floorObjective, string? horde, string? quest, string? portals, int maxLength = MaxLength)
    {
        var parts = new[] { floorObjective, horde, quest, portals }.Where(p => !string.IsNullOrWhiteSpace(p)).Select(p => p!.Trim()).ToList();
        var line = string.Join(Gap, parts);
        return line.Length <= maxLength ? line : line[..(maxLength - 1)].TrimEnd() + "…";
    }
}
