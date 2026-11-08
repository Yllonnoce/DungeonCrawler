namespace DungeonCrawler.Net;

/// <summary>
/// Ping (co-op, pillar 7; key X): a marker where the lead is looking, 2 s, with the player's name, relayed as
/// <see cref="PingEvt"/>; 1 s cooldown. The server refuses a ping inside the cooldown or far from the sender's body.
/// </summary>
public static class PingRules
{
    public const float LifetimeSeconds = 2f;
    public const float CooldownSeconds = 1f;
    /// <summary>The aim ray's length (tune).</summary>
    public const float MaxAimDistance = 40f;
    /// <summary>The server refuses a point farther than this from the sender's body (a guard against teleported pings).</summary>
    public const float MaxRangeFromBody = 45f;

    public static bool CanPing(double lastAt, double now) => lastAt < 0 || now - lastAt >= CooldownSeconds;
    public static bool Accepts(float distanceFromBody) => distanceFromBody <= MaxRangeFromBody;
}

public readonly record struct PingMark(string Name, float X, float Y, float Z, double PlacedAt);

/// <summary>The live pings: one per name (a new ping by the same player replaces the old), pruned after <see cref="PingRules.LifetimeSeconds"/>.</summary>
public sealed class PingBoard
{
    private readonly List<PingMark> _marks = new();
    public IReadOnlyList<PingMark> Active => _marks;

    public void Place(string name, float x, float y, float z, double now)
    {
        _marks.RemoveAll(m => m.Name == name);
        _marks.Add(new PingMark(name, x, y, z, now));
    }

    public void Prune(double now) => _marks.RemoveAll(m => now - m.PlacedAt >= PingRules.LifetimeSeconds);

    public void Clear() => _marks.Clear();
}
