using System.Text.Json.Nodes;
using DungeonCrawler.Adventure.Ai;
using DungeonCrawler.Adventure.Voice;
using Xunit;

namespace DungeonCrawler.Tests.Adventure;

public class RunwareTests
{
    private static ClaudeRequest Request() => new("floor", "anthropic:claude@sonnet-4.6",
        new[] { new PromptBlock("You are a dungeon master.", true), new PromptBlock("Rules follow.", false) },
        "Plan floor 3.", "{\"type\":\"object\"}", "high", 12000);

    [Fact]
    public void TextRequest_IsOneTask_WithSystemPrompt_SchemaAsText_AndThinking()
    {
        var json = JsonNode.Parse(RunwareWire.TextRequestJson(Request(), "uuid-1"))!.AsArray();
        Assert.Single(json);
        var task = json[0]!;
        Assert.Equal("textInference", task["taskType"]!.GetValue<string>());
        Assert.Equal("uuid-1", task["taskUUID"]!.GetValue<string>());
        Assert.Equal("anthropic:claude@sonnet-4.6", task["model"]!.GetValue<string>());
        var messages = task["messages"]!.AsArray();
        Assert.Single(messages);
        Assert.Equal("user", messages[0]!["role"]!.GetValue<string>()); // Runware takes no system role
        var user = messages[0]!["content"]!.GetValue<string>();
        Assert.StartsWith("Plan floor 3.", user);
        Assert.Contains("{\"type\":\"object\"}", user);
        var settings = task["settings"]!;
        Assert.Equal("You are a dungeon master.\n\nRules follow.", settings["systemPrompt"]!.GetValue<string>());
        Assert.Equal(12000, settings["maxTokens"]!.GetValue<int>());
        Assert.Equal("off", settings["thinkingLevel"]!.GetValue<string>()); // Claude on Runware: any thinking level drafted the whole plan in thought and ran out of tokens
        Assert.True(task["includeCost"]!.GetValue<bool>());
        Assert.True(task["includeUsage"]!.GetValue<bool>());
        Assert.Null(task["jsonSchema"]);

        Assert.Equal("async", task["deliveryMethod"]!.GetValue<string>()); // a sync reply is cut off after about two minutes
        Assert.Null(JsonNode.Parse(RunwareWire.TextRequestJson(Request(), "uuid-3", delivery: "sync"))![0]!["deliveryMethod"]);
        var noThinking = JsonNode.Parse(RunwareWire.TextRequestJson(Request(), "uuid-2", thinking: false))![0]!["settings"]!;
        Assert.Null(noThinking["thinkingLevel"]);
        Assert.Equal("high", RunwareWire.ThinkingLevel("xhigh", "deepseek:v4@flash")); // a fixed level is a budget; "max" spent a whole 48k outline on thinking
        Assert.Equal("high", RunwareWire.ThinkingLevel("max", "google:gemini@3.5-flash"));
        Assert.Equal("medium", RunwareWire.ThinkingLevel("medium", "deepseek:v4@flash"));
        Assert.Equal("off", RunwareWire.ThinkingLevel("xhigh", "anthropic:claude@sonnet-4.6"));
        Assert.Equal("off", RunwareWire.ThinkingLevel("medium", "anthropic-claude-sonnet-4-6"));
        Assert.Equal("off", RunwareWire.ThinkingLevel("low", "anthropic:claude@sonnet-4.6"));
        Assert.Equal("low", RunwareWire.ThinkingLevel("LOW"));
    }

