using DungeonCrawler.Core;
using Godot;

namespace DungeonCrawler.Dungeon.Rails;

/// <summary>
/// A forge drop hammer over the party line: an iron block on a rod through a timber crossbeam on two posts outside the lane.
/// It runs on the floor's metronome (<see cref="SetPieceRules.Phase"/> on <see cref="DungeonBuilder.FloorTick"/>, offset by
/// <see cref="Index"/>): up, a red glow for the 1.5 s warning, then it drops <see cref="Drop"/> metres onto the line, rests and
/// rises. Its rest is clear of the tallest seated head; at the bottom it meets the tub's rim. Dressing with no collision overhead
/// (the posts are solid): the <see cref="RailEncounterController"/> judges what is under it at the drop. Built on the server (a
/// client's FloorTick follows the server's floor clock, so a client copy falls in step with the server's).
/// </summary>
public partial class DropHammer : Node3D
{
    /// <summary>The block's underside at rest, above the track: clear of the tallest seated head (<see cref="LowBeam.Underside"/>).</summary>
    public const float RestBottom = 2.9f;
    /// <summary>How far the block falls: down to the tub's rim.</summary>
    public const float Drop = 1.5f;
    /// <summary>Half the block's length along the line.</summary>
    public const float HalfLength = 0.45f;
    const float HeadHeight = 0.5f, HeadWidth = 1.2f, RodLength = 1.9f, PostOut = 1.35f, PostSize = 0.26f;
    const float BeamHeight = RestBottom + HeadHeight + 0.3f;
    const float FallSeconds = 0.15f, RiseSeconds = 0.75f;

    /// <summary>The party line's S under the hammer.</summary>
    public float S { get; init; }
    /// <summary>Its place in the row: the metronome's offset.</summary>
    public int Index { get; init; }
    /// <summary>The phase the judge last saw (the encounter controller keeps it to catch the drop and the glow once each).</summary>
    public HammerPhase Judged { get; set; } = HammerPhase.Up;

    private readonly StandardMaterial3D _wood = new() { AlbedoColor = new Color(0.27f, 0.18f, 0.1f), Roughness = 0.9f };
    private readonly StandardMaterial3D _iron = new() { AlbedoColor = new Color(0.16f, 0.15f, 0.14f), Metallic = 0.75f, Roughness = 0.5f };
    private readonly StandardMaterial3D _head = new() { AlbedoColor = new Color(0.2f, 0.17f, 0.15f), Metallic = 0.7f, Roughness = 0.5f, EmissionEnabled = true, Emission = new Color(1f, 0.12f, 0.05f), EmissionEnergyMultiplier = 0f };
    private Node3D _block = null!;
    private OmniLight3D _light = null!;

