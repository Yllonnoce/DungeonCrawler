using System.Text.Json.Nodes;
using DungeonCrawler.Adventure.Ai;

namespace DungeonCrawler.Adventure.Voice;

/// <summary>
/// Text-to-speech through Runware's audioInference task. Runware fronts several TTS providers, each with its own
/// voice names, so the game's Gemini voice names (what every caller passes) are mapped per model family here.
/// </summary>
public static class RunwareVoices
{
    public const string DefaultModel = "xai:tts@0";
    public const string DefaultMinorModel = "xai:tts@0";
    public const string DefaultFallbacks = "inworld:tts@2";

    /// <summary>A TTS model the game can voice: its price per thousand characters (0 when Runware bills it another way) and a note for the menu.</summary>
    public sealed record KnownModel(string Model, string Name, double PerThousandChars, string Note)
    {
        public string Label => PerThousandChars > 0 ? $"{Name} (${PerThousandChars:0.000} per 1k chars)" : $"{Name} ({Note})";
    }

    /// <summary>
    /// The TTS models we know how to voice, dearest first (Runware's listed prices on 2026-09-07); anything else gets
    /// the Gemini voice names unchanged.
    /// </summary>
    public static readonly KnownModel[] KnownModels =
    {
        new("minimax:speech@2.8", "MiniMax Speech 2.8", 0.10, "MiniMax; about $0.10 per thousand characters"),
        new("google:gemini@3.1-flash-tts", "Gemini 3.1 Flash TTS", 0, "billed per token, $1 in / $20 out per 1M; the same voice names and delivery styles as the direct Gemini path"),
        new("inworld:tts@1.5-max", "Inworld TTS-1.5 Max", 0.05, "Inworld, the most expressive"),
        new("inworld:tts@2", "Inworld Realtime TTS-2", 0.035, "Inworld"),
        new("inworld:tts@1.5-mini", "Inworld TTS-1.5 Mini", 0.025, "Inworld, cheaper and quicker"),
        new("xai:tts@0", "xAI Text-to-Speech", 0.015, "xAI, the cheapest"),
    };

    /// <summary>A model's family (the part before the colon), lower case.</summary>
    public static string Family(string model)
    {
        var i = model.IndexOf(':');
        return (i < 0 ? model : model[..i]).Trim().ToLowerInvariant();
    }

    private sealed record Bank(string Narrator, string Boss, string[] Npcs, Dictionary<string, string> Classes, string Default, string Language);

    private static readonly Dictionary<string, Bank> Banks = new()
    {
        ["xai"] = new("orion", "zagan", new[] { "luna", "iris", "helix", "perseus", "celeste", "castor", "ara", "cosmo" },
            new() { ["class.warrior"] = "atlas", ["class.rogue"] = "lux", ["class.ranger"] = "kepler", ["class.mage"] = "rigel" }, "eve", "en"),
        // the party as built: Brand, Wren and Ilex are men, Sable a woman (Marlene had the mage as a woman)
        ["inworld"] = new("Mortimer", "Graham", new[] { "Loretta", "Hank", "Evelyn", "Pippa", "Tessa", "Liam", "Callum", "Abby" },
            new() { ["class.warrior"] = "Hamish", ["class.rogue"] = "Tessa", ["class.ranger"] = "Callum", ["class.mage"] = "Theodore" }, "Hank", "en"),
        ["minimax"] = new("English_expressive_narrator", "English_magnetic_voiced_man", new[] { "English_CalmWoman", "English_radiant_girl", "English_PlayfulGirl", "English_magnetic_voiced_man" },
            new() { ["class.warrior"] = "English_magnetic_voiced_man", ["class.rogue"] = "English_PlayfulGirl", ["class.ranger"] = "English_CalmWoman", ["class.mage"] = "English_expressive_narrator" }, "English_CalmWoman", "en"),
    };

    /// <summary>
    /// Gemini voices with no class, NPC, narrator or boss slot, by family: the fighters' and the villain's voices, and the
    /// rest of the catalogue, each on a voice of the same sex. Without an entry such a name fell to the family default.
    /// </summary>
    private static readonly Dictionary<string, Dictionary<string, string>> Named = new()
    {
        ["inworld"] = new()
        {
            ["Schedar"] = "Hades", ["Sadaltager"] = "Conrad", ["Achird"] = "Oliver", ["Fenrir"] = "Dominus",
            ["Sulafat"] = "Riley", ["Gacrux"] = "Wendy", ["Zephyr"] = "Tessa", ["Autonoe"] = "Deborah", ["Iapetus"] = "Theodore", // Gacrux: Maud Sorrel, an old woman
        },
    };