    [Fact]
    public void TextResponse_ParsesText_Usage_Cost_AndStopReasons()
    {
        var ok = RunwareWire.ParseTextResponse("{\"data\":[{\"taskType\":\"textInference\",\"taskUUID\":\"u\",\"text\":\"{\\\"Reply\\\":\\\"ok\\\"}\",\"finishReason\":\"stop\",\"usage\":{\"promptTokens\":120,\"completionTokens\":8,\"totalTokens\":128},\"cost\":0.00048}]}");
        Assert.True(ok.Ok);
        Assert.Equal("{\"Reply\":\"ok\"}", ok.Text);
        Assert.Equal(120, ok.Usage.InputTokens);
        Assert.Equal(8, ok.Usage.OutputTokens);
        Assert.Equal(0.00048, ok.Usage.CostUsd);
        Assert.Equal(0.00048, Pricing.Cost(ok.Usage), 9); // the reported price wins over list prices

        var truncated = RunwareWire.ParseTextResponse("{\"data\":[{\"taskType\":\"textInference\",\"text\":\"{\",\"finishReason\":\"length\"}]}");
        Assert.Equal("max_tokens", truncated.StopReason);
        var filtered = RunwareWire.ParseTextResponse("{\"data\":[{\"taskType\":\"textInference\",\"text\":\"\",\"finishReason\":\"content_filter\"}]}");
        Assert.Equal("refusal", filtered.StopReason);
        Assert.Equal("content_filter", filtered.RefusalCategory);

        var error = RunwareWire.ParseTextResponse("{\"errors\":[{\"code\":\"invalidApiKey\",\"message\":\"Invalid API key. Get one at https://runware.ai/signup\",\"parameter\":\"apiKey\",\"taskType\":\"authentication\"}]}");
        Assert.NotNull(error.Error);
        Assert.Contains("invalidApiKey", error.Error);
        Assert.NotNull(RunwareWire.ParseTextResponse("garbage").Error);
        Assert.NotNull(RunwareWire.ParseTextResponse("{\"data\":[]}").Error);
    }

    [Fact]
    public void AccountResponse_ReadsBalance_AsNumberOrObject()
    {
        // the live reply (2026-09-07): balance is a plain number beside the organisation and team details
        var (live, liveError) = RunwareWire.ParseAccountResponse("{\"data\":[{\"organizationUUID\":\"x\",\"organizationName\":\"ScenePlay\",\"AIRSource\":\"sceneplay\",\"balance\":22.23513,\"team\":[{\"name\":\"E\",\"roles\":[\"Owner\"]}],\"apiKeys\":[{\"name\":\"Playground\"}]}]}");
        Assert.Null(liveError);
        Assert.Equal(22.23513, live);
        var (balance, error) = RunwareWire.ParseAccountResponse("{\"data\":[{\"taskType\":\"accountManagement\",\"balance\":{\"amount\":2.5,\"freeBalance\":0.75,\"currency\":\"USD\"}}]}");
        Assert.Null(error);
        Assert.Equal(3.25, balance);
        Assert.NotNull(RunwareWire.ParseAccountResponse("{\"errors\":[{\"code\":\"invalidApiKey\",\"message\":\"bad\"}]}").Error);
        var req = JsonNode.Parse(RunwareWire.AccountRequestJson("u"))![0]!;
        Assert.Equal("accountManagement", req["taskType"]!.GetValue<string>());
        Assert.Equal("getDetails", req["operation"]!.GetValue<string>());
    }

