using DungeonCrawler.Core;
using DungeonCrawler.Dungeon.Generation;
using DungeonCrawler.Party;
using Godot;

namespace DungeonCrawler.Dungeon.Props;

/// <summary>
/// Seals a vault. KeyDoor opens with an iron key or a lockpick; LeverGate only from its lever. Until someone with the
/// Perception for it notices, the door is a stretch of wall like any other: the same stone, no prompt, no way in. Once
/// noticed, a lock plate (or the seams of a gate) shows on the faces that open onto floor and it can be used.
/// </summary>
public partial class Door : StaticBody3D, IInteractable
{
    public VaultKind Kind { get; set; } = VaultKind.KeyDoor;
    public int VaultIndex { get; set; }
    public GridPos Cell { get; set; }
    public bool IsOpen { get; private set; }
    /// <summary>Someone has made out the door in the wall; before that it is just wall.</summary>
    public bool Detected { get; private set; }

    private readonly List<Node3D> _marks = new();

    /// <summary>A puzzle gate's name (Gate II), hung over it; null for an ordinary vault door.</summary>
    public string? GateLabel { get; set; }
    /// <summary>Whether this door belongs to the floor's objective (index from <see cref="Adventure.FloorContents.ExtraIndexBase"/> up): the objective, not the save's vault list, owns its state.</summary>
    public bool IsObjectiveGate => VaultIndex >= Adventure.FloorContents.ExtraIndexBase;

    public string Prompt => IsOpen || !Detected ? "" : Kind == VaultKind.KeyDoor ? "Locked door: use a key or pick the lock" : IsObjectiveGate ? $"{GateLabel ?? "Gate"}: shut; the levers move it" : "Sealed gate: find its lever";

    public override void _Ready()
    {
        var mesh = GetNode<MeshInstance3D>("Mesh");
        var theme = Main.Instance?.Builder.CurrentTheme;
        // the wall's own stone, a full cell of it, so nothing gives the door away
        mesh.Mesh = new BoxMesh { Size = new Vector3(DungeonBuilder.CellSize, DungeonBuilder.CellSize, DungeonBuilder.CellSize) };
        mesh.SetSurfaceOverrideMaterial(0, DungeonBuilder.Material(theme?.WallTexture ?? "", theme?.WallColor ?? new Color(0.45f, 0.42f, 0.40f), theme?.MetersPerTile ?? 2f));
        if (GetNode<CollisionShape3D>("Collision").Shape is BoxShape3D box) box.Size = new Vector3(DungeonBuilder.CellSize, DungeonBuilder.CellSize, DungeonBuilder.CellSize);
        CollisionLayer = 1; // wall, not yet a thing to use
        BuildMarks();
        if (GateLabel != null)
            AddChild(new Label3D
            {
                Text = GateLabel, FontSize = 48, PixelSize = 0.006f, Modulate = new Color(0.85f, 0.8f, 0.6f), OutlineSize = 10,
                Billboard = BaseMaterial3D.BillboardModeEnum.Enabled, Position = new Vector3(0, DungeonBuilder.CellSize / 2f + 0.6f, 0), VisibilityRangeEnd = 16f, VisibilityRangeEndMargin = 3f,
            });
    }

    /// <summary>The gate slides back down: a puzzle gate the levers shut again.</summary>
    public void Close(bool silent = false)
    {
        if (!IsOpen) return;
        IsOpen = false;
        if (!silent) EventBus.Instance.PlaySfx("door");
        CollisionLayer = Detected ? 9u : 1u;
        GetNode<MeshInstance3D>("Mesh").Visible = true;
        foreach (var m in _marks) m.Visible = Detected;
        Main.Instance?.Builder.SetDoorClosed(Cell);
        if (!silent) EventBus.Instance.Log($"{GateLabel ?? "A gate"} grinds shut.");
        EventBus.Instance.RaisePropChanged(Net.NetRefs.Door(VaultIndex), "closed");
    }

    public void SetOpen(bool open, bool silent = false) { if (open) Open(silent); else Close(silent); }

