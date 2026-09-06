using Godot;

namespace DungeonCrawler.Audio;

/// <summary>Sounds made in code, for cues no free pack covers.</summary>
public static class Synth
{
    private const int Rate = 22050;
    private static AudioStreamWav? _rumble, _thud;
    private static readonly Random Rng = new(11);

    /// <summary>Stone giving way: a low rolling rumble with grit on top, three seconds, fading out.</summary>
    public static AudioStreamWav Rumble()
    {
        if (_rumble != null) return _rumble;
        var n = (int)(Rate * 3.2f);
        var samples = new float[n];
        float lp1 = 0f, lp2 = 0f, lp3 = 0f;
        for (var i = 0; i < n; i++)
        {
            var t = (float)i / Rate;
            var white = (float)(Rng.NextDouble() * 2 - 1);
            lp1 += (white - lp1) * 0.08f;
            lp2 += (lp1 - lp2) * 0.03f;
            lp3 += (lp2 - lp3) * 0.01f;
            var env = Math.Min(1f, t * 3f) * MathF.Exp(-Math.Max(0f, t - 1.2f) * 1.6f);
            var throb = 0.7f + 0.3f * MathF.Sin(t * 5.5f) * MathF.Sin(t * 1.7f);
            var grit = (Rng.NextDouble() < 0.004 ? (float)(Rng.NextDouble() - 0.5) * 1.5f : 0f);
            samples[i] = (lp3 * 14f + lp2 * 2f + grit) * env * throb;
        }
        return _rumble = Wav(samples, 0.75f);
    }

    /// <summary>A stone landing: a short low knock with a little grit.</summary>
    public static AudioStreamWav Thud()
    {
        if (_thud != null) return _thud;
        var n = (int)(Rate * 0.3f);
        var samples = new float[n];
        float lp = 0f;
        for (var i = 0; i < n; i++)
        {
            var t = (float)i / Rate;
            var white = (float)(Rng.NextDouble() * 2 - 1);
            lp += (white - lp) * 0.2f;
            var knock = MathF.Sin(t * 2f * MathF.PI * (70f + 40f * MathF.Exp(-t * 30f))) * MathF.Exp(-t * 18f);
            samples[i] = knock * 0.9f + lp * MathF.Exp(-t * 40f) * 0.6f;
        }
        return _thud = Wav(samples, 0.8f);
    }

    public static AudioStreamWav Wav(float[] samples, float gain, bool loop = false)
    {
        var data = new byte[samples.Length * 2];
        for (var i = 0; i < samples.Length; i++)
        {
            var v = (short)Mathf.Clamp(samples[i] * gain * 32767f, -32767f, 32767f);
            data[i * 2] = (byte)(v & 0xFF);
            data[i * 2 + 1] = (byte)((v >> 8) & 0xFF);
        }
        return new AudioStreamWav { Format = AudioStreamWav.FormatEnum.Format16Bits, MixRate = Rate, Stereo = false, Data = data, LoopMode = loop ? AudioStreamWav.LoopModeEnum.Forward : AudioStreamWav.LoopModeEnum.Disabled, LoopBegin = 0, LoopEnd = samples.Length };
    }

    /// <summary>Play a stream once at a spot; the player frees itself.</summary>
    public static void PlayAt(Node parent, AudioStream stream, Vector3 at, float volumeDb = 0f, float maxDistance = 30f)
    {
        var p = new AudioStreamPlayer3D { Stream = stream, Bus = "SFX", VolumeDb = volumeDb, MaxDistance = maxDistance, UnitSize = 6f };
        parent.AddChild(p);
        p.GlobalPosition = at;
        p.Finished += p.QueueFree;
        p.Play();
    }
}