    [Fact]
    public void AsyncPolling_Shapes()
    {
        var poll = JsonNode.Parse(RunwareWire.PollRequestJson("abc"))![0]!;
        Assert.Equal("getResponse", poll["taskType"]!.GetValue<string>());
        Assert.Equal("abc", poll["taskUUID"]!.GetValue<string>());
        // the async submit acknowledges with just the ids (seen live 2026-09-07)
        var (pending, _) = RunwareWire.ParsePoll("{\"data\":[{\"taskType\":\"textInference\",\"taskUUID\":\"abc\"}]}");
        Assert.True(pending);
        Assert.True(RunwareWire.ParsePoll("{\"data\":[{\"taskType\":\"textInference\",\"status\":\"processing\",\"progress\":47}]}").Pending);
        var (done, response) = RunwareWire.ParsePoll("{\"data\":[{\"taskUUID\":\"abc\",\"taskType\":\"textInference\",\"status\":\"success\",\"text\":\"{\\\"Reply\\\":\\\"ok\\\"}\",\"finishReason\":\"stop\",\"usage\":{\"promptTokens\":29,\"completionTokens\":8},\"cost\":0.000207}]}");
        Assert.False(done);
        Assert.True(response!.Ok);
        Assert.Equal("{\"Reply\":\"ok\"}", response.Text);
        Assert.Equal(0.000207, response.Usage.CostUsd);
        var (_, failed) = RunwareWire.ParsePoll("{\"data\":[{\"taskType\":\"textInference\",\"status\":\"error\",\"message\":\"Task processing timeout\"}]}");
        Assert.Contains("timeout", failed!.Error!);
        // a sync reply (no status) is finished when it carries text
        var (syncPending, sync) = RunwareWire.ParsePoll("{\"data\":[{\"taskType\":\"textInference\",\"text\":\"hi\",\"finishReason\":\"stop\"}]}");
        Assert.False(syncPending);
        Assert.Equal("hi", sync!.Text);
        Assert.NotNull(RunwareWire.ParsePoll("{\"errors\":[{\"code\":\"failedTaskTimeout\",\"message\":\"Task processing timeout.\"}]}").Response!.Error);
    }

    [Fact]
    public void LiveTextResponse_CarriesCacheCounts()
    {
        var r = RunwareWire.ParseTextResponse("{\"data\":[{\"taskUUID\":\"u\",\"taskType\":\"textInference\",\"text\":\"{\\\"Reply\\\":\\\"ok\\\"}\",\"finishReason\":\"stop\",\"usage\":{\"promptTokens\":29,\"completionTokens\":8,\"totalTokens\":37,\"promptTokensDetails\":{\"cachedTokens\":5,\"cacheCreationTokens\":2}},\"cost\":0.000207}]}");
        Assert.True(r.Ok);
        Assert.Equal(29, r.Usage.InputTokens);
        Assert.Equal(5, r.Usage.CacheReadTokens);
        Assert.Equal(2, r.Usage.CacheWriteTokens);
        Assert.Equal(0.000207, r.Usage.CostUsd);
    }

    [Fact]
    public void ModelSlugs_ConvertToAirIds_AndListParses()
    {
        Assert.Equal("anthropic:claude@sonnet-4.6", RunwareWire.ToAir("anthropic-claude-sonnet-4-6"));
        Assert.Equal("google:gemini@3.5-flash", RunwareWire.ToAir("google-gemini-3-5-flash"));
        Assert.Equal("deepseek:v4@flash", RunwareWire.ToAir("deepseek-v4-flash"));
        Assert.Equal("openai:gpt@5.4-mini", RunwareWire.ToAir("openai-gpt-5-4-mini"));
        Assert.Equal("anthropic:claude@fable-5", RunwareWire.ToAir("anthropic-claude-fable-5"));
        Assert.Equal("anthropic:claude@sonnet-4.6", RunwareWire.ToAir(" anthropic:claude@sonnet-4.6 "));
        Assert.Equal("xai:tts@0", RunwareWire.ToAir("xai:tts@0"));
        var (models, error) = RunwareWire.ParseModelList("{\"object\":\"list\",\"data\":[{\"id\":\"google-gemini-3-5-flash\",\"object\":\"model\"},{\"id\":\"anthropic-claude-sonnet-4-6\"}]}");
        Assert.Null(error);
        Assert.Equal(new[] { "anthropic-claude-sonnet-4-6", "google-gemini-3-5-flash" }, models);
        // a slug in the request becomes an AIR id on the wire
        var req = Request() with { Model = "anthropic-claude-sonnet-4-6" };
        Assert.Equal("anthropic:claude@sonnet-4.6", JsonNode.Parse(RunwareWire.TextRequestJson(req, "u"))![0]!["model"]!.GetValue<string>());
    }

