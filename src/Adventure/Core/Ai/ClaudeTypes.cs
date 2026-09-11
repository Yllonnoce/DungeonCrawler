using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace DungeonCrawler.Adventure.Ai;

/// <summary>One system-prompt block; cached blocks form the stable prefix and must come first.</summary>
public sealed record PromptBlock(string Text, bool Cache);

/// <summary>Everything one planning call needs, independent of any SDK. Kind names the call for logs and fixtures.</summary>
public sealed record ClaudeRequest(string Kind, string Model, IReadOnlyList<PromptBlock> System, string User, string SchemaJson, string Effort, int MaxTokens)
{
    /// <summary>Stable digest of the request content; replay fixtures are keyed by it.</summary>
    public string Hash
    {
        get
        {
            var sb = new StringBuilder();
            sb.Append(Model).Append('\n').Append(Effort).Append('\n').Append(MaxTokens).Append('\n');
            foreach (var b in System) sb.Append(b.Text).Append('\n');
            sb.Append(User).Append('\n').Append(SchemaJson);
            return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(sb.ToString())))[..16].ToLowerInvariant();
        }
    }
    public int ApproxInputTokens => (System.Sum(b => b.Text.Length) + User.Length + SchemaJson.Length) / 4;
}

/// <summary>Token counts of one call; <paramref name="CostUsd"/> is set when the provider reports the price itself (Runware does), else the list prices apply.</summary>
public sealed record ClaudeUsage(long InputTokens, long OutputTokens, long CacheReadTokens, long CacheWriteTokens, double? CostUsd = null)
{
    public static readonly ClaudeUsage Zero = new(0, 0, 0, 0);
}

public sealed record ClaudeResponse(string Text, string StopReason, ClaudeUsage Usage, string? RefusalCategory = null, string? Error = null)
{
    public bool Ok => Error == null && StopReason is "end_turn" or "stop_sequence";
    public static ClaudeResponse Failed(string error) => new("", "error", ClaudeUsage.Zero, null, error);
}

public interface IClaudeClient
{
    Task<ClaudeResponse> CreateAsync(ClaudeRequest request, CancellationToken ct);
}

/// <summary>Test double: answers from a handler and remembers every request.</summary>
public sealed class FakeClaudeClient : IClaudeClient
{
    private readonly Func<ClaudeRequest, ClaudeResponse> _handler;
    public List<ClaudeRequest> Requests { get; } = new();
    public FakeClaudeClient(Func<ClaudeRequest, ClaudeResponse> handler) => _handler = handler;
    public Task<ClaudeResponse> CreateAsync(ClaudeRequest request, CancellationToken ct)
    {
        Requests.Add(request);
        return Task.FromResult(_handler(request));
    }
}

/// <summary>Answers from recorded fixtures (<c>{dir}/{hash}.response.json</c>); a missing fixture is a failed call.</summary>
/// <summary>
/// Answers planning calls from files. A recording is looked up by its content hash (what <see cref="RecordingClaudeClient"/>
/// wrote) and then by name: outline, floor-N (N read from the prompt), barks. A call with no recording is written out as
/// NAME.request.json so an answer can be authored by hand, and NAME is left in <see cref="Pending"/> for a bake to notice.
/// </summary>
public sealed class ReplayClaudeClient : IClaudeClient
{
    private readonly string _dir;
    public ReplayClaudeClient(string dir) => _dir = dir;
    public static string ResponsePath(string dir, ClaudeRequest r) => Path.Combine(dir, $"{r.Hash}.response.json");
    public static string RequestPath(string dir, ClaudeRequest r) => Path.Combine(dir, $"{r.Hash}.request.json");
    /// <summary>The name of the last call that had no recording, or null.</summary>
    public static string? Pending { get; private set; }

    /// <summary>outline, floor-N, barks: the name a hand-written recording goes under.</summary>
    public static string NameOf(ClaudeRequest r)
    {
        if (!r.Kind.StartsWith("floor")) return r.Kind;
        var m = System.Text.RegularExpressions.Regex.Match(r.User, @"Floor (\d+) menu:");
        return m.Success ? $"floor-{m.Groups[1].Value}" : r.Kind;
    }

    public Task<ClaudeResponse> CreateAsync(ClaudeRequest request, CancellationToken ct)
    {
        var name = NameOf(request);
        var path = ResponsePath(_dir, request);
        if (!File.Exists(path)) path = Path.Combine(_dir, $"{name}.response.json");
        if (!File.Exists(path))
        {
            try { Directory.CreateDirectory(_dir); File.WriteAllText(Path.Combine(_dir, $"{name}.request.json"), JsonSerializer.Serialize(request, PlanFormat.Options)); } catch { }
            Pending = name;
            return Task.FromResult(ClaudeResponse.Failed($"no recording for {name} ({request.Hash})"));
        }
        try
        {
            var r = JsonSerializer.Deserialize<ClaudeResponse>(File.ReadAllText(path), PlanFormat.Options);
            return Task.FromResult(r ?? ClaudeResponse.Failed("empty recording"));
        }
        catch (Exception e) { return Task.FromResult(ClaudeResponse.Failed($"bad recording: {e.Message}")); }
    }
}

/// <summary>Wraps a client and writes every request and response to a directory, so runs can be replayed and inspected.</summary>
public sealed class RecordingClaudeClient : IClaudeClient
{
    private readonly IClaudeClient _inner;
    private readonly string _dir;
    public RecordingClaudeClient(IClaudeClient inner, string dir) { _inner = inner; _dir = dir; }

    public async Task<ClaudeResponse> CreateAsync(ClaudeRequest request, CancellationToken ct)
    {
        Directory.CreateDirectory(_dir);
        var stamp = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss");
        try { File.WriteAllText(ReplayClaudeClient.RequestPath(_dir, request), JsonSerializer.Serialize(request, PlanFormat.Options)); } catch { }
        var response = await _inner.CreateAsync(request, ct);
        try
        {
            File.WriteAllText(ReplayClaudeClient.ResponsePath(_dir, request), JsonSerializer.Serialize(response, PlanFormat.Options));
            File.AppendAllText(Path.Combine(_dir, "log.txt"), $"{stamp} {request.Kind} {request.Hash} stop={response.StopReason} in={response.Usage.InputTokens} out={response.Usage.OutputTokens} cacheRead={response.Usage.CacheReadTokens} cacheWrite={response.Usage.CacheWriteTokens}{(response.Error != null ? " error=" + response.Error : "")}\n");
        }
        catch { }
        return response;
    }
}
