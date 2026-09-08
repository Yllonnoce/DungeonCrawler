using System.Text.Json.Nodes;

namespace DungeonCrawler.Adventure.Ai;

/// <summary>
/// Request and response shapes of Runware's task API (https://api.runware.ai/v1: one POST, a JSON array of tasks,
/// a <c>data</c> array back, or an <c>errors</c> array). Kept out of the HTTP clients so they are unit tested.
/// The text side is used by <c>RunwareTextClient</c> for the planner; the audio side by <c>RunwareTtsClient</c>.
/// </summary>
public static class RunwareWire
{
    public const string Endpoint = "https://api.runware.ai/v1";
    public const string Host = "api.runware.ai";
    /// <summary>The planner model when Runware is selected: Claude through Runware's pass-through catalogue.</summary>
    public const string DefaultTextModel = "anthropic:claude@sonnet-4.6";
    /// <summary>Lists the text models as slugs (anthropic-claude-sonnet-4-6), the form the OpenAI-compatible endpoint takes.</summary>
    public const string ModelsEndpoint = "https://api.runware.ai/v1/models";
    /// <summary>
    /// The OpenAI-compatible chat endpoint. It takes every model in <see cref="ModelsEndpoint"/>'s list (slug or AIR),
    /// while the textInference task rejects a good third of them with invalidModel; the client falls back to it.
    /// </summary>
    public const string ChatEndpoint = "https://api.runware.ai/v1/chat/completions";

    /// <summary>
    /// The task API wants AIR ids (creator:family@version) while the model list and the chat endpoint use slugs
    /// (creator-family-version-parts); a slug typed into the model field is converted: the first two parts become
    /// creator and family, and runs of numbers in the rest are joined with dots (google-gemini-3-5-flash becomes
    /// google:gemini@3.5-flash). Anything already holding a colon is returned as typed.
    /// </summary>
    public static string ToAir(string model)
    {
        model = model.Trim();
        if (model.Length == 0 || model.Contains(':')) return model;
        var parts = model.Split('-', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 3) return model;
        var version = new System.Text.StringBuilder(parts[2]);
        for (var i = 3; i < parts.Length; i++)
        {
            var numeric = parts[i].All(char.IsDigit) && parts[i - 1].All(char.IsDigit);
            version.Append(numeric ? '.' : '-').Append(parts[i]);
        }
        return $"{parts[0]}:{parts[1]}@{version}";
    }

    /// <summary>The model slugs in a GET /v1/models reply (an OpenAI-style list), sorted; empty with an error text otherwise.</summary>
    public static (List<string> Models, string? Error) ParseModelList(string body)
    {
        try
        {
            if (ErrorOf(body) is { } error) return (new List<string>(), error);
            var root = JsonNode.Parse(body);
            var list = (root?["data"] as JsonArray)?.OfType<JsonObject>().Select(m => m["id"]?.GetValue<string>()).Where(id => !string.IsNullOrEmpty(id)).Select(id => id!).OrderBy(id => id).ToList() ?? new List<string>();
            return (list, list.Count == 0 ? "no models in the response" : null);
        }
        catch (Exception e) { return (new List<string>(), $"unreadable response: {e.Message}"); }
    }

    /// <summary>
    /// The priced catalogue in a GET /v1/models reply, dearest first (by output price, then input); models the reply
    /// prices at zero sort last. Empty with an error text when the body is not a model list.
    /// </summary>
    public static (List<RunwareTextModel> Models, string? Error) ParseModelCatalogue(string body)
    {
        try
        {
            if (ErrorOf(body) is { } error) return (new List<RunwareTextModel>(), error);
            var root = JsonNode.Parse(body);
            var list = new List<RunwareTextModel>();
            foreach (var m in (root?["data"] as JsonArray)?.OfType<JsonObject>() ?? Enumerable.Empty<JsonObject>())
            {
                var id = m["id"]?.GetValue<string>();
                if (string.IsNullOrEmpty(id)) continue;
                var pricing = m["pricing"] as JsonObject;
                list.Add(new RunwareTextModel(id, m["name"]?.GetValue<string>() ?? id, PerMillion(pricing?["prompt"]), PerMillion(pricing?["completion"])));
            }
            return (RunwareTextModel.DearestFirst(list), list.Count == 0 ? "no models in the response" : null);
        }
        catch (Exception e) { return (new List<RunwareTextModel>(), $"unreadable response: {e.Message}"); }

        static double PerMillion(JsonNode? perToken)
        {
            if (perToken == null) return 0;
            var text = perToken is JsonValue v && v.TryGetValue<string>(out var s) ? s : perToken.ToJsonString().Trim('"');
            return double.TryParse(text, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var d) ? d * 1_000_000 : 0;
        }
    }

