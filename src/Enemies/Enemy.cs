using DungeonCrawler.Characters;
using DungeonCrawler.Combat;
using DungeonCrawler.Core;
using DungeonCrawler.Dungeon;
using DungeonCrawler.Dungeon.Generation;
using DungeonCrawler.Enemies.Ai;
using DungeonCrawler.Enemies.Collectives;
using DungeonCrawler.Items;
using DungeonCrawler.Party;
using Godot;

namespace DungeonCrawler.Enemies;

/// <summary>An enemy driven by a small FSM, built from an EnemyProfile scaled to its floor.</summary>
public partial class Enemy : CharacterBody3D, ICombatTarget, IHazardVictim
{
    /// <summary>Set before adding to the tree.</summary>
    public EnemyProfile Profile { get; set; } = new();
    public int Floor { get; set; } = 1;
    public int SpawnIndex { get; set; } = -1;
    /// <summary>Network identity, identical on every peer: the spawn index for planned enemies, 100000 for the boss, 200000+ for summoned/horde/wave ones in spawn order.</summary>
    public int NetId { get; set; } = -1;
    public const int BossNetId = 100000;
    public const int DynamicNetIdBase = 200000;
    /// <summary>How much keener than their data enemies are: sight range and how far they will chase.</summary>
    public const float Aggression = 1.4f;
    /// <summary>A fight wakes every unaware enemy within this many metres, whatever its kind.</summary>
    public const float NoiseRadius = 10f;
    [Export] public float GiveUpSeconds { get; set; } = 9f;
    [Export] public float CorpseSeconds { get; set; } = 1.2f;
    [Export] public PackedScene? LootDropScene { get; set; }

    /// <summary>A turned NPC keeps their name on the creature they became.</summary>
    public string? NameOverride { get; set; }
    /// <summary>Counts for a quest as if part of this collective (a turned NPC's creatures).</summary>
    public string? QuestGroupId { get; set; }
    public string DisplayName => NameOverride ?? Profile.Name;
    public EnemyKind Kind => Profile.Kind;
    public bool IsBoss => Profile.IsBoss;
    public float MaxHp { get; private set; }
    public float Hp { get; private set; }
    public bool IsAlive => Hp > 0;
    public bool IsUnaware => Net.NetSession.IsClient ? (_netFlags & Net.EntityState.FlagAware) == 0 : State.Unaware;
    /// <summary>The collective this enemy belongs to, if any (set before entering the tree).</summary>
    public Collective? Group { get; set; }
    public int GroupOrdinal { get; set; }
    /// <summary>Where it was posted; grouped enemies return here.</summary>
    public Vector3 HomePosition { get; set; }
    /// <summary>Latest group order; states poll it.</summary>
    public OrderKind Order { get; set; } = OrderKind.HoldPost;
    public int PatrolIndex { get; set; }
    public Vector3 PostPosition => Group?.PostFor(this) ?? HomePosition;
    public float DistanceToPost() { var p = PostPosition; return new Vector3(GlobalPosition.X - p.X, 0, GlobalPosition.Z - p.Z).Length(); }
    public PartyController? Party { get; private set; }
    /// <summary>The party member this enemy is after (by threat and proximity).</summary>
    public Adventurer? Target { get; private set; }
    public bool HasTarget => Target != null && IsInstanceValid(Target) && !Target.Downed;
    /// <summary>Position to chase: the target, else the nearest living member, else our own spot.</summary>
    public Vector3 TargetPosition => HasTarget ? Target!.GlobalPosition : (Party?.NearestAlive(GlobalPosition)?.GlobalPosition ?? GlobalPosition);
    /// <summary>Actively fighting (companions in Defensive stance react to these).</summary>
    public bool IsHostile => IsAlive && !State.Unaware;
    private readonly Dictionary<int, float> _threat = new(); // keyed by member index: stable across parking and peers
    private float _retargetTimer;
    public AiState State { get; private set; } = new IdleState();
    public Stats Stats { get; private set; } = Stats.Empty;
    public StatusEffectController Effects { get; } = new();
    public AttackProfile Attack { get; private set; } = new();
    public int EmbeddedArrows { get; set; }
    public float AttackRange => Profile.AttackRange;
    public float WindupSeconds => Profile.WindupSeconds;
    public float RecoverSeconds => Profile.RecoverSeconds;
    public float EffectiveMoveSpeed => Profile.MoveSpeed * Effects.Multiplier(Stat.MoveSpeed) * _speedBoost * RunBoost * (InWater ? WaterSlow : 1f);
    /// <summary>Wading through a pond or stream costs everyone the same share of their pace.</summary>
    public const float WaterSlow = 0.65f;
    public bool InWater { get; private set; }
    /// <summary>Swarm fodder from a nest: worth a little XP, drops almost nothing, never saved.</summary>
    public bool IsHorde { get; set; }
    /// <summary>Came through a boss's portal: worth its XP, but rarely carries loot, so lingering never pays.</summary>
    public bool IsReinforcement { get; set; }
    /// <summary>The NPC this creature hunts (a ritual's attackers); it goes for the party only when the party gets in its face.</summary>
    public Adventure.Npc? NpcTarget { get; set; }
    /// <summary>The prisoner this creature is marching to the jail (Jailbreak).</summary>
    public Adventure.Npc? Captive { get; set; }
    /// <summary>A jailer: drops an iron key when it dies.</summary>
    public bool CarriesJailKey { get; set; }
    /// <summary>Seconds since the party last hurt this creature.</summary>
    public float SinceHurt { get; private set; } = 999f;
    /// <summary>What to do with nothing to chase: hunt the NPC again, or stand.</summary>
    public AiState RestState() => NpcTarget is { IsAlive: true } ? new HuntNpcState() : new IdleState();
    /// <summary>The horde this creature came out of (server bookkeeping for joiners).</summary>
    public string? HordeId { get; set; }
    private bool _animPaused;
    private float _detailTimer;