    /// <summary>A lock plate with a keyhole (KeyDoor) or the seams of a gate (LeverGate), on every face a corridor or room touches; hidden until noticed.</summary>
    private void BuildMarks()
    {
        var layout = Main.Instance?.Builder.Layout;
        var dirs = new[] { (new GridPos(0, 1), Vector3.Back, 0f), (new GridPos(0, -1), Vector3.Forward, Mathf.Pi), (new GridPos(1, 0), Vector3.Right, -Mathf.Pi / 2f), (new GridPos(-1, 0), Vector3.Left, Mathf.Pi / 2f) };
        var iron = new StandardMaterial3D { AlbedoColor = new Color(0.12f, 0.11f, 0.1f), Metallic = 0.6f, Roughness = 0.6f };
        var dark = new StandardMaterial3D { AlbedoColor = new Color(0.05f, 0.045f, 0.04f), Roughness = 1f };
        var half = DungeonBuilder.CellSize / 2f + 0.03f;
        foreach (var (step, normal, yaw) in dirs)
        {
            if (layout != null ? !layout.IsWalkable(Cell + step) : normal != Vector3.Back) continue;
            var frame = new Node3D { Position = normal * half, Rotation = new Vector3(0, yaw, 0), Visible = false };
            if (Kind == VaultKind.KeyDoor)
            {
                var brass = new StandardMaterial3D { AlbedoColor = new Color(0.45f, 0.36f, 0.18f), Metallic = 0.8f, Roughness = 0.45f };
                frame.AddChild(new MeshInstance3D { Mesh = new BoxMesh { Size = new Vector3(0.2f, 0.3f, 0.04f) }, MaterialOverride = iron, Position = new Vector3(0.6f, 1.25f, 0) });
                frame.AddChild(new MeshInstance3D { Mesh = new TorusMesh { InnerRadius = 0.025f, OuterRadius = 0.045f, Rings = 12, RingSegments = 8 }, MaterialOverride = brass, Position = new Vector3(0.6f, 1.29f, 0.025f), Rotation = new Vector3(Mathf.Pi / 2f, 0, 0) });
                frame.AddChild(new MeshInstance3D { Mesh = new BoxMesh { Size = new Vector3(0.035f, 2.2f, 0.05f) }, MaterialOverride = dark, Position = new Vector3(-1.2f, 1.6f, 0), Rotation = new Vector3(0, 0, Mathf.DegToRad(2f)) });
            }
            else
            {
                // a gate's seams: a faint rectangle where the slab meets the stone
                frame.AddChild(new MeshInstance3D { Mesh = new BoxMesh { Size = new Vector3(0.035f, 3.4f, 0.05f) }, MaterialOverride = dark, Position = new Vector3(-1.5f, 1.75f, 0) });
                frame.AddChild(new MeshInstance3D { Mesh = new BoxMesh { Size = new Vector3(0.035f, 3.4f, 0.05f) }, MaterialOverride = dark, Position = new Vector3(1.5f, 1.75f, 0) });
                frame.AddChild(new MeshInstance3D { Mesh = new BoxMesh { Size = new Vector3(3.0f, 0.035f, 0.05f) }, MaterialOverride = dark, Position = new Vector3(0, 3.45f, 0) });
            }
            AddChild(frame);
            _marks.Add(frame);
        }
    }

    /// <summary>Someone made out the door: the marks show and it can be used.</summary>
    public void Reveal()
    {
        if (Detected || IsOpen) return;
        Detected = true;
        CollisionLayer = 9;
        foreach (var m in _marks) m.Visible = true;
        EventBus.Instance.RaisePropChanged(Net.NetRefs.Door(VaultIndex), "revealed");
    }

    public void Interact(PartyController party, Adventurer actor)
    {
        if (IsOpen) return;
        if (Kind == VaultKind.LeverGate)
        {
            EventBus.Instance.Log(IsObjectiveGate ? $"{GateLabel ?? "The gate"} will not budge. The levers on this floor move it." : "The gate will not budge. A lever somewhere on this floor must open it.");
            return;
        }
        if (party.TryUnlock("the door")) Open();
    }

    /// <param name="silent">No sound or log line: a client applying what the server already announced.</param>
    public void Open(bool silent = false)
    {
        if (IsOpen) return;
        IsOpen = true;
        Detected = true;
        if (!silent) EventBus.Instance.PlaySfx("door");
        CollisionLayer = 0;
        GetNode<MeshInstance3D>("Mesh").Visible = false;
        foreach (var m in _marks) m.Visible = false;
        Main.Instance.Builder.SetDoorOpen(Cell);
        if (!silent) EventBus.Instance.Log(Kind == VaultKind.KeyDoor ? "The door swings open." : GateLabel != null ? $"{GateLabel} grinds open." : "Somewhere, a gate grinds open.");
        EventBus.Instance.RaisePropChanged(Net.NetRefs.Door(VaultIndex), "opened");
    }
}
