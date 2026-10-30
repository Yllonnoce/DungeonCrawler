using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace DungeonCrawler.Adventure.Voice;

/// <summary>Who speaks a line; decides voice and delivery style.</summary>
public enum Speaker { Narrator, Boss, Npc, Companion, Villain }

/// <summary>How a line is delivered. A line may ask for one; otherwise its speaker's persona, then the speaker kind, decides.</summary>
public enum Delivery { Default, Whisper, Weary, Calm, Warm, Cheerful, Sly, Pleading, Angry, Mocking, Shout, Booming, Frantic }

public static class Deliveries
{
    public static readonly Delivery[] All = { Delivery.Whisper, Delivery.Weary, Delivery.Calm, Delivery.Warm, Delivery.Cheerful, Delivery.Sly, Delivery.Pleading, Delivery.Angry, Delivery.Mocking, Delivery.Shout, Delivery.Booming, Delivery.Frantic };
    public static string Id(Delivery d) => d.ToString().ToLowerInvariant();
    public static IEnumerable<string> Ids => All.Select(Id);

    public static bool TryParse(string? id, out Delivery delivery)
    {
        delivery = Delivery.Default;
        return !string.IsNullOrWhiteSpace(id) && Enum.TryParse(id.Trim(), ignoreCase: true, out delivery) && Enum.IsDefined(delivery) && delivery != Delivery.Default;
    }

    /// <summary>The sentence the synth is told.</summary>
    public static string Instruction(Delivery d) => d switch
    {
        Delivery.Whisper => "Whisper it, quiet and close, afraid of being overheard",
        Delivery.Weary => "Say it tired and worn, the voice of someone who has had enough",
        Delivery.Calm => "Say it plainly and calmly at a normal speaking volume",
        Delivery.Warm => "Say it warmly, kindly, with a smile in the voice",
        Delivery.Cheerful => "Say it brightly and cheerfully, glad of the company",
        Delivery.Sly => "Say it slyly, half a smirk, weighing every word",
        Delivery.Pleading => "Plead it, urgent and anxious, on the edge of tears",
        Delivery.Angry => "Say it angrily, hard and clipped, teeth together",
        Delivery.Mocking => "Say it mockingly, dripping contempt, enjoying every word",
        Delivery.Shout => "Shout it, loud and raw, over the din of a fight",
        Delivery.Booming => "Boom it out, huge and slow, a voice that fills a hall",
        Delivery.Frantic => "Say it fast and frantic, breathless, almost panicked",
        _ => "Say it plainly at a normal speaking volume",
    };
}