    [Fact]
    public void Wav_WithStreamingHeader_Decodes()
    {
        // xAI's WAV (seen live 2026-09-07): RIFF size 0x80000023 and a data size of int.MaxValue, 48 kHz mono 16-bit
        var pcm = new byte[480 * 2];
        for (var i = 0; i < 480; i++) BitConverter.GetBytes((short)(i * 10)).CopyTo(pcm, i * 2);
        var wav = WavWriter.Wrap(pcm, 48000, 1);
        BitConverter.GetBytes(unchecked((int)0x80000023)).CopyTo(wav, 4);
        BitConverter.GetBytes(int.MaxValue).CopyTo(wav, 40);
        var (mono, rate) = PcmTools.DecodeWav(wav);
        Assert.Equal(48000, rate);
        Assert.Equal(pcm, mono);
        Assert.Equal(240 * 2, PcmTools.ToCachePcm(wav).Length);
    }

    [Fact]
    public void CostMeter_UsesReportedCost_WhenPresent()
    {
        var meter = new CostMeter(new SpendDto(), 3.0);
        meter.Record(new ClaudeUsage(1_000_000, 0, 0, 0, 0.10));
        Assert.Equal(0.10, meter.Spend.Usd, 9);
        meter.Record(new ClaudeUsage(1_000_000, 0, 0, 0));
        Assert.Equal(0.10 + Pricing.InputPerMillion, meter.Spend.Usd, 9);
    }

    [Fact]
    public void Voices_MapPerFamily_AndPassThroughForGemini()
    {
        Assert.Equal("orion", RunwareVoices.Map("xai:tts@0", Voices.Narrator));
        Assert.Equal("zagan", RunwareVoices.Map("xai:tts@0", Voices.Boss));
        Assert.Equal("Mortimer", RunwareVoices.Map("inworld:tts@2", Voices.Narrator));
        Assert.Equal("English_expressive_narrator", RunwareVoices.Map("minimax:speech@2.8", Voices.Narrator));
        Assert.Equal(Voices.Narrator, RunwareVoices.Map("google:gemini@3.1-flash-tts", Voices.Narrator));
        Assert.Equal("Kore", RunwareVoices.Map("someone:else@1", "Kore"));
        // every NPC and class voice lands on a real name of the family, deterministically
        foreach (var model in new[] { "xai:tts@0", "inworld:tts@2", "minimax:speech@2.8" })
        {
            foreach (var npc in Voices.Npcs) { Assert.NotEqual(npc, RunwareVoices.Map(model, npc)); Assert.Equal(RunwareVoices.Map(model, npc), RunwareVoices.Map(model, npc)); }
            foreach (var cls in Voices.Classes.Values) Assert.NotEqual(cls, RunwareVoices.Map(model, cls));
        }
        Assert.Equal("atlas", RunwareVoices.Map("xai:tts@0", Voices.ForClass("class.warrior")));
        Assert.Equal("en", RunwareVoices.Language("xai:tts@0"));
        Assert.Equal("en-US", RunwareVoices.Language("google:gemini@3.1-flash-tts"));
    }

