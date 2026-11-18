using DungeonCrawler.Core;
using Godot;

namespace DungeonCrawler.Party;

/// <summary>
/// The headset and the two controllers of the body this machine plays (see <see cref="Vr"/>). The rig stands in the
/// world on its own; the body walks under the head, so walls stop a player who walks about their room, and faces
/// where the head faces, so "forward" on the stick is where you look. The body's flat <see cref="Adventurer.Camera"/>
/// is kept on the head: the torch, the interact ray, the melee cast and the weapon in view all ride it unchanged.
/// Left stick walks (click it to sprint), right stick snap-turns; right trigger attacks (hold for a heavy blow),
/// right grip uses what the right hand points at, A jumps, B dodges; left trigger guards or parries, left grip picks the next ability.
/// The interface hangs before you on a <see cref="VrPanel"/>, which takes the hands over while a menu is open.
/// </summary>
public partial class VrRig : XROrigin3D
{
    /// <summary>The body follows the head at most this far in one tick; further is a recentre, and the rig comes to the body instead.</summary>
    private const float MaxStepToHead = 0.5f;
    /// <summary>The hero's eyes are this far above their feet, however tall the player is and whether they stand or sit.</summary>
    private const float EyeHeight = 1.6f;
    /// <summary>The headset's first reports are taken as the player's height after this long, once it has settled.</summary>
    private const float MeasureAfterSeconds = 0.5f;

    private float _lift;          // how far the room is raised so the player's eyes are at the hero's
    private float _measureIn = -1f;
    private bool _measured, _measureHeld;

    private Adventurer _body = null!;
    private XRCamera3D _head = null!;
    private XRController3D _left = null!, _right = null!;
    private bool _turnArmed = true;
    private bool _attackHeld, _useHeld, _nextHeld, _guardHeld, _jumpHeld, _dodgeHeld;

    public static VrRig Attach(Adventurer body)
    {
        var rig = new VrRig { Name = "VrRig", TopLevel = true, _body = body };
        rig._head = new XRCamera3D { Name = "Head", Current = true };
        rig._left = Hand("LeftHand", "left_hand");
        rig._right = Hand("RightHand", "right_hand");
        rig.AddChild(rig._head);
        rig.AddChild(rig._left);
        rig.AddChild(rig._right);
        body.AddChild(rig);
        rig.TakeAim();
        VrPanel.Attach(rig, body, rig._head, rig._left, rig._right);
        rig._head.Position = new Vector3(0, 1.6f, 0); // until the headset reports in
        rig.GlobalTransform = new Transform3D(new Basis(Vector3.Up, body.Rotation.Y), body.GlobalPosition);
        return rig;
    }

