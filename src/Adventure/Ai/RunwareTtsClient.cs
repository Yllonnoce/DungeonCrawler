using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using DungeonCrawler.Adventure.Ai;
using DungeonCrawler.Adventure.Voice;

namespace DungeonCrawler.Adventure.AiClients;

/// <summary>
/// Text-to-speech through Runware's audioInference task (xAI, Inworld, MiniMax, Gemini TTS... behind one key).
/// Asks for WAV, decodes it to the cache's 16-bit 24 kHz mono PCM, or returns null after recording why.
/// </summary>
public sealed class RunwareTtsClient : IVoiceSynth, IVoiceSynthDiagnostics
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(120) };
    private readonly string _key;
    public string Model { get; }
    public string? LastError { get; private set; }
    public int LastStatus { get; private set; }
    /// <summary>A 402: the balance is gone, so every model on this key is done for now.</summary>
    public bool QuotaExhausted { get; private set; }
    public float? RetryAfterSeconds { get; private set; }
    /// <summary>Dollars Runware reported for the lines this client rendered.</summary>
    public double SpentUsd { get; private set; }

    public RunwareTtsClient(string key, string? model = null)
    {
        _key = key;
        Model = model ?? Voices.Model;
    }

    public async Task<byte[]?> RenderPcmAsync(string text, string voice, string style, CancellationToken ct)
    {
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Post, RunwareWire.Endpoint);
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _key);
            req.Content = new StringContent(RunwareVoices.RequestJson(Model, text, voice, style, Guid.NewGuid().ToString()), Encoding.UTF8, "application/json");
            using var res = await Http.SendAsync(req, ct);
            var body = await res.Content.ReadAsStringAsync(ct);
            LastStatus = (int)res.StatusCode;
            RetryAfterSeconds = res.Headers.RetryAfter?.Delta is { } delta ? (float)delta.TotalSeconds : null;
            QuotaExhausted = LastStatus == 402;
            if (!res.IsSuccessStatusCode)
            {
                LastError = $"{RunwareWire.DescribeStatus(LastStatus)}: {RunwareWire.ErrorOf(body) ?? body[..Math.Min(200, body.Length)]}";
                return null;
            }
            var (wav, cost, error) = RunwareVoices.ParseResponse(body);
            LastError = error;
            if (wav == null) return null;
            SpentUsd += cost;
            return PcmTools.ToCachePcm(wav);
        }
        catch (OperationCanceledException) { LastError = "cancelled"; return null; }
        catch (Exception e) { LastError = e.Message; return null; }
    }
}
