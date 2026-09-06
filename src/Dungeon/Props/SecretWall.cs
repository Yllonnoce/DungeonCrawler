using DungeonCrawler.Core;
using DungeonCrawler.Dungeon.Generation;
using DungeonCrawler.Party;
using Godot;

namespace DungeonCrawler.Dungeon.Props;

/// <summary>A wall that is really a door. Hidden until the party's scout notices the seam; E opens it.</summary>
public partial class SecretWall : Area3D, IInteractable
{
    public int Index { get; set; }
    public GridPos Cell { get; set; }
    public int Level { get; set; }
    public bool Detected { get; private set; }
    public bool IsOpen { get; private set; }

    private MeshInstance3D _seam = null!;
    /// <summary>A hairline crack down the wall and a shorter one across it, dark and unlit, on every face that a corridor or room touches.</summary>
    private void BuildCracks()
    {
        var layout = Main.Instance?.Builder.Layout;
        var faces = new List<(Vector3 normal, float yaw)>();
        var dirs = new[] { (new GridPos(0, 1), Vector3.Back, 0f), (new GridPos(0, -1), Vector3.Forward, Mathf.Pi), (new GridPos(1, 0), Vector3.Right, -Mathf.Pi / 2f), (new GridPos(-1, 0), Vector3.Left, Mathf.Pi / 2f) };
        foreach (var (step, normal, yaw) in dirs)
            if (layout == null ? normal == Vector3.Back : layout.IsWalkable(Cell + step)) faces.Add((normal, yaw));
        var dark = new StandardMaterial3D { AlbedoColor = new Color(0.05f, 0.045f, 0.04f), Roughness = 1f };
        var half = DungeonBuilder.CellSize / 2f + 0.03f;
        foreach (var (normal, yaw) in faces)
        {
            var frame = new Node3D { Position = normal * half, Rotation = new Vector3(0, yaw, 0), Visible = false };
            frame.AddChild(new MeshInstance3D { Mesh = new BoxMesh { Size = new Vector3(0.035f, 2.3f, 0.05f) }, MaterialOverride = dark, Position = new Vector3(0.35f, 1.55f, 0), Rotation = new Vector3(0, 0, Mathf.DegToRad(4f)) });
            frame.AddChild(new MeshInstance3D { Mesh = new BoxMesh { Size = new Vector3(0.9f, 0.03f, 0.05f) }, MaterialOverride = dark, Position = new Vector3(-0.1f, 1.1f, 0), Rotation = new Vector3(0, 0, Mathf.DegToRad(-3f)) });
            AddChild(frame);
            _frames.Add(frame);
        }
    }

    private readonly List<Node3D> _frames = new();

    public string Prompt => Detected && !IsOpen ? "Push the loose stones" : "";

    public override void _Ready()
    {
        CollisionLayer = 0;
        _seam = GetNode<MeshInstance3D>("Seam");
        _seam.Visible = false; // the scene's panel is replaced by cracks on the faces that open onto floor
        BuildCracks();
    }

    public void Reveal()
    {
        if (Detected) return;
        Detected = true;
        foreach (var f in _frames) f.Visible = true;
        CollisionLayer = 8;
        EventBus.Instance.RaisePropChanged(Net.NetRefs.Secret(Index), "revealed");
    }

    public void Interact(PartyController party, Adventurer actor)
    {
        if (!Detected || IsOpen) return;
        Open();
        EventBus.Instance.Log("Stones grind aside, revealing a hidden alcove.");
    }

    public void Open(bool silent = false)
    {
        if (IsOpen) return;
        IsOpen = true;
        Detected = true;
        if (!silent) EventBus.Instance.PlaySfx("secret");
        EventBus.Instance.RaisePropChanged(Net.NetRefs.Secret(Index), "opened");
        foreach (var f in _frames) f.Visible = false;
        CollisionLayer = 0;
        Main.Instance.Builder.OpenSecret(Cell, Level);
    }
}
