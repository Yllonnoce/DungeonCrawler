using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using DungeonCrawler.Adventure.Ai;

namespace DungeonCrawler.Adventure.AiClients;

/// <summary>
/// <see cref="IClaudeClient"/> over Runware's textInference task, so the planner can run on any model Runware routes
/// (Claude, Gemini, GPT, DeepSeek...) with one key. The schema always goes as text; the cost comes back from Runware
/// itself. Tasks are submitted with async delivery and polled: a synchronous reply is cut off after about two
/// minutes ("failedTaskTimeout"), and a campaign outline with a large thinking budget runs longer than that.
/// Errors become failed responses, never exceptions.
/// </summary>
public sealed class RunwareTextClient : IClaudeClient
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromMinutes(10) };
    /// <summary>The chat endpoint answers in one synchronous reply, so a long plan needs a long wait.</summary>
    private static readonly HttpClient ChatHttp = new() { Timeout = TimeSpan.FromMinutes(25) };
    /// <summary>Models the task endpoint refused with invalidModel this session: they go straight to the chat endpoint from then on.</summary>
    public static readonly HashSet<string> ChatOnlyModels = new(StringComparer.OrdinalIgnoreCase);
    private readonly string _key;
    /// <summary>Set after a model rejected thinkingLevel; later calls leave it out.</summary>
    private bool _noThinking;
    /// <summary>Set after a model rejected async delivery; later calls go synchronously.</summary>
    private bool _syncOnly;
    public int LastStatus { get; private set; }
    /// <summary>How long to keep polling an async task before giving up.</summary>
    public TimeSpan PollTimeout { get; set; } = TimeSpan.FromMinutes(20);
    /// <summary>Progress lines (task submitted, still waiting after N seconds) for the console and the bake status.</summary>
    public Action<string>? Log { get; set; }
    /// <summary>Polls made for the last call (for logs and tests).</summary>
    public int LastPolls { get; private set; }

    public RunwareTextClient(string apiKey) => _key = apiKey;

    public async Task<ClaudeResponse> CreateAsync(ClaudeRequest request, CancellationToken ct)
    {
        try
        {
            if (ChatOnlyModels.Contains(RunwareWire.ToAir(request.Model))) return await CreateChatAsync(request, ct);
            Log?.Invoke($"{request.Kind}: asking Runware's task endpoint for {RunwareWire.ToAir(request.Model)}");
            var response = await CreateOnceAsync(request, !_noThinking, ct);
            if (response.Error != null) Log?.Invoke($"{request.Kind}: task endpoint answered: {response.Error}");
            if (response.Error != null && !_noThinking && response.Error.Contains("thinking", StringComparison.OrdinalIgnoreCase))
            {
                _noThinking = true;
                response = await CreateOnceAsync(request, thinking: false, ct);
                if (response.Error != null) Log?.Invoke($"{request.Kind}: without thinkingLevel the task endpoint answered: {response.Error}");
            }
            if (response.Error != null && !_syncOnly && response.Error.Contains("deliveryMethod", StringComparison.OrdinalIgnoreCase))
            {
                _syncOnly = true;
                response = await CreateOnceAsync(request, !_noThinking, ct);
                if (response.Error != null) Log?.Invoke($"{request.Kind}: synchronously the task endpoint answered: {response.Error}");
            }
            // last, since a model the task endpoint does not know may first complain about the thinking level: the
            // task endpoint routes fewer models than the catalogue, the chat endpoint takes them all
            if (response.Error != null && response.Error.Contains("invalidModel", StringComparison.OrdinalIgnoreCase))
            {
                ChatOnlyModels.Add(RunwareWire.ToAir(request.Model));
                Log?.Invoke($"{request.Kind}: Runware's task endpoint does not route {RunwareWire.ToAir(request.Model)}; using its chat endpoint instead");
                return await CreateChatAsync(request, ct);
            }
            return response;
        }
        catch (OperationCanceledException) { return ClaudeResponse.Failed("cancelled"); }
        catch (Exception e) { return ClaudeResponse.Failed(e.GetType().Name + ": " + e.Message); }
    }

    private async Task<ClaudeResponse> CreateOnceAsync(ClaudeRequest request, bool thinking, CancellationToken ct)
    {
        LastPolls = 0;
        var uuid = Guid.NewGuid().ToString();
        var delivery = _syncOnly ? "sync" : "async";
        var (status, body) = await PostAsync(RunwareWire.TextRequestJson(request, uuid, thinking, delivery), ct);
        LastStatus = status;
        if (status < 200 || status >= 300)
            return ClaudeResponse.Failed($"{RunwareWire.DescribeStatus(status)}: {RunwareWire.ErrorOf(body) ?? body[..Math.Min(200, body.Length)]}");
        if (delivery == "sync") return RunwareWire.ParseTextResponse(body);
        var (pending, response) = RunwareWire.ParsePoll(body);
        if (!pending) return response!;
        Log?.Invoke($"{request.Kind}: submitted to Runware as task {uuid[..8]} ({RunwareWire.ToAir(request.Model)}, up to {request.MaxTokens} tokens); polling");
        // poll with a gentle backoff: the docs ask for one or two seconds to start, growing from there
        var started = DateTime.UtcNow;
        var delay = 2.0;
        var lastNote = started;
        while (DateTime.UtcNow - started < PollTimeout)
        {
            await Task.Delay(TimeSpan.FromSeconds(delay), ct);
            delay = Math.Min(delay * 1.5, 10.0);
            LastPolls++;
            if (DateTime.UtcNow - lastNote > TimeSpan.FromSeconds(45))
            {
                lastNote = DateTime.UtcNow;
                Log?.Invoke($"{request.Kind}: still waiting on Runware task {uuid[..8]} ({(DateTime.UtcNow - started).TotalSeconds:0} s, {LastPolls} polls; giving up after {PollTimeout.TotalMinutes:0} min)");
            }
            (status, body) = await PostAsync(RunwareWire.PollRequestJson(uuid), ct);
            LastStatus = status;
            if (status < 200 || status >= 300)
            {
                if (status is 429 or 503 or 504) { Log?.Invoke($"{request.Kind}: poll {LastPolls} answered {RunwareWire.DescribeStatus(status)}; the task is still queued"); continue; } // the poll itself was throttled; the task is still running
                return ClaudeResponse.Failed($"{RunwareWire.DescribeStatus(status)} while polling: {RunwareWire.ErrorOf(body) ?? body[..Math.Min(200, body.Length)]}");
            }
            (pending, response) = RunwareWire.ParsePoll(body);
            if (!pending) return response!;
        }
        return ClaudeResponse.Failed($"no result from Runware after {PollTimeout.TotalMinutes:0} minutes of polling ({request.Kind})");
    }

    /// <summary>The same request through the OpenAI-compatible endpoint, one synchronous reply, priced from the catalogue.</summary>
    private async Task<ClaudeResponse> CreateChatAsync(ClaudeRequest request, CancellationToken ct)
    {
        LastPolls = 0;
        using var req = new HttpRequestMessage(HttpMethod.Post, RunwareWire.ChatEndpoint);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _key);
        req.Content = new StringContent(RunwareWire.ChatRequestJson(request), Encoding.UTF8, "application/json");
        using var res = await ChatHttp.SendAsync(req, ct);
        var body = await res.Content.ReadAsStringAsync(ct);
        LastStatus = (int)res.StatusCode;
        if (!res.IsSuccessStatusCode)
            return ClaudeResponse.Failed($"{RunwareWire.DescribeStatus(LastStatus)}: {RunwareWire.ErrorOf(body) ?? body[..Math.Min(200, body.Length)]}");
        return RunwareWire.ParseChatResponse(body, RunwareCatalog.PriceFor(request.Model));
    }

    private async Task<(int Status, string Body)> PostAsync(string json, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, RunwareWire.Endpoint);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _key);
        req.Content = new StringContent(json, Encoding.UTF8, "application/json");
        using var res = await Http.SendAsync(req, ct);
        return ((int)res.StatusCode, await res.Content.ReadAsStringAsync(ct));
    }

    /// <summary>The text models this key may use, as Runware's slugs (the field accepts those or AIR ids); empty with an error text when the list cannot be fetched.</summary>
    public static async Task<(List<string> Models, string? Error)> ListTextModelsAsync(string key, CancellationToken ct)
    {
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, RunwareWire.ModelsEndpoint);
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);
            using var res = await Http.SendAsync(req, ct);
            var body = await res.Content.ReadAsStringAsync(ct);
            if (!res.IsSuccessStatusCode) return (new List<string>(), $"{RunwareWire.DescribeStatus((int)res.StatusCode)}: {RunwareWire.ErrorOf(body) ?? body[..Math.Min(200, body.Length)]}");
            return RunwareWire.ParseModelList(body);
        }
        catch (Exception e) { return (new List<string>(), e.Message); }
    }

    /// <summary>The text models this key may use with their prices, dearest first; empty with an error text when the list cannot be fetched.</summary>
    public static async Task<(List<RunwareTextModel> Models, string? Error)> ListTextCatalogueAsync(string key, CancellationToken ct)
    {
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, RunwareWire.ModelsEndpoint);
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);
            using var res = await Http.SendAsync(req, ct);
            var body = await res.Content.ReadAsStringAsync(ct);
            if (!res.IsSuccessStatusCode) return (new List<RunwareTextModel>(), $"{RunwareWire.DescribeStatus((int)res.StatusCode)}: {RunwareWire.ErrorOf(body) ?? body[..Math.Min(200, body.Length)]}");
            return RunwareWire.ParseModelCatalogue(body);
        }
        catch (Exception e) { return (new List<RunwareTextModel>(), e.Message); }
    }

    /// <summary>Check a key without running a model: the account balance, or an error text.</summary>
    public static async Task<(double? Balance, string? Error)> CheckKeyAsync(string key, CancellationToken ct)
    {
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Post, RunwareWire.Endpoint);
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);
            req.Content = new StringContent(RunwareWire.AccountRequestJson(Guid.NewGuid().ToString()), Encoding.UTF8, "application/json");
            using var res = await Http.SendAsync(req, ct);
            var body = await res.Content.ReadAsStringAsync(ct);
            if (!res.IsSuccessStatusCode) return (null, $"{RunwareWire.DescribeStatus((int)res.StatusCode)}: {RunwareWire.ErrorOf(body) ?? body[..Math.Min(200, body.Length)]}");
            return RunwareWire.ParseAccountResponse(body);
        }
        catch (Exception e) { return (null, e.Message); }
    }
}
