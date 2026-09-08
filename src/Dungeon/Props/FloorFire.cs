using DungeonCrawler.Characters;
using DungeonCrawler.Combat;
using DungeonCrawler.Core;
using DungeonCrawler.Enemies;
using DungeonCrawler.Party;
using Godot;

namespace DungeonCrawler.Dungeon.Props;

/// <summary>
/// A patch of burning floor on a Burning floor: scorched stone, flames and a glow, and a burn for whoever stands in it,
/// every so often while they do. Bodies path around the cell; the party may wade through. The server does the burning.
/// </summary>
public partial class FloorFire : Area3D
{
    public int Floor { get; set; } = 1;
    private const float Tick = 0.8f;
    private float _t;
    private static readonly Random Rng = new();

    public override void _Ready()
    {
        CollisionLayer = 0;
        CollisionMask = 6; // adventurers and enemies, as a trap plate sees them
        AddChild(new CollisionShape3D { Shape = new CylinderShape3D { Radius = 1.0f, Height = 1.6f }, Position = new Vector3(0, 0.8f, 0) });
        var scorch = new MeshInstance3D
        {
            Mesh = new CylinderMesh { TopRadius = 1.55f, BottomRadius = 1.55f, Height = 0.03f, RadialSegments = 14 },
            MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color(0.05f, 0.04f, 0.03f), Roughness = 1f },
            Position = new Vector3(0, 0.02f, 0), Name = "Scorch",
        };
        AddChild(scorch);
        var flames = Fire.Create(0.6f, 1.5f, 2.2f, 8f);
        flames.Name = "Flames";
        flames.Position = new Vector3(0, 0.08f, 0);
        AddChild(flames);
        // a smaller tongue off to one side, so the patch reads as burning debris rather than a campfire
        var yaw = (float)Rng.NextDouble() * Mathf.Tau;
        var tongue = Fire.Create(0.3f, 0.9f, 0.7f, 4f);
        tongue.Name = "Tongue";
        tongue.Position = new Vector3(Mathf.Cos(yaw) * 0.95f, 0.05f, Mathf.Sin(yaw) * 0.95f);
        AddChild(tongue);
        _t = (float)Rng.NextDouble() * Tick;
    }

    public override void _PhysicsProcess(double delta)
    {
        if (Net.NetSession.IsClient) return; // the server burns and everyone sees the result
        _t -= (float)delta;
        if (_t > 0) return;
        _t = Tick;
        var attack = TrapRules.FloorFire(Floor);
        foreach (var body in GetOverlappingBodies())
        {
            if (body is Adventurer { Downed: false } adv) adv.ReceiveAttack(Trap.TrapStats, attack, "The flames");
            else if (body is Enemy { IsAlive: true } enemy && Main.Instance != null) enemy.ReceiveAttack(Trap.TrapStats, attack, new HitContext(), Main.Instance.Party.Rng, "the flames");
        }
    }
}
