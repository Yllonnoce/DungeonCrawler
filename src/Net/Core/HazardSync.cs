namespace DungeonCrawler.Net;

/// <summary>
/// How a hazard phase travels to clients: the server's clock when it began and how long it lasts, in seconds. A phase that
/// lasts until something happens (a Press waiting Armed, a pressure bed waiting Down) has an infinite duration in the rules; on
/// the wire it is 0, never infinity. A client turns the start into seconds already elapsed with its own estimate of the
/// server's clock (<see cref="NetClock"/>), so a 90 Hz headset and a 60 Hz server agree. Engine-free.
/// </summary>
public static class HazardSync
{
    /// <summary>A phase's length on the wire; 0 for an open-ended phase (infinite, NaN or not positive).</summary>
    public static float DurationSeconds(float seconds) => float.IsFinite(seconds) && seconds > 0f ? seconds : 0f;

    /// <summary>How far into the phase a client is when the message lands: never negative, never past the phase's end (an open-ended phase grows).</summary>
    public static float SecondsIn(double serverNow, double sentServerTime, float durationSeconds)
    {
        var elapsed = (float)Math.Max(0, serverNow - sentServerTime);
        return durationSeconds > 0f ? Math.Min(elapsed, durationSeconds) : elapsed;
    }
}
