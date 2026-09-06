using DungeonCrawler.Core;
using DungeonCrawler.Party;
using Godot;

namespace DungeonCrawler.Dungeon;

/// <summary>Stairs between floors. Direction +1 descends, -1 ascends. Locking (boss floors) arrives in Phase 5.</summary>
public partial class Stairs : Area3D, IInteractable
{
    [Export] public int Direction { get; set; } = 1;
    [Export] public bool Locked { get; set; }

    public string Prompt => Locked ? "The stairs are sealed until the guardian falls" : Direction > 0 ? "Descend" : "Ascend";

    public void Unlock()
    {
        Locked = false;
        Retint();
    }

    private readonly List<MeshInstance3D> _steps = new();

    public override void _Ready()
    {
        var mesh = GetNode<MeshInstance3D>("Mesh");
        mesh.Visible = false;
        var tier = Main.Instance?.Builder.CurrentTier;
        var stone = DungeonBuilder.Material(tier?.WallTexture ?? "", tier?.WallColor ?? new Color(0.45f, 0.42f, 0.4f), 1.5f);
        // three steps: up-stairs rise toward the back wall, down-stairs sink into the floor
        for (var i = 0; i < 3; i++)
        {
            var step = new MeshInstance3D { Mesh = new BoxMesh { Size = new Vector3(2.4f, 0.35f, 0.8f), Material = stone } };
            var z = -1.0f + i * 0.8f;
            var y = Direction < 0 ? 0.17f + (2 - i) * 0.35f : 0.17f - i * 0.5f;
            step.Position = new Vector3(0, y, z);
            AddChild(step);
            _steps.Add(step);
        }
        if (Direction > 0)
        {
            var hole = new MeshInstance3D { Mesh = new BoxMesh { Size = new Vector3(2.6f, 0.05f, 2.6f), Material = new StandardMaterial3D { AlbedoColor = new Color(0.02f, 0.02f, 0.03f) } } };
            hole.Position = new Vector3(0, -1.2f, 0);
            AddChild(hole);
        }
        Retint();
    }

    private void Retint()
    {
        var glow = Locked ? new Color(0.9f, 0.15f, 0.1f) : Direction > 0 ? new Color(0.25f, 0.4f, 1f) : new Color(1f, 0.85f, 0.4f);
        var marker = GetNodeOrNull<OmniLight3D>("Glow") ?? new OmniLight3D { Name = "Glow", OmniRange = 4f, LightEnergy = 1.2f, ShadowEnabled = false, Position = new Vector3(0, 0.8f, 0) };
        marker.LightColor = glow;
        if (marker.GetParent() == null) AddChild(marker);
    }

    public void Interact(PartyController party, Adventurer actor)
    {
        if (Locked)
        {
            EventBus.Instance.Log("The stairs are sealed. The floor's guardian must fall first.");
            return;
        }
        Game.Instance.ChangeFloor(Direction);
    }
}
