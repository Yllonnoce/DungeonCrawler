using DungeonCrawler.Enemies;
using Godot;

namespace DungeonCrawler.Combat.Fx;

/// <summary>
/// The red mark on the floor under a boss signature: a ring the size of the Slam, a lane for the Charge, a pulsing circle for Volley and Summon.
/// Lives beside the boss under the floor root, follows it, and frees itself when the delay runs out or <see cref="Hide"/> is called.
/// </summary>
public partial class TelegraphDecal : Node3D
{
    public const string NodeName = "TelegraphDecal";
    public static readonly Color Red = new("#e03030");

    public Enemy? Boss { get; private set; }
    private Signature _sig;
    private float _left;
    private float _age;
    private Vector3 _laneDir = Vector3.Forward;
    private StandardMaterial3D? _pulse;

    public static void Show(Enemy boss, Signature sig, float seconds)
    {
        Hide(boss);
        if (boss.GetParent() is not { } parent || !GodotObject.IsInstanceValid(parent)) return;
        var d = new TelegraphDecal { Name = NodeName, Boss = boss, _sig = sig, _left = seconds };
        var toTarget = boss.TargetPosition - boss.GlobalPosition; toTarget.Y = 0f;
        d._laneDir = toTarget.LengthSquared() > 1e-4f ? toTarget.Normalized() : -boss.GlobalTransform.Basis.Z;
        d.Build();
        parent.AddChild(d);
        d.Follow();
    }

    public static void Hide(Enemy boss)
    {
        if (boss.GetParent() is not { } parent || !GodotObject.IsInstanceValid(parent)) return;
        foreach (var d in parent.GetChildren().OfType<TelegraphDecal>())
            if (d.Boss == boss && !d.IsQueuedForDeletion()) d.QueueFree();
    }

    private static StandardMaterial3D Mat(float alpha) => new()
    {
        ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
        AlbedoColor = new Color(Red, alpha), CullMode = BaseMaterial3D.CullModeEnum.Disabled, DisableReceiveShadows = true, NoDepthTest = false,
    };

    private void Build()
    {
        switch (_sig)
        {
            case Signature.Slam:
                AddChild(new MeshInstance3D { Name = "Disc", Mesh = new CylinderMesh { TopRadius = TelegraphRules.SlamRadius, BottomRadius = TelegraphRules.SlamRadius, Height = 0.02f, RadialSegments = 48 }, MaterialOverride = Mat(0.18f), CastShadow = GeometryInstance3D.ShadowCastingSetting.Off });
                AddChild(new MeshInstance3D { Name = "Ring", Mesh = new TorusMesh { InnerRadius = TelegraphRules.SlamRadius - 0.2f, OuterRadius = TelegraphRules.SlamRadius, Rings = 48 }, Scale = new Vector3(1f, 0.1f, 1f), MaterialOverride = Mat(0.7f), CastShadow = GeometryInstance3D.ShadowCastingSetting.Off });
                break;
            case Signature.Charge:
                var lane = new MeshInstance3D { Name = "Lane", Mesh = new BoxMesh { Size = new Vector3(TelegraphRules.ChargeLaneWidth, 0.02f, TelegraphRules.ChargeLaneLength) }, MaterialOverride = Mat(0.35f), CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
                lane.Position = Vector3.Forward * (TelegraphRules.ChargeLaneLength * 0.5f);
                AddChild(lane);
                break;
            default:
                _pulse = Mat(0.25f);
                AddChild(new MeshInstance3D { Name = "Pulse", Mesh = new CylinderMesh { TopRadius = TelegraphRules.PulseRadius, BottomRadius = TelegraphRules.PulseRadius, Height = 0.02f, RadialSegments = 32 }, MaterialOverride = _pulse, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off });
                break;
        }
    }

    private void Follow()
    {
        if (Boss == null || !GodotObject.IsInstanceValid(Boss)) return;
        var at = Boss.GlobalPosition with { Y = Boss.GlobalPosition.Y + 0.03f };
        GlobalPosition = at;
        if (_sig == Signature.Charge) LookAt(at + _laneDir, Vector3.Up); // the lane's -Z points down the charge
    }

    public override void _Process(double delta)
    {
        if (IsQueuedForDeletion()) return;
        var dt = (float)delta;
        _age += dt;
        _left -= dt;
        if (_left <= 0f || Boss == null || !GodotObject.IsInstanceValid(Boss) || !Boss.IsAlive) { QueueFree(); return; }
        Follow();
        if (_pulse != null) _pulse.AlbedoColor = new Color(Red, 0.25f + 0.2f * Mathf.Sin(_age * 8f));
    }
}
