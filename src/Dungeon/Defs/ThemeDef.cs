using DungeonCrawler.Core;
using Godot;

namespace DungeonCrawler.Dungeon.Defs;

/// <summary>
/// One look a floor can wear: floor, wall and ceiling textures with tints, and the lighting that goes with them
/// (data/themes/*.tres). Each belongs to a tier (or to any tier with TierIndex -1); floors pick one from their tier.
/// </summary>
[GlobalClass]
public partial class ThemeDef : Resource, IHasId
{
    [Export] public string Id { get; set; } = "";
    [Export] public string DisplayName { get; set; } = "";
    /// <summary>Tier this look belongs to; -1 means any tier.</summary>
    [Export] public int TierIndex { get; set; } = -1;
    [Export] public float Weight { get; set; } = 1f;
    [Export(PropertyHint.MultilineText)] public string Description { get; set; } = "";

    [ExportGroup("Surfaces")]
    /// <summary>Poly Haven texture folder names under assets/textures; empty = flat color.</summary>
    [Export] public string FloorTexture { get; set; } = "";
    [Export] public string WallTexture { get; set; } = "";
    [Export] public string CeilingTexture { get; set; } = "";
    [Export] public Color FloorColor { get; set; } = new(0.32f, 0.30f, 0.28f);
    [Export] public Color WallColor { get; set; } = new(0.45f, 0.42f, 0.40f);
    [Export] public Color CeilingColor { get; set; } = new(0.18f, 0.17f, 0.17f);
    /// <summary>How many metres one texture tile covers; smaller repeats more.</summary>
    [Export] public float MetersPerTile { get; set; } = 2f;

    [ExportGroup("Light")]
    [Export] public Color AmbientColor { get; set; } = new(0.35f, 0.33f, 0.3f);
    [Export] public float AmbientEnergy { get; set; } = 0.35f;
    [Export] public Color FogColor { get; set; } = new(0.05f, 0.05f, 0.06f);
    [Export] public float FogDensity { get; set; } = 0.035f;
    [Export] public Color TorchColor { get; set; } = new(1f, 0.85f, 0.65f);
    [Export] public int WallLightsPerRoom { get; set; } = 2;

    public ThemeProfile ToProfile() => new(Id, DisplayName, TierIndex, Weight, Description);

    /// <summary>A theme that reproduces a tier's own look, for tiers with no themes of their own.</summary>
    public static ThemeDef FromTier(TierDef t) => new()
    {
        Id = $"{t.Id}.default", DisplayName = t.DisplayName, TierIndex = t.Index, Weight = 1f,
        FloorTexture = t.FloorTexture, WallTexture = t.WallTexture, CeilingTexture = t.CeilingTexture,
        FloorColor = t.FloorColor, WallColor = t.WallColor, CeilingColor = t.CeilingColor,
        AmbientColor = t.AmbientColor, AmbientEnergy = t.AmbientEnergy, FogColor = t.FogColor, FogDensity = t.FogDensity, TorchColor = t.TorchColor,
        WallLightsPerRoom = t.WallLightsPerRoom,
    };
}