/// <summary>How a character sounds: a short description of the voice and manner, a default delivery, and (optionally) which prebuilt voice speaks them.</summary>
public sealed class Persona
{
    public const int MaxStyle = 120;
    public string VoiceStyle { get; set; } = "";
    public string? DefaultDelivery { get; set; }
    public string? Voice { get; set; }
    public bool IsEmpty => string.IsNullOrWhiteSpace(VoiceStyle) && DefaultDelivery == null && Voice == null;
}

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
    public static string ModelFor(Speaker speaker, bool major) => speaker is Speaker.Narrator or Speaker.Boss or Speaker.Villain || (speaker == Speaker.Npc && major) ? Model : MinorModel;
    public const string Narrator = "Charon";
    public const string Boss = "Algenib";
    /// <summary>The antagonist: excitable, a voice made for yelling.</summary>
    public const string Villain = "Fenrir";
    public static readonly string[] Npcs = { "Kore", "Puck", "Leda", "Orus", "Aoede", "Enceladus", "Umbriel", "Despina" };

    /// <summary>Gemini prebuilt voices a planner may pick for a character, with what they sound like.</summary>
    public static readonly (string Name, string Sound)[] Catalog =
    {
        ("Kore", "woman, firm and level"), ("Leda", "woman, young and light"), ("Aoede", "woman, breezy and quick"), ("Despina", "woman, smooth and low"),
        ("Zephyr", "woman, bright and clear"), ("Autonoe", "woman, bright and warm"), ("Gacrux", "woman, mature and grave"), ("Sulafat", "woman, warm and rich"),
        ("Puck", "man, upbeat and boyish"), ("Orus", "man, firm and gruff"), ("Enceladus", "man, breathy and soft"), ("Umbriel", "man, easy-going"),
        ("Iapetus", "man, clear and dry"), ("Achird", "man, friendly and open"), ("Schedar", "man, even and steady"), ("Sadaltager", "man, older and knowing"),
        ("Charon", "man, deep, the narrator"), ("Algenib", "man, gravelly, a monster"), ("Fenrir", "man, excitable, a villain"),
    };
    public static bool IsKnown(string? voice) => voice != null && Catalog.Any(c => c.Name.Equals(voice.Trim(), StringComparison.OrdinalIgnoreCase));
    /// <summary>The catalog name with its own casing, for a name the planner typed.</summary>
    public static string? Canonical(string? voice) => voice == null ? null : Catalog.FirstOrDefault(c => c.Name.Equals(voice.Trim(), StringComparison.OrdinalIgnoreCase)).Name;

    /// <summary>The companions' voices and manners, by class.</summary>
    public static Persona CompanionPersona(string classId) => classId switch
    {
        "class.warrior" => new Persona { VoiceStyle = "a big, blunt soldier, gravel in the throat, no patience", DefaultDelivery = "calm" },
        "class.rogue" => new Persona { VoiceStyle = "a quick, sly thief who finds everything a little funny", DefaultDelivery = "sly" },
        "class.ranger" => new Persona { VoiceStyle = "a dry, watchful hunter of few words", DefaultDelivery = "calm" },
        "class.mage" => new Persona { VoiceStyle = "a precise, slightly superior scholar", DefaultDelivery = "calm" },
        _ => new Persona { VoiceStyle = "a seasoned adventurer", DefaultDelivery = "calm" },
    };

    /// <summary>What a companion's bark sounds like by default: a fight is shouted, the rest said.</summary>
    public static Delivery BarkDelivery(BarkTrigger trigger) => trigger switch
    {
        BarkTrigger.Kill => Delivery.Shout,
        BarkTrigger.Hurt => Delivery.Angry,
        BarkTrigger.BossSeen => Delivery.Frantic,
        BarkTrigger.Downed => Delivery.Weary,
        BarkTrigger.Revived => Delivery.Weary,
        BarkTrigger.Idle => Delivery.Mocking,
        BarkTrigger.Loot => Delivery.Cheerful,
        BarkTrigger.LevelUp => Delivery.Cheerful,
        _ => Delivery.Calm,
    };
    public static readonly Dictionary<string, string> Classes = new()
    {
        // the party as the owner built them: Brand, Wren and Ilex are men, Sable is a woman
        ["class.warrior"] = "Orus", ["class.rogue"] = "Zephyr", ["class.ranger"] = "Umbriel", ["class.mage"] = "Iapetus",
    };

    /// <summary>The recruitable fighters' voices and manners, by NPC id: a man's voice for the men, a woman's for the women.</summary>
    public static readonly (string Id, string Voice, Persona Persona)[] Fighters =
    {
        ("viewer_foehammer", "Schedar", new Persona { VoiceStyle = "a big, cheerful dwarf fighter, loud and proud of his hammer", DefaultDelivery = "cheerful" }),
        ("fighter_tamsin", "Sulafat", new Persona { VoiceStyle = "a jovial, greedy miner turned sellsword, quick to laugh", DefaultDelivery = "cheerful" }),
        ("fighter_oskar", "Sadaltager", new Persona { VoiceStyle = "a patient, proud smith of few words, dry and heavy", DefaultDelivery = "calm" }),
        ("fighter_ione", "Leda", new Persona { VoiceStyle = "a frightened, pious young nun, soft and unsteady", DefaultDelivery = "whisper" }),
    };

    /// <summary>A fighter's voice and persona; anyone unlisted sounds like a seasoned man-at-arms.</summary>
    public static (string Voice, Persona Persona) FighterVoice(string npcId)
    {
        foreach (var (id, voice, persona) in Fighters) if (id == npcId) return (voice, persona);
        return ("Orus", new Persona { VoiceStyle = "a seasoned fighter", DefaultDelivery = "calm" });
    }

    public static string ForNpc(string npcName) => Npcs[Math.Abs(StableHash(npcName)) % Npcs.Length];
    public static string ForClass(string classId) => Classes.GetValueOrDefault(classId, "Puck");

    /// <summary>The speaker kind's own manner, used when no persona says otherwise.</summary>
    public static string Style(Speaker s) => Style(s, null, null);

    /// <summary>The speaker's default delivery when neither the line nor the persona asks for one.</summary>
    public static Delivery DefaultDelivery(Speaker s) => s switch
    {
        Speaker.Boss => Delivery.Booming,
        Speaker.Villain => Delivery.Mocking,
        _ => Delivery.Calm,
    };

    private static string Base(Speaker s) => s switch
    {
        Speaker.Narrator => "Narrate slowly and gravely, low and unhurried, like the teller of a grim old tale",
        Speaker.Boss => "Speak as an ancient, enormous monster, contemptuous and slow",
        Speaker.Villain => "Speak as the master of this dungeon, spiteful and theatrical, hurling the words at the intruders",
        Speaker.Npc => "Speak as a person met in the dark below, in their own voice",
        _ => "Speak as an adventurer in the middle of a dungeon",
    };

    /// <summary>
    /// The delivery style handed to the synth: who the speaker is (the persona's words, else the speaker kind's), then how
    /// this line is delivered (the line's own ask, else the persona's default, else the kind's). Part of the line id, so
    /// a changed style renders afresh.
    /// </summary>
    public static string Style(Speaker s, Persona? persona, Delivery? delivery)
    {
        var who = persona is { VoiceStyle.Length: > 0 } ? $"Speak as {persona.VoiceStyle.Trim().TrimEnd('.')}" : Base(s);
        var how = delivery is { } d && d != Delivery.Default ? d
            : persona != null && Deliveries.TryParse(persona.DefaultDelivery, out var pd) ? pd
            : DefaultDelivery(s);
        return s == Speaker.Narrator && delivery is null or Delivery.Default ? who : $"{who}. {Deliveries.Instruction(how)}";
    }

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
/// <param name="FallbackText">Rendered under the same id when the provider refuses <paramref name="Text"/>.</param>
public sealed record VoiceJob(string Id, string Text, string Voice, string Style, VoicePriority Priority, string Model = "", string? FallbackText = null);