    [Fact]
    public void AudioRequest_AsksForWav_AndOnlyGeminiHearsTheStyle()
    {
        var xai = JsonNode.Parse(RunwareVoices.RequestJson("xai:tts@0", "Go.", Voices.Narrator, "Narrate gravely", "u1"))![0]!;
        Assert.Equal("audioInference", xai["taskType"]!.GetValue<string>());
        Assert.Equal("xai:tts@0", xai["model"]!.GetValue<string>());
        Assert.Equal("Go.", xai["speech"]!["text"]!.GetValue<string>());
        Assert.Equal("orion", xai["speech"]!["voice"]!.GetValue<string>());
        Assert.Equal("WAV", xai["outputFormat"]!.GetValue<string>());
        Assert.Equal("base64Data", xai["outputType"]!.GetValue<string>());
        Assert.True(xai["includeCost"]!.GetValue<bool>());

        var gemini = JsonNode.Parse(RunwareVoices.RequestJson("google:gemini@3.1-flash-tts", "Go.", Voices.Narrator, "Narrate gravely", "u2"))![0]!;
        Assert.Equal("Narrate gravely: Go.", gemini["speech"]!["text"]!.GetValue<string>());
        Assert.Equal(Voices.Narrator, gemini["speech"]!["voice"]!.GetValue<string>());
    }

    [Fact]
    public void AudioResponse_ReturnsWavBytes_OrError()
    {
        var wav = WavWriter.Wrap(new byte[] { 1, 0, 2, 0 });
        var (bytes, cost, error) = RunwareVoices.ParseResponse("{\"data\":[{\"taskType\":\"audioInference\",\"taskUUID\":\"u\",\"audioBase64Data\":\"" + Convert.ToBase64String(wav) + "\",\"cost\":0.0042}]}");
        Assert.Null(error);
        Assert.Equal(wav, bytes);
        Assert.Equal(0.0042, cost);
        Assert.NotNull(RunwareVoices.ParseResponse("{\"data\":[{\"taskType\":\"audioInference\",\"audioURL\":\"https://x\"}]}").Error);
        Assert.Contains("insufficient", RunwareVoices.ParseResponse("{\"errors\":[{\"code\":\"insufficientCredits\",\"message\":\"insufficient credits\"}]}").Error!);
        Assert.NotNull(RunwareVoices.ParseResponse("nope").Error);
    }

    [Fact]
    public void Wav_DecodesStereo_OtherRates_AndBitDepths_ToMono24k()
    {
        // 48 kHz stereo 16-bit: 480 frames = 10 ms, left = +1000, right = -1000 → mono 0
        var stereo = new byte[480 * 4];
        for (var i = 0; i < 480; i++)
        {
            BitConverter.GetBytes((short)1000).CopyTo(stereo, i * 4);
            BitConverter.GetBytes((short)-1000).CopyTo(stereo, i * 4 + 2);
        }
        var wav = WavWriter.Wrap(stereo, 48000, 2);
        var (mono, rate) = PcmTools.DecodeWav(wav);
        Assert.Equal(48000, rate);
        Assert.Equal(480 * 2, mono.Length);
        Assert.All(Enumerable.Range(0, 480), i => Assert.Equal(0, BitConverter.ToInt16(mono, i * 2)));
        var cache = PcmTools.ToCachePcm(wav);
        Assert.Equal(240 * 2, cache.Length); // 10 ms at 24 kHz

        // 24-bit mono at 22050: full scale positive
        var pcm24 = new byte[10 * 3];
        for (var i = 0; i < 10; i++) { pcm24[i * 3] = 0xFF; pcm24[i * 3 + 1] = 0xFF; pcm24[i * 3 + 2] = 0x7F; }
        var wav24 = Wav(pcm24, 22050, 1, 24, 1);
        var (m24, r24) = PcmTools.DecodeWav(wav24);
        Assert.Equal(22050, r24);
        Assert.Equal(32767, BitConverter.ToInt16(m24, 0));

        // float32 mono: -0.5
        var f32 = new byte[4 * 4];
        for (var i = 0; i < 4; i++) BitConverter.GetBytes(-0.5f).CopyTo(f32, i * 4);
        var (mf, _) = PcmTools.DecodeWav(Wav(f32, 24000, 1, 32, 3));
        Assert.InRange(BitConverter.ToInt16(mf, 0), -16400, -16300);

        // resampling keeps duration and a constant signal
        var flat = new byte[1000 * 2];
        for (var i = 0; i < 1000; i++) BitConverter.GetBytes((short)500).CopyTo(flat, i * 2);
        var up = PcmTools.Resample(flat, 16000, 24000);
        Assert.Equal(1500 * 2, up.Length);
        Assert.All(Enumerable.Range(0, 1500), i => Assert.Equal(500, BitConverter.ToInt16(up, i * 2)));
        Assert.Same(flat, PcmTools.Resample(flat, 24000, 24000));
        Assert.Throws<InvalidDataException>(() => PcmTools.DecodeWav(new byte[] { 1, 2, 3 }));
    }

