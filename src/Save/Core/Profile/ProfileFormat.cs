using System.Text.Json;

namespace DungeonCrawler.Save.Profile;

/// <summary>The profile's JSON, with the save format's options. A missing or older Version is accepted; bad JSON is null.</summary>
public static class ProfileFormat
{
    public static string Serialize(ProfileDto p) => JsonSerializer.Serialize(p, SaveFormat.Options);

    public static ProfileDto? Deserialize(string json)
    {
        try
        {
            var p = JsonSerializer.Deserialize<ProfileDto>(json, SaveFormat.Options);
            if (p == null) return null;
            if (p.Version > ProfileDto.CurrentVersion) return null;
            p.Version = ProfileDto.CurrentVersion;
            return p;
        }
        catch (JsonException) { return null; }
    }
}