    /// <summary>
    /// Fifty of these at once must stay cheap: no shadows, coarser mesh LODs sooner, shared materials instead of a tinted
    /// copy each, culled past fifty metres, and the skeleton only animates within twenty metres of the party (see Tick).
    /// </summary>
    public void SetSwarmDetail()
    {
        IsHorde = true;
        if (_model == null) return;
        foreach (var mi in FindMeshes(_model))
        {
            mi.CastShadow = GeometryInstance3D.ShadowCastingSetting.Off;
            mi.LodBias = 0.35f;
            mi.VisibilityRangeEnd = 50f;
            for (var i = 0; i < mi.GetSurfaceOverrideMaterialCount(); i++) mi.SetSurfaceOverrideMaterial(i, null);
        }
        _modelMaterials.Clear();
        _mesh.CastShadow = GeometryInstance3D.ShadowCastingSetting.Off;
    }

    private void TickSwarmDetail(float dt)
    {
        _detailTimer -= dt;
        if (_detailTimer > 0f || _anim == null || Party == null || !Party.HasBodies) return;
        _detailTimer = 0.4f;
        var far = GlobalPosition.DistanceSquaredTo(Party.Anchor.GlobalPosition) > 20f * 20f;
        if (far && !_animPaused) { _anim.Pause(); _animPaused = true; }
        else if (!far && _animPaused) { _anim.Play(); _animPaused = false; }
    }
    private float _rippleTimer;

    private void Wade(float dt, bool entered)
    {
        var builder = Main.Instance!.Builder;
        var at = GlobalPosition with { Y = builder.WaterLevel(DungeonBuilder.WorldToCell(GlobalPosition)) + 0.02f };
        var root = builder.FloorRoot ?? GetParent();
        if (entered) { Dungeon.Props.WaterAudio.SplashAt(root, at, -9f); Dungeon.Props.Ripple.Spawn(root, at, 1.4f); }
        _rippleTimer -= dt;
        if (_rippleTimer <= 0f && new Vector2(Velocity.X, Velocity.Z).Length() > 0.6f) { Dungeon.Props.Ripple.Spawn(root, at); _rippleTimer = 0.35f; }
    }
    /// <summary>Sustained speed multiplier set by states (alarm runners sprint).</summary>
    public float RunBoost { get; set; } = 1f;
    /// <summary>Actually fighting or hunting with eyes on the party (a blind chase does not count).</summary>
    public bool IsEngaged => IsAlive && (State is AttackState or ShootState or SignatureState || (State is ChaseState && CanSeeParty()));
    /// <summary>Total ground covered; used by the pathing tests.</summary>
    public float DistanceTravelled { get; private set; }

    private MeshInstance3D _mesh = null!;
    private StandardMaterial3D _material = null!;
    private Color _baseColor;
    private float _gravity;
    private float _flash;
    private bool _dying;
    private float _speedBoost = 1f;
    private float _signatureTimer;
    private Random _rng = new(1);
    private PackedScene _projectileScene = null!;

    private List<PathNode>? _path;
    private int _pathIndex;
    public string PathDebug => $"cell={DungeonBuilder.WorldToCell(GlobalPosition)} pos=({GlobalPosition.X:0.0},{GlobalPosition.Z:0.0}) path={(_path == null ? "none" : _path.Count.ToString())} idx={_pathIndex} next={(_path != null && _pathIndex < _path.Count ? _path[_pathIndex].Cell.ToString() + (_path[_pathIndex].Under ? "u" : "") : "-")} vel=({Velocity.X:0.0},{Velocity.Z:0.0}) onFloor={IsOnFloor()} speed={EffectiveMoveSpeed:0.0} y={GlobalPosition.Y:0.00} hit={(GetSlideCollisionCount() > 0 ? (GetLastSlideCollision().GetCollider() as Node)?.Name + " n=" + GetLastSlideCollision().GetNormal().ToString("0.0") : "-")}";
    private float _repathTimer;
    private GridPos _pathTarget;

    public override void _Ready()
    {
        AddToGroup("enemies");
        _rng = new Random(Rng.ActorSeed(Game.Instance?.GameSeed ?? 0UL, Floor, NetId)); // the same rolls on every peer and run
        MaxHp = Profile.ScaledHp(Floor);
        Hp = MaxHp;
        Stats = Profile.BuildStats();
        Attack = Profile.BuildAttack(Floor);
        _signatureTimer = Profile.SignatureCooldown * 0.5f;

        _mesh = GetNode<MeshInstance3D>("Mesh");
        _material = new StandardMaterial3D { AlbedoColor = new Color(Profile.Color), Roughness = 0.8f };
        _baseColor = _material.AlbedoColor;
        _mesh.SetSurfaceOverrideMaterial(0, _material);
        if (Math.Abs(Profile.Scale - 1f) > 0.01f)
        {
            _mesh.Scale = Vector3.One * Profile.Scale;
            _mesh.Position = new Vector3(0, 0.9f * Profile.Scale, 0);
            var col = GetNode<CollisionShape3D>("Collision");
            col.Shape = new CapsuleShape3D { Radius = 0.45f * Profile.Scale, Height = 1.8f * Profile.Scale };
            col.Position = new Vector3(0, 0.9f * Profile.Scale, 0);
        }
        LoadModel();
        _gravity = (float)ProjectSettings.GetSetting("physics/3d/default_gravity", 9.8f).AsDouble();
        Party = Main.Instance?.Party;
        LootDropScene ??= GD.Load<PackedScene>("res://scenes/Props/LootDrop.tscn");
        _projectileScene = GD.Load<PackedScene>("res://scenes/Props/Projectile.tscn");
        if (HomePosition == Vector3.Zero) HomePosition = GlobalPosition;
        if (Group != null) State = Group.RestState(this);
        State.Enter(this);
    }

