using System.Text.Json;
using System.Text.Json.Nodes;
using DungeonCrawler.Save;

namespace DungeonCrawler.Adventure;

/// <summary>Versioning and (de)serialisation for plans stored on their own (builtin campaign files, AI logs).</summary>
public static class PlanFormat
{
    public const int CurrentVersion = 1;

    public static JsonSerializerOptions Options => SaveFormat.Options;

    public static string Serialize<T>(T plan) => JsonSerializer.Serialize(plan, Options);

    public static T Deserialize<T>(string json)
    {
        var node = JsonNode.Parse(json) as JsonObject ?? throw new InvalidDataException("Plan is not a JSON object");
        node = Migrate(node);
        return node.Deserialize<T>(Options) ?? throw new InvalidDataException("Plan is empty");
    }

    public static JsonObject Migrate(JsonObject root)
    {
        var version = root["SchemaVersion"]?.GetValue<int>() ?? 1;
        if (version > CurrentVersion) throw new InvalidDataException($"Plan schema {version} is newer than this build supports ({CurrentVersion})");
        while (version < CurrentVersion)
        {
            root = version switch
            {
                _ => throw new InvalidDataException($"No migration from plan schema {version}"),
            };
            version++;
            root["SchemaVersion"] = version;
        }
        return root;
    }
}
