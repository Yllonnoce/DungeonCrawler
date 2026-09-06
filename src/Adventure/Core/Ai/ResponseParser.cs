using System.Text.Json;
using System.Text.Json.Serialization;

namespace DungeonCrawler.Adventure.Ai;

public sealed record ParseResult<T>(T? Value, string? Error)
{
    public bool Ok => Value != null && Error == null;
}

/// <summary>Turns a model response into a plan DTO, tolerating code fences and reporting refusals and truncation as errors.</summary>
public static class ResponseParser
{
    private static readonly JsonSerializerOptions Options = Build();

    private static JsonSerializerOptions Build()
    {
        var o = new JsonSerializerOptions(PlanFormat.Options)
        {
            PropertyNameCaseInsensitive = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true,
        };
        o.Converters.Clear();
        o.Converters.Add(new LenientEnumConverterFactory()); // a misspelt enum from the model becomes the default, not a dropped plan
        return o;
    }

    public static ParseResult<T> Parse<T>(ClaudeResponse response) where T : class
    {
        if (response.Error != null) return new ParseResult<T>(null, response.Error);
        if (response.StopReason == "refusal") return new ParseResult<T>(null, $"refused ({response.RefusalCategory ?? "unspecified"})");
        if (response.StopReason == "max_tokens") return new ParseResult<T>(null, "truncated (max_tokens)");
        if (!response.Ok) return new ParseResult<T>(null, $"unexpected stop reason {response.StopReason}");
        var text = StripFences(response.Text);
        if (string.IsNullOrWhiteSpace(text)) return new ParseResult<T>(null, "empty response");
        try
        {
            var value = JsonSerializer.Deserialize<T>(text, Options);
            return value == null ? new ParseResult<T>(null, "null JSON") : new ParseResult<T>(value, null);
        }
        catch (JsonException e)
        {
            return new ParseResult<T>(null, $"invalid JSON: {e.Message}");
        }
    }

    /// <summary>Enums by name, case-insensitive; anything unknown (or a number) falls back to the enum's first value.</summary>
    public sealed class LenientEnumConverterFactory : JsonConverterFactory
    {
        public override bool CanConvert(Type t) => t.IsEnum;
        public override JsonConverter CreateConverter(Type t, JsonSerializerOptions options)
            => (JsonConverter)Activator.CreateInstance(typeof(LenientEnumConverter<>).MakeGenericType(t))!;
    }

    public sealed class LenientEnumConverter<T> : JsonConverter<T> where T : struct, Enum
    {
        public override T Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType == JsonTokenType.String && Enum.TryParse<T>(reader.GetString(), ignoreCase: true, out var v) && Enum.IsDefined(v)) return v;
            if (reader.TokenType == JsonTokenType.Number && reader.TryGetInt32(out var n) && Enum.IsDefined(typeof(T), n)) return (T)Enum.ToObject(typeof(T), n);
            return Enum.GetValues<T>()[0];
        }

        public override void Write(Utf8JsonWriter writer, T value, JsonSerializerOptions options) => writer.WriteStringValue(value.ToString());
    }

    public static string StripFences(string text)
    {
        var t = text.Trim();
        if (t.StartsWith("```"))
        {
            var firstLine = t.IndexOf('\n');
            if (firstLine > 0) t = t[(firstLine + 1)..];
            if (t.EndsWith("```")) t = t[..^3];
        }
        // prose before or after the document: keep the outermost braces
        var first = t.IndexOf('{');
        var last = t.LastIndexOf('}');
        if (first >= 0 && last > first) t = t[first..(last + 1)];
        return t.Trim();
    }
}