    // ---- textInference ----------------------------------------------------------------------------------

    /// <summary>
    /// One planning call as a Runware textInference task. Runware messages only take user and assistant roles, so the
    /// system blocks go in settings.systemPrompt; the JSON schema is appended to the user turn as text (the same
    /// fallback the direct Claude client ends up in for these large schemas) rather than sent as jsonSchema, which not
    /// every routed model accepts. <paramref name="thinking"/> off omits thinkingLevel for models that reject it.
    /// </summary>
    /// <param name="delivery">"async" (the default: submit, then poll with <see cref="PollRequestJson"/>, so a long plan is not cut off by the synchronous reply window), "stream" or "sync".</param>
    public static string TextRequestJson(ClaudeRequest request, string taskUuid, bool thinking = true, string delivery = "async")
    {
        var settings = new JsonObject { ["maxTokens"] = request.MaxTokens };
        var system = string.Join("\n\n", request.System.Select(b => b.Text));
        if (system.Length > 0) settings["systemPrompt"] = system;
        if (thinking) settings["thinkingLevel"] = ThinkingLevel(request.Effort, request.Model);
        var user = UserWithSchema(request);
        var task = new JsonObject
        {
            ["taskType"] = "textInference",
            ["taskUUID"] = taskUuid,
            ["model"] = ToAir(request.Model),
            ["messages"] = new JsonArray(new JsonObject { ["role"] = "user", ["content"] = user }),
            ["settings"] = settings,
            ["includeUsage"] = true,
            ["includeCost"] = true,
        };
        if (delivery != "sync") task["deliveryMethod"] = delivery;
        return new JsonArray(task).ToJsonString();
    }

    /// <summary>The user turn with the schema appended as text, the same on both routes.</summary>
    public static string UserWithSchema(ClaudeRequest request)
        => request.User + "\n\nReply with exactly one JSON object and nothing else: no prose, no code fence. It must match this JSON Schema (every property present, enum values spelled as listed, \"\" or -1 where a field does not apply):\n" + request.SchemaJson;

    // ---- chat completions -------------------------------------------------------------------------------

    /// <summary>The same planning call for the OpenAI-compatible endpoint: system and user messages, the model as typed (slug or AIR both work there).</summary>
    public static string ChatRequestJson(ClaudeRequest request)
    {
        var messages = new JsonArray();
        var system = string.Join("\n\n", request.System.Select(b => b.Text));
        if (system.Length > 0) messages.Add(new JsonObject { ["role"] = "system", ["content"] = system });
        messages.Add(new JsonObject { ["role"] = "user", ["content"] = UserWithSchema(request) });
        return new JsonObject
        {
            ["model"] = ToAir(request.Model),
            ["messages"] = messages,
            ["max_tokens"] = request.MaxTokens,
            ["stream"] = false,
        }.ToJsonString();
    }

