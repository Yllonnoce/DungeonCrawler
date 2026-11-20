using Godot;

namespace DungeonCrawler.Dungeon.Rails;

/// <summary>
/// Wood breaking on the line: a one-shot burst of splinters (<see cref="Burst"/>) and a few timbers tumbling on ahead
/// (<see cref="Tumble"/>), all cosmetic (layer 0) and freed by timers. The buffer stop's smash and the barricades share them.
/// </summary>
public static class TimberDebris
{
    /// <summary>A tumbling timber is freed after this long.</summary>
    public const float TimberSeconds = 4f;
    const float SplinterSeconds = 2f;

    /// <summary>
    /// Splinters burst from <paramref name="at"/> (its origin; its −Z is the way they fly, up and on), across <paramref name="width"/>
    /// metres of timber, in <paramref name="wood"/>.
    /// </summary>
    public static void Burst(Node3D root, Transform3D at, float width, Material wood)
    {
        var splinters = new GpuParticles3D
        {
            Name = "Splinters", Amount = 70, Lifetime = 1.2, OneShot = true, Explosiveness = 0.95f, Emitting = true,
            ProcessMaterial = new ParticleProcessMaterial
            {
                Direction = -at.Basis.Z + Vector3.Up * 0.6f, Spread = 60f, InitialVelocityMin = 2.5f, InitialVelocityMax = 6f, Gravity = new Vector3(0, -9f, 0),
                ScaleMin = 0.6f, ScaleMax = 1.4f, EmissionShape = ParticleProcessMaterial.EmissionShapeEnum.Box, EmissionBoxExtents = new Vector3(width / 2f - 0.05f, 0.2f, 0.15f),
            },
            DrawPass1 = new BoxMesh { Size = new Vector3(0.05f, 0.03f, 0.18f) },
            MaterialOverride = wood,
        };
        root.AddChild(splinters);
        splinters.GlobalPosition = at.Origin;
        root.GetTree().CreateTimer(SplinterSeconds).Timeout += () => { if (GodotObject.IsInstanceValid(splinters)) splinters.QueueFree(); };
    }

    /// <summary>
    /// Every other one of <paramref name="parts"/> (box size, its place in <paramref name="frame"/>'s space, its material) flies on ahead of
    /// a cart moving at <paramref name="cartVelocity"/>, tumbling, and is freed after <see cref="TimberSeconds"/>. Each body is named <paramref name="name"/>.
    /// </summary>
    public static void Tumble(Node3D root, Transform3D frame, IReadOnlyList<(Vector3 size, Transform3D local, Material material)> parts, Vector3 cartVelocity, string name = "BufferTimber")
    {
        for (var i = 0; i < parts.Count; i += 2)
        {
            var (size, local, material) = parts[i];
            var body = new RigidBody3D { Name = name, CollisionLayer = 0, CollisionMask = 1, Mass = 20f };
            body.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = size } });
            body.AddChild(new MeshInstance3D { Mesh = new BoxMesh { Size = size }, MaterialOverride = material });
            root.AddChild(body);
            body.GlobalTransform = frame * local;
            body.LinearVelocity = cartVelocity * 0.8f + Vector3.Up * (2f + i * 0.3f) + frame.Basis.X * ((i % 3) - 1f);
            body.AngularVelocity = new Vector3(2f, (i % 2) * 3f - 1.5f, 1f);
            root.GetTree().CreateTimer(TimberSeconds).Timeout += () => { if (GodotObject.IsInstanceValid(body)) body.QueueFree(); };
        }
    }
}
