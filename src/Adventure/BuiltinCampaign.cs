using DungeonCrawler.Core;
using DungeonCrawler.Dungeon;
using Godot;

namespace DungeonCrawler.Adventure;

/// <summary>
/// The shipped campaign: a campaign JSON and twenty floor plans under data/adventures/builtin, produced once with
/// <c>--bake-campaign</c>. Plays with no keys and no network; every floor was validated when it was baked.
/// </summary>
public static class BuiltinCampaign
{
    public const string ShippedDir = "res://data/adventures/builtin";
    /// <summary>A campaign rebaked from Settings lands here and takes precedence over the shipped one.</summary>
    public const string UserDir = "user://adventures/builtin";

    /// <summary>Where the campaign is read from: the user's rebake if there is one, else the shipped copy.</summary>
    public static string Dir => Godot.FileAccess.FileExists($"{UserDir}/campaign.json") ? UserDir : ShippedDir;
    public static bool IsUserCopy => Dir == UserDir;
    public static bool Exists => Godot.FileAccess.FileExists($"{Dir}/campaign.json");

    public static (CampaignPlan Campaign, Dictionary<int, FloorPlan> Floors)? Load()
    {
        if (!Exists) return null;
        try
        {
            var dir = Dir;
            var campaign = PlanFormat.Deserialize<CampaignPlan>(Read($"{dir}/campaign.json"));
            campaign.Source = PlanSources.Builtin;
            var floors = new Dictionary<int, FloorPlan>();
            for (var f = 1; f <= TierRules.FinalFloor; f++)
            {
                var path = $"{dir}/floor_{f:00}.json";
                if (!Godot.FileAccess.FileExists(path)) continue;
                var plan = PlanFormat.Deserialize<FloorPlan>(Read(path));
                plan.Source = PlanSources.Builtin;
                floors[f] = plan;
            }
            return (campaign, floors);
        }
        catch (Exception e)
        {
            GD.PushWarning($"[Builtin] campaign could not be read: {e.Message}");
            return null;
        }
    }

    private static string Read(string path)
    {
        using var f = Godot.FileAccess.Open(path, Godot.FileAccess.ModeFlags.Read) ?? throw new IOException($"cannot open {path}");
        return f.GetAsText();
    }

    /// <summary>Read a campaign from an absolute directory (a bake in progress); sources are kept as written.</summary>
    public static (CampaignPlan Campaign, Dictionary<int, FloorPlan> Floors)? LoadFrom(string dir)
    {
        var campaignPath = Path.Combine(dir, "campaign.json");
        if (!File.Exists(campaignPath)) return null;
        try
        {
            var campaign = PlanFormat.Deserialize<CampaignPlan>(File.ReadAllText(campaignPath));
            var floors = new Dictionary<int, FloorPlan>();
            for (var f = 1; f <= TierRules.FinalFloor; f++)
            {
                var path = Path.Combine(dir, $"floor_{f:00}.json");
                if (File.Exists(path)) floors[f] = PlanFormat.Deserialize<FloorPlan>(File.ReadAllText(path));
            }
            return (campaign, floors);
        }
        catch (Exception e)
        {
            GD.PushWarning($"[Builtin] {dir} could not be read: {e.Message}");
            return null;
        }
    }

    /// <summary>Write a baked campaign to a directory (absolute path); used by the bake flag.</summary>
    public static void Write(string dir, CampaignPlan campaign, IReadOnlyDictionary<int, FloorPlan> floors)
    {
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "campaign.json"), PlanFormat.Serialize(campaign));
        foreach (var (floor, plan) in floors) File.WriteAllText(Path.Combine(dir, $"floor_{floor:00}.json"), PlanFormat.Serialize(plan));
    }
}
