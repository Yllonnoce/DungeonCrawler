namespace DungeonCrawler.Net;

/// <summary>
/// How a hazard phase travels to clients. A phase that lasts until something happens (a Press waiting Armed, a
/// pressure bed waiting Down) has an infinite duration in the rules; on the wire it is 0 ticks, never a cast of
/// infinity (which lands on int.MinValue and makes the client's clamp throw). Engine-free.
/// </summary>
public static class HazardSync
{
    public const int TicksPerSecond = 60;

    /// <summary>Seconds to wire ticks; 0 for an open-ended phase.</summary>
    public static int DurationTicks(float seconds) => float.IsFinite(seconds) && seconds > 0f ? (int)Math.Round(seconds * TicksPerSecond) : 0;

    /// <summary>How far into the phase a client is when the message lands: never negative, never past the phase's end.</summary>
    public static float SecondsIn(int localTick, int serverTickOffset, int serverTick, int durationTicks)
    {
        var elapsed = Math.Max(0, localTick + serverTickOffset - serverTick) / (float)TicksPerSecond;
        return durationTicks > 0 ? Math.Min(elapsed, durationTicks / (float)TicksPerSecond) : elapsed;
    }
}