    // ---- animated model ------------------------------------------------------------------------

    private Node3D? _model;
    private AnimationPlayer? _anim;
    private string _currentAnim = "";
    private readonly List<StandardMaterial3D> _modelMaterials = new();

    /// <summary>Weapon meshes KayKit characters carry all at once; every one but the kind's own is hidden.</summary>
    private static readonly HashSet<string> KayKitWeapons = new() { "Knife_Offhand", "1H_Crossbow", "2H_Crossbow", "Knife", "Throwable", "1H_Axe_Offhand", "Barbarian_Round_Shield", "1H_Axe", "2H_Axe", "Mug", "1H_Sword_Offhand", "Badge_Shield", "Rectangle_Shield", "Round_Shield", "Spike_Shield", "1H_Sword", "2H_Sword" };

    /// <summary>Clip names per logical animation: the Quaternius names first, then KayKit's.</summary>
    private static readonly Dictionary<string, string[]> Clips = new()
    {
        ["Idle"] = new[] { "Idle" },
        ["Walk"] = new[] { "Walk", "Walking_A" },
        ["Run"] = new[] { "Run", "Running_A" },
        ["Punch"] = new[] { "Punch", "1H_Melee_Attack_Chop", "Unarmed_Melee_Attack_Punch_A" },
        ["Shoot"] = new[] { "Punch", "2H_Ranged_Shoot", "1H_Ranged_Shoot" },
        ["HitReact"] = new[] { "HitReact", "Hit_A" },
        ["Death"] = new[] { "Death", "Death_A" },
    };

    private void LoadModel()
    {
        if (Profile.Model == "") return;
        // a plain name is a Quaternius monster; a path under assets/models is any other pack (KayKit characters are .glb)
        var path = Profile.Model.Contains('/') ? $"res://assets/models/{Profile.Model}.glb" : $"res://assets/models/quaternius_monsters/{Profile.Model}.gltf";
        if (!ResourceLoader.Exists(path) && Profile.Model.Contains('/')) path = $"res://assets/models/{Profile.Model}.gltf";
        if (!ResourceLoader.Exists(path)) return;
        _model = GD.Load<PackedScene>(path).Instantiate<Node3D>();
        _model.Name = "Model";
        AddChild(_model);
        var keep = Profile.Kind == EnemyKind.Ranged ? "2H_Crossbow" : FindMeshes(_model).Select(m => m.Name.ToString()).FirstOrDefault(n => n is "Knife" or "1H_Axe" or "1H_Sword") ?? "";
        foreach (var mi in FindMeshes(_model))
            if (KayKitWeapons.Contains(mi.Name.ToString())) mi.Visible = mi.Name == keep;
        // Pose the rig before measuring: a character from the 3dObject viewer stands differently in its saved pose
        // than in its bind pose, and the feet have to be measured where Idle puts them (see AnimatedModel.Attach).
        _anim = FindAnimationPlayer(_model);
        PlayAnim("Idle");
        _anim?.Seek(0, update: true);
        // scale the model so it stands 1.8 m tall times the profile scale
        var aabb = new Aabb();
        var first = true;
        foreach (var mi in FindMeshes(_model))
        {
            if (!mi.Visible) continue;
            var box = (AnimatedModel.SkinnedBox(mi) ?? mi.GetAabb()).Abs();
            var xf = mi.GlobalTransform.AffineInverse() * mi.GlobalTransform; // identity; keep local box
            aabb = first ? box : aabb.Merge(box);
            first = false;
        }
        var height = Math.Max(0.5f, aabb.Size.Y);
        var scale = 1.8f * Profile.Scale / height;
        _model.Scale = Vector3.One * scale;
        _model.Position = new Vector3(0, -aabb.Position.Y * scale, 0);
        _model.RotateY(Mathf.Pi); // glTF forward is +Z; our LookAt faces -Z
        _mesh.Visible = false;

        foreach (var mi in FindMeshes(_model))
            for (var i = 0; i < mi.GetSurfaceOverrideMaterialCount(); i++)
            {
                if (mi.GetActiveMaterial(i)?.Duplicate() is not StandardMaterial3D mat) continue;
                mat.AlbedoColor = Colors.White.Lerp(new Color(Profile.Color), 0.25f);
                mi.SetSurfaceOverrideMaterial(i, mat);
                _modelMaterials.Add(mat);
            }
        if (Profile.Weapon != "" && WeaponGrip.Attach(_model, Profile.Weapon, "Right") == null) GD.Print($"[Enemy] {Profile.Id}: no grip bone or no file for weapon {Profile.Weapon}");
        if (Profile.Model.Contains('/')) GD.Print($"[Enemy] {Profile.Id}: {path} box {aabb.Position:0.000} size {aabb.Size:0.000} scale {_model.Scale.Y:0.00} lift {_model.Position.Y:0.000}");
    }

