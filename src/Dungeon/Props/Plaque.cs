using DungeonCrawler.Core;
using DungeonCrawler.Party;
using Godot;

namespace DungeonCrawler.Dungeon.Props;

/// <summary>A stone tablet on a plinth with words cut into it: a clue for the floor's puzzle. Read with E; the text also hangs over it for anyone close.</summary>
public partial class Plaque : StaticBody3D, IInteractable
{
    public int Index { get; set; }
    public string Text { get; set; } = "";
    public string Prompt => "Read the plaque";

    public override void _Ready()
    {
        Name = $"Plaque{Index}";
        CollisionLayer = 9; // solid, and something to use
        CollisionMask = 0;
        var stone = new StandardMaterial3D { AlbedoColor = new Color(0.36f, 0.34f, 0.31f), Roughness = 0.9f };
        var dark = new StandardMaterial3D { AlbedoColor = new Color(0.16f, 0.15f, 0.14f), Roughness = 1f };
        AddChild(new MeshInstance3D { Mesh = new BoxMesh { Size = new Vector3(0.9f, 0.5f, 0.6f) }, MaterialOverride = stone, Position = new Vector3(0, 0.25f, 0) });
        AddChild(new MeshInstance3D { Mesh = new BoxMesh { Size = new Vector3(0.8f, 1.1f, 0.14f) }, MaterialOverride = stone, Position = new Vector3(0, 1.0f, 0), Rotation = new Vector3(Mathf.DegToRad(-12f), 0, 0) });
        AddChild(new MeshInstance3D { Mesh = new BoxMesh { Size = new Vector3(0.62f, 0.8f, 0.02f) }, MaterialOverride = dark, Position = new Vector3(0, 1.02f, 0.085f), Rotation = new Vector3(Mathf.DegToRad(-12f), 0, 0) });
        AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = new Vector3(0.9f, 1.6f, 0.6f) }, Position = new Vector3(0, 0.8f, 0) });
        AddChild(new Label3D
        {
            Text = Text, FontSize = 30, PixelSize = 0.0045f, Modulate = new Color(0.9f, 0.86f, 0.7f), OutlineSize = 8,
            Billboard = BaseMaterial3D.BillboardModeEnum.Enabled, NoDepthTest = false, Position = new Vector3(0, 1.9f, 0),
            AutowrapMode = TextServer.AutowrapMode.Word, Width = 420f, HorizontalAlignment = HorizontalAlignment.Center, VisibilityRangeEnd = 9f, VisibilityRangeEndMargin = 2f,
        });
    }

    public void Interact(PartyController party, Adventurer actor)
    {
        EventBus.Instance.Log($"The plaque reads: \"{Text}\"");
        EventBus.Instance.PlaySfx("ui_open");
    }
}
