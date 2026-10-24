using DungeonCrawler.Adventure;
using DungeonCrawler.Characters;
using DungeonCrawler.Combat;
using DungeonCrawler.Core;
using DungeonCrawler.Dungeon;
using DungeonCrawler.Dungeon.Generation;
using DungeonCrawler.Dungeon.Props;
using DungeonCrawler.Enemies;
using DungeonCrawler.Items;
using Godot;

namespace DungeonCrawler.Party;

/// <summary>
/// One party member's body in the world. Its <see cref="Control"/> says who drives it: the local player (input through
/// the PartyController), companion AI (<see cref="CompanionBrain"/>: follow in formation, fight by stance, drink
/// potions when hurt, pick up loot they pass), another player over the network, or nobody while parked.
/// </summary>
public partial class Adventurer : CharacterBody3D, IHazardVictim, IInteractable
{
    public const float WalkSpeed = 4.0f;
    public const float SprintMultiplier = 1.6f;
    public const float Acceleration = 14.0f;
    public const float MaxPitchDegrees = 85f;
    public const float EngageRadius = 12f;
    public const float JumpVelocity = 5.4f;
    public const float CrouchSpeedMultiplier = 0.55f;
    /// <summary>Holding forward this long switches to sprinting until forward is released.</summary>
    public const float AutoRunSeconds = 2f;
    public const float FellOutY = -8f;
    public bool Crouching { get; private set; }
    /// <summary>Standing in a pond or stream: slower, and enemies hear the splashing.</summary>
    public bool InWater { get; private set; }
    public bool AutoRunning { get; private set; }
    private float _forwardHeld;
    private CollisionShape3D _collision = null!;
    private CapsuleShape3D _capsule = null!;
    private Vector3 _lastGround;

    public PartyController Party { get; set; } = null!;
    public int Index { get; set; }
    public CharacterState State => Party.Members[Index];
    /// <summary>Who drives this body; see <see cref="ControlKind"/>.</summary>
    public ControlKind Control { get; private set; } = ControlKind.Companion;
    public bool IsLocal => Control == ControlKind.LocalHuman;
    public bool Parked => Control == ControlKind.Parked;
    [Obsolete("Use IsLocal.")]
    public bool IsLead => IsLocal;
    /// <summary>This tick's movement input (local bodies; a server fills it from the wire).</summary>
    public InputIntent Intent { get; set; }
    /// <summary>Where a remote player's body should be; null until the first pose arrives.</summary>
    public RemotePose? Remote { get; set; }
    /// <summary>On a client, the teammate a crushed local body is watching while it waits; null when not spectating.</summary>
    public Adventurer? SpectateTarget { get; private set; }
    private Vector3 _cameraHome;
    /// <summary>Holding block. Only a driven body (local or remote) can guard.</summary>
    public bool Guarding { get; private set; }
    private double _holdStart = -1;
    public bool Downed => !State.IsAlive;
    public Stance Stance { get; set; } = Stance.Defensive;
    public Enemy? Target { get; private set; }
    public IInteractable? LookingAt { get; private set; }
    public Camera3D Camera { get; private set; } = null!;
    public OmniLight3D Torch { get; private set; } = null!;
    public Vector2? ForcedMoveInput { get; set; }
    public float DistanceTravelled { get; private set; }
    public string CurrentActionName => _action.Kind.ToString();

    private ShapeCast3D _meleeCast = null!;
    private RayCast3D _interactRay = null!;
    private MeshInstance3D _body = null!;
    private MeshInstance3D _head = null!;
    private Label3D _label = null!;
    private float _pitch;
    private float _gravity;
    private float _fallStart;
    private bool _falling;
    private float _stepTimer;

    private CompanionAction _action = CompanionAction.Idle;
    private float _thinkTimer;
    private float _potionCooldown;
    private float _unreachable;
    private float _lootTimer;
    private readonly PathMover _mover = new();

    private static readonly Dictionary<string, string> ClassModels = new()
    {
        ["class.warrior"] = "Warrior", ["class.rogue"] = "Rogue", ["class.ranger"] = "Ranger", ["class.mage"] = "Wizard",
    };
    private AnimatedModel? _model;
    private FirstPersonWeapon _fpWeapon = null!;
    private float _flash;
    // clip names differ per Quaternius character: (melee attack, ranged/spell attack, hit reaction)
    private static readonly Dictionary<string, (string melee, string cast, string hit)> ClassClips = new()
    {
        ["class.warrior"] = ("Sword_Attack", "Sword_Attack2", "RecieveHit"),
        ["class.rogue"] = ("Dagger_Attack", "Dagger_Attack2", "RecieveHit"),
        ["class.ranger"] = ("Punch", "Bow_Shoot", "RecieveHit"),
        ["class.mage"] = ("Staff_Attack", "Spell1", "RecieveHit"),
    };
    private (string melee, string cast, string hit) Clips => ClassClips.GetValueOrDefault(State.Class.Id, ("Punch", "Punch", "RecieveHit"));

    private static readonly Dictionary<string, Color> ClassColors = new()
    {
        ["class.warrior"] = new Color(0.55f, 0.58f, 0.65f),
        ["class.rogue"] = new Color(0.25f, 0.22f, 0.3f),
        ["class.ranger"] = new Color(0.3f, 0.5f, 0.3f),
        ["class.mage"] = new Color(0.45f, 0.3f, 0.65f),
    };