    private static IEnumerable<MeshInstance3D> FindMeshes(Node n)
    {
        if (n is MeshInstance3D mi) yield return mi;
        foreach (var c in n.GetChildren()) foreach (var m in FindMeshes(c)) yield return m;
    }

    private static AnimationPlayer? FindAnimationPlayer(Node n)
    {
        if (n is AnimationPlayer ap) return ap;
        foreach (var c in n.GetChildren()) if (FindAnimationPlayer(c) is { } found) return found;
        return null;
    }

    /// <summary>Play a clip if the model has it; Idle/Walk/Run loop, the rest play once.</summary>
    public void PlayAnim(string name, bool force = false)
    {
        if (_anim == null || (!force && _currentAnim == name)) return;
        var key = name == "Punch" && Profile.Kind == EnemyKind.Ranged ? "Shoot" : name;
        var clip = (Clips.TryGetValue(key, out var options) ? options : new[] { name }).FirstOrDefault(c => _anim.HasAnimation(c));
        if (clip == null) return;
        var loop = name is "Idle" or "Walk" or "Run";
        _anim.GetAnimation(clip).LoopMode = loop ? Animation.LoopModeEnum.Linear : Animation.LoopModeEnum.None;
        _anim.Play(clip, customBlend: 0.15);
        _currentAnim = name;
    }

    private void UpdateAnimation()
    {
        if (_anim == null || !IsAlive) return;
        if (_currentAnim is "Punch" or "HitReact" && _anim.IsPlaying()) { _anim.SpeedScale = 1f; return; }   // one-shots play at their own pace
        var ground = new Vector2(Velocity.X, Velocity.Z).Length();
        var moving = ground > 0.3f;
        var viewer = Profile.Model.StartsWith("viewer/");
        PlayAnim(viewer ? ViewerGait.GaitFor(1.8f * Profile.Scale, ground, _currentAnim is "Walk" or "Run" ? _currentAnim : null) : moving ? (EffectiveMoveSpeed > 3.2f ? "Run" : "Walk") : "Idle");
        // a viewer-rigged character picks the cycle nearest its pace and plays it at the pace the ground actually passes, so the feet stay planted
        if (viewer) _anim.SpeedScale = moving ? ViewerGait.SpeedScaleFor(_currentAnim, 1.8f * Profile.Scale, ground) : 1f;
    }

    // ---- networking: a client shows what the server streams --------------------------------------

    private Net.PoseBuffer? _net;
    private byte _netState, _netFlags;
    private bool _clientDead;
    private float _corpseTimer;

    /// <summary>A snapshot entry for this enemy (client only): where it is, how hurt it is, what it is doing.</summary>
    public void ApplyNetState(Net.EntityState s, int tick, double now)
    {
        _net ??= new Net.PoseBuffer(16);
        _net.Push(now, tick, s.Position, s.Yaw, 0f, Net.NetVec.Zero, Net.PoseFlags.None);
        Hp = s.Hp;
        _netState = s.State;
        _netFlags = s.Flags;
        if ((s.Flags & Net.EntityState.FlagAlive) == 0 && !_clientDead) ClientDie();
    }

    /// <summary>A client showing a hit the server resolved.</summary>
    public void ClientHurt(float amount, float hp)
    {
        Hp = hp;
        _flash = 0.25f;
        if (hp > 0) PlayAnim("HitReact", force: true);
        else if (!_clientDead) ClientDie();
    }

    /// <summary>Cosmetic death: the server already handed out XP and loot.</summary>
    public void ClientDie()
    {
        _clientDead = true;
        Hp = 0;
        PlayAnim("Death", force: true);
        CollisionLayer = 0;
        CollisionMask = 1;
        _material.AlbedoColor = _baseColor.Darkened(0.6f);
        Effects.Clear();
        EventBus.Instance.PlaySfx(IsBoss ? "boss" : "enemy_die");
    }

    private void ClientTick(float dt)
    {
        var now = Net.NetSession.Instance.Now - Net.Protocol.InterpolationMs / 1000.0;
        var before = GlobalPosition;
        if (_net?.At(now) is { } s)
        {
            var target = new Vector3(s.Position.X, s.Position.Y, s.Position.Z);
            GlobalPosition = before.DistanceSquaredTo(target) > 9f ? target : before.Lerp(target, Math.Min(1f, dt * 15f));
            Rotation = new Vector3(0, Net.PoseBuffer.LerpAngle(Rotation.Y, s.Yaw, Math.Min(1f, dt * 12f)), 0);
        }
        Velocity = (GlobalPosition - before) / Math.Max(dt, 0.0001f);
        if (IsHorde) TickSwarmDetail(dt);
        UpdateAnimation();
        UpdateTint(dt);
        if (!_clientDead) return;
        _corpseTimer += dt;
        if (_corpseTimer > CorpseSeconds) Position += Vector3.Down * dt * 0.8f;
        if (_corpseTimer >= CorpseSeconds + 1.5f) QueueFree();
    }