    /// <summary>A WAV with an arbitrary format tag and bit depth (WavWriter only writes 16-bit PCM).</summary>
    private static byte[] Wav(byte[] data, int rate, int channels, int bits, int formatTag)
    {
        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);
        w.Write("RIFF"u8); w.Write(36 + data.Length); w.Write("WAVE"u8);
        w.Write("fmt "u8); w.Write(16); w.Write((short)formatTag); w.Write((short)channels); w.Write(rate);
        w.Write(rate * channels * bits / 8); w.Write((short)(channels * bits / 8)); w.Write((short)bits);
        w.Write("data"u8); w.Write(data.Length); w.Write(data);
        w.Flush();
        return ms.ToArray();
    }

    [Fact]
    public void ParseModelCatalogue_PricesDearestFirst()
    {
        var body = "{\"object\":\"list\",\"data\":[" +
            "{\"id\":\"deepseek-v4-flash\",\"name\":\"DeepSeek V4 Flash\",\"pricing\":{\"prompt\":\"0.00000008\",\"completion\":\"0.00000015\"}}," +
            "{\"id\":\"anthropic-claude-sonnet-4-6\",\"name\":\"Claude Sonnet 4.6\",\"pricing\":{\"prompt\":\"0.000003\",\"completion\":\"0.000015\"}}," +
            "{\"id\":\"mystery-model-1\",\"name\":\"Mystery\"}," +
            "{\"id\":\"anthropic-claude-haiku-4-5\",\"name\":\"Claude Haiku 4.5\",\"pricing\":{\"prompt\":\"0.000001\",\"completion\":\"0.000005\"}}]}";
        var (models, error) = RunwareWire.ParseModelCatalogue(body);
        Assert.Null(error);
        Assert.Equal(new[] { "anthropic-claude-sonnet-4-6", "anthropic-claude-haiku-4-5", "deepseek-v4-flash", "mystery-model-1" }, models.Select(m => m.Slug));
        Assert.Equal(15, models[0].OutputPerMillion, 6);
        Assert.Equal(3, models[0].InputPerMillion, 6);
        Assert.Equal("anthropic:claude@sonnet-4.6", models[0].Air);
        Assert.Contains("$3.00 in / $15.00 out", models[0].Label);
        Assert.False(models[3].Priced);
        Assert.Contains("not listed", models[3].Label);
        var (none, err) = RunwareWire.ParseModelCatalogue("{\"error\":\"bad key\"}");
        Assert.Empty(none);
        Assert.NotNull(err);
    }

    [Fact]
    public void ShippedCatalogues_AreDearestFirst()
    {
        var text = RunwareCatalog.TextModels;
        for (var i = 1; i < text.Count; i++) Assert.True(text[i - 1].OutputPerMillion >= text[i].OutputPerMillion, $"{text[i - 1].Slug} before {text[i].Slug}");
        Assert.Contains(text, m => m.Air == RunwareWire.DefaultTextModel);
        var tts = RunwareVoices.KnownModels;
        var priced = tts.Where(m => m.PerThousandChars > 0).Select(m => m.PerThousandChars).ToList();
        Assert.Equal(priced.OrderByDescending(p => p), priced);
        Assert.Equal("xai:tts@0", tts[^1].Model); // the cheapest is the default
        foreach (var m in tts.Where(m => !m.Model.StartsWith("google"))) Assert.NotEqual(Voices.Narrator, RunwareVoices.Map(m.Model, Voices.Narrator)); // every listed model has a voice bank
    }

    [Fact]
    public void ChatRequest_CarriesSystemUserAndSchema()
    {
        var req = new ClaudeRequest("ping", "deepseek-v4-pro", new[] { new PromptBlock("Be terse.", false) }, "Say ok.", "{\"type\":\"object\"}", "low", 256);
        var json = JsonNode.Parse(RunwareWire.ChatRequestJson(req))!;
        Assert.Equal("deepseek:v4@pro", json["model"]!.GetValue<string>());
        Assert.Equal(256, json["max_tokens"]!.GetValue<int>());
        var messages = json["messages"]!.AsArray();
        Assert.Equal("system", messages[0]!["role"]!.GetValue<string>());
        Assert.Equal("Be terse.", messages[0]!["content"]!.GetValue<string>());
        Assert.Equal("user", messages[1]!["role"]!.GetValue<string>());
        Assert.Contains("Say ok.", messages[1]!["content"]!.GetValue<string>());
        Assert.Contains("{\"type\":\"object\"}", messages[1]!["content"]!.GetValue<string>());
    }

    [Fact]
    public void ChatResponse_ParsesTextUsageAndCost()
    {
        var body = "{\"choices\":[{\"message\":{\"role\":\"assistant\",\"content\":\"{\\\"Reply\\\":\\\"ok\\\"}\",\"reasoning\":\"thinking\"},\"finish_reason\":\"stop\"}]," +
            "\"usage\":{\"prompt_tokens\":102,\"completion_tokens\":25,\"prompt_tokens_details\":{\"cached_tokens\":2,\"created_cache_tokens\":3}}}";
        var r = RunwareWire.ParseChatResponse(body, (1.0, 2.0));
        Assert.True(r.Ok);
        Assert.Equal("{\"Reply\":\"ok\"}", r.Text);
        Assert.Equal(102, r.Usage.InputTokens);
        Assert.Equal(25, r.Usage.OutputTokens);
        Assert.Equal(2, r.Usage.CacheReadTokens);
        Assert.Equal(3, r.Usage.CacheWriteTokens);
        Assert.Equal((102 * 1.0 + 25 * 2.0) / 1_000_000, r.Usage.CostUsd!.Value, 12);
        Assert.Null(RunwareWire.ParseChatResponse(body).Usage.CostUsd);
        var cut = RunwareWire.ParseChatResponse("{\"choices\":[{\"message\":{\"role\":\"assistant\",\"reasoning_content\":\"We are asked\"},\"finish_reason\":\"length\"}],\"usage\":{\"prompt_tokens\":5,\"completion_tokens\":8}}");
        Assert.Equal("max_tokens", cut.StopReason);
        Assert.Equal("", cut.Text);
        var err = RunwareWire.ParseChatResponse("{\"error\":{\"message\":\"Invalid model\",\"type\":\"invalid_request_error\"}}");
        Assert.NotNull(err.Error);
    }

    [Fact]
    public void Catalogue_PricesByAirOrSlug()
    {
        RunwareCatalog.Live = null;
        Assert.Equal((3.0, 15.0), RunwareCatalog.PriceFor("anthropic:claude@sonnet-4.6"));
        Assert.Equal((0.96, 1.92), RunwareCatalog.PriceFor("deepseek-v4-pro"));
        Assert.Null(RunwareCatalog.PriceFor("nobody:knows@this"));
        RunwareCatalog.Live = new[] { new RunwareTextModel("x-y-1", "XY", 7, 9) };
        Assert.Equal((7.0, 9.0), RunwareCatalog.PriceFor("x:y@1"));
        Assert.Null(RunwareCatalog.PriceFor("anthropic:claude@sonnet-4.6"));
        RunwareCatalog.Live = null;
    }
}
