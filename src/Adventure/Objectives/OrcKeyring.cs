using DungeonCrawler.Core;
using DungeonCrawler.Party;
using Godot;

namespace DungeonCrawler.Adventure.Objectives;

/// <summary>
/// The orc's keyring at dead Grisel's belt on the condemned branch of floor 12. A dumb prop: the crew controller answers
/// the interact key through <see cref="PromptFor"/> and <see cref="OnUse"/>, and hides it once the keys are taken.
/// </summary>
public partial class OrcKeyring : StaticBody3D, IInteractable
{
    const string ModelPath = "res://assets/models/kaykit_dungeon/keyring.gltf.glb";

    public Func<Adventurer, string>? PromptFor { get; set; }
    public Action<Adventurer>? OnUse { get; set; }

    public string Prompt => Main.Instance?.Party?.Local is { } me ? PromptFor?.Invoke(me) ?? "" : "";

    public override void _Ready()
    {
        CollisionLayer = 9; // something to use
        CollisionMask = 0;
        var scene = ResourceLoader.Exists(ModelPath) ? ResourceLoader.Load<PackedScene>(ModelPath) : null;
        if (scene?.Instantiate() is Node3D model)
        {
            model.Scale = Vector3.One * 1.6f;
            AddChild(model);
        }
        else
        {
            var iron = new StandardMaterial3D { AlbedoColor = new Color(0.55f, 0.5f, 0.35f), Metallic = 0.7f, Roughness = 0.5f };
            AddChild(new MeshInstance3D { Mesh = new BoxMesh { Size = new Vector3(0.18f, 0.05f, 0.18f) }, MaterialOverride = iron, Position = new Vector3(0, 0.03f, 0) });
        }
        AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = new Vector3(0.5f, 0.3f, 0.5f) }, Position = new Vector3(0, 0.15f, 0) });
    }

    /// <summary>The keys are taken: nothing to see or use here any more.</summary>
    public void Take()
    {
        Visible = false;
        CollisionLayer = 0;
    }

    public void Interact(PartyController party, Adventurer actor)
    {
        if (PromptFor?.Invoke(actor) is { Length: > 0 }) OnUse?.Invoke(actor);
    }
}
