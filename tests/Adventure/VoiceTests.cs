using System.Text.Json.Nodes;
using DungeonCrawler.Adventure.Voice;
using Xunit;

namespace DungeonCrawler.Tests.Adventure;

public class VoiceTests
{
    [Fact]
    public void LineIds_AreStable_AndDistinct()
    {
        var a = VoiceLineId.For("Hello there.", "Charon", "grave");
        Assert.Equal(a, VoiceLineId.For("  Hello there. ", "Charon", "grave"));
        Assert.Equal(16, a.Length);
        Assert.NotEqual(a, VoiceLineId.For("Hello there.", "Puck", "grave"));
        Assert.NotEqual(a, VoiceLineId.For("Hello there.", "Charon", "cheerful"));
        Assert.NotEqual(a, VoiceLineId.For("Hello there!", "Charon", "grave"));
    }

    [Fact]
    public void Wav_RoundTrips()
    {
        var pcm = new byte[480];
        for (var i = 0; i < pcm.Length; i++) pcm[i] = (byte)(i * 7);
        var wav = WavWriter.Wrap(pcm);
        Assert.Equal(44 + pcm.Length, wav.Length);
        Assert.Equal("RIFF", System.Text.Encoding.ASCII.GetString(wav, 0, 4));
        Assert.Equal("WAVE", System.Text.Encoding.ASCII.GetString(wav, 8, 4));
        var (back, rate) = WavWriter.Unwrap(wav);
        Assert.Equal(IVoiceSynth.SampleRate, rate);
        Assert.Equal(pcm, back);
    }

    [Fact]
    public void GeminiWire_BuildsRequest_AndParsesAudioOrError()
    {
        var json = JsonNode.Parse(GeminiTtsWire.RequestJson("Go.", "Kore", "whisper"))!;
        Assert.Equal("whisper: Go.", json["contents"]![0]!["parts"]![0]!["text"]!.GetValue<string>());
        Assert.Equal("Kore", json["generationConfig"]!["speechConfig"]!["voiceConfig"]!["prebuiltVoiceConfig"]!["voiceName"]!.GetValue<string>());
        Assert.Equal("AUDIO", json["generationConfig"]!["responseModalities"]![0]!.GetValue<string>());
        Assert.Contains($"{Voices.Model}:generateContent", GeminiTtsWire.Endpoint(Voices.Model));

        var pcm = new byte[] { 1, 2, 3, 4 };
        var ok = GeminiTtsWire.ParseResponse("{\"candidates\":[{\"content\":{\"parts\":[{\"inlineData\":{\"mimeType\":\"audio/L16;codec=pcm;rate=24000\",\"data\":\"" + Convert.ToBase64String(pcm) + "\"}}]}}]}");
        Assert.Null(ok.Error);
        Assert.Equal(pcm, ok.Pcm);
        var err = GeminiTtsWire.ParseResponse("{\"error\":{\"code\":400,\"message\":\"API key not valid\"}}");
        Assert.Null(err.Pcm);
        Assert.Contains("API key", err.Error);
        Assert.NotNull(GeminiTtsWire.ParseResponse("garbage").Error);
        Assert.NotNull(GeminiTtsWire.ParseResponse("{\"candidates\":[]}").Error);
    }

    [Fact]
    public void RateLimit_Classification()
    {
        var daily = GeminiTtsWire.ClassifyRateLimit("{\"error\":{\"code\":429,\"message\":\"You exceeded your current quota\",\"details\":[{\"@type\":\"type.googleapis.com/google.rpc.QuotaFailure\",\"violations\":[{\"quotaMetric\":\"generativelanguage.googleapis.com/generate_content_free_tier_requests\",\"quotaId\":\"GenerateRequestsPerDayPerProjectPerModel-FreeTier\"}]},{\"@type\":\"type.googleapis.com/google.rpc.RetryInfo\",\"retryDelay\":\"37s\"}]}}");
        Assert.True(daily.DailyQuota);
        Assert.Equal(37f, daily.RetrySeconds);
        var minute = GeminiTtsWire.ClassifyRateLimit("{\"error\":{\"code\":429,\"message\":\"Resource has been exhausted\",\"details\":[{\"@type\":\"type.googleapis.com/google.rpc.QuotaFailure\",\"violations\":[{\"quotaId\":\"GenerateRequestsPerMinutePerProjectPerModel-FreeTier\"}]}]}}");
        Assert.False(minute.DailyQuota);
        Assert.Null(minute.RetrySeconds);
        Assert.False(GeminiTtsWire.ClassifyRateLimit("not json").DailyQuota);
    }

