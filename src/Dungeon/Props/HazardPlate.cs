using DungeonCrawler.Adventure;
using DungeonCrawler.Enemies;
using DungeonCrawler.Party;
using Godot;

namespace DungeonCrawler.Dungeon.Props;

/// <summary>
/// The pressure plate of a lethal hazard. Anything with a body sets it off: a party member, an enemy, an NPC, or a
/// placed trap springing on it. Hidden until Perception reveals it (like a floor trap's slab); disabled while its
/// hazard is doing anything but waiting.
/// </summary>
public partial class HazardPlate : Area3D
{
    public event Action<Node3D>? Pressed;
    public bool Enabled { get; set; } = true;
    public bool Detected { get; private set; }

    private MeshInstance3D _mesh = null!;

    public override void _Ready()
    {
        CollisionLayer = 0;
        CollisionMask = 2 | 4 | 8; // adventurers, enemies, NPCs
        var shape = new CollisionShape3D { Shape = new BoxShape3D { Size = new Vector3(2.4f, 0.6f, 2.4f) }, Position = new Vector3(0, 0.3f, 0) };
        AddChild(shape);
        _mesh = new MeshInstance3D { Mesh = new BoxMesh { Size = new Vector3(2.2f, 0.08f, 2.2f) }, Position = new Vector3(0, 0.04f, 0), Visible = false };
        _mesh.SetSurfaceOverrideMaterial(0, new StandardMaterial3D { AlbedoColor = new Color(0.2f, 0.19f, 0.18f), Roughness = 1f });
        AddChild(_mesh);
        BodyEntered += OnBodyEntered;
    }

    /// <summary>Noticed: the slab shows as slightly wrong stone, like a revealed trap.</summary>
    public void Reveal()
    {
        if (Detected) return;
        Detected = true;
        _mesh.Visible = true;
    }

    private void OnBodyEntered(Node3D body)
    {
        if (!Enabled || Net.NetSession.IsClient) return;
        var counts = body switch
        {
            Adventurer a => !a.Downed,
            Enemy e => e.IsAlive,
            Npc n => n.IsAlive,
            _ => false,
        };
        if (Net.NetSession.Instance?.TestServer == true) GD.Print($"[net-server] plate {GetParent().Name} touched by {body.Name} counts={counts}");
        if (counts) Pressed?.Invoke(body);
    }

    /// <summary>A placed trap sprang on this cell, an arrow landed here: the plate counts it as a step.</summary>
    public void Trip(Node3D by)
    {
        if (!Enabled || Net.NetSession.IsClient) return;
        Pressed?.Invoke(by);
    }
}