    public override void _Ready()
    {
        Camera = GetNode<Camera3D>("Camera");
        _cameraHome = Camera.Position;
        Torch = GetNode<OmniLight3D>("Camera/Torch");
        _meleeCast = GetNode<ShapeCast3D>("Camera/MeleeCast");
        _interactRay = GetNode<RayCast3D>("Camera/InteractRay");
        _body = GetNode<MeshInstance3D>("Body");
        _head = GetNode<MeshInstance3D>("Head");
        _label = GetNode<Label3D>("Label");
        _collision = GetNode<CollisionShape3D>("Collision");
        _capsule = (CapsuleShape3D)_collision.Shape.Duplicate(); // per body, so crouching one does not shrink the others
        _collision.Shape = _capsule;
        _gravity = (float)ProjectSettings.GetSetting("physics/3d/default_gravity", 9.8f).AsDouble();
        var color = ClassColors.GetValueOrDefault(State.Class.Id, new Color(0.5f, 0.5f, 0.5f));
        _body.SetSurfaceOverrideMaterial(0, new StandardMaterial3D { AlbedoColor = color, Roughness = 0.85f });
        _head.SetSurfaceOverrideMaterial(0, new StandardMaterial3D { AlbedoColor = new Color(0.85f, 0.7f, 0.6f) });
        _label.Text = State.Name;
        AddToGroup("adventurers");
        _fpWeapon = new FirstPersonWeapon { Name = "FirstPersonWeapon" };
        _fpWeapon.Bind(this);
        Camera.AddChild(_fpWeapon);
        if (ClassModels.TryGetValue(State.Class.Id, out var modelName))
        {
            _model = AnimatedModel.Attach(this, $"res://assets/models/quaternius_rpg/{modelName}.gltf", 1.8f, color, 0.1f);
            if (_model != null) { _body.Visible = false; _head.Visible = false; }
        }
        SetControl(Control);
    }

    /// <summary>Switch who drives this body. Parked bodies leave the world (no collision, invisible, no physics).</summary>
    public void SetControl(ControlKind control)
    {
        Control = control;
        Guarding = false;
        _holdStart = -1;
        var parked = control == ControlKind.Parked;
        Visible = !parked;
        ProcessMode = parked ? ProcessModeEnum.Disabled : ProcessModeEnum.Inherit;
        CollisionLayer = parked || Downed ? 0u : 2u;
        CollisionMask = control == ControlKind.Remote ? 0u : 5u;
        if (parked) { Target = null; _action = CompanionAction.Idle; LookingAt = null; }
        SetLocalView(control == ControlKind.LocalHuman);
    }

    [Obsolete("Use SetControl.")]
    public void SetLead(bool lead) => SetControl(lead ? ControlKind.LocalHuman : ControlKind.Companion);

    /// <summary>Camera, torch and hidden own body for the character this machine looks through.</summary>
    private void SetLocalView(bool local)
    {
        if (Camera == null) return; // before _Ready
        Camera.Current = local;
        Torch.ShadowEnabled = local;
        Torch.LightEnergy = local ? 2.5f : 1.0f;
        Torch.OmniRange = local ? 16f : 8f;
        _body.Visible = !local && _model == null;
        _head.Visible = !local && _model == null;
        if (_model != null) _model.Root.Visible = !local; // no body in the way of the local player's own camera
        _label.Visible = !local;
        if (local) Camera.Fov = Settings.Fov;
    }

    /// <summary>Attack button down: melee weapon swings charge while held; anything else fires now.</summary>
    public void BeginAttack(double now)
    {
        var m = State;
        var a = Party.GetAbilityFor(m, m.ActiveAbilityId);
        if (a.Kind == AbilityKind.WeaponAttack && !m.WeaponProfile.IsRanged) _holdStart = now;
        else Attack();
    }

    public void EndAttack(double now)
    {
        if (_holdStart < 0) return;
        var held = (float)(now - _holdStart);
        _holdStart = -1;
        var heavy = held >= PartyController.HeavyHoldSeconds;
        Attack(heavy, heavy && held >= PartyController.PerfectWindowStart && held <= PartyController.PerfectWindowEnd);
    }

    public void CancelHold() => _holdStart = -1;

    public void ApplySettings() => Camera.Fov = Settings.Fov;

    private float _shakeAmp, _shakeLeft, _shakeTotal;
    private readonly Random _shakeRng = new();
    public bool Shaking => _shakeLeft > 0f;

    /// <summary>Rattle the camera: a tremor, a horde tearing loose. Amplitude in metres, fading over the seconds given.</summary>
    public void Shake(float amplitude, float seconds)
    {
        _shakeAmp = Math.Max(_shakeAmp * (_shakeLeft / Math.Max(_shakeTotal, 0.01f)), amplitude);
        _shakeLeft = _shakeTotal = Math.Max(seconds, 0.05f);
    }

    public override void _Process(double delta)
    {
        if (_shakeLeft <= 0f) return;
        _shakeLeft -= (float)delta;
        if (_shakeLeft <= 0f) { Camera.HOffset = 0f; Camera.VOffset = 0f; return; }
        var t = _shakeLeft / _shakeTotal;
        var amp = _shakeAmp * t * t;
        Camera.HOffset = ((float)_shakeRng.NextDouble() * 2f - 1f) * amp;
        Camera.VOffset = ((float)_shakeRng.NextDouble() * 2f - 1f) * amp * 0.7f;
    }

    public void Look(Vector2 relative)
    {
        if (SpectateTarget != null) return;
        var sens = 0.0022f * Settings.MouseSensitivity;
        RotateY(-relative.X * sens);
        _pitch = Mathf.Clamp(_pitch - relative.Y * sens * (Settings.InvertY ? -1f : 1f), Mathf.DegToRad(-MaxPitchDegrees), Mathf.DegToRad(MaxPitchDegrees));
        Camera.Rotation = new Vector3(_pitch, 0, 0);
    }

    public void Teleport(Vector3 position, Vector3? lookTarget = null)
    {
        GlobalPosition = position;
        Velocity = Vector3.Zero;
        _fallStart = position.Y;
        _falling = false;
        _mover.Reset();
        if (lookTarget is { } t) FaceToward(t);
        _pitch = 0;
        Camera.Rotation = Vector3.Zero;
    }

    public void FaceToward(Vector3 target)
    {
        var flat = new Vector3(target.X, GlobalPosition.Y, target.Z);
        if (flat.DistanceSquaredTo(GlobalPosition) > 0.001f) LookAt(flat, Vector3.Up);
    }

