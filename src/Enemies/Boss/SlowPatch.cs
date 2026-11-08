using DungeonCrawler.Combat;
using DungeonCrawler.Party;
using Godot;

namespace DungeonCrawler.Enemies;

/// <summary>A web on the arena floor (the Broodmother's phase two): whoever stands in it is Slowed, every so often while they do. The server slows.</summary>
public partial class SlowPatch : Area3D
{
    private const float Tick = 0.8f;
    private float _t;
    private static readonly StandardMaterial3D Silk = new() { AlbedoColor = new Color(0.9f, 0.9f, 0.85f, 0.45f), Transparency = BaseMaterial3D.TransparencyEnum.Alpha, ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded };

    public override void _Ready()
    {
        CollisionLayer = 0;
        CollisionMask = 2; // adventurers
        AddChild(new CollisionShape3D { Shape = new CylinderShape3D { Radius = BossRules.SlowPatchRadius, Height = 1.6f }, Position = new Vector3(0, 0.8f, 0) });
        AddChild(new MeshInstance3D { Mesh = new CylinderMesh { TopRadius = BossRules.SlowPatchRadius, BottomRadius = BossRules.SlowPatchRadius, Height = 0.02f, RadialSegments = 18 }, MaterialOverride = Silk, Position = new Vector3(0, 0.03f, 0), CastShadow = GeometryInstance3D.ShadowCastingSetting.Off });
        for (var i = 0; i < 6; i++)
        {
            var ang = i * Mathf.Tau / 6f;
            var strand = new MeshInstance3D { Mesh = new CylinderMesh { TopRadius = 0.02f, BottomRadius = 0.02f, Height = BossRules.SlowPatchRadius * 1.6f, RadialSegments = 4 }, MaterialOverride = Silk, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
            strand.Position = new Vector3(Mathf.Cos(ang) * BossRules.SlowPatchRadius * 0.8f, 0.05f, Mathf.Sin(ang) * BossRules.SlowPatchRadius * 0.8f);
            strand.Rotation = new Vector3(0, -ang, Mathf.Pi / 2f);
            AddChild(strand);
        }
        _t = Tick * 0.5f;
    }

    public override void _PhysicsProcess(double delta)
    {
        if (Net.NetSession.IsClient) return;
        _t -= (float)delta;
        if (_t > 0f) return;
        _t = Tick;
        foreach (var body in GetOverlappingBodies())
            if (body is Adventurer { Downed: false } adv) { adv.State.Effects.Apply(new StatusApplication(StatusKind.Slow, BossRules.SlowPatchSeconds, 0f)); adv.State.Recalculate(); }
    }
}
