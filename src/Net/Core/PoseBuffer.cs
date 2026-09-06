namespace DungeonCrawler.Net;

/// <summary>
/// Timed samples of one remote body's pose. The client asks for the pose at "now minus the interpolation delay" and
/// gets a blend of the two samples around that time, or a short extrapolation past the newest one. Samples that arrive
/// out of order (older than the newest) are dropped.
/// </summary>
public sealed class PoseBuffer
{
    public readonly record struct Sample(double Time, int Tick, NetVec Position, float Yaw, float Pitch, NetVec Velocity, PoseFlags Flags);

    private readonly List<Sample> _samples = new();
    private readonly int _capacity;

    public PoseBuffer(int capacity = 32) => _capacity = capacity;

    public int Count => _samples.Count;
    public Sample? Newest => _samples.Count > 0 ? _samples[^1] : null;

    /// <summary>Add a sample stamped with the receive (or server) time. Returns false when it was older than what we have.</summary>
    public bool Push(double time, int tick, NetVec position, float yaw, float pitch, NetVec velocity, PoseFlags flags)
    {
        if (_samples.Count > 0 && tick <= _samples[^1].Tick) return false;
        if (_samples.Count > 0 && time < _samples[^1].Time) time = _samples[^1].Time;
        _samples.Add(new Sample(time, tick, position, yaw, pitch, velocity, flags));
        if (_samples.Count > _capacity) _samples.RemoveAt(0);
        return true;
    }

    public bool Push(double time, PlayerPoseState p, int tick) => Push(time, tick, p.Position, p.Yaw, p.Pitch, p.Velocity, p.Flags);

    /// <summary>The pose to show at <paramref name="renderTime"/>; null until a sample exists.</summary>
    public Sample? At(double renderTime, double maxExtrapolationSeconds = Protocol.MaxExtrapolationMs / 1000.0)
    {
        if (_samples.Count == 0) return null;
        var newest = _samples[^1];
        if (renderTime >= newest.Time)
        {
            var ahead = Math.Min(renderTime - newest.Time, maxExtrapolationSeconds);
            return newest with { Time = renderTime, Position = newest.Position + newest.Velocity * (float)ahead };
        }
        if (renderTime <= _samples[0].Time) return _samples[0];
        for (var i = _samples.Count - 1; i > 0; i--)
        {
            var a = _samples[i - 1];
            var b = _samples[i];
            if (renderTime < a.Time) continue;
            var span = b.Time - a.Time;
            var t = span <= 0 ? 1f : (float)((renderTime - a.Time) / span);
            return new Sample(renderTime, b.Tick, NetVec.Lerp(a.Position, b.Position, t), LerpAngle(a.Yaw, b.Yaw, t), a.Pitch + (b.Pitch - a.Pitch) * t,
                NetVec.Lerp(a.Velocity, b.Velocity, t), t < 0.5f ? a.Flags : b.Flags);
        }
        return newest;
    }

    /// <summary>Drop samples older than <paramref name="olderThan"/> seconds before the newest (keeps memory bounded on long sessions).</summary>
    public void Trim(double olderThan)
    {
        if (_samples.Count == 0) return;
        var cutoff = _samples[^1].Time - olderThan;
        _samples.RemoveAll(s => s.Time < cutoff && s != _samples[^1]);
    }

    public static float LerpAngle(float a, float b, float t)
    {
        var d = b - a;
        while (d > MathF.PI) d -= MathF.Tau;
        while (d < -MathF.PI) d += MathF.Tau;
        return a + d * t;
    }
}
