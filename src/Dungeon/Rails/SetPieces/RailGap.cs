using Godot;

namespace DungeonCrawler.Dungeon.Rails;

/// <summary>
/// A gap in the party line about <see cref="Length"/> metres long: the track is laid bare there (<see cref="Props.RailTrack.Cut"/>:
/// no rail, no sleeper) and a channel of glowing slag runs under it, with the broken rail ends propped at each lip. Dressing:
/// the <see cref="RailController"/> judges the jump at the lip (<see cref="RailController.SetGap"/>) on the server; every peer builds and cuts it alike.
/// </summary>
public partial class RailGap : Node3D
{
    public const float Length = 5f;
    const float ChannelWidth = 1.6f;

    /// <summary>The party line's S at the gap's middle.</summary>
    public float S { get; init; }
    public float From => S - Length / 2f;
    public float To => S + Length / 2f;

    private readonly StandardMaterial3D _molten = new() { AlbedoColor = new Color(1f, 0.4f, 0.06f), EmissionEnabled = true, Emission = new Color(1f, 0.38f, 0.05f), EmissionEnergyMultiplier = 3f, Roughness = 0.5f };
    private readonly StandardMaterial3D _crust = new() { AlbedoColor = new Color(0.12f, 0.08f, 0.06f), Roughness = 1f };
    private readonly StandardMaterial3D _steel = new() { AlbedoColor = new Color(0.18f, 0.18f, 0.2f), Metallic = 0.8f, Roughness = 0.45f };
    private OmniLight3D _light = null!;
    private float _t;

    /// <summary>Lay the channel along <paramref name="track"/> under the gap. The node itself stays at the origin.</summary>
    public void Build(TrackSampler track)
    {
        for (var s = From + 0.25f; s < To; s += 0.5f)
        {
            var frame = track.Frame(s);
            var basis = frame.Basis.Orthonormalized();
            Piece(new Transform3D(basis, frame.Origin + basis.Y * 0.015f), new Vector3(ChannelWidth, 0.03f, 0.52f), _molten);
            foreach (var side in new[] { -1f, 1f }) // a crusted bank either side
                Piece(new Transform3D(basis, frame.Origin + basis.X * (side * (ChannelWidth / 2f + 0.12f)) + basis.Y * 0.06f), new Vector3(0.26f, 0.12f, 0.52f), _crust);
        }
        foreach (var (lip, dir) in new[] { (From, 1f), (To, -1f) }) // the torn rail ends, bent down into the channel
        {
            var frame = track.Frame(lip);
            var basis = frame.Basis.Orthonormalized();
            foreach (var side in new[] { -1f, 1f })
            {
                var tip = new Transform3D(basis * new Basis(Vector3.Right, dir * 0.35f), frame.Origin + basis.X * (side * Props.RailTrack.Gauge / 2f) + basis.Y * 0.14f - basis.Z * (dir * 0.25f));
                Piece(tip, new Vector3(0.07f, 0.12f, 0.5f), _steel);
            }
        }
        var mid = track.Frame(S);
        _light = new OmniLight3D { Name = "Glow", OmniRange = 6f, LightEnergy = 2f, LightColor = new Color(1f, 0.42f, 0.1f), ShadowEnabled = false };
        AddChild(_light);
        _light.GlobalPosition = mid.Origin + Vector3.Up * 0.8f;
    }

    private void Piece(Transform3D at, Vector3 size, Material material)
    {
        var mesh = new MeshInstance3D { Mesh = new BoxMesh { Size = size }, MaterialOverride = material };
        AddChild(mesh);
        mesh.GlobalTransform = at;
    }

    public override void _Process(double delta)
    {
        if (_light == null) return;
        _t += (float)delta;
        _light.LightEnergy = 2f + 0.4f * Mathf.Sin(_t * 3.1f) + 0.2f * Mathf.Sin(_t * 7.3f); // the slag breathes
    }
}