    /// <summary>Companions aim their eyes at a point so melee casts and projectiles go where they look.</summary>
    private void AimAt(Vector3 point)
    {
        FaceToward(point);
        var eye = Camera.GlobalPosition;
        var flatDist = new Vector2(point.X - eye.X, point.Z - eye.Z).Length();
        _pitch = Mathf.Clamp(Mathf.Atan2(point.Y - eye.Y, Mathf.Max(0.01f, flatDist)), Mathf.DegToRad(-MaxPitchDegrees), Mathf.DegToRad(MaxPitchDegrees));
        Camera.Rotation = new Vector3(_pitch, 0, 0);
    }

    // ---- physics -------------------------------------------------------------------------------

    public override void _PhysicsProcess(double delta)
    {
        var dt = (float)delta;
        if (Parked) return;
        if (Main.Instance?.Builder.Layout == null) return; // no floor yet (the planner is still writing it): stand still rather than fall forever
        if (Control == ControlKind.Remote) { RemoteTick(dt); return; }
        var velocity = Velocity;
        var wasInWater = InWater;
        InWater = Main.Instance?.Builder.IsInWater(GlobalPosition) == true;
        if (InWater) Wade(dt, !wasInWater);
        if (!IsOnFloor())
        {
            velocity.Y -= _gravity * dt;
            _fallStart = Math.Max(_fallStart, GlobalPosition.Y);
            _falling = true;
        }
        else
        {
            if (_falling)
            {
                var drop = _fallStart - GlobalPosition.Y;
                if (drop >= 3f) OnHardLanding(drop);
                _falling = false;
            }
            _fallStart = GlobalPosition.Y;
            velocity.Y = 0;
            _lastGround = GlobalPosition;
        }
        if (GlobalPosition.Y < FellOutY) { FellOut(); return; }

        Vector3 target;
        if (Downed) { target = Vector3.Zero; Guarding = false; }
        else if (IsLocal)
        {
            var input = Intent;
            _forwardHeld = input.Live && input.Move.Y < -0.5f ? _forwardHeld + dt : 0f;
            AutoRunning = _forwardHeld >= AutoRunSeconds;
            SetCrouch(input.Crouch);
            Guarding = input.Guard && State.Class.CanBlock;
            var sprint = !Crouching && (input.Sprint || AutoRunning);
            var wish = (Transform.Basis * new Vector3(input.Move.X, 0, input.Move.Y)).Normalized();
            var speed = WalkSpeed * State.Stats[Stat.MoveSpeed] * (sprint ? SprintMultiplier : 1f) * (Guarding ? 0.6f : 1f) * (Crouching ? CrouchSpeedMultiplier : 1f) * (InWater ? Enemies.Enemy.WaterSlow : 1f);
            target = wish * speed;
            if (input.JumpPressed && IsOnFloor() && !Crouching)
            {
                velocity.Y = JumpVelocity;
                Audio.AudioManager.Instance?.Play("footstep", -6f);
            }
        }
        else
        {
            SetCrouch(false);
            Guarding = false;
            target = CompanionVelocity(dt);
        }

        velocity.X = Mathf.MoveToward(velocity.X, target.X, Acceleration * dt);
        velocity.Z = Mathf.MoveToward(velocity.Z, target.Z, Acceleration * dt);
        Velocity = velocity;
        var before = GlobalPosition;
        MoveAndSlide();
        var moved = GlobalPosition - before; moved.Y = 0;
        DistanceTravelled += moved.Length();

        var planar = moved.Length() / Math.Max(dt, 0.0001f);
        if (_model != null && !Downed)
        {
            if (_flash > 0f) { _flash -= dt; _model.Flash(Math.Max(0f, _flash * 2f)); }
            if (!_model.IsPlayingOneShot) _model.Play(planar > 0.5f ? (planar > 5f ? "Run" : "Walk") : "Idle");
        }
        if (IsOnFloor() && planar > 0.5f)
        {
            _stepTimer -= dt * planar / 4f;
            if (_stepTimer <= 0f) { _stepTimer = 0.5f; Audio.AudioManager.Instance?.Play("footstep", IsLocal ? -10f : -18f); }
        }

        if (IsLocal && !Downed)
        {
            _fpWeapon.SetGuard(Guarding);
            _interactRay.ForceRaycastUpdate();
            LookingAt = _interactRay.IsColliding() ? _interactRay.GetCollider() as IInteractable : null;
            if (LookingAt is { Prompt: "" }) LookingAt = null;
        }
        else LookingAt = null;
        if (IsLocal && Downed && State.Mangled && Net.NetSession.IsClient) Spectate(); else if (SpectateTarget != null) EndSpectate();

        if (Control == ControlKind.Companion && !Downed) ScanLoot(dt);
    }

    /// <summary>Another player's body: ease toward the pose the network reported and animate from the motion.</summary>
    private void RemoteTick(float dt)
    {
        if (Remote is not { } pose) return;
        var before = GlobalPosition;
        var gap = pose.Position - before;
        GlobalPosition = gap.Length() > 3f ? pose.Position : before.Lerp(pose.Position, Math.Min(1f, dt * 12f));
        Rotation = new Vector3(0, Mathf.LerpAngle(Rotation.Y, pose.Yaw, Math.Min(1f, dt * 12f)), 0);
        _pitch = pose.Pitch;
        Camera.Rotation = new Vector3(_pitch, 0, 0);
        Velocity = pose.Velocity;
        SetCrouch(pose.Crouch);
        Guarding = pose.Guard && State.Class.CanBlock;
        var wasInWater = InWater;
        InWater = Main.Instance?.Builder.IsInWater(GlobalPosition) == true;
        if (InWater) Wade(dt, !wasInWater);
        var moved = GlobalPosition - before; moved.Y = 0;
        DistanceTravelled += moved.Length();
        var planar = new Vector2(pose.Velocity.X, pose.Velocity.Z).Length();
        if (_model != null && !Downed)
        {
            if (_flash > 0f) { _flash -= dt; _model.Flash(Math.Max(0f, _flash * 2f)); }
            if (!_model.IsPlayingOneShot) _model.Play(planar > 0.5f ? (planar > 5f ? "Run" : "Walk") : "Idle");
        }
        if (pose.OnFloor && planar > 0.5f)
        {
            _stepTimer -= dt * planar / 4f;
            if (_stepTimer <= 0f) { _stepTimer = 0.5f; Audio.AudioManager.Instance?.Play("footstep", -18f); }
        }
    }