    [Fact]
    public void Queue_Clear_ForgetsWaitingLines()
    {
        var q = new VoiceQueue();
        q.Enqueue("A", "Kore", "x", VoicePriority.Npc, _ => false);
        q.Clear();
        Assert.Equal(0, q.Count);
        q.Enqueue("A", "Kore", "x", VoicePriority.Npc, _ => false);
        Assert.Equal(1, q.Count);
    }

    [Fact]
    public void Queue_Prioritises_Dedupes_AndPauses()
    {
        var q = new VoiceQueue();
        var bark = q.Enqueue("Down.", "Orus", Voices.Style(Speaker.Companion), VoicePriority.Bark, _ => false);
        var narr = q.Enqueue("The crypt swallows your light.", Voices.Narrator, Voices.Style(Speaker.Narrator), VoicePriority.Narrator, _ => false);
        q.Enqueue("Down.", "Orus", Voices.Style(Speaker.Companion), VoicePriority.Bark, _ => false); // duplicate
        var cached = q.Enqueue("Already there.", "Kore", "x", VoicePriority.Npc, _ => true);
        Assert.Equal(2, q.Count);
        Assert.Equal(16, cached.Length);
        q.Paused = true;
        Assert.Null(q.Next());
        q.Paused = false;
        Assert.Equal(narr, q.Next()!.Id);
        Assert.Equal(bark, q.Next()!.Id);
        Assert.Null(q.Next());
        q.Forget(bark);
        q.Enqueue("Down.", "Orus", Voices.Style(Speaker.Companion), VoicePriority.Bark, _ => false);
        Assert.Equal(1, q.Count);
    }

    [Fact]
    public void VoiceAssignment_IsDeterministic()
    {
        Assert.Equal(Voices.ForNpc("Old Teodor"), Voices.ForNpc("Old Teodor"));
        Assert.Contains(Voices.ForNpc("Mara"), Voices.Npcs);
        Assert.Equal("Orus", Voices.ForClass("class.warrior"));
        Assert.Equal("Puck", Voices.ForClass("class.unknown"));
    }
}

public class VoiceModelSplitTests
{
    [Xunit.Fact]
    public void MainVoicesGetTheMainModel_OthersTheMinorOne()
    {
        var main = DungeonCrawler.Adventure.Voice.Voices.Model;
        var minor = DungeonCrawler.Adventure.Voice.Voices.MinorModel;
        Xunit.Assert.NotEqual(main, minor);
        Xunit.Assert.Equal(main, DungeonCrawler.Adventure.Voice.Voices.ModelFor(DungeonCrawler.Adventure.Voice.Speaker.Narrator, false));
        Xunit.Assert.Equal(main, DungeonCrawler.Adventure.Voice.Voices.ModelFor(DungeonCrawler.Adventure.Voice.Speaker.Boss, false));
        Xunit.Assert.Equal(main, DungeonCrawler.Adventure.Voice.Voices.ModelFor(DungeonCrawler.Adventure.Voice.Speaker.Npc, true));
        Xunit.Assert.Equal(minor, DungeonCrawler.Adventure.Voice.Voices.ModelFor(DungeonCrawler.Adventure.Voice.Speaker.Npc, false));
        Xunit.Assert.Equal(minor, DungeonCrawler.Adventure.Voice.Voices.ModelFor(DungeonCrawler.Adventure.Voice.Speaker.Companion, true));
        var q = new DungeonCrawler.Adventure.Voice.VoiceQueue();
        var a = q.Enqueue("Hello.", "Kore", "s", DungeonCrawler.Adventure.Voice.VoicePriority.Npc, _ => false, main);
        var b = q.Enqueue("Hello.", "Kore", "s", DungeonCrawler.Adventure.Voice.VoicePriority.Npc, _ => false, minor);
        Xunit.Assert.NotEqual(a, b);
        Xunit.Assert.Equal(2, q.Count);
        Xunit.Assert.Equal(main, q.Next()!.Model);
    }
}