/// <summary>Ordered, de-duplicated work list for the renderer. Engine-free so its policy is unit tested.</summary>
public sealed class VoiceQueue
{
    private readonly List<VoiceJob> _jobs = new();
    private readonly HashSet<string> _known = new();
    public int Count => _jobs.Count;
    public bool Paused { get; set; }

    /// <summary>Queue a line unless it is already known (queued, rendering or done). Returns the job's id.</summary>
    public string Enqueue(string text, string voice, string style, VoicePriority priority, Func<string, bool> alreadyRendered, string? model = null, string? fallbackText = null)
    {
        model ??= Voices.Model;
        var id = VoiceLineId.For(text, voice, style, model);
        if (_known.Contains(id) || alreadyRendered(id)) { _known.Add(id); return id; }
        _known.Add(id);
        _jobs.Add(new VoiceJob(id, text, voice, style, priority, model, fallbackText));
        return id;
    }

    /// <summary>Put a job back with different words under the same id (the clean twin of a refused line).</summary>
    public void Requeue(VoiceJob job)
    {
        _known.Add(job.Id);
        _jobs.Add(job);
    }

    /// <summary>Whether an error from the provider means the words themselves were turned away, not the request.</summary>
    public static bool LooksLikeRefusal(string? error)
        => error != null && (error.Contains("refus", StringComparison.OrdinalIgnoreCase) || error.Contains("content", StringComparison.OrdinalIgnoreCase) || error.Contains("safety", StringComparison.OrdinalIgnoreCase)
            || error.Contains("block", StringComparison.OrdinalIgnoreCase) || error.Contains("prohibit", StringComparison.OrdinalIgnoreCase) || error.Contains("policy", StringComparison.OrdinalIgnoreCase) || error.Contains("no audio", StringComparison.OrdinalIgnoreCase));

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