    /// <summary>
    /// A chat completion as the planner's response record. The endpoint reports no dollar cost, so the caller passes
    /// the model's list prices per million tokens (null leaves the cost unset and the list prices of the planner apply).
    /// </summary>
    public static ClaudeResponse ParseChatResponse(string body, (double InputPerMillion, double OutputPerMillion)? prices = null)
    {
        try
        {
            if (ErrorOf(body) is { } error) return ClaudeResponse.Failed(error);
            var root = JsonNode.Parse(body);
            var choice = (root?["choices"] as JsonArray)?.OfType<JsonObject>().FirstOrDefault();
            if (choice == null) return ClaudeResponse.Failed("no choices in the response");
            var message = choice["message"];
            var text = message?["content"]?.GetValue<string>() ?? "";
            var finish = choice["finish_reason"]?.GetValue<string>() ?? "stop";
            var stop = finish.ToLowerInvariant() switch
            {
                "stop" or "end_turn" => "end_turn",
                "length" or "max_tokens" => "max_tokens",
                "content_filter" or "refusal" => "refusal",
                _ => text.Length > 0 ? "end_turn" : finish,
            };
            if (stop == "end_turn" && text.Length == 0 && message?["refusal"]?.GetValue<string>() is { Length: > 0 } refusal) return new ClaudeResponse("", "refusal", ClaudeUsage.Zero, refusal);
            var usage = root?["usage"];
            var input = usage?["prompt_tokens"]?.GetValue<long>() ?? 0;
            var output = usage?["completion_tokens"]?.GetValue<long>() ?? 0;
            var details = usage?["prompt_tokens_details"];
            var cacheRead = details?["cached_tokens"]?.GetValue<long>() ?? 0;
            var cacheWrite = details?["created_cache_tokens"]?.GetValue<long>() ?? 0;
            double? cost = prices is { } p ? (input * p.InputPerMillion + output * p.OutputPerMillion) / 1_000_000.0 : null;
            return new ClaudeResponse(text, stop, new ClaudeUsage(input, output, cacheRead, cacheWrite, cost), stop == "refusal" ? finish : null);
        }
        catch (Exception e) { return ClaudeResponse.Failed($"unreadable response: {e.Message}"); }
    }

    /// <summary>Ask for the result of an async task.</summary>
    public static string PollRequestJson(string taskUuid)
        => new JsonArray(new JsonObject { ["taskType"] = "getResponse", ["taskUUID"] = taskUuid }).ToJsonString();

    /// <summary>
    /// Read a submit or poll reply: <c>Pending</c> while the task is still processing; otherwise the finished
    /// response (a success, a task-level error, or a failure to read it). A reply without a status field (a sync
    /// answer, or an async submit that already carries text) counts as finished when it holds text.
    /// </summary>
    public static (bool Pending, ClaudeResponse? Response) ParsePoll(string body)
    {
        try
        {
            if (ErrorOf(body) is { } error) return (false, ClaudeResponse.Failed(error));
            var root = JsonNode.Parse(body);
            var task = (root?["data"] as JsonArray)?.OfType<JsonObject>().FirstOrDefault(t => t["taskType"]?.GetValue<string>() == "textInference")
                       ?? (root?["data"] as JsonArray)?.OfType<JsonObject>().FirstOrDefault();
            if (task == null) return (false, ClaudeResponse.Failed("no textInference result in the response"));
            var status = task["status"]?.GetValue<string>()?.ToLowerInvariant();
            var hasText = task["text"] != null;
            if (status == "processing" || status == "pending" || status == "queued" || (status == null && !hasText)) return (true, null);
            if (status == "error" || status == "failed")
                return (false, ClaudeResponse.Failed(task["message"]?.GetValue<string>() ?? task["error"]?.GetValue<string>() ?? "the task failed"));
            return (false, ParseTextResponse(body));
        }
        catch (Exception e) { return (false, ClaudeResponse.Failed($"unreadable response: {e.Message}")); }
    }

    /// <summary>
    /// Anthropic's effort as Runware's thinkingLevel. Claude models get "off": measured on the real outline request
    /// on 2026-09-07, "max", "high" and "adaptive" each let the model draft the whole plan inside its thinking until
    /// all 48,000 tokens were gone ($0.75, fifteen minutes, no text), while "off" returned a complete campaign in
    /// 10k tokens, $0.18 and 154 s. Other models get the fixed level nearest the effort, never "max".
    /// </summary>
    public static string ThinkingLevel(string effort, string model = "")
    {
        var e = effort.ToLowerInvariant();
        var claude = ToAir(model).StartsWith("anthropic:", StringComparison.OrdinalIgnoreCase);
        if (claude) return "off";
        return e switch
        {
            "low" => "low",
            "medium" => "medium",
            _ => "high",
        };
    }