    public override void _PhysicsProcess(double delta)
    {
        var dt = (float)delta;
        if (Party == null || !IsInstanceValid(Party)) Party = Main.Instance?.Party;
        if (Net.NetSession.IsClient) { ClientTick(dt); return; }
        var wasInWater = InWater;
        InWater = Main.Instance?.Builder.IsInWater(GlobalPosition) == true;
        if (InWater && IsAlive) Wade(dt, !wasInWater);
        if (IsHorde) TickSwarmDetail(dt);
        if (Party == null || !IsInstanceValid(Party)) return;
        UpdateAnimation();

        if (IsAlive)
        {
            foreach (var tick in Effects.Update(dt))
            {
                if (tick.IsHeal) { Hp = Math.Min(MaxHp, Hp + tick.Amount); continue; }
                Hp = Math.Max(0, Hp - DamagePipeline.Mitigate(tick.Amount, tick.Type, Stats));
                if (Hp <= 0) { EventBus.Instance.Log($"{DisplayName} succumbs to {tick.Kind.ToString().ToLower()}."); _dying = true; break; }
            }
            _signatureTimer -= dt;
        }

        if (_dying && State is not DeadState) ChangeState(new DeadState());

        if (IsAlive && !State.Unaware) Retarget(dt);
        if (IsAlive && Effects.IsStunned) MoveToward(GlobalPosition, dt);
        else State.Update(this, dt);

        UpdateTint(dt);
    }

    private void UpdateTint(float dt)
    {
        if (_flash > 0f)
        {
            _flash -= dt;
            _material.Emission = Colors.White * Mathf.Max(0f, _flash * 3f);
            _material.EmissionEnabled = _flash > 0f;
        }
        if (!IsAlive) return;
        var tint = _baseColor;
        if (Effects.Has(StatusKind.Frost)) tint = tint.Lerp(new Color(0.5f, 0.8f, 1f), 0.6f);
        else if (Effects.Has(StatusKind.Poison)) tint = tint.Lerp(new Color(0.3f, 0.9f, 0.3f), 0.5f);
        else if (Effects.Has(StatusKind.Burn)) tint = tint.Lerp(new Color(1f, 0.6f, 0.1f), 0.5f);
        if (Effects.IsStunned) tint = tint.Lerp(Colors.Yellow, 0.5f);
        _material.AlbedoColor = tint;
        foreach (var m in _modelMaterials)
        {
            m.AlbedoColor = Colors.White.Lerp(tint, 0.35f);
            m.EmissionEnabled = _flash > 0f;
            m.Emission = Colors.White * Mathf.Max(0f, _flash * 2f);
        }
    }

    public void ChangeState(AiState next)
    {
        if (DebugTools.TraceAi) GD.Print($"[ai] {Name}: {State.Name} -> {next.Name} (hp {Hp:0}, dist {DistanceToParty():0.0})");
        var wasUnaware = State.Unaware;
        State.Exit(this);
        State = next;
        State.Enter(this);
        if (wasUnaware && next is ChaseState) { AlertPack(); WakeNearby(); Group?.Notify(this); if (IsBoss) EventBus.Instance.RaiseBossSeen(this); }
    }

    /// <summary>Run directly away from the party (used by FleeState).</summary>
    public void FleeStep(float dt)
    {
        if (Party == null) return;
        var away = GlobalPosition - TargetPosition;
        away.Y = 0;
        if (away.LengthSquared() < 0.01f) away = Vector3.Forward;
        MoveToward(GlobalPosition + away.Normalized() * 3f, dt);
    }

    /// <summary>Debug: notice the party regardless of range.</summary>
    public void ForceAggro() { if (IsAlive && State.Unaware) ChangeState(new ChaseState()); }

    // ---- perception --------------------------------------------------------------------------

    public float DistanceToParty() => Party == null || !Party.PartyAlive ? float.MaxValue : GlobalPosition.DistanceTo(TargetPosition);

    private bool LineOfSightTo(Vector3 point)
    {
        var space = GetWorld3D().DirectSpaceState;
        var query = PhysicsRayQueryParameters3D.Create(GlobalPosition + Vector3.Up * 1.2f, point + Vector3.Up * 1.2f, collisionMask: 1);
        return space.IntersectRay(query).Count == 0;
    }

    public bool HasLineOfSight() => Party != null && Party.PartyAlive && LineOfSightTo(TargetPosition);

    /// <summary>Noticed anyone? Each member is checked against their own stealth; the first seen becomes the target.</summary>
    public bool CanSeeParty()
    {
        if (Party == null || !Party.PartyAlive) return false;
        if (HasTarget)
        {
            var range = Profile.DetectRange * Aggression * Mathf.Clamp(1f - Target!.State.Stats[Stat.Stealth] / 200f, 0.25f, 1f) * (Target.Crouching ? 0.6f : 1f) * (Target.InWater ? 1.3f : 1f);
            if (GlobalPosition.DistanceTo(Target.GlobalPosition) <= range && LineOfSightTo(Target.GlobalPosition)) return true;
        }
        foreach (var adv in Party.Adventurers)
        {
            if (adv.Downed) continue;
            var range = Profile.DetectRange * Aggression * Mathf.Clamp(1f - adv.State.Stats[Stat.Stealth] / 200f, 0.25f, 1f) * (adv.Crouching ? 0.6f : 1f) * (adv.InWater ? 1.3f : 1f); // splashing carries
            if (GlobalPosition.DistanceTo(adv.GlobalPosition) > range || !LineOfSightTo(adv.GlobalPosition)) continue;
            Target = adv;
            return true;
        }
        return false;
    }

    public void NoteThreat(Adventurer from, float amount)
    {
        var id = from.Index;
        _threat[id] = _threat.GetValueOrDefault(id) + amount;
        if (!HasTarget) Target = from;
    }

