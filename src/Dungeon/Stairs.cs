using DungeonCrawler.Core;
using DungeonCrawler.Party;
using Godot;

namespace DungeonCrawler.Dungeon;

/// <summary>
/// Stairs between floors, built as a real flight you walk: up-stairs climb from the cell's open side to a landing 2 m
/// up between parapets; down-stairs descend into a well to a landing 2 m below. Reaching the landing asks the player
/// whether to take the stairs (<see cref="Hud"/>); saying yes calls <see cref="Interact"/>, which changes the floor.
/// Direction +1 descends, -1 ascends. Local -Z is the back of the flight, +Z the open front.
/// </summary>
public partial class Stairs : Area3D, IInteractable
{
    [Export] public int Direction { get; set; } = 1;
    [Export] public bool Locked { get; set; }
    /// <summary>Why the way down is sealed: the guardian, or whatever the floor's objective demands.</summary>
    public string LockReason { get; set; } = "The stairs are sealed until the guardian falls.";

    // the flight: eight risers of a quarter metre over 2.8 m of run, then a landing against the back parapet
    public const float Rise = 2f;
    private const float Run = 2.8f;
    private const float FrontZ = 1.8f;
    private const float StepRise = 0.25f;
    private const float StepTread = 0.35f;
    private const int Steps = 7;
    private const float Width = 2.4f;
    private const float LandingBack = -1.7f;
    private const float WellDepth = 0.2f; // stone under the lowest tread of a well

    /// <summary>The stairs are never a look-at target; the landing asks instead.</summary>
    public string Prompt => "";

    /// <summary>What the landing asks.</summary>
    public string Question => Locked ? LockReason : Direction > 0 ? $"Descend to floor {(Game.Instance?.CurrentFloor ?? 0) + 1}?" : $"Climb back to floor {(Game.Instance?.CurrentFloor ?? 0) - 1}?";

    public void Unlock()
    {
        Locked = false;
        Retint();
    }

    private Area3D _landing = null!;
    private readonly HashSet<ulong> _onLanding = new();

    public override void _Ready()
    {
        GetNode<MeshInstance3D>("Mesh").Visible = false;
        CollisionLayer = 0; // not an interact-ray target
        var tier = Main.Instance?.Builder.CurrentTier;
        var theme = Main.Instance?.Builder.CurrentTheme;
        var stone = DungeonBuilder.Material(theme?.WallTexture ?? tier?.WallTexture ?? "", theme?.WallColor ?? tier?.WallColor ?? new Color(0.45f, 0.42f, 0.4f), 1.5f);
        var tread = DungeonBuilder.Material(theme?.FloorTexture ?? tier?.FloorTexture ?? "", (theme?.FloorColor ?? tier?.FloorColor ?? new Color(0.32f, 0.3f, 0.28f)).Darkened(0.1f), 1.2f);
        var body = new StaticBody3D { Name = "Body", CollisionLayer = 1, CollisionMask = 0 };
        AddChild(body);
        var sign = Direction < 0 ? 1f : -1f; // up-stairs rise, down-stairs sink
        var bottom = Direction < 0 ? 0f : -Rise - WellDepth; // where solid stone starts under the flight

        // treads: blocks from the stone under the flight to each step's top (the slope below does the walking)
        for (var i = 0; i < Steps; i++)
        {
            var top = sign * StepRise * (i + 1);
            var z0 = FrontZ - StepTread * (i + 1);
            var z1 = FrontZ - StepTread * i;
            Block(body, new Vector3(-Width / 2f, bottom, z0), new Vector3(Width / 2f, top, z1), tread, solid: false);
        }
        var landingTop = sign * Rise;
        var landingFront = FrontZ - StepTread * Steps;
        Block(body, new Vector3(-Width / 2f, bottom, LandingBack), new Vector3(Width / 2f, landingTop, landingFront), tread, solid: true);
        // the slope the feet actually walk (a capsule cannot climb a quarter-metre step): up through the step nosings
        // from just outside the cell to the landing edge, down from the floor's edge to the landing
        var slopeLen = Mathf.Sqrt(Run * Run + Rise * Rise);
        var angle = Mathf.Atan2(Rise, Run);
        var slope = new CollisionShape3D { Shape = new BoxShape3D { Size = new Vector3(Width, 0.2f, slopeLen) } };
        var slopeFront = Direction < 0 ? FrontZ + StepTread : FrontZ;
        var midZ = slopeFront - Run / 2f;
        var midY = sign * Rise / 2f - 0.1f;
        slope.Transform = new Transform3D(new Basis(Vector3.Right, Direction < 0 ? angle : -angle), new Vector3(0, midY, midZ));
        body.AddChild(slope);

        var parapetTop = Direction < 0 ? Rise + 1.0f : 1.0f; // a waist-high rail above the landing / around the well mouth
        var parapetBottom = Direction < 0 ? 0f : bottom;
        // side parapets along the full cell, the back parapet behind the landing
        Block(body, new Vector3(-2f, parapetBottom, -2f), new Vector3(-Width / 2f, parapetTop, 2f), stone, solid: true);
        Block(body, new Vector3(Width / 2f, parapetBottom, -2f), new Vector3(2f, parapetTop, 2f), stone, solid: true);
        Block(body, new Vector3(-Width / 2f, parapetBottom, -2f), new Vector3(Width / 2f, parapetTop, LandingBack), stone, solid: true);
        if (Direction > 0)
        {
            // the well replaces the cell's floor slab: a strip of floor in front of the first tread
            Block(body, new Vector3(-Width / 2f, -0.2f, FrontZ), new Vector3(Width / 2f, 0f, 2f), tread, solid: true);
        }

        // the landing asks
        _landing = new Area3D { Name = "Landing", CollisionLayer = 0, CollisionMask = 2, Monitoring = true };
        // the landing plus the last tread (a capsule is tall, so only the box's footprint decides when the feet are near)
        var askFront = landingFront + 0.3f;
        var shape = new CollisionShape3D { Shape = new BoxShape3D { Size = new Vector3(Width, 2.8f, askFront - LandingBack) } };
        shape.Position = new Vector3(0, landingTop + 1.1f, (askFront + LandingBack) / 2f);
        _landing.AddChild(shape);
        _landing.BodyEntered += OnLandingEntered;
        _landing.BodyExited += OnLandingExited;
        AddChild(_landing);
        Retint();
    }