    /// <summary>
    /// The sex of a provider voice the game uses, "male", "female" or "unknown". Inworld's confirmed ones come from its
    /// own voice descriptions and listings (2026-10-29); Hamish, Callum, Liam, Hank, Mortimer, Graham, Loretta, Evelyn and
    /// Marlene are taken from the names, which Inworld's catalogue has matched in every case checked.
    /// </summary>
    public static string Gender(string model, string providerVoice)
    {
        if (Family(model) != "inworld") return "unknown";
        if (InworldWomen.Contains(providerVoice)) return "female";
        if (InworldMen.Contains(providerVoice)) return "male";
        return "unknown";
    }

    private static readonly HashSet<string> InworldWomen = new(StringComparer.OrdinalIgnoreCase)
        { "Loretta", "Evelyn", "Tessa", "Abby", "Pippa", "Deborah", "Riley", "Miranda", "Marlene", "Julia", "Olivia", "Ashley", "Luna", "Celeste", "Chloe", "Darlene", "Amina", "Wendy", "Elizabeth", "Sarah", "Pixie", "Priya" };
    private static readonly HashSet<string> InworldMen = new(StringComparer.OrdinalIgnoreCase)
        { "Mortimer", "Graham", "Hank", "Liam", "Callum", "Hamish", "Theodore", "Hades", "Conrad", "Oliver", "Dominus", "Edward", "Dennis", "Alex", "Brian", "Sebastian", "Gareth" };

    /// <summary>Whether the model is Google's Gemini TTS behind Runware, which keeps Gemini's names and reads a delivery style.</summary>
    public static bool IsGemini(string model) => Family(model) == "google";

    /// <summary>The provider's voice for one of our Gemini voice names on the given model.</summary>
    public static string Map(string model, string geminiVoice)
    {
        if (!Banks.TryGetValue(Family(model), out var bank)) return geminiVoice;
        if (geminiVoice == Voices.Narrator) return bank.Narrator;
        if (geminiVoice == Voices.Boss) return bank.Boss;
        if (Named.TryGetValue(Family(model), out var named) && named.TryGetValue(geminiVoice, out var exact)) return exact;
        // class voices first: "Orus" is both the warrior's voice and an NPC voice, and the companion should sound like a warrior
        foreach (var (classId, name) in Voices.Classes)
            if (name == geminiVoice) return bank.Classes.GetValueOrDefault(classId, bank.Default);
        var npc = Array.IndexOf(Voices.Npcs, geminiVoice);
        if (npc >= 0) return bank.Npcs[npc % bank.Npcs.Length];
        return bank.Default;
    }

    public static string Language(string model) => IsGemini(model) ? "en-US" : Banks.TryGetValue(Family(model), out var b) ? b.Language : "en";

    /// <summary>
    /// One line as an audioInference task, asking for WAV in the body. Only Gemini reads a delivery style; the others
    /// would speak it, so it is dropped for them.
    /// </summary>
    public static string RequestJson(string model, string text, string geminiVoice, string style, string taskUuid)
    {
        var speech = new JsonObject
        {
            ["text"] = IsGemini(model) && style.Length > 0 ? $"{style}: {text}" : text,
            ["voice"] = Map(model, geminiVoice),
            ["language"] = Language(model),
        };
        var task = new JsonObject
        {
            ["taskType"] = "audioInference",
            ["taskUUID"] = taskUuid,
            ["model"] = model,
            ["speech"] = speech,
            ["outputType"] = "base64Data",
            ["outputFormat"] = "WAV",
            ["includeCost"] = true,
        };
        return new JsonArray(task).ToJsonString();
    }

    /// <summary>The audio bytes (a WAV file) and reported cost in a reply, or null with a reason.</summary>
    public static (byte[]? Wav, double Cost, string? Error) ParseResponse(string json)
    {
        try
        {
            if (RunwareWire.ErrorOf(json) is { } error) return (null, 0, error);
            var root = JsonNode.Parse(json);
            var task = (root?["data"] as JsonArray)?.OfType<JsonObject>().FirstOrDefault(t => t["taskType"]?.GetValue<string>() == "audioInference")
                       ?? (root?["data"] as JsonArray)?.OfType<JsonObject>().FirstOrDefault();
            var data = task?["audioBase64Data"]?.GetValue<string>();
            if (string.IsNullOrEmpty(data)) return (null, 0, task?["audioURL"] != null ? "the response carried a URL instead of audio data" : "no audio in the response");
            var cost = task?["cost"]?.GetValue<double>() ?? 0;
            return (Convert.FromBase64String(data), cost, null);
        }
        catch (Exception e) { return (null, 0, $"unreadable response: {e.Message}"); }
    }
}

