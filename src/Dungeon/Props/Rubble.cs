using Godot;

namespace DungeonCrawler.Dungeon.Props;

/// <summary>
/// Fallen stone, built from boxes in the theme's wall material: mounds bodies path around, loose scatter along walls
/// and corridors, and the columns of a ruined colonnade (standing, snapped, or lying where they fell).
/// </summary>
public static class Rubble
{
    private static int _serial;
    private static string Unique(string stem) => $"{stem}_{_serial++}"; // Godot mangles duplicate sibling names, which the checks read

    private static MeshInstance3D Block(Node3D parent, Vector3 size, Vector3 position, Vector3 rotation, Material material)
    {
        var mi = new MeshInstance3D { Mesh = new BoxMesh { Size = size }, MaterialOverride = material, Position = position, Rotation = rotation };
        parent.AddChild(mi);
        return mi;
    }

    private static void Collider(Node3D parent, Shape3D shape, Vector3 position)
    {
        var body = new StaticBody3D { CollisionLayer = 1, CollisionMask = 0 };
        body.AddChild(new CollisionShape3D { Shape = shape, Position = position });
        parent.AddChild(body);
    }

    /// <summary>A heap about two metres across and knee to waist high; solid.</summary>
    public static Node3D Mound(Node3D parent, Vector3 at, Material stone, Random rng, float scale = 1f)
    {
        var node = new Node3D { Name = Unique("Rubble") };
        parent.AddChild(node);
        node.GlobalPosition = at;
        var count = 10 + rng.Next(6);
        for (var i = 0; i < count; i++)
        {
            var r = (float)rng.NextDouble();
            var radius = r * 1.15f * scale;
            var a = (float)rng.NextDouble() * Mathf.Tau;
            var size = new Vector3(0.3f + (float)rng.NextDouble() * 0.5f, 0.2f + (float)rng.NextDouble() * 0.35f, 0.3f + (float)rng.NextDouble() * 0.5f) * scale;
            var height = (0.75f - r * 0.6f) * scale;                 // higher toward the middle
            var pos = new Vector3(Mathf.Cos(a) * radius, height * 0.5f + size.Y * 0.25f, Mathf.Sin(a) * radius);
            Block(node, size, pos, new Vector3(((float)rng.NextDouble() - 0.5f) * 0.9f, (float)rng.NextDouble() * Mathf.Tau, ((float)rng.NextDouble() - 0.5f) * 0.9f), stone);
        }
        // a couple of broken flagstones leaning on the heap
        for (var i = 0; i < 2; i++)
        {
            var a = (float)rng.NextDouble() * Mathf.Tau;
            Block(node, new Vector3(0.9f, 0.12f, 0.7f) * scale, new Vector3(Mathf.Cos(a) * 1.0f, 0.35f, Mathf.Sin(a) * 1.0f) * scale, new Vector3(0.5f + (float)rng.NextDouble() * 0.4f, a, 0), stone);
        }
        Collider(node, new CylinderShape3D { Radius = 1.15f * scale, Height = 0.55f * scale }, new Vector3(0, 0.27f * scale, 0));
        Collider(node, new CylinderShape3D { Radius = 0.6f * scale, Height = 1.0f * scale }, new Vector3(0, 0.5f * scale, 0));
        return node;
    }

    /// <summary>A few loose stones; nothing to trip over, no collision.</summary>
    public static Node3D Scatter(Node3D parent, Vector3 at, Material stone, Random rng)
    {
        var node = new Node3D { Name = Unique("Scatter") };
        parent.AddChild(node);
        node.GlobalPosition = at;
        var count = 4 + rng.Next(4);
        for (var i = 0; i < count; i++)
        {
            var size = new Vector3(0.12f + (float)rng.NextDouble() * 0.28f, 0.08f + (float)rng.NextDouble() * 0.16f, 0.12f + (float)rng.NextDouble() * 0.28f);
            var pos = new Vector3(((float)rng.NextDouble() - 0.5f) * 1.6f, size.Y * 0.4f, ((float)rng.NextDouble() - 0.5f) * 1.6f);
            Block(node, size, pos, new Vector3(((float)rng.NextDouble() - 0.5f) * 0.4f, (float)rng.NextDouble() * Mathf.Tau, ((float)rng.NextDouble() - 0.5f) * 0.4f), stone);
        }
        return node;
    }