    private float _rippleTimer;

    /// <summary>Splash on the way in, rings while moving: the water tells on whoever crosses it.</summary>
    private void Wade(float dt, bool entered)
    {
        var builder = Main.Instance!.Builder;
        var cell = DungeonBuilder.WorldToCell(GlobalPosition);
        var at = GlobalPosition with { Y = builder.WaterLevel(cell) + 0.02f };
        var root = builder.FloorRoot ?? GetParent();
        if (entered) { Dungeon.Props.WaterAudio.SplashAt(root, at, IsLocal ? -3f : -8f); Dungeon.Props.Ripple.Spawn(root, at, 1.6f); _rippleTimer = 0.25f; }
        _rippleTimer -= dt;
        if (_rippleTimer <= 0f && new Vector2(Velocity.X, Velocity.Z).Length() > 0.6f) { Dungeon.Props.Ripple.Spawn(root, at); _rippleTimer = 0.3f; }
    }

    /// <summary>Crouch: a shorter capsule, a lower eye, slower steps; enemies notice a crouching character from closer.</summary>
    public void SetCrouch(bool crouch)
    {
        if (crouch == Crouching) return;
        Crouching = crouch;
        _capsule.Height = crouch ? 1.1f : 1.8f;
        _collision.Position = new Vector3(0, crouch ? 0.55f : 0.9f, 0);
        Camera.Position = new Vector3(0, crouch ? 0.95f : 1.6f, 0);
        if (_model != null) _model.Root.Scale = new Vector3(_model.Root.Scale.X, _model.Root.Scale.X * (crouch ? 0.7f : 1f), _model.Root.Scale.Z);
    }

    /// <summary>Fell out of the world (a hole in the geometry): put the body back on the last ground it stood on.</summary>
    private void FellOut()
    {
        var builder = Main.Instance.Builder;
        var safe = _lastGround.Y > FellOutY + 4f ? _lastGround : builder.Layout is { } layout ? builder.Surface(DungeonBuilder.WalkableNeighbor(layout, layout.UpStairs)) : Vector3.Zero;
        GD.PushWarning($"[Adventurer] {State.Name} fell out of the world at {GlobalPosition:0.0} (cell {DungeonBuilder.WorldToCell(GlobalPosition)}); returned to {safe:0.0}");
        EventBus.Instance.Log($"{State.Name} slips into a crack in the dark and claws back out.");
        Teleport(safe + Vector3.Up * 0.5f);
        var dmg = MathF.Round(State.Stats[Stat.MaxHp] * 0.1f);
        if (!Downed && dmg > 0) ReceiveAttack(Trap.TrapStats, new AttackProfile { MinDamage = dmg, MaxDamage = dmg }, "The fall");
    }

    private void OnHardLanding(float drop)
    {
        var dmg = MathF.Round((drop - 2.5f) * 3f * TierRules.DamageScale(Game.Instance.CurrentFloor));
        EventBus.Instance.PlaySfx("fall");
        EventBus.Instance.Log(drop >= 3.5f ? $"{State.Name} drops into darkness!" : $"{State.Name} stumbles down a drop.");
        if (dmg > 0) ReceiveAttack(Trap.TrapStats, new AttackProfile { MinDamage = dmg, MaxDamage = dmg }, "The fall");
        if (Main.Instance.Builder.FloorRoot is { } root)
            foreach (var e in root.GetChildren().OfType<Enemy>())
                if (e.IsAlive && e.GlobalPosition.DistanceTo(GlobalPosition) < 12f && Main.Instance.Builder.IsUnderAt(e.GlobalPosition) == Main.Instance.Builder.IsUnderAt(GlobalPosition)) e.ForceAggro();
    }

    // ---- companion AI ----------------------------------------------------------------------------

    private Vector3 CompanionVelocity(float dt)
    {
        _thinkTimer -= dt;
        if (_potionCooldown > 0) _potionCooldown -= dt;
        if (_thinkTimer <= 0f) { _thinkTimer = 0.25f; Think(); }

        var lead = Party.Lead;
        switch (_action.Kind)
        {
            case ActionKind.Follow:
                return Steer(FormationSpot(), dt, stopWithin: 0.6f);
            case ActionKind.Approach when Target != null && IsInstanceValid(Target):
                return Steer(Target.GlobalPosition, dt, stopWithin: State.WeaponProfile.IsRanged ? 6f : Math.Max(1.2f, State.WeaponProfile.Range * 0.8f));
            case ActionKind.Retreat when Target != null && IsInstanceValid(Target):
                var away = GlobalPosition - Target.GlobalPosition; away.Y = 0;
                var dest = GlobalPosition + (away.LengthSquared() > 0.01f ? away.Normalized() : -GlobalTransform.Basis.Z) * 3f;
                if (Target != null) FaceToward(Target.GlobalPosition);
                return Planar(dest) * WalkSpeed * State.Stats[Stat.MoveSpeed] * (InWater ? Enemies.Enemy.WaterSlow : 1f);
            case ActionKind.Regroup:
                Teleport(lead.GlobalPosition + lead.GlobalTransform.Basis * new Vector3(0, 0, 1.5f), lead.GlobalPosition + -lead.GlobalTransform.Basis.Z * 5f);
                _unreachable = 0;
                _mover.Reset();
                _action = CompanionAction.Idle;
                return Vector3.Zero;
            default:
                if (Target != null && IsInstanceValid(Target) && Target.IsAlive) FaceToward(Target.GlobalPosition);
                return Vector3.Zero;
        }
    }

    private Vector3 Planar(Vector3 to)
    {
        var d = to - GlobalPosition; d.Y = 0;
        return d.LengthSquared() > 0.01f ? d.Normalized() : Vector3.Zero;
    }