    /// <summary>The first error message in a Runware body (the <c>errors</c> array), or null when there is none.</summary>
    public static string? ErrorOf(string body)
    {
        try
        {
            var root = JsonNode.Parse(body);
            if (root?["errors"] is JsonArray errors && errors.Count > 0)
            {
                var e = errors[0];
                var code = e?["code"]?.GetValue<string>();
                var message = e?["message"]?.GetValue<string>() ?? "error";
                return code != null ? $"{code}: {message}" : message;
            }
            if (root?["error"] is JsonObject single) return single["message"]?.GetValue<string>() ?? "error";
        }
        catch { }
        return null;
    }

    /// <summary>A text task's reply as the planner's response record: text, stop reason, usage and the dollar cost Runware reports.</summary>
    public static ClaudeResponse ParseTextResponse(string body)
    {
        try
        {
            if (ErrorOf(body) is { } error) return ClaudeResponse.Failed(error);
            var root = JsonNode.Parse(body);
            var task = (root?["data"] as JsonArray)?.OfType<JsonObject>().FirstOrDefault(t => t["taskType"]?.GetValue<string>() == "textInference")
                       ?? (root?["data"] as JsonArray)?.OfType<JsonObject>().FirstOrDefault();
            if (task == null) return ClaudeResponse.Failed("no textInference result in the response");
            var text = task["text"]?.GetValue<string>() ?? "";
            var finish = task["finishReason"]?.GetValue<string>() ?? "stop";
            var stop = finish.ToLowerInvariant() switch
            {
                "stop" or "end_turn" => "end_turn",
                "length" or "max_tokens" => "max_tokens",
                "content_filter" or "refusal" => "refusal",
                _ => text.Length > 0 ? "end_turn" : finish,
            };
            var usage = task["usage"];
            var input = usage?["promptTokens"]?.GetValue<long>() ?? 0;
            var output = usage?["completionTokens"]?.GetValue<long>() ?? 0;
            var details = usage?["promptTokensDetails"];
            var cacheRead = details?["cachedTokens"]?.GetValue<long>() ?? 0;
            var cacheWrite = details?["cacheCreationTokens"]?.GetValue<long>() ?? 0;
            double? cost = task["cost"] is { } c ? c.GetValue<double>() : null;
            return new ClaudeResponse(text, stop, new ClaudeUsage(input, output, cacheRead, cacheWrite, cost), stop == "refusal" ? finish : null);
        }
        catch (Exception e) { return ClaudeResponse.Failed($"unreadable response: {e.Message}"); }
    }

    // ---- accountManagement ------------------------------------------------------------------------------

    /// <summary>A key check that runs no model: account details, including the balance.</summary>
    public static string AccountRequestJson(string taskUuid)
        => new JsonArray(new JsonObject { ["taskType"] = "accountManagement", ["taskUUID"] = taskUuid, ["operation"] = "getDetails" }).ToJsonString();

    /// <summary>
    /// The balance in the account reply, or null with an error text. The live API sends <c>balance</c> as a plain
    /// number; the documented object form (amount plus freeBalance) is read too.
    /// </summary>
    public static (double? Balance, string? Error) ParseAccountResponse(string body)
    {
        try
        {
            if (ErrorOf(body) is { } error) return (null, error);
            var root = JsonNode.Parse(body);
            var task = (root?["data"] as JsonArray)?.OfType<JsonObject>().FirstOrDefault();
            var balance = task?["balance"];
            if (balance == null) return (null, "no balance in the response");
            if (balance is JsonValue value)
            {
                if (value.TryGetValue<double>(out var number)) return (number, null);
                if (value.TryGetValue<string>(out var text) && double.TryParse(text, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var parsed)) return (parsed, null);
                return (null, "unreadable balance");
            }
            var amount = balance["amount"]?.GetValue<double>() ?? 0;
            var free = balance["freeBalance"]?.GetValue<double>() ?? 0;
            return (amount + free, null);
        }
        catch (Exception e) { return (null, $"unreadable response: {e.Message}"); }
    }

    /// <summary>A short human reading of an HTTP status from Runware.</summary>
    public static string DescribeStatus(int status) => status switch
    {
        401 => "the Runware key was rejected",
        402 => "the Runware balance is used up",
        429 => "Runware is rate limiting requests",
        503 or 504 => "Runware is over capacity; try again in a moment",
        _ => $"HTTP {status}",
    };
}
