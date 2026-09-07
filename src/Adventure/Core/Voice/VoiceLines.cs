using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace DungeonCrawler.Adventure.Voice;

/// <summary>Who speaks a line; decides voice and delivery style.</summary>
public enum Speaker { Narrator, Boss, Npc, Companion }

public enum VoicePriority { Narrator = 0, Boss = 1, Npc = 2, Bark = 3 }

/// <summary>Renders text to raw 16-bit little-endian PCM at <see cref="SampleRate"/> Hz, mono. Null means failure.</summary>
public interface IVoiceSynth
{
    const int SampleRate = 24000;
    Task<byte[]?> RenderPcmAsync(string text, string voice, string style, CancellationToken ct);
}

/// <summary>What a live synth can say about its last call, so the renderer backs off, switches model or stops without knowing the provider.</summary>
public interface IVoiceSynthDiagnostics
{
    string Model { get; }
    string? LastError { get; }
    int LastStatus { get; }
    /// <summary>The last failure means more requests to this model will only fail (a daily quota, an empty balance).</summary>
    bool QuotaExhausted { get; }
    /// <summary>Seconds the provider asked us to wait after the last 429, when it said.</summary>
    float? RetryAfterSeconds { get; }
}

/// <summary>Gemini prebuilt voice names, grouped by the roles that suit them.</summary>
public static class Voices
{
    public const string DefaultModel = "gemini-3.1-flash-tts-preview";
    public const string DefaultFallbacks = "gemini-2.5-pro-preview-tts, gemini-2.5-flash-preview-tts";
    public const string DefaultMinorModel = "gemini-2.5-flash-preview-tts";
    /// <summary>The TTS model for the lines that matter most (narrator, bosses, the cast); Settings sets it. Line ids include the model, so changing it renders afresh.</summary>
    public static string Model { get; set; } = DefaultModel;
    /// <summary>The model for everything else (one-off NPCs, companion barks), so the best model's quota goes to the main voices first.</summary>
    public static string MinorModel { get; set; } = DefaultMinorModel;

    /// <summary>Which model a line renders with: the narrator, bosses and cast members get the main one; the rest the minor one.</summary>
    public static string ModelFor(Speaker speaker, bool major) => speaker is Speaker.Narrator or Speaker.Boss || (speaker == Speaker.Npc && major) ? Model : MinorModel;
    public const string Narrator = "Charon";
    public const string Boss = "Algenib";
    public static readonly string[] Npcs = { "Kore", "Puck", "Leda", "Orus", "Aoede", "Enceladus", "Umbriel", "Despina" };
    public static readonly Dictionary<string, string> Classes = new()
    {
        ["class.warrior"] = "Orus", ["class.rogue"] = "Zephyr", ["class.ranger"] = "Autonoe", ["class.mage"] = "Iapetus",
    };

    public static string ForNpc(string npcName) => Npcs[Math.Abs(StableHash(npcName)) % Npcs.Length];
    public static string ForClass(string classId) => Classes.GetValueOrDefault(classId, "Puck");

    public static string Style(Speaker s) => s switch
    {
        Speaker.Narrator => "Narrate slowly and gravely, low and unhurried, like the teller of a grim old tale",
        Speaker.Boss => "Speak as an ancient, enormous monster: booming, contemptuous, slow",
        Speaker.Npc => "Speak quietly and wearily, a frightened survivor whispering in the dark",
        _ => "Say this tersely under your breath in the middle of a fight",
    };

    private static int StableHash(string s)
    {
        unchecked { var h = 17; foreach (var c in s) h = h * 31 + c; return h; }
    }
}

/// <summary>Content-addressed id for a rendered line: the same text, voice, style and model always map to the same file.</summary>
public static class VoiceLineId
{
    public static string For(string text, string voice, string style, string? model = null)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes($"{model ?? Voices.Model}\n{voice}\n{style}\n{text.Trim()}"));
        return Convert.ToHexString(bytes)[..16].ToLowerInvariant();
    }
}

/// <summary>RIFF/WAVE wrapper for raw PCM so the cache holds ordinary .wav files.</summary>
public static class WavWriter
{
    public static byte[] Wrap(byte[] pcm16, int sampleRate = IVoiceSynth.SampleRate, int channels = 1)
    {
        var bits = 16;
        var byteRate = sampleRate * channels * bits / 8;
        var blockAlign = channels * bits / 8;
        using var ms = new MemoryStream(44 + pcm16.Length);
        using var w = new BinaryWriter(ms);
        w.Write(Encoding.ASCII.GetBytes("RIFF"));
        w.Write(36 + pcm16.Length);
        w.Write(Encoding.ASCII.GetBytes("WAVE"));
        w.Write(Encoding.ASCII.GetBytes("fmt "));
        w.Write(16);
        w.Write((short)1);
        w.Write((short)channels);
        w.Write(sampleRate);
        w.Write(byteRate);
        w.Write((short)blockAlign);
        w.Write((short)bits);
        w.Write(Encoding.ASCII.GetBytes("data"));
        w.Write(pcm16.Length);
        w.Write(pcm16);
        w.Flush();
        return ms.ToArray();
    }

