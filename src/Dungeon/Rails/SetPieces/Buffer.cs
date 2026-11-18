using DungeonCrawler.Core;
using Godot;

namespace DungeonCrawler.Dungeon.Rails;

/// <summary>
/// A timber-and-iron buffer stop across the party line just short of the last stop: a heavy beam on two iron uprights, braced
/// back. Solid to walkers (layer 1) while it stands. The cart's front smashes it (<see cref="Smash"/>): it vanishes in a burst of
/// splinters and a few tumbling timbers (cosmetic, freed after <see cref="TimberSeconds"/>), with a thud. It does no harm.
/// <see cref="Restore"/> stands it up again. Server only.
/// </summary>
public partial class BufferStop : StaticBody3D
{
    /// <summary>From the buffer's middle to the face the cart meets.</summary>
    public const float HalfDepth = 0.3f;
    const float BeamHeight = 0.65f, Width = 1.7f;
    const float TimberSeconds = 4f;

    /// <summary>The party line's S at the buffer's middle.</summary>
    public float S { get; init; }
    public float FaceS => S - HalfDepth;
    public bool Smashed { get; private set; }

    private readonly StandardMaterial3D _wood = new() { AlbedoColor = new Color(0.34f, 0.22f, 0.12f), Roughness = 0.9f };
    private readonly StandardMaterial3D _iron = new() { AlbedoColor = new Color(0.16f, 0.15f, 0.14f), Metallic = 0.75f, Roughness = 0.5f };
    private readonly StandardMaterial3D _paint = new() { AlbedoColor = new Color(0.75f, 0.6f, 0.1f), Roughness = 0.7f };
    private Node3D _look = null!;
    private CollisionShape3D _shape = null!;
    private readonly List<(Vector3 size, Transform3D local, Material material)> _parts = new();

    public BufferStop()
    {
        CollisionMask = 0;
    }

    public override void _Ready()
    {
        CollisionLayer = 1;
        _shape = new CollisionShape3D { Shape = new BoxShape3D { Size = new Vector3(Width, BeamHeight + 0.3f, HalfDepth * 2f) }, Position = new Vector3(0, (BeamHeight + 0.3f) / 2f, 0) };
        AddChild(_shape);
        _look = new Node3D { Name = "Look" };
        AddChild(_look);
        Part(new Vector3(Width, 0.32f, 0.3f), new Transform3D(Basis.Identity, new Vector3(0, BeamHeight, 0)), _wood); // the beam
        Part(new Vector3(Width * 0.9f, 0.08f, 0.31f), new Transform3D(Basis.Identity, new Vector3(0, BeamHeight + 0.05f, 0)), _paint); // a warning stripe on its face
        foreach (var side in new[] { -1f, 1f })
        {
            Part(new Vector3(0.14f, BeamHeight + 0.2f, 0.14f), new Transform3D(Basis.Identity, new Vector3(side * Props.RailTrack.Gauge / 2f, (BeamHeight + 0.2f) / 2f, 0)), _iron); // uprights on the rails
            Part(new Vector3(0.12f, 0.12f, 1f), new Transform3D(new Basis(Vector3.Right, -0.6f), new Vector3(side * Props.RailTrack.Gauge / 2f, 0.35f, -0.4f)), _wood); // back braces, away from the cart (−Z: on toward the stop)
            Part(new Vector3(0.18f, 0.18f, 0.12f), new Transform3D(Basis.Identity, new Vector3(side * 0.55f, BeamHeight, 0.2f)), _iron); // buffer heads toward the cart (+Z: the way it comes from)
        }
    }

    private void Part(Vector3 size, Transform3D local, Material material)
    {
        _look.AddChild(new MeshInstance3D { Mesh = new BoxMesh { Size = size }, MaterialOverride = material, Transform = local });
        _parts.Add((size, local, material));
    }

    /// <summary>The cart's front meets it: gone in splinters, a thud, a few timbers tumbling ahead. No damage.</summary>
    public void Smash(Node3D root, Vector3 cartVelocity)
    {
        if (Smashed) return;
        Smashed = true;
        _look.Visible = false;
        _shape.Disabled = true;
        CollisionLayer = 0;
        var at = GlobalPosition + Vector3.Up * BeamHeight;
        Audio.Synth.PlayAt(root, Audio.Synth.Thud(), at, -1f);
        EventBus.Instance.PlaySfx("timber_smash"); // placeholder: plan 6 adds the sound
        var splinters = new GpuParticles3D
        {
            Name = "Splinters", Amount = 70, Lifetime = 1.2, OneShot = true, Explosiveness = 0.95f, Emitting = true,
            ProcessMaterial = new ParticleProcessMaterial
            {
                Direction = -GlobalBasis.Z + Vector3.Up * 0.6f, Spread = 60f, InitialVelocityMin = 2.5f, InitialVelocityMax = 6f, Gravity = new Vector3(0, -9f, 0),
                ScaleMin = 0.6f, ScaleMax = 1.4f, EmissionShape = ParticleProcessMaterial.EmissionShapeEnum.Box, EmissionBoxExtents = new Vector3(0.8f, 0.2f, 0.15f),
            },
            DrawPass1 = new BoxMesh { Size = new Vector3(0.05f, 0.03f, 0.18f) },
            MaterialOverride = _wood,
        };
        root.AddChild(splinters);
        splinters.GlobalPosition = at;
        GetTree().CreateTimer(2f).Timeout += () => { if (IsInstanceValid(splinters)) splinters.QueueFree(); };
        for (var i = 0; i < _parts.Count; i += 2) // a few timbers fly on ahead of the cart
        {
            var (size, local, material) = _parts[i];
            var body = new RigidBody3D { Name = "BufferTimber", CollisionLayer = 0, CollisionMask = 1, Mass = 20f };
            body.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = size } });
            body.AddChild(new MeshInstance3D { Mesh = new BoxMesh { Size = size }, MaterialOverride = material });
            root.AddChild(body);
            body.GlobalTransform = GlobalTransform * local;
            body.LinearVelocity = cartVelocity * 0.8f + Vector3.Up * (2f + i * 0.3f) + GlobalBasis.X * ((i % 3) - 1f);
            body.AngularVelocity = new Vector3(2f, (i % 2) * 3f - 1.5f, 1f);
            GetTree().CreateTimer(TimberSeconds).Timeout += () => { if (IsInstanceValid(body)) body.QueueFree(); };
        }
    }

    /// <summary>Standing again, or (<paramref name="smashed"/>) gone without a sound: the cart is past it already.</summary>
    public void Restore(bool smashed = false)
    {
        Smashed = smashed;
        if (_look == null) return;
        _look.Visible = !smashed;
        _shape.Disabled = smashed;
        CollisionLayer = smashed ? 0u : 1u;
    }
}
