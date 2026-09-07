using System.Net.Http;
using System.Text;
using DungeonCrawler.Adventure.Voice;

namespace DungeonCrawler.Adventure.AiClients;

/// <summary>Gemini text-to-speech over plain HTTPS. Returns raw 16-bit 24 kHz mono PCM, or null after logging why.</summary>
public sealed class GeminiTtsClient : IVoiceSynth, IVoiceSynthDiagnostics
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(60) };
    private readonly string _key;
    private readonly string _model;
    public string? LastError { get; private set; }
    public int LastStatus { get; private set; }
    /// <summary>Set after a 429 so the renderer can back off.</summary>
    public DateTime RateLimitedUntil { get; private set; } = DateTime.MinValue;
    /// <summary>The last 429 named a per-day quota: more requests today will only fail.</summary>
    public bool QuotaExhausted { get; private set; }
    /// <summary>Seconds Google asked us to wait after the last 429, when it said.</summary>
    public float? RetryAfterSeconds { get; private set; }

    public string Model => _model;

    public GeminiTtsClient(string key, string? model = null)
    {
        _key = key;
        _model = model ?? Voices.Model;
    }

    /// <summary>The text-to-speech models this key may call, newest first; empty with an error text when the request fails.</summary>
    public static async Task<(List<string> Models, string? Error)> ListTtsModelsAsync(string key, CancellationToken ct)
    {
        try
        {
            var models = new List<string>();
            string? page = null;
            for (var i = 0; i < 5; i++)
            {
                using var req = new HttpRequestMessage(HttpMethod.Get, "https://generativelanguage.googleapis.com/v1beta/models?pageSize=100" + (page != null ? "&pageToken=" + page : ""));
                req.Headers.Add("x-goog-api-key", key);
                using var res = await Http.SendAsync(req, ct);
                var body = await res.Content.ReadAsStringAsync(ct);
                if (!res.IsSuccessStatusCode) return (models, $"HTTP {(int)res.StatusCode}: {body[..Math.Min(200, body.Length)]}");
                using var doc = System.Text.Json.JsonDocument.Parse(body);
                if (doc.RootElement.TryGetProperty("models", out var arr))
                    foreach (var m in arr.EnumerateArray())
                    {
                        var name = m.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "";
                        name = name.StartsWith("models/") ? name["models/".Length..] : name;
                        var speaks = m.TryGetProperty("supportedGenerationMethods", out var g) && g.EnumerateArray().Any(x => x.GetString() == "generateContent");
                        if (name.Contains("tts", StringComparison.OrdinalIgnoreCase) && speaks) models.Add(name);
                    }
                page = doc.RootElement.TryGetProperty("nextPageToken", out var t) ? t.GetString() : null;
                if (string.IsNullOrEmpty(page)) break;
            }
            return (models.OrderByDescending(m => m).ToList(), null);
        }
        catch (Exception e) { return (new List<string>(), e.Message); }
    }

    public async Task<byte[]?> RenderPcmAsync(string text, string voice, string style, CancellationToken ct)
    {
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Post, GeminiTtsWire.Endpoint(_model));
            req.Headers.Add("x-goog-api-key", _key);
            req.Content = new StringContent(GeminiTtsWire.RequestJson(text, voice, style), Encoding.UTF8, "application/json");
            using var res = await Http.SendAsync(req, ct);
            var body = await res.Content.ReadAsStringAsync(ct);
            LastStatus = (int)res.StatusCode;
            if (LastStatus == 429)
            {
                var (daily, retry) = GeminiTtsWire.ClassifyRateLimit(body);
                QuotaExhausted = daily;
                RetryAfterSeconds = retry;
                RateLimitedUntil = DateTime.UtcNow.AddSeconds(retry ?? 20f);
            }
            if (!res.IsSuccessStatusCode)
            {
                var parsed = GeminiTtsWire.ParseResponse(body);
                LastError = $"HTTP {(int)res.StatusCode}: {parsed.Error ?? body[..Math.Min(200, body.Length)]}";
                return null;
            }
            var (pcm, error) = GeminiTtsWire.ParseResponse(body);
            LastError = error;
            return pcm;
        }
        catch (OperationCanceledException) { LastError = "cancelled"; return null; }
        catch (Exception e) { LastError = e.Message; return null; }
    }
}
