using System.Text.Json;
using System.Text.Json.Nodes;

namespace DungeonCrawler.Save;

/// <summary>JSON (de)serialisation with a version stamp and a migration chain. Engine-free.</summary>
public static class SaveFormat
{
    public const int CurrentVersion = 9;

    public static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = null,
        IncludeFields = false,
        NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.AllowNamedFloatingPointLiterals,
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() },
    };

    public static string Serialize(SaveData data) => JsonSerializer.Serialize(data, Options);

    /// <summary>Parse a file of any supported version, migrating it forward first.</summary>
    public static SaveData Deserialize(string json)
    {
        var node = JsonNode.Parse(json) as JsonObject ?? throw new InvalidDataException("Save file is not a JSON object");
        node = SaveMigrations.Migrate(node);
        return node.Deserialize<SaveData>(Options) ?? throw new InvalidDataException("Save file is empty");
    }

    /// <summary>Just the header fields, for slot lists. Tolerates any version.</summary>
    public static (int version, string label, int floor, string savedAt, double playSeconds, List<string> members)? ReadHeader(string json)
    {
        try
        {
            var node = JsonNode.Parse(json) as JsonObject;
            if (node == null) return null;
            var members = new List<string>();
            if (node["Party"]?["Members"] is JsonArray arr)
                foreach (var m in arr)
                    members.Add($"{m?["Name"]?.GetValue<string>() ?? "?"} L{m?["Level"]?.GetValue<int>() ?? 1}");
            return (node["Version"]?.GetValue<int>() ?? 0, node["Label"]?.GetValue<string>() ?? "", node["CurrentFloor"]?.GetValue<int>() ?? 1,
                    node["SavedAtUtc"]?.GetValue<string>() ?? "", node["PlaySeconds"]?.GetValue<double>() ?? 0, members);
        }
        catch (JsonException) { return null; }
    }
}

/// <summary>
/// Version-to-version upgrades. Each step transforms the raw JSON of version N into version N+1 so old saves keep loading.
/// Add a case here whenever <see cref="SaveFormat.CurrentVersion"/> is bumped.
/// </summary>
public static class SaveMigrations
{
    public static JsonObject Migrate(JsonObject root)
    {
        var version = root["Version"]?.GetValue<int>() ?? 0;
        while (version < SaveFormat.CurrentVersion)
        {
            root = version switch
            {
                0 => From0To1(root),
                1 => From1To2(root),
                2 => root, // v3 added LearnedSkills per character; missing lists deserialize empty
                3 => root, // v4 added OpenedSecrets per floor
                4 => root, // v5 added per-member Poses and Stances (empty lists fall back to the lead's pose)
                5 => root, // v6 added Adventure (campaign + floor plans); missing means legacy random population
                6 => root, // v7 added Multiplayer and Players (a dedicated server's roster); missing means single-player
                7 => root, // v8 added Objective per floor (the floor objective's progress); missing means none or not started
                8 => From8To9(root), // v9 added lethal hazards per floor and Mangled members; floors an old run already visited keep no hazards
                _ => throw new InvalidDataException($"No migration from save version {version}"),
            };
            version++;
            root["Version"] = version;
        }
        if (version > SaveFormat.CurrentVersion) throw new InvalidDataException($"Save version {version} is newer than this build supports ({SaveFormat.CurrentVersion})");
        return root;
    }

    /// <summary>Version 9 added the lethal hazards. A run that is already under way gets them only on floors it has not seen, so saved floors stay exactly as the player left them.</summary>
    private static JsonObject From8To9(JsonObject root)
    {
        if (root["Adventure"] is JsonObject adventure)
        {
            var current = root["CurrentFloor"]?.GetValue<int>() ?? 1;
            adventure["HazardsFromFloor"] = current + 1;
        }
        return root;
    }

    /// <summary>Version 2 added traps, vaults, keys and the victory flag. Missing lists deserialize as empty, so only the flag needs a default.</summary>
    private static JsonObject From1To2(JsonObject root)
    {
        root["Victory"] ??= false;
        return root;
    }

    /// <summary>Version 0 = pre-release files without a version stamp: fill in defaults that later code relies on.</summary>
    private static JsonObject From0To1(JsonObject root)
    {
        root["Label"] ??= "";
        root["PlaySeconds"] ??= 0;
        root["Floors"] ??= new JsonObject();
        root["Party"] ??= new JsonObject();
        return root;
    }
}