    /// <summary>Re-pick the target every half second: recent damage plus proximity, preferring members we can see.</summary>
    private void Retarget(float dt)
    {
        SinceHurt += dt;
        if (Party == null) return;
        foreach (var k in _threat.Keys.ToList()) _threat[k] *= MathF.Pow(0.85f, dt);
        _retargetTimer -= dt;
        if (_retargetTimer > 0) return;
        _retargetTimer = 0.5f;
        Adventurer? best = null;
        var bestScore = float.MinValue;
        foreach (var adv in Party.Adventurers)
        {
            if (adv.Downed) continue;
            var dist = GlobalPosition.DistanceTo(adv.GlobalPosition);
            if (dist > Profile.DetectRange * Aggression * 1.5f) continue;
            var score = _threat.GetValueOrDefault(adv.Index) + 12f / (dist + 1f) + (LineOfSightTo(adv.GlobalPosition) ? 4f : 0f);
            if (score > bestScore) { bestScore = score; best = adv; }
        }
        if (best != null) Target = best;
        else if (!HasTarget) Target = Party.NearestAlive(GlobalPosition);
    }

    // ---- locomotion --------------------------------------------------------------------------

    /// <summary>Enemies do not collide with each other (they would block corridors); a soft push keeps them from stacking.</summary>
    private Vector3 Separation()
    {
        var push = Vector3.Zero;
        foreach (var other in GetParent().GetChildren())
        {
            if (other is not Enemy e || e == this || !e.IsAlive) continue;
            var d = GlobalPosition - e.GlobalPosition;
            d.Y = 0;
            var len = d.Length();
            var radius = 0.9f * (Profile.Scale + e.Profile.Scale) * 0.5f;
            if (len > radius || len < 0.001f) continue;
            push += d / len * (radius - len) / radius;
        }
        return push;
    }

    public void MoveToward(Vector3 target, float dt)
    {
        var v = Velocity;
        v.Y = IsOnFloor() ? 0f : v.Y - _gravity * dt;
        var to = target - GlobalPosition;
        to.Y = 0;
        var planar = to.LengthSquared() > 0.01f ? to.Normalized() * EffectiveMoveSpeed : Vector3.Zero;
        // separation only nudges sideways, so a blocker straight ahead never cancels forward progress
        var push = Separation();
        if (planar != Vector3.Zero)
        {
            var dir = planar.Normalized();
            push -= dir * push.Dot(dir);
        }
        planar += push * EffectiveMoveSpeed * 0.8f;
        v.X = planar.X;
        v.Z = planar.Z;
        Velocity = v;
        var before = GlobalPosition;
        MoveAndSlide();
        var moved = GlobalPosition - before; moved.Y = 0;
        DistanceTravelled += moved.Length();
        if (planar != Vector3.Zero) FaceToward(target);
    }

    /// <summary>Follow an A* path through the corridors; go straight when the target is close and visible.</summary>
    public void MoveAlongPathToward(Vector3 target, float dt)
    {
        var builder = Main.Instance?.Builder;
        var layout = builder?.Layout;
        if (layout == null) { MoveToward(target, dt); return; }

        var dist = GlobalPosition.DistanceTo(target);
        if (dist < 5f && Math.Abs(GlobalPosition.Y - target.Y) < 1.5f && HasLineOfSight()) { _path = null; MoveToward(target, dt); return; }

        var me = new PathNode(DungeonBuilder.WorldToCell(GlobalPosition), builder!.IsUnderAt(GlobalPosition));
        var goal = new PathNode(DungeonBuilder.WorldToCell(target), builder.IsUnderAt(target));
        _repathTimer -= dt;
        if (_path == null || goal.Cell != _pathTarget || _repathTimer <= 0f)
        {
            _path = GridPathfinder.FindPath(layout, me, goal, builder.IsCellBlocked, builder.IsSecretOpen);
            _pathIndex = 1;
            _pathTarget = goal.Cell;
            _repathTimer = IsHorde ? 0.9f : 0.5f; // a swarm can afford a staler path
        }
        if (_path == null || _path.Count < 2) { MoveToward(target, dt); return; }

        _pathIndex = Math.Clamp(_pathIndex, 1, _path.Count - 1);
        var wp = Waypoint(_path[_pathIndex]);
        if (Flat(GlobalPosition).DistanceTo(Flat(wp)) < 0.7f && _pathIndex < _path.Count - 1) { _pathIndex++; wp = Waypoint(_path[_pathIndex]); }
        MoveToward(_pathIndex >= _path.Count - 1 ? target : wp, dt);
    }

    private Vector3 Waypoint(PathNode n) => Main.Instance!.Builder.Surface(n.Cell, n.Under) with { Y = GlobalPosition.Y };
    private static Vector3 Flat(Vector3 v) => new(v.X, 0, v.Z);

    public void ChargeStep(float dt)
    {
        if (Party == null) return;
        _speedBoost = 3.5f;
        MoveToward(TargetPosition, dt);
        _speedBoost = 1f;
    }

    public void FaceToward(Vector3 target)
    {
        var flat = new Vector3(target.X, GlobalPosition.Y, target.Z);
        if (flat.DistanceSquaredTo(GlobalPosition) > 0.001f) LookAt(flat, Vector3.Up);
    }

    // ---- attacks -----------------------------------------------------------------------------

    public void StrikeParty()
    {
        PlayAnim("Punch", force: true);
        if (!HasTarget || DistanceToParty() > AttackRange * 1.15f) return;
        Target!.ReceiveAttack(Stats, Attack, DisplayName, this);
    }

    /// <summary>A blow at the NPC this creature hunts.</summary>
    public void StrikeNpc(Adventure.Npc npc)
    {
        PlayAnim("Punch", force: true);
        if (!npc.IsAlive || GlobalPosition.DistanceTo(npc.GlobalPosition) > AttackRange * 1.15f) return;
        npc.ReceiveAttack(Stats, Attack, new HitContext(), Party?.Rng ?? new Random(), DisplayName);
    }