    /// <summary>Path toward a point (through corridors and across levels); go straight when close and visible.</summary>
    private Vector3 Steer(Vector3 target, float dt, float stopWithin)
    {
        var flatDist = new Vector2(target.X - GlobalPosition.X, target.Z - GlobalPosition.Z).Length();
        var speed = WalkSpeed * State.Stats[Stat.MoveSpeed] * (flatDist > 8f ? SprintMultiplier : 1f) * (InWater ? Enemies.Enemy.WaterSlow : 1f);
        var dest = _mover.Next(this, target, dt, stopWithin, ClearLine, out var straight);
        _unreachable = _mover.Unreachable;
        if (dest is not { } d) return Vector3.Zero;
        if (!straight) FaceToward(d);
        return Planar(d) * speed + Separation() * speed * 0.5f;
    }

    private bool ClearLine(Vector3 target)
    {
        var space = GetWorld3D().DirectSpaceState;
        var q = PhysicsRayQueryParameters3D.Create(GlobalPosition + Vector3.Up * 1.2f, target + Vector3.Up * 1.2f, collisionMask: 1);
        return space.IntersectRay(q).Count == 0;
    }

    /// <summary>Soft push away from other party members (they do not collide with each other).</summary>
    private Vector3 Separation()
    {
        var push = Vector3.Zero;
        foreach (var other in Party.Active)
        {
            if (other == this || other.Downed) continue;
            var d = GlobalPosition - other.GlobalPosition; d.Y = 0;
            var len = d.Length();
            if (len > 1.0f || len < 0.001f) continue;
            push += d / len * (1.0f - len);
        }
        return push;
    }

    private Vector3 FormationSpot()
    {
        var lead = Party.Lead;
        var slot = Party.FormationSlot(Index);
        var world = lead.GlobalPosition + lead.GlobalTransform.Basis * new Vector3(slot.X, 0, slot.Y);
        var layout = Main.Instance.Builder.Layout;
        if (layout != null && !layout.IsWalkable(DungeonBuilder.WorldToCell(world))) world = lead.GlobalPosition;
        return world;
    }

    private void Think()
    {
        var lead = Party.Lead;
        var root = Main.Instance.Builder.FloorRoot;
        // enemies are identified by their index in this list (instance ids do not fit an int)
        var enemies = new List<EnemyView>();
        var byId = new List<Enemy>();
        int? leadTargetId = null;
        if (root != null)
            foreach (var e in root.GetChildren().OfType<Enemy>())
            {
                if (!e.IsAlive) continue;
                var dist = e.GlobalPosition.DistanceTo(GlobalPosition);
                if (dist > EngageRadius + 4f) continue;
                var id = byId.Count;
                byId.Add(e);
                if (Party.LeadTarget == e) leadTargetId = id;
                enemies.Add(new EnemyView(id, dist, e.IsHostile, ClearLine(e.GlobalPosition), e.Hp / Math.Max(1f, e.MaxHp)));
            }

        var w = State.WeaponProfile;
        var abilities = new List<AbilityView>();
        for (var i = 0; i < State.KnownAbilities.Count; i++)
        {
            var a = Party.GetAbilityFor(State, State.KnownAbilities[i]);
            var ready = AbilityRules.WhyCannotUse(State, a, Party.ArrowCount) == null;
            var range = a.Kind switch
            {
                AbilityKind.WeaponAttack => w.IsRanged ? 14f : w.Range + 0.4f,
                AbilityKind.Projectile => 14f,
                AbilityKind.MeleeStrike => w.Range + 0.4f,
                AbilityKind.Nova => a.Radius,
                _ => 999f,
            };
            abilities.Add(new AbilityView(i, a, ready, range));
        }

        var prefersRange = w.IsRanged || State.KnownAbilities.Any(id => Party.GetAbility(id).Kind == AbilityKind.Projectile && Party.GetAbility(id).ArrowCost == 0);
        var alive = Party.Members.Where(m => m.IsAlive).ToList();
        var spot = FormationSpot();
        var ctx = new CompanionContext
        {
            HpFraction = State.Hp / Math.Max(1f, State.Stats[Stat.MaxHp]),
            HasHealingPotion = _potionCooldown <= 0 && Party.Bag.Items.Any(i => i.Consumable is { HealHp: > 0 }),
            Stance = Stance,
            PrefersRange = prefersRange,
            MeleeRange = w.Range,
            Enemies = enemies,
            Abilities = abilities,
            LeadTargetId = leadTargetId,
            PartyMissingShield = alive.Any(m => !m.Effects.Has(StatusKind.Shield)),
            PartyMissingHaste = alive.Any(m => !m.Effects.Has(StatusKind.Haste)),
            PartyWounded = alive.Any(m => m.Hp < m.Stats[Stat.MaxHp] * 0.7f) && enemies.Count > 0,
            SelfHasShield = State.Effects.Has(StatusKind.Shield),
            StaminaFraction = State.Stamina / Math.Max(1f, State.Stats[Stat.MaxStamina]),
            DistanceToFormation = new Vector2(spot.X - GlobalPosition.X, spot.Z - GlobalPosition.Z).Length(),
            FormationReachable = _unreachable < 5f && GlobalPosition.DistanceTo(lead.GlobalPosition) < 25f,
        };

        _action = CompanionBrain.Decide(ctx);
        Target = _action.TargetId >= 0 && _action.TargetId < byId.Count ? byId[_action.TargetId] : null;
        if (DebugTools.TraceAi && enemies.Count > 0)
            GD.Print($"[cai] {State.Name}: {_action.Kind} target={(Target?.DisplayName ?? "-")} nearest={enemies.Min(e => e.Distance):0.0}m abilities=[{string.Join(", ", abilities.Select(a => $"{a.Ability.Name}:{(a.Ready ? "ready" : AbilityRules.WhyCannotUse(State, a.Ability, Party.ArrowCount))}/r{a.Range:0.0}"))}] stamina={State.Stamina:0} cd={State.CooldownRemaining:0.00}");

        switch (_action.Kind)
        {
            case ActionKind.DrinkPotion:
                DrinkPotion();
                _action = CompanionAction.Idle;
                break;
            case ActionKind.Attack when Target != null:
                State.ActiveAbilityIndex = _action.AbilityIndex;
                AimAt(Target.GlobalPosition + Vector3.Up * 1.0f);
                Attack(_action.Heavy, false);
                break;
        }
    }