    /// <summary>A controller, shown as a small block until the hands have models.</summary>
    private static XRController3D Hand(string name, string tracker)
    {
        var hand = new XRController3D { Name = name, Tracker = tracker };
        hand.AddChild(new MeshInstance3D
        {
            Mesh = new BoxMesh { Size = new Vector3(0.05f, 0.04f, 0.12f) },
            MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color(0.75f, 0.6f, 0.5f), Roughness = 0.9f },
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        });
        return hand;
    }

    /// <summary>How far the pointing hand reaches (the interact ray's own length).</summary>
    private const float AimReach = 3f;

    private RayCast3D? _aim;
    private MeshInstance3D _beam = null!, _spot = null!;
    private StandardMaterial3D _aimLook = null!;
    private static readonly Color AimIdle = new(1f, 1f, 1f, 0.25f), AimOn = new(1f, 0.85f, 0.3f, 0.9f);

    /// <summary>
    /// What you can use is what the right hand points at, not what the head faces: the body's interact ray moves onto
    /// the right controller, with a thin beam and a spot that turn gold on something usable.
    /// </summary>
    private void TakeAim()
    {
        _aim = _body.GetNodeOrNull<RayCast3D>("Camera/InteractRay");
        if (_aim == null) return;
        _aim.Reparent(_right, false);
        _aim.Transform = Transform3D.Identity;
        _aim.TargetPosition = new Vector3(0, 0, -AimReach);
        _aimLook = new StandardMaterial3D { ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, Transparency = BaseMaterial3D.TransparencyEnum.Alpha, AlbedoColor = AimIdle, NoDepthTest = true, RenderPriority = 5 };
        _beam = new MeshInstance3D { Name = "AimBeam", Mesh = new BoxMesh { Size = new Vector3(0.003f, 0.003f, 1f) }, MaterialOverride = _aimLook, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
        _spot = new MeshInstance3D { Name = "AimSpot", Mesh = new SphereMesh { Radius = 0.02f, Height = 0.04f }, MaterialOverride = _aimLook, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
        _right.AddChild(_beam);
        _right.AddChild(_spot);
    }

    /// <summary>The beam from the hand to what it points at (the ray itself is cast by the body each physics tick).</summary>
    private void ShowAim()
    {
        if (_aim == null) return;
        var playing = !_body.Party.InputLocked && !_body.Downed;
        _beam.Visible = _spot.Visible = playing;
        if (!playing) return;
        var reach = _aim.IsColliding() ? Mathf.Clamp(_right.GlobalPosition.DistanceTo(_aim.GetCollisionPoint()), 0.05f, AimReach) : AimReach;
        _beam.Position = new Vector3(0, 0, -reach / 2f);
        _beam.Scale = new Vector3(1, 1, reach);
        _spot.Position = new Vector3(0, 0, -reach);
        _aimLook.AlbedoColor = _body.LookingAt != null ? AimOn : AimIdle;
    }

    /// <summary>The body is no longer played from this headset: give its interact ray back to its camera and go.</summary>
    public void Detach()
    {
        if (_aim != null && IsInstanceValid(_aim))
        {
            _aim.Reparent(_body.Camera, false);
            _aim.Transform = Transform3D.Identity;
        }
        QueueFree();
    }

    /// <summary>The head is the camera the game draws from (the body's flat camera must never be).</summary>
    public void TakeView()
    {
        _body.Camera.Current = false;
        _head.MakeCurrent();
    }

    public override void _Process(double delta)
    {
        if (!_head.Current) TakeView(); // something made another camera current (a possession, a spectate ending)
        CarryCamera();
        ShowAim();
    }

    /// <summary>The body's flat camera rides the head, with everything that hangs from it.</summary>
    private void CarryCamera() => _body.Camera.GlobalTransform = _head.GlobalTransform;

    /// <summary>The way the head faces, laid flat (radians, the body's measure of yaw).</summary>
    public float HeadYaw()
    {
        var f = -_head.GlobalTransform.Basis.Z;
        return new Vector2(f.X, f.Z).LengthSquared() > 1e-4f ? Mathf.Atan2(-f.X, -f.Z) : _body.Rotation.Y; // looking straight up or down: keep the facing
    }

    /// <summary>Before the body moves this tick: step under the head (walls permitting) and face where it faces.</summary>
    public void BeforeMove()
    {
        MeasureHeight((float)GetPhysicsProcessDeltaTime());
        var toHead = _head.GlobalPosition - _body.GlobalPosition;
        toHead.Y = 0f;
        if (toHead.LengthSquared() > 1e-8f && toHead.Length() <= MaxStepToHead) _body.MoveAndCollide(toHead);
        _body.Rotation = new Vector3(0, HeadYaw(), 0);
        CarryCamera();
    }

    /// <summary>After the body moved (the stick, a fall, a wall, a teleport): bring the room along so the head is over the body again.</summary>
    public void AfterMove()
    {
        var off = _body.GlobalPosition - _head.GlobalPosition;
        GlobalPosition += new Vector3(off.X, _body.GlobalPosition.Y + _lift - GlobalPosition.Y, off.Z);
        CarryCamera();
    }

    /// <summary>Seated in a cart (the body's FollowSeat): turn the room about the head by <paramref name="turn"/> (the cart's bend, yaw only) and bring it over the body, level.</summary>
    public void RideAlong(float turn)
    {
        if (Mathf.Abs(turn) > 1e-6f) TurnBy(turn);
        AfterMove();
    }

    /// <summary>
    /// The player's own eye height, measured once the headset reports in and again whenever the right stick is clicked:
    /// the room is raised or lowered so that height is the hero's. Sit, stand or be any height; ducking still ducks.
    /// </summary>
    private void MeasureHeight(float dt)
    {
        var click = _right.IsButtonPressed("primary_click");
        var asked = click && !_measureHeld;
        _measureHeld = click;
        if (!_measured && _measureIn < 0f && !_head.Position.IsEqualApprox(new Vector3(0, EyeHeight, 0))) _measureIn = MeasureAfterSeconds; // the first real pose
        if (_measureIn >= 0f && (_measureIn -= dt) < 0f) asked = true;
        if (!asked) return;
        _measured = true;
        _lift = EyeHeight - _head.Position.Y;
        GD.Print($"[vr] eye height measured {_head.Position.Y:0.00} m, room moved {_lift:+0.00;-0.00} m");
    }

    /// <summary>Turn the room about the head so the head faces <paramref name="yaw"/> (the body's own measure of facing).</summary>
    public void TurnTo(float yaw) => TurnBy(yaw - HeadYaw());

    private void TurnBy(float radians)
    {
        var head = _head.GlobalPosition;
        var t = GlobalTransform;
        t.Origin -= head;
        t = t.Rotated(Vector3.Up, radians);
        t.Origin += head;
        GlobalTransform = t;
        CarryCamera();
    }

    /// <summary>A short buzz in a hand: the blade biting.</summary>
    public void Pulse(bool rightHand, float strength, float seconds)
        => (rightHand ? _right : _left).TriggerHapticPulse("haptic", 0, strength, seconds, 0);

    /// <summary>
    /// The controllers, once a physics tick: what the sticks and the held buttons ask of the body comes back as the
    /// intent the keyboard would give; presses (attack, use, next ability, turn) go straight to the party.
    /// <paramref name="live"/> is false while a menu holds the game.
    /// </summary>
    public InputIntent Read(bool live, Vector2? forced)
    {
        var party = _body.Party;
        var attack = Squeezed(_right, "trigger", _attackHeld);
        var use = Squeezed(_right, "grip", _useHeld);
        var next = Squeezed(_left, "grip", _nextHeld);
        var guard = Squeezed(_left, "trigger", _guardHeld);
        var jump = _right.IsButtonPressed("ax_button");
        var dodge = _right.IsButtonPressed("by_button");
        var intent = InputIntent.None;
        if (live)
        {
            if (attack != _attackHeld) party.VrAttack(attack);
            if (use && !_useHeld) party.VrUse();
            if (next && !_nextHeld) party.VrNextAbility();

            var turn = _right.GetVector2("primary").X;
            if (Mathf.Abs(turn) < Vr.TurnRelease) _turnArmed = true;
            else if (_turnArmed && Mathf.Abs(turn) > Vr.TurnPush)
            {
                _turnArmed = false;
                TurnBy(-Mathf.Sign(turn) * Mathf.DegToRad(Vr.SnapTurnDegrees));
            }

            var stick = _left.GetVector2("primary");
            if (stick.Length() < Vr.StickDeadzone) stick = Vector2.Zero;
            intent = new InputIntent
            {
                Move = forced ?? new Vector2(stick.X, -stick.Y), // a stick pushed away reads +Y; the body's forward is −Y
                Sprint = _left.IsButtonPressed("primary_click"),
                JumpPressed = jump && !_jumpHeld,
                Guard = guard,
                ParryPressed = guard && !_guardHeld,
                DodgePressed = dodge && !_dodgeHeld,
                Live = true,
            };
        }
        else if (forced is { } f) intent = new InputIntent { Move = f, Live = true }; // a menu is open: the hands belong to VrPanel
        (_attackHeld, _useHeld, _nextHeld, _guardHeld, _jumpHeld, _dodgeHeld) = (attack, use, next, guard, jump, dodge);
        return intent;
    }

    /// <summary>A trigger or a grip, with a gap between pressed and let go so a resting finger does not flutter.</summary>
    private static bool Squeezed(XRController3D hand, string action, bool was)
        => hand.GetFloat(action) > (was ? Vr.SqueezeOff : Vr.SqueezeOn);
}