    public void FireAtParty() => Fire(Attack, 0f);

    private void Fire(AttackProfile attack, float yawOffsetDegrees)
    {
        PlayAnim("Punch", force: true);
        if (Party == null || Main.Instance?.Builder.FloorRoot is not { } root) return;
        var from = GlobalPosition + Vector3.Up * (1.3f * Profile.Scale);
        var to = TargetPosition + Vector3.Up * 1.2f;
        var dir = (to - from).Normalized().Rotated(Vector3.Up, Mathf.DegToRad(yawOffsetDegrees));
        var p = _projectileScene.Instantiate<Projectile>();
        root.AddChild(p);
        p.GlobalPosition = from + dir * 0.8f;
        p.SetupEnemy(this, attack, dir * Profile.ProjectileSpeed, new Color(Profile.Color).Lightened(0.3f));
    }

    public bool TrySignature()
    {
        if (!IsBoss || Profile.Signature == Signature.None || _signatureTimer > 0f || Party == null || !HasTarget) return false;
        if (DistanceToParty() > 9f || !HasLineOfSight()) return false;
        _signatureTimer = Profile.SignatureCooldown;
        ChangeState(new SignatureState(Profile.Signature));
        return true;
    }

    public void AnnounceSignature()
    {
        EventBus.Instance.Log(Profile.Signature switch
        {
            Signature.Slam => $"{DisplayName} raises its fists!",
            Signature.Volley => $"{DisplayName} rears back!",
            Signature.Charge => $"{DisplayName} lowers its head and charges!",
            Signature.Summon => $"{DisplayName} calls into the dark!",
            _ => $"{DisplayName} does something.",
        });
    }

    public void PerformSignature()
    {
        if (Party == null) return;
        switch (Profile.Signature)
        {
            case Signature.Slam:
                var victims = Party.AdventurersWithin(GlobalPosition, 4.5f).ToList();
                if (victims.Count > 0)
                {
                    var slam = Attack with { MinDamage = Attack.MinDamage * 1.4f, MaxDamage = Attack.MaxDamage * 1.4f, OnHit = Attack.OnHit.Append(new StatusApplication(StatusKind.Stun, 1.2f, 0)).ToArray() };
                    foreach (var v in victims) v.ReceiveAttack(Stats, slam, $"{DisplayName}'s slam", this);
                }
                else EventBus.Instance.Log("The ground shakes, but no one is in reach.");
                break;
            case Signature.Volley:
                Fire(Attack, -14f); Fire(Attack, 0f); Fire(Attack, 14f);
                break;
            case Signature.Charge:
                var charge = Attack with { MinDamage = Attack.MinDamage * 2f, MaxDamage = Attack.MaxDamage * 2f, OnHit = Attack.OnHit.Append(new StatusApplication(StatusKind.Stun, 1f, 0)).ToArray() };
                if (HasTarget) Target!.ReceiveAttack(Stats, charge, $"{DisplayName}'s charge", this);
                break;
            case Signature.Summon:
                var builder = Main.Instance!.Builder;
                var alive = GetParent().GetChildren().OfType<Enemy>().Count(e => e.IsAlive && e.Profile.Id == Profile.SummonId);
                var spawned = 0;
                for (var i = 0; i < 2 && alive + spawned < 4; i++)
                {
                    var offset = new Vector3(i == 0 ? 1.5f : -1.5f, 0, 1f);
                    if (builder.SpawnEnemy(Profile.SummonId, GlobalPosition + offset, Floor) is { } minion) { minion.ForceAggro(); spawned++; }
                }
                if (spawned > 0) EventBus.Instance.Log($"{spawned} {(spawned == 1 ? "creature answers" : "creatures answer")} the call.");
                break;
        }
    }

    // ---- damage ------------------------------------------------------------------------------

    public DamageResult ReceiveAttack(Stats attackerStats, AttackProfile attack, HitContext ctx, Random rng, string attackerName)
    {
        if (!IsAlive) return DamageResult.Miss(attack.Type);
        var result = HitResolver.Resolve(attackerStats, Stats, Effects, attack, ctx, rng);
        if (!result.Hit) return result;
        Hp = Math.Max(0, Hp - result.Amount);
        _flash = 0.25f;
        SinceHurt = 0f;
        EventBus.Instance.RaiseEnemyHurt(this, result.Amount, result.Crit);
        if (Hp <= 0) _dying = true;
        else { PlayAnim("HitReact", force: true); EventBus.Instance.PlaySfx("enemy_hurt"); }
        if (Hp > 0 && State.Unaware) ChangeState(new ChaseState());
        else if (!_fled && Profile.FleeHpFraction > 0 && Hp / MaxHp < Profile.FleeHpFraction && State is not FleeState)
        {
            _fled = true;
            ChangeState(new FleeState());
        }
        return result;
    }

    public void ApplyStatus(StatusApplication status)
    {
        if (!IsAlive) return;
        Effects.Apply(status);
    }

    private bool _fled;

    /// <summary>Pack hunters wake their idle kin nearby when they notice the party.</summary>
    public void AlertPack()
    {
        if (!Profile.PackHunter) return;
        foreach (var other in GetParent().GetChildren().OfType<Enemy>())
            if (other != this && other.IsAlive && other.Profile.Id == Profile.Id && other.State is IdleState && other.Group == null && other.GlobalPosition.DistanceTo(GlobalPosition) < 12f)
                other.ChangeState(new ChaseState());
    }