    private void DrinkPotion()
    {
        var potion = Party.Bag.Items.FirstOrDefault(i => i.Consumable is { HealHp: > 0 });
        if (potion == null) return;
        var text = ConsumableRules.Use(potion.Consumable!, new[] { State });
        Party.Bag.TakeOne(potion);
        _potionCooldown = 6f;
        EventBus.Instance.PlaySfx("potion");
        EventBus.Instance.Log($"{State.Name} drinks a {potion.DisplayName}: {text}");
        EventBus.Instance.RaiseMemberChanged(Index);
    }

    private void ScanLoot(float dt)
    {
        _lootTimer -= dt;
        if (_lootTimer > 0 || Main.Instance.Builder.FloorRoot is not { } root) return;
        _lootTimer = 0.5f;
        foreach (var child in root.GetChildren())
        {
            if (child is LootDrop { DroppedByParty: false } drop && drop.GlobalPosition.DistanceTo(GlobalPosition) < 1.6f && Party.Bag.FreeSlots > 0)
            {
                EventBus.Instance.Log($"{State.Name} picks up {drop.Item}.");
                if (Party.Bag.TryAdd(drop.Item)) { EventBus.Instance.RaiseItemPickedUp(drop.Item); EventBus.Instance.PlaySfx("pickup"); drop.NotifyPicked(); drop.QueueFree(); }
                return;
            }
            if (child is Chest { Opened: false, Locked: false } chest && chest.GlobalPosition.DistanceTo(GlobalPosition) < 1.8f && Target == null)
            {
                EventBus.Instance.Log($"{State.Name} opens a chest.");
                chest.Interact(Party, this);
                return;
            }
        }
    }

    // ---- combat --------------------------------------------------------------------------------

    public void TryInteract()
    {
        if (Downed)
        {
            // a mangled player on a client cannot act, but can shout: everyone reads it in the log
            if (Net.NetSession.IsClient && State.Mangled) Net.NetSession.Instance.Send(new Net.ShoutIntent());
            return;
        }
        if (LookingAt is not { } target) return;
        // talking is local (the choice goes over the wire); everything else the server does for us
        if (Net.NetSession.IsClient && target is not Npc)
        {
            if (target is Adventurer { Downed: true, State.Mangled: true } mangled) { Net.NetSession.Instance.Send(new Net.RescueIntent(mangled.Index)); return; }
            if (Net.NetRefResolver.RefOf(target) is { } netRef) Net.NetSession.Instance.Send(new Net.InteractIntent(netRef));
            return;
        }
        target.Interact(Party, this);
    }

    /// <summary>Use the active ability. Heavy/perfect only matter for melee weapon swings.</summary>
    public void Attack(bool heavy = false, bool perfect = false)
    {
        if (Downed) return;
        var m = State;
        var a = Party.GetAbilityFor(m, m.ActiveAbilityId);
        var why = AbilityRules.WhyCannotUse(m, a, Party.ArrowCount, heavy);
        if (why == "Recovering") return;
        if (why != null && heavy && AbilityRules.WhyCannotUse(m, a, Party.ArrowCount, false) == null) { heavy = false; perfect = false; }
        else if (why != null)
        {
            if (IsLocal) EventBus.Instance.Log(why + ".");
            return;
        }

        if (Net.NetSession.IsClient)
        {
            // the server rolls the attack; this machine only shows the swing
            if (!IsLocal) return;
            var wp = m.WeaponProfile;
            _model?.Play(a.Kind is AbilityKind.WeaponAttack or AbilityKind.MeleeStrike ? (wp.IsRanged ? Clips.cast : Clips.melee) : Clips.cast, force: true);
            _fpWeapon.PlayAttack(a.Kind, heavy, wp.IsRanged && a.Kind == AbilityKind.WeaponAttack);
            var cam = Camera.GlobalPosition;
            Net.NetSession.Instance.Send(new Net.AttackIntent(heavy, perfect, new Net.NetVec(cam.X, cam.Y, cam.Z), Rotation.Y, Camera.Rotation.X));
            return;
        }
        var arrows = AbilityRules.ArrowCost(m, a);
        if (arrows > 0) Party.Bag.Consume(PartyController.ArrowDefId, arrows);
        AbilityRules.PayCosts(m, a, heavy);
        EventBus.Instance.RaiseMemberChanged(Index);

        var w = m.WeaponProfile;
        EventBus.Instance.RaiseAbilityUsed(Index, a.Kind);
        _model?.Play(a.Kind is AbilityKind.WeaponAttack or AbilityKind.MeleeStrike ? (w.IsRanged ? Clips.cast : Clips.melee) : Clips.cast, force: true);
        if (IsLocal) _fpWeapon.PlayAttack(a.Kind, heavy, w.IsRanged && a.Kind == AbilityKind.WeaponAttack);
        EventBus.Instance.PlaySfx(a.Kind switch
        {
            AbilityKind.WeaponAttack when w.IsRanged => "bow",
            AbilityKind.WeaponAttack => heavy ? "swing_heavy" : "swing",
            AbilityKind.MeleeStrike => "swing_heavy",
            _ => "cast",
        });
        switch (a.Kind)
        {
            case AbilityKind.WeaponAttack when w.IsRanged:
                FireProjectile(w.ToAttack(), 40f, new Color(0.8f, 0.7f, 0.5f), isArrow: true);
                break;
            case AbilityKind.WeaponAttack:
                MeleeSwing(w.ToAttack(), heavy, perfect, w.HeavyMultiplier);
                break;
            case AbilityKind.MeleeStrike:
                var bonus = a.Attack ?? new AttackProfile();
                var strike = new AttackProfile
                {
                    MinDamage = w.MinDamage + bonus.MinDamage, MaxDamage = w.MaxDamage + bonus.MaxDamage,
                    Type = w.Type, Kind = AttackKind.Melee, CritBonus = bonus.CritBonus, OnHit = w.OnHit.Concat(bonus.OnHit).ToArray(),
                };
                EventBus.Instance.Log($"{m.Name} uses {a.Name}!");
                MeleeSwing(strike, false, false, 1f);
                break;
            case AbilityKind.Projectile:
                EventBus.Instance.Log($"{m.Name} {(a.ArrowCost > 0 ? "looses" : "casts")} {a.Name}.");
                for (var i = 0; i < a.ProjectileCount; i++)
                {
                    var spread = a.ProjectileCount == 1 ? 0f : (i - (a.ProjectileCount - 1) / 2f) * 7f;
                    FireProjectile(a.Attack ?? new AttackProfile(), a.ProjectileSpeed, new Color(a.Color), isArrow: a.ArrowCost > 0, yawDegrees: spread);
                }
                break;
            case AbilityKind.SelfBuff:
                Party.ApplyBuff(a, new[] { m });
                EventBus.Instance.Log($"{m.Name} casts {a.Name}.");
                break;
            case AbilityKind.PartyBuff:
                Party.ApplyBuff(a, Party.Members.Where(x => x.IsAlive));
                EventBus.Instance.Log($"{m.Name} casts {a.Name} on the party.");
                break;
            case AbilityKind.Nova:
                EventBus.Instance.Log($"{m.Name} casts {a.Name}!");
                Nova(a);
                break;
        }
    }

