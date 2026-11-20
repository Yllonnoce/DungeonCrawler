using DungeonCrawler.Core;
using DungeonCrawler.Dungeon.Props;
using DungeonCrawler.Net;
using Godot;

namespace DungeonCrawler.Dungeon.Rails;

/// <summary>
/// A timber-and-iron buffer stop across the party line just short of the last stop: a heavy beam on two iron uprights, braced
/// back. Solid to walkers (layer 1) while it stands. The cart's front smashes it (<see cref="Smash"/>): it vanishes in a burst of
/// splinters and a few tumbling timbers (<see cref="TimberDebris"/>: cosmetic, freed after <see cref="TimberDebris.TimberSeconds"/>), with a thud. It does no harm.
/// <see cref="Restore"/> stands it up again. The server tells "smashed" and "restored" (<see cref="NetRef"/>); a client's
/// copy shows them (<see cref="ShowSmashed"/>, with its splinters and timbers).
/// </summary>
public partial class BufferStop : StaticBody3D, ISoundPoint
{
    /// <summary>Where the smash is heard, on every role: at the beam.</summary>
    public Vector3 SoundPoint => GlobalPosition + Vector3.Up * BeamHeight;

    /// <summary>From the buffer's middle to the face the cart meets.</summary>
    public const float HalfDepth = 0.3f;
    const float BeamHeight = 0.65f, Width = 1.7f;

    /// <summary>The party line's S at the buffer's middle.</summary>
    public float S { get; init; }
    public float FaceS => S - HalfDepth;
    public bool Smashed { get; private set; }
    /// <summary>Its wire name ("buffer:{piece}"), set by the builder; empty: not told.</summary>
    public string NetRef { get; init; } = "";

    private readonly StandardMaterial3D _wood = new() { AlbedoColor = new Color(0.34f, 0.22f, 0.12f), Roughness = 0.9f };
    private readonly StandardMaterial3D _iron = new() { AlbedoColor = new Color(0.16f, 0.15f, 0.14f), Metallic = 0.75f, Roughness = 0.5f };
    private readonly StandardMaterial3D _paint = new() { AlbedoColor = new Color(0.75f, 0.6f, 0.1f), Roughness = 0.7f };
    private Node3D _look = null!;
    private CollisionShape3D _shape = null!;
    private readonly List<(Vector3 size, Transform3D local, Material material)> _parts = new();
    private Node3D? _model;

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
        // the mine pack's Blockade over the solid box's size; without it the beam, uprights and braces below stand drawn
        _model = MinePack.Piece("Blockade", new Vector3(Width, BeamHeight + 0.3f, HalfDepth * 2f), keepAspect: false);
        if (_model != null) _look.AddChild(_model);
        Part(new Vector3(Width, 0.32f, 0.3f), new Transform3D(Basis.Identity, new Vector3(0, BeamHeight, 0)), _wood); // the beam
        Part(new Vector3(Width * 0.9f, 0.08f, 0.31f), new Transform3D(Basis.Identity, new Vector3(0, BeamHeight + 0.05f, 0)), _paint); // a warning stripe on its face
        foreach (var side in new[] { -1f, 1f })
        {
            Part(new Vector3(0.14f, BeamHeight + 0.2f, 0.14f), new Transform3D(Basis.Identity, new Vector3(side * Props.RailTrack.Gauge / 2f, (BeamHeight + 0.2f) / 2f, 0)), _iron); // uprights on the rails
            Part(new Vector3(0.12f, 0.12f, 1f), new Transform3D(new Basis(Vector3.Right, -0.6f), new Vector3(side * Props.RailTrack.Gauge / 2f, 0.35f, -0.4f)), _wood); // back braces, away from the cart (−Z: on toward the stop)
            Part(new Vector3(0.18f, 0.18f, 0.12f), new Transform3D(Basis.Identity, new Vector3(side * 0.55f, BeamHeight, 0.2f)), _iron); // buffer heads toward the cart (+Z: the way it comes from)
        }
    }

    /// <summary>One timber or iron of the stop: drawn when the model did not load, and always one of the pieces its smash throws.</summary>
    private void Part(Vector3 size, Transform3D local, Material material)
    {
        if (_model == null) _look.AddChild(new MeshInstance3D { Mesh = new BoxMesh { Size = size }, MaterialOverride = material, Transform = local });
        _parts.Add((size, local, material));
    }

    /// <summary>The cart's front meets it: gone in splinters, a thud, a few timbers tumbling ahead. No damage.</summary>
    public void Smash(Node3D root, Vector3 cartVelocity)
    {
        if (Smashed) return;
        Break(root, cartVelocity);
        RideAudio.At("timber_smash", SoundPoint); // a client's comes from its "smashed" (RideAudio.OnProp), at the same point
        Tell("smashed");
    }

    /// <summary>Client: smashed as on the server, with the thud, the splinters and the timbers (<paramref name="debris"/>; a repair is quiet), no cue.</summary>
    public void ShowSmashed(Node3D root, Vector3 cartVelocity, bool debris = true)
    {
        if (Smashed) return;
        if (debris) Break(root, cartVelocity);
        else Restore(smashed: true);
    }

    /// <summary>Client: standing again.</summary>
    public void ShowRestored() { if (Smashed) Restore(smashed: false); }

    private void Tell(string what)
    {
        if (NetRef.Length > 0 && !NetSession.IsClient) EventBus.Instance?.RaisePropChanged(NetRef, what);
    }

    /// <summary>Gone in splinters with a thud, a few timbers flying on ahead.</summary>
    private void Break(Node3D root, Vector3 cartVelocity)
    {
        Smashed = true;
        _look.Visible = false;
        _shape.Disabled = true;
        CollisionLayer = 0;
        var at = GlobalPosition + Vector3.Up * BeamHeight;
        Audio.Synth.PlayAt(root, Audio.Synth.Thud(), at, -1f);
        TimberDebris.Burst(root, new Transform3D(GlobalBasis, at), Width, _wood);
        TimberDebris.Tumble(root, GlobalTransform, _parts, cartVelocity); // a few timbers fly on ahead of the cart
    }

    /// <summary>Standing again, or (<paramref name="smashed"/>) gone without a sound: the cart is past it already (a load). The quiet "smashed-quiet" is told, which no peer hears.</summary>
    public void Restore(bool smashed = false)
    {
        var was = Smashed;
        Smashed = smashed;
        if (was != smashed) Tell(smashed ? "smashed-quiet" : "restored");
        if (_look == null) return;
        _look.Visible = !smashed;
        _shape.Disabled = smashed;
        CollisionLayer = smashed ? 0u : 1u;
    }
}
