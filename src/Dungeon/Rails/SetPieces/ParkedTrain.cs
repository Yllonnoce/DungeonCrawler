using DungeonCrawler.Dungeon.Props;
using DungeonCrawler.Net;
using Godot;

namespace DungeonCrawler.Dungeon.Rails;

/// <summary>
/// Ride 5's parked train (plan 15 Task 5, spec B.5; Ruling 23): a cart (a <see cref="MineCart.AsChaser"/> tub, <see cref="CartIds.Lift"/>) and four
/// <see cref="TrainWagon"/> tubs behind it (Numbers 1..4, <see cref="TrainWagon.OfLift"/>, wire names "liftwagon:{k}"), standing on the party lane
/// with chocks under the cart's front wheels and a lantern on its last tub's back. Its tubs are never the party's: they are not in
/// <see cref="RailController.Wagons"/>, and every reading of a wagon as the party's checks <c>RailController.WagonAt(w.Number) == w</c>.
/// Standing (<see cref="Stand"/>) its five bodies are shown and solid (layers 1|8); gone (<see cref="Hide"/>, or <see cref="Smash"/>'s five
/// tumbles, layer 0, freed after <see cref="ParkedRules.TumbleSeconds"/>) they are hidden with no collision. <see cref="Alarm"/> blinks the
/// lantern as the ore heap's does. Built on every role by the <see cref="RailEncounterController"/>, which owns its state; a dumb prop here.
/// </summary>
public partial class ParkedTrain : Node3D, ISoundPoint
{
    /// <summary>The tumbling copies' node name (the flows count them).</summary>
    public const string TumbleName = "ParkedTumble";
    /// <summary>Its bodies while standing: solid like a wall, and a thing to use (the interact ray sees layer 8).</summary>
    public const uint StandingLayers = 1 | 8;
    /// <summary>The lantern on the last tub's back, in that tub's space: above the rim on its back wall (+Z is toward lower S, where the party comes from).</summary>
    static readonly Vector3 LanternAt = new(0.45f, MineCart.TubBottom + MineCart.TubHeight + 0.5f, CartRules.HalfLength + 0.12f);

    /// <summary>Its set piece's index (its wire name's number, the same on every peer).</summary>
    public int Index { get; init; }
    /// <summary>Its wire name ("parked:{piece}"); empty: not told.</summary>
    public string NetRef { get; init; } = "";
    /// <summary>The side of the party lane away from the slag line, +1 its frame's +X: the tubs tumble off that side.</summary>
    public int OuterSide { get; set; } = 1;

    public MineCart Cart { get; private set; } = null!;
    public IReadOnlyList<TrainWagon> Wagons => _wagons;
    /// <summary>Its five bodies stand (in the way, or a lift); false while gone.</summary>
    public bool Standing { get; private set; }
    /// <summary>The party's train bears down on it: the lantern blinks.</summary>
    public bool Alarm { get; set; }
    /// <summary>Where its cart's centre stands on the party lane, and its last body's rear face (the face the party's front meets), from <see cref="Pose"/>.</summary>
    public float CartS { get; private set; } = float.NaN;
    public float RearFaceS { get; private set; } = float.NaN;
    /// <summary>The lantern's place in the world (the call's sight check aims at it).</summary>
    public Vector3 RearLantern => _lanternMesh != null && IsInstanceValid(_lanternMesh) ? _lanternMesh.GlobalPosition : GlobalPosition;
    public Vector3 SoundPoint => RearLantern;

    private readonly List<TrainWagon> _wagons = new();
    private readonly StandardMaterial3D _glass = new() { AlbedoColor = new Color(1f, 0.55f, 0.15f), EmissionEnabled = true, Emission = new Color(1f, 0.45f, 0.1f), EmissionEnergyMultiplier = 3f };
    private Material _wood = null!;
    private Node3D _chocks = null!;
    private MeshInstance3D? _lanternMesh;
    private OmniLight3D _lantern = null!;
    private float _blink;
    private bool _built;

    public override void _Ready() => Build();

    private void Build()
    {
        if (_built) return;
        _built = true;
        _wood = DungeonBuilder.Material("weathered_planks", new Color(0.5f, 0.38f, 0.24f), 1.6f);
        Cart = new MineCart { CartId = CartIds.Lift }.AsChaser();
        Cart.Name = "LiftCart";
        AddChild(Cart);
        for (var k = 1; k <= ParkedRules.Wagons; k++)
        {
            var wagon = new TrainWagon { Name = $"LiftWagon{k}", Number = k, OfLift = true, WireRef = NetRefs.LiftWagon(k) };
            AddChild(wagon);
            _wagons.Add(wagon);
        }
        // the chocks: two wooden wedges on the rails before the cart's front wheels (they stay at the place: a lift rolls off them)
        _chocks = new Node3D { Name = "Chocks" };
        AddChild(_chocks);
        foreach (var x in new[] { -RailTrack.Gauge / 2f, RailTrack.Gauge / 2f })
            _chocks.AddChild(new MeshInstance3D { Name = "Chock", Mesh = new BoxMesh { Size = new Vector3(0.14f, 0.14f, 0.24f) }, MaterialOverride = _wood, Position = new Vector3(x, 0.31f, -CartRules.HalfLength + 0.08f) });
        // the lantern on the last tub's back, on its own node (not the Body a tumble copies): a bracket up from the back wall and the lamp
        var last = _wagons[^1];
        last.AddChild(new MeshInstance3D { Name = "LanternPost", Mesh = new BoxMesh { Size = new Vector3(0.05f, 0.6f, 0.05f) }, MaterialOverride = _wood, Position = new Vector3(LanternAt.X, LanternAt.Y - 0.35f, CartRules.HalfLength + 0.04f) });
        _lanternMesh = new MeshInstance3D { Name = "Lantern", Mesh = new BoxMesh { Size = new Vector3(0.18f, 0.24f, 0.18f) }, MaterialOverride = _glass, Position = LanternAt };
        last.AddChild(_lanternMesh);
        _lantern = new OmniLight3D { Name = "LanternLight", OmniRange = 7f, LightEnergy = 1.8f, LightColor = new Color(1f, 0.45f, 0.1f), ShadowEnabled = false, Position = LanternAt + Vector3.Up * 0.05f };
        last.AddChild(_lantern);
        Stand();
    }