    private void MeleeSwing(AttackProfile attack, bool heavy, bool perfect, float heavyMultiplier)
    {
        var m = State;
        _meleeCast.TargetPosition = new Vector3(0, 0, -Mathf.Max(1.0f, m.WeaponProfile.Range));
        _meleeCast.ForceShapecastUpdate();
        var hitSomething = false;
        for (var i = 0; i < _meleeCast.GetCollisionCount(); i++)
        {
            if (_meleeCast.GetCollider(i) is not ICombatTarget target || !target.IsAlive) continue;
            hitSomething = true;
            var ctx = new HitContext { Heavy = heavy, HeavyMultiplier = heavyMultiplier * (perfect ? 1.25f : 1f), SneakAttack = target.IsUnaware, SneakMultiplier = m.Class.SneakAttackMultiplier };
            var result = target.ReceiveAttack(m.Stats, attack, ctx, Party.Rng, m.Name);
            Party.LogResult(m.Name, target.DisplayName, result, heavy, perfect);
            Party.OnHitLanded(m, target, result);
            if (target is Enemy e) { e.NoteThreat(this, result.Amount + 5f); if (IsLocal) Party.LeadTarget = e; }
        }
        if (!hitSomething && IsLocal) EventBus.Instance.Log($"{m.Name} swings at nothing.");
    }

    private void FireProjectile(AttackProfile attack, float speed, Color color, bool isArrow, float yawDegrees = 0f)
    {
        Node root = Main.Instance.Builder.FloorRoot ?? (Node)GetParent();
        var p = GD.Load<PackedScene>("res://scenes/Props/Projectile.tscn").Instantiate<Projectile>();
        root.AddChild(p);
        var forward = (-Camera.GlobalTransform.Basis.Z).Rotated(Vector3.Up, Mathf.DegToRad(yawDegrees));
        p.GlobalPosition = Camera.GlobalPosition + forward * 0.7f + Vector3.Down * 0.15f;
        p.Setup(Party, State, attack, forward * speed, isArrow, color, this);
    }

    private void Nova(AbilityProfile a)
    {
        if (Main.Instance.Builder.FloorRoot is not { } root || a.Attack == null) return;
        var any = false;
        foreach (var child in root.GetChildren())
        {
            if (child is not Enemy { IsAlive: true } e || e.GlobalPosition.DistanceTo(GlobalPosition) > a.Radius) continue;
            any = true;
            var ctx = new HitContext { SneakAttack = e.IsUnaware, SneakMultiplier = State.Class.SneakAttackMultiplier };
            var r = e.ReceiveAttack(State.Stats, a.Attack, ctx, Party.Rng, State.Name);
            Party.LogResult(State.Name, e.DisplayName, r, false, false);
            Party.OnHitLanded(State, e, r);
            e.NoteThreat(this, r.Amount + 5f);
        }
        if (!any && IsLocal) EventBus.Instance.Log("Nothing is close enough.");
    }

    /// <summary>An attack lands on this character. Only a driven body can be guarding.</summary>
    public void ReceiveAttack(Stats attackerStats, AttackProfile attack, string attackerName, ICombatTarget? attacker = null)
    {
        if (Downed) return;
        var m = State;
        var blocking = Guarding && m.Class.CanBlock && m.Stamina >= PartyController.BlockStaminaCost;
        var ctx = new HitContext { DefenderBlocking = blocking, DefenderStunned = m.IsStunned };
        var r = HitResolver.Resolve(attackerStats, m.Stats, m.Effects, attack, ctx, Party.Rng);
        if (!r.Hit) { EventBus.Instance.Log($"{attackerName} misses {m.Name}."); EventBus.Instance.PlaySfx("miss"); return; }
        if (r.Blocked) m.Stamina = Math.Max(0, m.Stamina - PartyController.BlockStaminaCost);
        m.TakeDamage(r.Amount);
        EventBus.Instance.RaiseMemberDamaged(Index, r.Amount);
        _flash = 0.25f;
        _model?.Play(Clips.hit, force: true);
        EventBus.Instance.PlaySfx(r.Blocked ? "block" : "hit_flesh");
        EventBus.Instance.RaiseMemberChanged(Index);

        var text = $"{attackerName} {(r.Crit ? "critically hits" : "hits")} {m.Name} for {r.Amount:0}";
        if (r.Type != DamageType.Physical) text += $" {r.Type.ToString().ToLower()}";
        if (r.Blocked) text += " (blocked)";
        if (r.Applied.Count > 0) text += $" [{string.Join(", ", r.Applied.Select(s => s.Kind))}]";
        EventBus.Instance.Log(text + (m.IsAlive ? "." : $". {m.Name} falls!"));

        if (!m.IsAlive) { OnDowned(); return; }
        if (r.Blocked && Party.Trigger(m, TriggerKind.SecondWind) is > 0 and var wind) m.Stamina = Math.Min(m.Stats[Stat.MaxStamina], m.Stamina + wind);
        if (attacker is { IsAlive: true })
        {
            if (r.Blocked && Party.Trigger(m, TriggerKind.Riposte) is > 0 and var riposte)
            {
                var w = m.WeaponProfile;
                var counter = w.ToAttack() with { MinDamage = w.MinDamage * riposte, MaxDamage = w.MaxDamage * riposte };
                var cr = attacker.ReceiveAttack(m.Stats, counter, new HitContext { DefenderStunned = true }, Party.Rng, m.Name);
                if (cr.Hit) EventBus.Instance.Log($"{m.Name} ripostes for {cr.Amount:0}.");
                Party.OnHitLanded(m, attacker, cr);
            }
            if (Party.Trigger(m, TriggerKind.Thorns) is > 0 and var thorns)
            {
                var tr = attacker.ReceiveAttack(m.Stats, new AttackProfile { MinDamage = thorns, MaxDamage = thorns }, new HitContext { DefenderStunned = true }, Party.Rng, $"{m.Name}'s thorns");
                if (tr.Hit) EventBus.Instance.Log($"{attackerName} is torn by thorns for {tr.Amount:0}.");
            }
        }
    }