    /// <summary>The noise of one enemy joining a fight rouses anything unaware nearby, so rooms answer as a whole.</summary>
    public void WakeNearby()
    {
        if (GetParent() is not { } parent) return;
        foreach (var other in parent.GetChildren().OfType<Enemy>())
            if (other != this && other.IsAlive && other.State.Unaware && other.GlobalPosition.DistanceTo(GlobalPosition) < NoiseRadius
                && Main.Instance?.Builder.IsUnderAt(other.GlobalPosition) == Main.Instance?.Builder.IsUnderAt(GlobalPosition))
                other.ChangeState(new ChaseState());
    }

    public void RestoreFrom(Save.EnemyDto dto)
    {
        Hp = Math.Clamp(dto.Hp, 1, MaxHp);
        EmbeddedArrows = dto.EmbeddedArrows;
        GlobalPosition = new Vector3(dto.X, dto.Y, dto.Z);
    }

    public void Kill()
    {
        if (!IsAlive) return;
        Hp = 0;
        _dying = true;
    }

    public VictimKind HazardVictimKind => IsBoss ? VictimKind.Boss : VictimKind.Enemy;

    private bool _environmentalKill;

    /// <summary>The dungeon itself caught this creature. Regulars die (no loot; XP if a member saw it); bosses stagger.</summary>
    public void ReceiveHazard(HazardDamage kind, HazardOutcome outcome, string sourceName, int hazardIndex)
    {
        if (!IsAlive) return;
        if (outcome.Lethal) { KillByEnvironment(sourceName, kind); return; }
        var amount = MaxHp * outcome.HpFraction;
        Hp = Math.Max(1, Hp - amount);
        _flash = 0.25f;
        SinceHurt = 0f;
        EventBus.Instance.RaiseEnemyHurt(this, amount, false);
        if (outcome.StunSeconds > 0) Effects.Apply(new StatusApplication(StatusKind.Stun, outcome.StunSeconds, 0));
        PlayAnim("HitReact", force: true);
        EventBus.Instance.Log($"{sourceName} staggers {DisplayName} for {amount:0}.");
    }

    /// <summary>An environmental kill: the death sequence without the loot, and only the XP if the party was close enough to count it.</summary>
    public void KillByEnvironment(string sourceName, HazardDamage kind)
    {
        if (!IsAlive) return;
        _environmentalKill = true;
        EventBus.Instance.Log($"{sourceName} {(kind == HazardDamage.Crush ? "crushes" : "impales")} {DisplayName}.");
        EventBus.Instance.RaiseEnemyKilledByHazard(this, sourceName);
        Kill();
    }

    public void OnDeath()
    {
        PlayAnim("Death", force: true);
        EventBus.Instance.PlaySfx(IsBoss ? "boss" : "enemy_die");
        EventBus.Instance.Log(IsBoss ? $"{DisplayName} falls! The way down is open." : $"{DisplayName} dies.");
        CollisionLayer = 0;
        CollisionMask = 1;
        _material.AlbedoColor = _baseColor.Darkened(0.6f);
        Effects.Clear();

        if (!_environmentalKill || Party?.Active.Any(a => !a.Downed && a.GlobalPosition.DistanceTo(GlobalPosition) <= 10f) == true) Party?.AwardXp(Profile.ScaledXp(Floor));
        Group?.OnMemberDied(this);
        EventBus.Instance.RaiseEnemyDied(this);
        if (_environmentalKill) { if (IsBoss) Main.Instance?.OnBossDefeated(Floor); return; } // the dungeon's kills leave no loot

        var cat = Game.Instance.Catalog;
        var level = TierRules.ItemLevel(Floor);
        if (CarriesJailKey && cat.TryBase("item.iron_key", out _)) Drop(cat.Create("item.iron_key"), new Vector3(0, 0, 0.5f)); // the jailer's key, whatever else it had
        if (IsHorde && _rng.NextDouble() > 0.04) return; // fifty of them would bury the floor in bones
        if (IsReinforcement && _rng.NextDouble() > 0.15) return; // an endless stream must not be a loot farm
        if (!IsHorde && _rng.NextDouble() < 0.5 && cat.TryBase("item.bone_shard", out _)) Drop(cat.Create("item.bone_shard"), Vector3.Zero);
        var tableId = IsBoss ? "table.chest" : "table.enemy";
        if (cat.Tables.TryGetValue(tableId, out var table))
        {
            var drops = IsBoss ? LootRoller.RollMany(cat, table with { Rolls = 3, DropChance = 1f }, level + 1, _rng) : LootRoller.RollMany(cat, table, level, _rng);
            for (var i = 0; i < drops.Count; i++) Drop(drops[i], new Vector3(-0.6f + i * 0.6f, 0, 0.4f));
        }
        var arrows = (EmbeddedArrows + 1) / 2;
        if (arrows > 0 && cat.TryBase("item.arrow", out _)) Drop(cat.Create("item.arrow", arrows), new Vector3(0.6f, 0, -0.3f));

        if (IsBoss) Main.Instance?.OnBossDefeated(Floor);
    }

    private void Drop(ItemInstance item, Vector3 offset)
    {
        if (LootDropScene?.Instantiate() is not LootDrop drop) return;
        drop.Item = item;
        GetParent().AddChild(drop);
        drop.GlobalPosition = GlobalPosition + Vector3.Up * 0.4f + offset;
        EventBus.Instance.RaiseLootSpawned(drop);
    }
}
