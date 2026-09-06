using System.Text.Json;
using Anthropic;
using Anthropic.Models.Messages;
using DungeonCrawler.Adventure.Ai;

namespace DungeonCrawler.Adventure.AiClients;

/// <summary>
/// <see cref="IClaudeClient"/> over the official Anthropic C# SDK: adaptive thinking, structured JSON output against
/// the request's schema, and prompt caching on the system blocks. Errors become failed responses, never exceptions.
/// </summary>
public sealed class ClaudeSdkClient : IClaudeClient
{
    private readonly AnthropicClient _client;

    public ClaudeSdkClient(string apiKey)
    {
        _client = new AnthropicClient { ApiKey = apiKey, Timeout = TimeSpan.FromMinutes(10), MaxRetries = 2 };
    }

    /// <summary>
    /// Plan schemas are large; the API compiles a structured-output schema into a grammar and refuses ones past its
    /// size limit. When that happens the schema is sent as text instead and the reply is parsed leniently.
    /// </summary>
    private bool _schemaAsText;
    public bool SchemaAsText => _schemaAsText;

    public async Task<ClaudeResponse> CreateAsync(ClaudeRequest request, CancellationToken ct)
    {
        try
        {
            return await CreateOnceAsync(request, _schemaAsText, ct);
        }
        catch (OperationCanceledException) { return ClaudeResponse.Failed("cancelled"); }
        catch (Exception e) when (!_schemaAsText && IsSchemaTooLarge(e.Message))
        {
            _schemaAsText = true;
            try { return await CreateOnceAsync(request, schemaAsText: true, ct); }
            catch (OperationCanceledException) { return ClaudeResponse.Failed("cancelled"); }
            catch (Exception e2) { return ClaudeResponse.Failed(e2.GetType().Name + ": " + e2.Message); }
        }
        catch (Exception e) { return ClaudeResponse.Failed(e.GetType().Name + ": " + e.Message); }
    }

    private static bool IsSchemaTooLarge(string message)
        => message.Contains("grammar", StringComparison.OrdinalIgnoreCase) || message.Contains("union", StringComparison.OrdinalIgnoreCase) || message.Contains("schema", StringComparison.OrdinalIgnoreCase);

    private async Task<ClaudeResponse> CreateOnceAsync(ClaudeRequest request, bool schemaAsText, CancellationToken ct)
    {
        {
            var system = new List<TextBlockParam>();
            foreach (var block in request.System)
                system.Add(block.Cache
                    ? new TextBlockParam { Text = block.Text, CacheControl = new CacheControlEphemeral { Ttl = Ttl.Ttl1h } }
                    : new TextBlockParam { Text = block.Text });
            var user = request.User;
            OutputConfig output;
            if (schemaAsText)
            {
                user += "\n\nReply with exactly one JSON object and nothing else: no prose, no code fence. It must match this JSON Schema (every property present, enum values spelled as listed, \"\" or -1 where a field does not apply):\n" + request.SchemaJson;
                output = new OutputConfig { Effort = ParseEffort(request.Effort) };
            }
            else
            {
                var schema = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(request.SchemaJson) ?? new Dictionary<string, JsonElement>();
                output = new OutputConfig { Effort = ParseEffort(request.Effort), Format = new JsonOutputFormat { Schema = schema } };
            }
            var parameters = new MessageCreateParams
            {
                Model = request.Model,
                MaxTokens = request.MaxTokens,
                System = system,
                Messages = [new() { Role = Role.User, Content = user }],
                Thinking = new ThinkingConfigAdaptive(),
                OutputConfig = output,
            };
            var message = await _client.Messages.Create(parameters, ct);
            var text = string.Concat(message.Content.Select(b => b.Value).OfType<TextBlock>().Select(t => t.Text));
            var stop = message.StopReason?.ToString() ?? "end_turn";
            var usage = new ClaudeUsage(message.Usage.InputTokens, message.Usage.OutputTokens, message.Usage.CacheReadInputTokens ?? 0, message.Usage.CacheCreationInputTokens ?? 0);
            string? refusal = null;
            if (stop.Equals("refusal", StringComparison.OrdinalIgnoreCase) && message.StopDetails is { } details) refusal = details.Category?.ToString();
            return new ClaudeResponse(text, NormalizeStop(stop), usage, refusal);
        }
    }

    private static string NormalizeStop(string stop)
    {
        var s = stop.Trim().Trim('"').ToLowerInvariant(); // the SDK's enum wrapper prints its JSON form, quotes included
        return s switch { "endturn" or "end_turn" => "end_turn", "maxtokens" or "max_tokens" => "max_tokens", "stopsequence" or "stop_sequence" => "stop_sequence", _ => s };
    }

    private static Effort ParseEffort(string effort) => effort.ToLowerInvariant() switch
    {
        "low" => Effort.Low,
        "medium" => Effort.Medium,
        "xhigh" => Effort.Xhigh,
        "max" => Effort.Max,
        _ => Effort.High,
    };
}