    public void ApplyTrap(TrapKind kind, int floor)
    {
        var attack = TrapRules.Attack(kind, floor);
        if (!TrapRules.HitsWholeParty(kind)) { ReceiveAttack(Trap.TrapStats, attack, $"The {kind.ToString().ToLower()} trap"); return; }
        foreach (var adv in Party.Active)
            if (!adv.Downed && adv.GlobalPosition.DistanceTo(GlobalPosition) < 2.5f) adv.ReceiveAttack(Trap.TrapStats, attack, $"The {kind.ToString().ToLower()} trap");
    }

    public VictimKind HazardVictimKind => VictimKind.Member;

    /// <summary>A lethal hazard caught this body. Lethal means down and Mangled (no auto-revive); otherwise a fraction of max HP, unmitigated.</summary>
    public void ReceiveHazard(HazardDamage kind, HazardOutcome outcome, string sourceName, int hazardIndex)
    {
        if (Downed) return;
        var m = State;
        if (outcome.Lethal)
        {
            m.Hp = 0;
            m.Mangled = true;
            m.MangledBy = hazardIndex;
            m.Effects.Clear();
            EventBus.Instance.Log($"{sourceName} {(kind == HazardDamage.Crush ? "crushes" : "impales")} {m.Name}!");
            EventBus.Instance.PlaySfx("hit_flesh");
            EventBus.Instance.RaiseMemberChanged(Index);
            OnDowned();
            EventBus.Instance.RaiseMemberKilledByHazard(Index, sourceName);
            return;
        }
        var amount = m.Stats[Stat.MaxHp] * outcome.HpFraction;
        m.TakeDamage(amount);
        EventBus.Instance.RaiseMemberDamaged(Index, amount);
        _flash = 0.25f;
        _model?.Play(Clips.hit, force: true);
        EventBus.Instance.PlaySfx("hit_flesh");
        EventBus.Instance.RaiseMemberChanged(Index);
        EventBus.Instance.Log($"{sourceName} hits {m.Name} for {amount:0}{(m.IsAlive ? "." : $". {m.Name} falls!")}");
        if (!m.IsAlive) OnDowned();
    }

    /// <summary>Only a Mangled body offers anything to a passer-by: pulling them free.</summary>
    public string Prompt => State.Mangled && Downed ? $"Rescue {State.Name}" : "";

    public void Interact(PartyController party, Adventurer actor)
    {
        if (!State.Mangled || !Downed || actor == this) return;
        party.TryRescue(this, actor, () => Main.Instance?.Builder.HazardArmed(State.MangledBy) ?? true);
    }

    private void OnDowned()
    {
        ShowDowned();
        Party.OnMemberDowned(Index);
    }

    /// <summary>The fallen pose and label, no bookkeeping (clients get the event from the server).</summary>
    public void ShowDowned()
    {
        CollisionLayer = State.Mangled ? 8u : 0u; // a Mangled body is something to reach for: the interact ray finds it
        if (_model != null) _model.Play("Death", force: true);
        else
        {
            _body.Rotation = new Vector3(Mathf.Pi / 2f, 0, 0);
            _body.Position = new Vector3(0, 0.35f, 0);
            _head.Position = new Vector3(0, 0.3f, -0.9f);
        }
        _label.Text = $"{State.Name} (down)";
        Target = null;
        _action = CompanionAction.Idle;
    }

    /// <summary>A crushed client body watches the nearest standing teammate from two metres behind and one up until it is freed.</summary>
    private void Spectate()
    {
        var target = Party.Active.Where(a => a != this && !a.Downed).OrderBy(a => a.GlobalPosition.DistanceSquaredTo(GlobalPosition)).FirstOrDefault();
        if (target == null) { if (SpectateTarget != null) EndSpectate(); return; }
        SpectateTarget = target;
        var eye = target.GlobalPosition + target.GlobalTransform.Basis.Z * 2f + Vector3.Up * 1f;
        Camera.GlobalPosition = eye;
        Camera.LookAt(target.GlobalPosition + Vector3.Up * 1.2f, Vector3.Up);
    }

    private void EndSpectate()
    {
        SpectateTarget = null;
        Camera.Position = _cameraHome;
        Camera.Rotation = new Vector3(_pitch, 0, 0);
    }

    public void Revive()
    {
        var wasDown = State.Hp <= 0f;
        State.Mangled = false;
        State.MangledBy = -1;
        State.Hp = Math.Max(1f, State.Hp);
        if (wasDown) EventBus.Instance.RaiseMemberRevived(Index);
        CollisionLayer = Parked ? 0u : 2u;
        _model?.Play("Idle", force: true);
        _body.Rotation = Vector3.Zero;
        _body.Position = new Vector3(0, 0.8f, 0);
        _head.Position = new Vector3(0, 1.72f, 0);
        _label.Text = State.Name;
    }
}