    /// <summary>
    /// One column of a colonnade. Standing: floor to ceiling with base and capital. Snapped: a stub with a jagged top,
    /// the fallen shaft lying beside it and stone scattered around. Both block their cell.
    /// </summary>
    public static Node3D Column(Node3D parent, Vector3 at, float ceiling, Material stone, Random rng, bool fallen, Vector3 lieAlong)
    {
        var node = new Node3D { Name = Unique(fallen ? "ColumnFallen" : "Column") };
        parent.AddChild(node);
        node.GlobalPosition = at;
        const float r = 0.42f;
        var baseMesh = new MeshInstance3D { Mesh = new CylinderMesh { TopRadius = r + 0.1f, BottomRadius = r + 0.22f, Height = 0.35f, RadialSegments = 10 }, MaterialOverride = stone, Position = new Vector3(0, 0.175f, 0) };
        node.AddChild(baseMesh);
        if (!fallen)
        {
            node.AddChild(new MeshInstance3D { Mesh = new CylinderMesh { TopRadius = r, BottomRadius = r, Height = ceiling, RadialSegments = 12 }, MaterialOverride = stone, Position = new Vector3(0, ceiling / 2f, 0) });
            node.AddChild(new MeshInstance3D { Mesh = new CylinderMesh { TopRadius = r + 0.25f, BottomRadius = r + 0.08f, Height = 0.4f, RadialSegments = 10 }, MaterialOverride = stone, Position = new Vector3(0, ceiling - 0.2f, 0) });
            Collider(node, new CylinderShape3D { Radius = r + 0.05f, Height = ceiling - 0.35f }, new Vector3(0, (ceiling - 0.35f) / 2f, 0));
            return node;
        }
        var stub = 0.7f + (float)rng.NextDouble() * 0.8f; // never a step toward anything
        node.AddChild(new MeshInstance3D { Mesh = new CylinderMesh { TopRadius = r, BottomRadius = r, Height = stub, RadialSegments = 12 }, MaterialOverride = stone, Position = new Vector3(0, stub / 2f, 0) });
        for (var i = 0; i < 5; i++)
        {
            var a = i * Mathf.Tau / 5f + (float)rng.NextDouble() * 0.4f;
            Block(node, new Vector3(0.25f, 0.2f + (float)rng.NextDouble() * 0.3f, 0.25f), new Vector3(Mathf.Cos(a) * r * 0.7f, stub + 0.05f, Mathf.Sin(a) * r * 0.7f), new Vector3(0.2f, a, 0.15f), stone);
        }
        Collider(node, new CylinderShape3D { Radius = r + 0.05f, Height = stub + 0.3f }, new Vector3(0, (stub + 0.3f) / 2f, 0));
        // the shaft lies along the row, just off the base, slightly rolled
        var length = 2.4f;
        var shaft = new MeshInstance3D { Mesh = new CylinderMesh { TopRadius = r * 0.95f, BottomRadius = r, Height = length, RadialSegments = 12 }, MaterialOverride = stone };
        node.AddChild(shaft);
        var dir = lieAlong.Normalized();
        shaft.Position = dir * (r + 0.3f + length / 2f) * 0.55f + new Vector3(0, r * 0.9f, 0) + new Vector3(-dir.Z, 0, dir.X) * 0.7f;
        shaft.Rotation = new Vector3(0, Mathf.Atan2(dir.X, dir.Z), 0);
        shaft.RotateObjectLocal(Vector3.Right, Mathf.Pi / 2f);
        shaft.RotateObjectLocal(Vector3.Forward, 0.06f);
        Collider(node, new BoxShape3D { Size = new Vector3(r * 2f, r * 1.8f, length) }, shaft.Position);
        for (var i = 0; i < 3; i++)
            Block(node, new Vector3(0.3f, 0.2f, 0.3f), shaft.Position + new Vector3(((float)rng.NextDouble() - 0.5f) * 1.2f, -r * 0.6f, ((float)rng.NextDouble() - 0.5f) * 1.2f), new Vector3(0, (float)rng.NextDouble() * 3f, 0), stone);
        return node;
    }

    /// <summary>Where the ceiling gave way: a dark cavity over a mound with stone still hanging from its rim.</summary>
    public static void Collapse(Node3D parent, Vector3 at, float ceiling, Material stone, Random rng)
    {
        var node = new Node3D { Name = Unique("Collapse") };
        parent.AddChild(node);
        node.GlobalPosition = at with { Y = at.Y + ceiling - 0.03f };
        node.AddChild(new MeshInstance3D
        {
            Mesh = new CylinderMesh { TopRadius = 1.4f, BottomRadius = 1.4f, Height = 0.02f, RadialSegments = 14 },
            MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color(0.01f, 0.01f, 0.01f), ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded },
        });
        // a little grey light falls through the gap
        node.AddChild(new OmniLight3D { LightColor = new Color(0.7f, 0.78f, 0.9f), LightEnergy = 1.1f, OmniRange = 9f, OmniAttenuation = 1.4f, ShadowEnabled = false, Position = new Vector3(0, -0.6f, 0) });
        var count = 6 + rng.Next(4);
        for (var i = 0; i < count; i++)
        {
            var a = (float)rng.NextDouble() * Mathf.Tau;
            var rad = 1.0f + (float)rng.NextDouble() * 0.5f;
            var size = new Vector3(0.3f + (float)rng.NextDouble() * 0.4f, 0.25f + (float)rng.NextDouble() * 0.5f, 0.3f + (float)rng.NextDouble() * 0.4f);
            Block(node, size, new Vector3(Mathf.Cos(a) * rad, -size.Y * 0.4f, Mathf.Sin(a) * rad), new Vector3(((float)rng.NextDouble() - 0.5f) * 0.6f, a, ((float)rng.NextDouble() - 0.5f) * 0.6f), stone);
        }
    }
}
