using DungeonCrawler.Enemies;
using Godot;

namespace DungeonCrawler.Combat.Fx;

/// <summary>The glow at a shooter's hand while it winds up: a small light and an emissive bead, children of the enemy, gone when the shot leaves.</summary>
public static class TelegraphFx
{
    public const string HandGlowName = "HandGlow";

    public static void HandGlow(Enemy e, float seconds)
    {
        if (!GodotObject.IsInstanceValid(e) || !e.IsInsideTree()) return;
        foreach (var old in e.GetChildren().OfType<HandGlowNode>()) old.QueueFree();
        var color = new Color(e.Profile.Color).Lightened(0.5f);
        var glow = new HandGlowNode { Name = HandGlowName, Ttl = seconds };
        glow.AddChild(new OmniLight3D { LightColor = color, LightEnergy = 1.5f, OmniRange = 2f, ShadowEnabled = false });
        glow.AddChild(new MeshInstance3D
        {
            Mesh = new SphereMesh { Radius = 0.08f, Height = 0.16f, RadialSegments = 12, Rings = 6 },
            MaterialOverride = new StandardMaterial3D { ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, AlbedoColor = color, EmissionEnabled = true, Emission = color, EmissionEnergyMultiplier = 3f },
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        });
        e.AddChild(glow);
        glow.Position = Vector3.Up * 1.3f * e.Profile.Scale + Vector3.Forward * 0.4f; // the enemy's hand, in its local frame (-Z is forward)
    }

}

/// <summary>The glow's node: frees itself after <see cref="Ttl"/> seconds.</summary>
public partial class HandGlowNode : Node3D
{
    public float Ttl { get; set; }
    public override void _Process(double delta)
    {
        Ttl -= (float)delta;
        if (Ttl <= 0f && !IsQueuedForDeletion()) QueueFree();
    }
}