    /// <summary>Put the cart's centre at <paramref name="cartS"/> on <paramref name="party"/> (the party lane) and wagon k at cart S − <see cref="TrainRules.Spacing"/>·k; the chocks stand at the cart's front.</summary>
    public void Pose(float cartS, TrackSampler party)
    {
        Build();
        if (!party.Valid) return;
        CartS = cartS;
        RearFaceS = cartS - ParkedRules.Wagons * TrainRules.Spacing - CartRules.HalfLength;
        Cart.Pose(party.Frame(cartS));
        for (var k = 1; k <= _wagons.Count; k++) _wagons[k - 1].Pose(party.Frame(cartS - TrainRules.Spacing * k));
        _chocks.GlobalTransform = Cart.GlobalTransform;
    }

    /// <summary>Its five bodies stand, shown and solid (layers 1|8), the chocks under the cart and the lantern lit.</summary>
    public void Stand()
    {
        Build();
        Standing = true;
        Alarm = false;
        Cart.Visible = true;
        Cart.CollisionLayer = StandingLayers;
        foreach (var wagon in _wagons)
        {
            wagon.SetCoupled(true); // shown (a tub "coupled" here is only shown: it is never the party's)
            wagon.CollisionLayer = StandingLayers;
        }
        _chocks.Visible = true;
        _lantern.LightEnergy = 1.8f;
    }

    /// <summary>Gone with no tumble (a takeover, a lift rolled away, a client's quiet catch-up): hidden, with no collision.</summary>
    public new void Hide() // hides Node3D.Hide on purpose: its five bodies go, the node itself (the chocks' parent) stays
    {
        Build();
        Standing = false;
        Alarm = false;
        Cart.Visible = false;
        Cart.CollisionLayer = 0;
        foreach (var wagon in _wagons) wagon.SetCoupled(false);
        _chocks.Visible = false;
    }

    /// <summary>
    /// The party's train smashes it: its five tubs tumble as the party cart's does (rigid copies on layer 0, the floor only, thrown with
    /// <paramref name="velocity"/>, off its outer side and up, freed after <see cref="ParkedRules.TumbleSeconds"/>), and it is hidden with no collision.
    /// </summary>
    public void Smash(Vector3 velocity)
    {
        if (!Standing) return;
        if (GetParent() is Node3D root && IsInsideTree())
        {
            Tumble(root, Cart, velocity, 0);
            for (var k = 0; k < _wagons.Count; k++) Tumble(root, _wagons[k], velocity, k + 1);
        }
        Hide();
    }

    private void Tumble(Node3D root, Node3D tubBody, Vector3 velocity, int k)
    {
        if (tubBody.GetNodeOrNull<Node3D>("Body") is not { } body) return;
        var tub = new RigidBody3D { Name = $"{TumbleName}{k}", CollisionLayer = 0, CollisionMask = 1, Mass = 300f };
        tub.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = new Vector3(MineCart.TubWidth, MineCart.TubHeight, MineCart.TubLength) }, Position = new Vector3(0, MineCart.TubBottom + MineCart.TubHeight / 2f, 0) });
        tub.AddChild(body.Duplicate());
        root.AddChild(tub);
        tub.GlobalTransform = tubBody.GlobalTransform;
        var basis = tubBody.GlobalTransform.Basis;
        var outward = basis.X * OuterSide;
        tub.LinearVelocity = velocity * (0.8f - 0.12f * k) + outward * (2.5f + 0.4f * k) + Vector3.Up * 4f; // the cart takes the hardest hit, the far tubs less
        tub.AngularVelocity = -basis.Z * (2f + 0.3f * k) * OuterSide + basis.X * 1.5f + Vector3.Up * 1f;
        GetTree().CreateTimer(ParkedRules.TumbleSeconds).Timeout += () => { if (IsInstanceValid(tub)) tub.QueueFree(); };
    }

    public override void _Process(double delta)
    {
        if (!_built || !Standing) return;
        if (!Alarm) { _lantern.LightEnergy = 1.8f; return; }
        _blink += (float)delta * 6f;
        _lantern.LightEnergy = Mathf.Sin(_blink * Mathf.Pi) > 0f ? 3f : 0.4f;
    }
}