    /// <summary>The PCM payload of a file written by <see cref="Wrap"/> (or any 16-bit mono PCM wav with a plain header).</summary>
    public static (byte[] Pcm, int SampleRate) Unwrap(byte[] wav)
    {
        if (wav.Length < 44 || Encoding.ASCII.GetString(wav, 0, 4) != "RIFF") throw new InvalidDataException("not a RIFF file");
        var sampleRate = BitConverter.ToInt32(wav, 24);
        var pos = 12;
        while (pos + 8 <= wav.Length)
        {
            var id = Encoding.ASCII.GetString(wav, pos, 4);
            var size = BitConverter.ToInt32(wav, pos + 4);
            if (id == "data") return (wav.AsSpan(pos + 8, Math.Min(size, wav.Length - pos - 8)).ToArray(), sampleRate);
            pos += 8 + size;
        }
        throw new InvalidDataException("no data chunk");
    }
}

/// <summary>Request and response shapes of the Gemini generateContent TTS call, kept out of the HTTP client so they are testable.</summary>
public static class GeminiTtsWire
{
    public static string Endpoint(string model) => $"https://generativelanguage.googleapis.com/v1beta/models/{model}:generateContent";

    public static string RequestJson(string text, string voice, string style)
    {
        var body = new JsonObject
        {
            ["contents"] = new JsonArray(new JsonObject { ["parts"] = new JsonArray(new JsonObject { ["text"] = $"{style}: {text}" }) }),
            ["generationConfig"] = new JsonObject
            {
                ["responseModalities"] = new JsonArray("AUDIO"),
                ["speechConfig"] = new JsonObject { ["voiceConfig"] = new JsonObject { ["prebuiltVoiceConfig"] = new JsonObject { ["voiceName"] = voice } } },
            },
        };
        return body.ToJsonString();
    }

    /// <summary>
    /// Read a 429 body: whether it names a per-day quota (retrying today is pointless) and any retry delay Google suggests.
    /// Unknown shapes count as a short limit.
    /// </summary>
    public static (bool DailyQuota, float? RetrySeconds) ClassifyRateLimit(string body)
    {
        var daily = body.Contains("PerDay", StringComparison.OrdinalIgnoreCase) || body.Contains("per day", StringComparison.OrdinalIgnoreCase);
        float? retry = null;
        try
        {
            var root = JsonNode.Parse(body);
            if (root?["error"]?["details"] is JsonArray details)
                foreach (var d in details)
                {
                    if (d?["retryDelay"]?.GetValue<string>() is { } delay && delay.EndsWith('s') && float.TryParse(delay[..^1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var secs)) retry = secs;
                    if (d?["violations"] is JsonArray violations)
                        foreach (var v in violations)
                            if (v?["quotaId"]?.GetValue<string>()?.Contains("PerDay", StringComparison.OrdinalIgnoreCase) == true) daily = true;
                }
        }
        catch { }
        return (daily, retry);
    }

    /// <summary>The PCM bytes in a response, or null with a reason.</summary>
    public static (byte[]? Pcm, string? Error) ParseResponse(string json)
    {
        try
        {
            var root = JsonNode.Parse(json);
            if (root?["error"] is { } err) return (null, err["message"]?.GetValue<string>() ?? "error");
            var part = root?["candidates"]?[0]?["content"]?["parts"]?[0];
            var data = part?["inlineData"]?["data"]?.GetValue<string>();
            if (string.IsNullOrEmpty(data)) return (null, "no audio in the response");
            return (Convert.FromBase64String(data), null);
        }
        catch (Exception e) { return (null, $"unreadable response: {e.Message}"); }
    }
}

/// <summary>One line waiting to be rendered.</summary>
public sealed record VoiceJob(string Id, string Text, string Voice, string Style, VoicePriority Priority, string Model = "");

/// <summary>Ordered, de-duplicated work list for the renderer. Engine-free so its policy is unit tested.</summary>
public sealed class VoiceQueue
{
    private readonly List<VoiceJob> _jobs = new();
    private readonly HashSet<string> _known = new();
    public int Count => _jobs.Count;
    public bool Paused { get; set; }

    /// <summary>Queue a line unless it is already known (queued, rendering or done). Returns the job's id.</summary>
    public string Enqueue(string text, string voice, string style, VoicePriority priority, Func<string, bool> alreadyRendered, string? model = null)
    {
        model ??= Voices.Model;
        var id = VoiceLineId.For(text, voice, style, model);
        if (_known.Contains(id) || alreadyRendered(id)) { _known.Add(id); return id; }
        _known.Add(id);
        _jobs.Add(new VoiceJob(id, text, voice, style, priority, model));
        return id;
    }

    public VoiceJob? Next()
    {
        if (Paused || _jobs.Count == 0) return null;
        var best = _jobs.OrderBy(j => (int)j.Priority).First();
        _jobs.Remove(best);
        return best;
    }

    /// <summary>A failed job may be retried later.</summary>
    public void Forget(string id) => _known.Remove(id);
    /// <summary>Drop everything still waiting; those lines may be queued again later.</summary>
    public void Clear()
    {
        foreach (var j in _jobs) _known.Remove(j.Id);
        _jobs.Clear();
    }
}