    /// <summary>Stand across the line at <paramref name="frame"/> (the track's frame at <see cref="S"/>).</summary>
    public void Place(Transform3D frame)
    {
        GlobalTransform = new Transform3D(frame.Basis.Orthonormalized(), frame.Origin);
        var height = BeamHeight + 0.2f;
        foreach (var side in new[] { -1f, 1f })
        {
            var post = new StaticBody3D { Name = side < 0 ? "PostL" : "PostR", CollisionLayer = 1, CollisionMask = 0, Position = new Vector3(side * PostOut, height / 2f, 0) };
            post.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = new Vector3(PostSize, height, PostSize) } });
            post.AddChild(new MeshInstance3D { Mesh = new BoxMesh { Size = new Vector3(PostSize, height, PostSize) }, MaterialOverride = _wood });
            AddChild(post);
        }
        AddChild(new MeshInstance3D { Name = "Crossbeam", Mesh = new BoxMesh { Size = new Vector3(PostOut * 2f + PostSize + 0.3f, 0.3f, 0.34f) }, MaterialOverride = _wood, Position = new Vector3(0, BeamHeight + 0.05f, 0) });
        AddChild(new MeshInstance3D { Name = "Guide", Mesh = new BoxMesh { Size = new Vector3(0.36f, 0.4f, 0.4f) }, MaterialOverride = _iron, Position = new Vector3(0, BeamHeight + 0.05f, 0) });
        _block = new Node3D { Name = "Block", Position = new Vector3(0, RestBottom, 0) };
        AddChild(_block);
        _block.AddChild(new MeshInstance3D { Name = "Head", Mesh = new BoxMesh { Size = new Vector3(HeadWidth, HeadHeight, HalfLength * 2f) }, MaterialOverride = _head, Position = new Vector3(0, HeadHeight / 2f, 0) });
        _block.AddChild(new MeshInstance3D { Name = "Face", Mesh = new BoxMesh { Size = new Vector3(HeadWidth + 0.06f, 0.08f, HalfLength * 2f + 0.06f) }, MaterialOverride = _iron, Position = new Vector3(0, 0.04f, 0) });
        _block.AddChild(new MeshInstance3D { Name = "Rod", Mesh = new CylinderMesh { TopRadius = 0.07f, BottomRadius = 0.07f, Height = RodLength, RadialSegments = 8 }, MaterialOverride = _iron, Position = new Vector3(0, HeadHeight + RodLength / 2f, 0) });
        _light = new OmniLight3D { Name = "Glow", OmniRange = 4.5f, LightEnergy = 0f, LightColor = new Color(1f, 0.15f, 0.06f), ShadowEnabled = false, Position = new Vector3(0, -0.3f, 0) };
        _block.AddChild(_light);
    }

    /// <summary>
    /// The hammer's cues carry this far: a walker near it hears it, a cart held at stop 3 does not (<see cref="SetPieceRules.CueRange"/>
    /// of its distance to stop 3, set at the floor's build on every role; 30 m until then).
    /// </summary>
    public float CueRange { get; set; } = SetPieceRules.MaxCueRange;
    /// <summary>A phase seen this late (a floor clock that jumped: a load, a client's catch-up) is shown silently.</summary>
    const int LateTicks = 30;
    private HammerPhase _heard;
    private int _heardTick = int.MinValue;

    public override void _Process(double delta)
    {
        if (_block == null || Main.Instance?.Builder is not { } builder) return;
        var phase = SetPieceRules.Phase(builder.FloorTick, Index, out var into);
        Hear(phase, builder.FloorTick);
        var fall = Mathf.Clamp(into / FallSeconds, 0f, 1f);
        var down = phase switch
        {
            HammerPhase.Fall => fall * fall,
            HammerPhase.Down => 1f,
            HammerPhase.Rise => 1f - Mathf.Clamp(into / RiseSeconds, 0f, 1f),
            _ => 0f,
        };
        _block.Position = new Vector3(0, RestBottom - Drop * down, 0);
        // the warning: the head heats red through the glow and stays hot through the blow
        var heat = phase switch
        {
            HammerPhase.Glow => 0.35f + 0.65f * Mathf.Clamp(into / 1.5f, 0f, 1f),
            HammerPhase.Fall or HammerPhase.Down => 1f,
            _ => 0f,
        };
        var pulse = phase == HammerPhase.Glow ? 0.75f + 0.25f * Mathf.Sin(into * 18f) : 1f;
        _head.EmissionEnergyMultiplier = 3f * heat * pulse;
        _light.LightEnergy = 2.5f * heat * pulse;
    }

    /// <summary>
    /// The wind as the glow starts and the blow as it falls, at the hammer (every role but a dedicated server, armed ride or not: it hurts
    /// walkers whenever it falls). The first frame after a build or a load, and a floor clock that jumped, only note the phase.
    /// </summary>
    private void Hear(HammerPhase phase, int tick)
    {
        var quiet = _heardTick == int.MinValue || tick < _heardTick || tick - _heardTick > LateTicks;
        var was = _heard;
        _heard = phase;
        _heardTick = tick;
        if (quiet || phase == was || Net.NetSession.IsServer || !SetPieceRules.CueAudible(CueRange)) return; // a range of 0 plays nothing (0 would be unlimited)
        if (phase == HammerPhase.Glow) RideAudio.At("hammer_wind", GlobalPosition + Vector3.Up * BeamHeight, 0f, CueRange);
        else if (was == HammerPhase.Glow) RideAudio.At("hammer_fall", GlobalPosition + Vector3.Up * 1.5f, 0f, CueRange); // a long frame may pass the fall itself
    }
}