    private static void Block(Node3D parent, Vector3 min, Vector3 max, Material material, bool solid)
    {
        var size = max - min;
        if (size.X <= 0 || size.Y <= 0 || size.Z <= 0) return;
        var centre = (min + max) / 2f;
        var mesh = new MeshInstance3D { Mesh = new BoxMesh { Size = size, Material = material }, Position = centre };
        parent.AddChild(mesh);
        if (!solid) return;
        var shape = new CollisionShape3D { Shape = new BoxShape3D { Size = size }, Position = centre };
        parent.AddChild(shape);
    }

    private void OnLandingEntered(Node3D body)
    {
        if (Net.NetSession.Instance?.TestClient == true) GD.Print($"[stairs] {Name} landing entered by {body.Name} (local {(body as Adventurer)?.IsLocal}, hud {Main.Instance?.Hud != null})");
        if (body is not Adventurer { IsLocal: true } me || Main.Instance?.Hud is not { } hud) return;
        if (!_onLanding.Add(me.GetInstanceId())) return;
        Callable.From(() => { if (_onLanding.Contains(me.GetInstanceId())) hud.AskStairs(this, me); }).CallDeferred(); // out of the physics step
    }

    private void OnLandingExited(Node3D body)
    {
        if (body is not Adventurer me) return;
        _onLanding.Remove(me.GetInstanceId());
        Main.Instance?.Hud?.DismissStairs(this);
    }

    /// <summary>Whether anyone stands on the landing right now.</summary>
    public bool Occupied => _onLanding.Count > 0;

    private void Retint()
    {
        var glow = Locked ? new Color(0.9f, 0.15f, 0.1f) : Direction > 0 ? new Color(0.25f, 0.4f, 1f) : new Color(1f, 0.85f, 0.4f);
        var marker = GetNodeOrNull<OmniLight3D>("Glow") ?? new OmniLight3D { Name = "Glow", OmniRange = 5f, LightEnergy = 1.2f, ShadowEnabled = false, Position = new Vector3(0, (Direction < 0 ? Rise : -Rise) + 1.2f, -1.2f) };
        marker.LightColor = glow;
        if (marker.GetParent() == null) AddChild(marker);
    }

    /// <summary>The player said yes on the landing: a client asks the server, everyone else changes the floor here.</summary>
    public void Confirm(PartyController party, Adventurer actor)
    {
        if (Net.NetSession.IsClient)
        {
            if (Net.NetRefResolver.RefOf(this) is { } netRef) Net.NetSession.Instance.Send(new Net.InteractIntent(netRef));
            return;
        }
        Interact(party, actor);
    }

    public void Interact(PartyController party, Adventurer actor)
    {
        if (Locked)
        {
            EventBus.Instance.Log(LockReason);
            return;
        }
        Game.Instance.ChangeFloor(Direction);
    }
}
