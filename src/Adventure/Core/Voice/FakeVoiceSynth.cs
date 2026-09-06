namespace DungeonCrawler.Adventure.Voice;

/// <summary>A tone per line, for testing the render queue, cache and playback without a key.</summary>
public sealed class FakeVoiceSynth : IVoiceSynth
{
    public int DelayMs { get; set; } = 300;

    public async Task<byte[]?> RenderPcmAsync(string text, string voice, string style, CancellationToken ct)
    {
        if (DelayMs > 0) await Task.Delay(DelayMs, ct);
        var seconds = Math.Clamp(text.Length * 0.04f, 0.4f, 6f);
        var samples = (int)(IVoiceSynth.SampleRate * seconds);
        var pcm = new byte[samples * 2];
        var freq = 180f + Math.Abs(voice.GetHashCode() % 200);
        for (var i = 0; i < samples; i++)
        {
            var env = Math.Min(1f, Math.Min(i, samples - i) / 2000f);
            var v = (short)(Math.Sin(2 * Math.PI * freq * i / IVoiceSynth.SampleRate) * 6000 * env);
            pcm[i * 2] = (byte)(v & 0xFF);
            pcm[i * 2 + 1] = (byte)((v >> 8) & 0xFF);
        }
        return pcm;
    }
}