/// <summary>Turns whatever PCM WAV a provider returns into the 16-bit mono 24 kHz the voice cache holds.</summary>
public static class PcmTools
{
    /// <summary>Decode a RIFF/WAVE file (PCM 8/16/24/32-bit or 32-bit float, any channel count) to 16-bit mono samples at its own rate.</summary>
    public static (byte[] Pcm16Mono, int SampleRate) DecodeWav(byte[] wav)
    {
        if (wav.Length < 12 || System.Text.Encoding.ASCII.GetString(wav, 0, 4) != "RIFF" || System.Text.Encoding.ASCII.GetString(wav, 8, 4) != "WAVE") throw new InvalidDataException("not a WAV file");
        int format = 0, channels = 0, rate = 0, bits = 0;
        var pos = 12;
        while (pos + 8 <= wav.Length)
        {
            var id = System.Text.Encoding.ASCII.GetString(wav, pos, 4);
            var size = BitConverter.ToInt32(wav, pos + 4);
            var start = pos + 8;
            if (id == "fmt " && size >= 16)
            {
                format = BitConverter.ToUInt16(wav, start);
                channels = BitConverter.ToUInt16(wav, start + 2);
                rate = BitConverter.ToInt32(wav, start + 4);
                bits = BitConverter.ToUInt16(wav, start + 14);
                if (format == 0xFFFE && size >= 40) format = BitConverter.ToUInt16(wav, start + 24); // WAVE_FORMAT_EXTENSIBLE: the sub-format GUID starts with the real tag
            }
            else if (id == "data")
            {
                if (channels <= 0 || rate <= 0) throw new InvalidDataException("data before fmt");
                var length = Math.Min(size < 0 ? wav.Length - start : size, wav.Length - start);
                return (ToMono16(wav.AsSpan(start, length), format, channels, bits), rate);
            }
            pos = start + size + (size & 1);
        }
        throw new InvalidDataException("no data chunk");
    }

    private static byte[] ToMono16(ReadOnlySpan<byte> data, int format, int channels, int bits)
    {
        var bytesPerSample = bits / 8;
        if (bytesPerSample == 0) throw new InvalidDataException("zero-bit samples");
        var frames = data.Length / (bytesPerSample * channels);
        var mono = new byte[frames * 2];
        for (var f = 0; f < frames; f++)
        {
            double sum = 0;
            for (var c = 0; c < channels; c++)
            {
                var at = (f * channels + c) * bytesPerSample;
                sum += format == 3 && bits == 32 ? BitConverter.ToSingle(data.Slice(at, 4))
                    : bits switch
                    {
                        8 => (data[at] - 128) / 128.0,
                        16 => BitConverter.ToInt16(data.Slice(at, 2)) / 32768.0,
                        24 => ((data[at] | data[at + 1] << 8 | (sbyte)data[at + 2] << 16)) / 8388608.0,
                        32 => BitConverter.ToInt32(data.Slice(at, 4)) / 2147483648.0,
                        _ => throw new InvalidDataException($"{bits}-bit samples"),
                    };
            }
            var v = (short)Math.Clamp(Math.Round(sum / channels * 32767.0), short.MinValue, short.MaxValue);
            mono[f * 2] = (byte)(v & 0xFF);
            mono[f * 2 + 1] = (byte)(v >> 8 & 0xFF);
        }
        return mono;
    }

    /// <summary>Linear resampling of 16-bit mono PCM; good enough for speech, and a no-op at the same rate.</summary>
    public static byte[] Resample(byte[] pcm16Mono, int fromRate, int toRate)
    {
        if (fromRate == toRate || fromRate <= 0 || toRate <= 0) return pcm16Mono;
        var inCount = pcm16Mono.Length / 2;
        if (inCount < 2) return pcm16Mono;
        var outCount = (int)((long)inCount * toRate / fromRate);
        var result = new byte[outCount * 2];
        var step = (double)fromRate / toRate;
        for (var i = 0; i < outCount; i++)
        {
            var src = i * step;
            var i0 = (int)src;
            var i1 = Math.Min(i0 + 1, inCount - 1);
            var t = src - i0;
            var a = BitConverter.ToInt16(pcm16Mono, i0 * 2);
            var b = BitConverter.ToInt16(pcm16Mono, i1 * 2);
            var v = (short)Math.Round(a + (b - a) * t);
            result[i * 2] = (byte)(v & 0xFF);
            result[i * 2 + 1] = (byte)(v >> 8 & 0xFF);
        }
        return result;
    }

    /// <summary>A provider's WAV as the cache's 16-bit mono PCM at <see cref="IVoiceSynth.SampleRate"/>.</summary>
    public static byte[] ToCachePcm(byte[] wav)
    {
        var (pcm, rate) = DecodeWav(wav);
        return Resample(pcm, rate, IVoiceSynth.SampleRate);
    }
}
