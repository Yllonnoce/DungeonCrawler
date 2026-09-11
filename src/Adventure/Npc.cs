using DungeonCrawler.Characters;
using DungeonCrawler.Combat;
using DungeonCrawler.Core;
using DungeonCrawler.Dungeon;
using DungeonCrawler.Enemies;
using DungeonCrawler.Party;
using Godot;

namespace DungeonCrawler.Adventure;

/// <summary>
/// A quest giver placed by the floor plan. Talk with E; when escorting, follows the lead and can be hurt by hostile
/// enemies standing next to it (the party cannot harm NPCs). Reaching the down stairs while escorted completes an escort.
/// </summary>
/// <summary>What an NPC is on this floor: a quest giver from the plan, or one of the floor objective's people.</summary>
public enum NpcMode { Quest, Ritualist, Prisoner }

public partial class Npc : CharacterBody3D, IInteractable, ICombatTarget
{
    public const float WalkSpeed = 4.2f;
    public const float FollowDistance = 2.4f;

    public NpcMode Mode { get; set; } = NpcMode.Quest;
    /// <summary>At 0 HP the objective's people go down instead of dying; the party gets them up again.</summary>
    public bool Downed { get; private set; }
    /// <summary>Where the objective wants this NPC to walk (a prisoner fleeing or following a jailer); null = stand.</summary>
    public Vector3? MoveTarget { get; set; }
    /// <summary>A prisoner in a jailer's hands, on the way to the jail.</summary>
    public bool Captured { get; set; }
    /// <summary>A prisoner behind bars.</summary>
    public bool Jailed { get; set; }
    /// <summary>A prisoner the party set free.</summary>
    public bool Freed { get; set; }

    public string Id { get; set; } = "";
    public string NpcName { get; set; } = "Stranger";
    public NpcScript Script { get; set; } = new();
    public string? QuestKey { get; set; }
    public int Floor { get; set; } = 1;
    public bool Dead { get; private set; }
    public bool Escorting { get; private set; }
    public bool Rescued { get; private set; }
    /// <summary>Betrayed the party: gone from the floor, their creatures stand in their place.</summary>
    public bool Turned { get; private set; }
    public float Hp { get; private set; }
    public float MaxHp { get; private set; }

    public string DisplayName => NpcName;
    public bool IsAlive => !Dead && !Downed;
    public bool IsUnaware => false;
    public string Prompt => Dead ? "" : Mode switch
    {
        NpcMode.Ritualist => Downed ? $"Revive {NpcName}" : Objectives.ObjectiveController.Instance is Objectives.RitualController { Started: false } ? $"Begin the ritual with {NpcName}" : "",
        NpcMode.Prisoner => Objectives.ObjectiveController.Instance is Objectives.JailbreakController jail ? jail.PromptFor(this) : "",
        _ => Rescued ? $"{NpcName} waits by the stairs" : $"Talk to {NpcName}",
    };

    private static readonly string[] Models = { "Cleric", "Monk" };
    private MeshInstance3D _body = null!, _head = null!;
    private Label3D _label = null!;
    private AnimatedModel? _model;
    private readonly PathMover _mover = new();
    private float _gravity;
    private float _hurtTimer;
    private float _flash;

    public override void _Ready()
    {
        _body = GetNode<MeshInstance3D>("Body");
        _head = GetNode<MeshInstance3D>("Head");
        _label = GetNode<Label3D>("Label");
        _gravity = (float)ProjectSettings.GetSetting("physics/3d/default_gravity", 9.8f).AsDouble();
        MaxHp = Hp = TierRules.NpcHp(Floor);
        var tint = new Color(0.75f, 0.65f, 0.45f);
        _body.SetSurfaceOverrideMaterial(0, new StandardMaterial3D { AlbedoColor = tint, Roughness = 0.85f });
        _head.SetSurfaceOverrideMaterial(0, new StandardMaterial3D { AlbedoColor = new Color(0.85f, 0.7f, 0.6f) });
        var model = Models[(int)((Rng.StableHash(Id) + (ulong)Floor) % (ulong)Models.Length)]; // stable per run and peer
        _model = AnimatedModel.Attach(this, $"res://assets/models/quaternius_rpg/{model}.gltf", 1.8f, tint, 0.1f);
        if (_model != null) { _body.Visible = false; _head.Visible = false; }
        _label.Text = NpcName;
        AddToGroup("npcs");
    }

    public void Interact(PartyController party, Adventurer actor)
    {
        if (Dead) return;
        switch (Mode)
        {
            case NpcMode.Ritualist:
                if (Net.NetSession.IsClient) return; // the intent went to the server
                if (Downed) { TryRevive(); return; }
                (Objectives.ObjectiveController.Instance as Objectives.RitualController)?.Begin(this);
                return;
            case NpcMode.Prisoner:
                if (Net.NetSession.IsClient) return;
                (Objectives.ObjectiveController.Instance as Objectives.JailbreakController)?.OnInteract(this, party, actor);
                return;
        }
        if (Main.Instance?.Hud is { } hud) hud.OpenDialogue(this); // a dedicated server has no screen; the client opened its own
    }

    /// <summary>Get a downed NPC up, if nothing hostile is close enough to make it pointless.</summary>
    public bool TryRevive()
    {
        if (!Downed || Dead) return false;
        if (GetParent() is { } parent && parent.GetChildren().OfType<Enemy>().Any(e => e.IsAlive && e.IsHostile && e.GlobalPosition.DistanceTo(GlobalPosition) < Objectives.RitualRules.ReviveClearRadius))
        {
            EventBus.Instance.Log($"Not with them at {NpcName}'s throat. Clear the shrine first.");
            return false;
        }
        Downed = false;
        Hp = MathF.Round(MaxHp * Objectives.RitualRules.ReviveHpFraction);
        _label.Text = NpcName;
        _model?.Play("Idle", force: true);
        EventBus.Instance.Log($"{NpcName} gets up, shaken.");
        EventBus.Instance.RaiseNpcEvent(Id, NpcEventKind.Revived);
        return true;
    }

    /// <summary>Down, not dead: the rite pauses, the creatures turn on the party, and the party can get them up.</summary>
    public void GoDown()
    {
        if (Dead || Downed) return;
        Downed = true;
        Hp = 0;
        _label.Text = $"{NpcName} (down)";
        _model?.Play("Death", force: true);
        EventBus.Instance.Log($"{NpcName} goes down!");
        EventBus.Instance.RaiseNpcEvent(Id, NpcEventKind.Downed);
    }

    public void StartFollowing()
    {
        if (Dead || Rescued) return;
        Escorting = true;
        EventBus.Instance.Log($"{NpcName} follows you.");
        EventBus.Instance.RaiseNpcEvent(Id, NpcEventKind.StartedFollowing);
    }

    private Net.PoseBuffer? _net;
    private byte _netFlags;

    /// <summary>A snapshot entry for this NPC (client only).</summary>
    public void ApplyNetState(Net.NpcState s, int tick, double now)
    {
        _net ??= new Net.PoseBuffer(16);
        _net.Push(now, tick, s.Position, s.Yaw, 0f, Net.NetVec.Zero, Net.PoseFlags.None);
        if (s.Flags != _netFlags)
        {
            _netFlags = s.Flags;
            Restore((s.Flags & Net.NpcState.FlagDead) != 0, (s.Flags & Net.NpcState.FlagEscorting) != 0, (s.Flags & Net.NpcState.FlagRescued) != 0, Hp, null, (s.Flags & Net.NpcState.FlagDowned) != 0);
            Captured = (s.Flags & Net.NpcState.FlagCaptured) != 0;
            Jailed = (s.Flags & Net.NpcState.FlagJailed) != 0;
            Freed = Escorting && Mode == NpcMode.Prisoner;
        }
    }

    private void ClientTick(float dt)
    {
        var before = GlobalPosition;
        if (_net?.At(Net.NetSession.Instance.Now - Net.Protocol.InterpolationMs / 1000.0) is { } s)
        {
            var target = new Vector3(s.Position.X, s.Position.Y, s.Position.Z);
            GlobalPosition = before.DistanceSquaredTo(target) > 9f ? target : before.Lerp(target, Math.Min(1f, dt * 15f));
            Rotation = new Vector3(0, Net.PoseBuffer.LerpAngle(Rotation.Y, s.Yaw, Math.Min(1f, dt * 12f)), 0);
        }
        var planar = (GlobalPosition - before) / Math.Max(dt, 0.0001f);
        if (_model != null)
        {
            if (_flash > 0f) { _flash -= dt; _model.Flash(Math.Max(0f, _flash * 2f)); }
            if (!Dead && !_model.IsPlayingOneShot) _model.Play(new Vector2(planar.X, planar.Z).Length() > 0.5f ? "Walk" : "Idle");
        }
    }

    public override void _PhysicsProcess(double delta)
    {
        var dt = (float)delta;
        if (Net.NetSession.IsClient) { ClientTick(dt); return; }
        var v = Velocity;
        v.Y = IsOnFloor() ? 0f : v.Y - _gravity * dt;
        var planar = Vector3.Zero;
        var party = Main.Instance?.Party;
        if (!Dead && !Downed && !Escorting && MoveTarget is { } goal)
        {
            var dest = _mover.Next(this, goal, dt, 0.6f, ClearLine, out var straight);
            if (dest is { } d)
            {
                if (!straight) FaceToward(d);
                planar = PathMover.Planar(GlobalPosition, d) * WalkSpeed;
            }
            if (_mover.Unreachable > 8f) { MoveTarget = null; _mover.Reset(); }
        }
        else if (!Dead && !Downed && Escorting && party != null && IsInstanceValid(party) && party.HasBodies)
        {
            var lead = party.Lead;
            var dest = _mover.Next(this, lead.GlobalPosition, dt, FollowDistance, ClearLine, out var straight);
            if (dest is { } d)
            {
                if (!straight) FaceToward(d);
                planar = PathMover.Planar(GlobalPosition, d) * WalkSpeed;
            }
            else FaceToward(lead.GlobalPosition);
            if (_mover.Unreachable > 5f)
            {
                GlobalPosition = lead.GlobalPosition + lead.GlobalTransform.Basis * new Vector3(0, 0, 1.5f);
                _mover.Reset();
            }
            if (Mode == NpcMode.Quest) CheckStairs();
            Hazard(dt);
        }
        v.X = planar.X;
        v.Z = planar.Z;
        Velocity = v;
        MoveAndSlide();
        if (_model != null)
        {
            if (_flash > 0f) { _flash -= dt; _model.Flash(Math.Max(0f, _flash * 2f)); }
            if (!Dead && !_model.IsPlayingOneShot) _model.Play(planar.Length() > 0.5f ? "Walk" : "Idle");
        }
    }

    private void CheckStairs()
    {
        if (Main.Instance?.Builder is not { Layout: { } layout } builder) return;
        var stairs = builder.Surface(layout.DownStairs);
        if (new Vector2(stairs.X - GlobalPosition.X, stairs.Z - GlobalPosition.Z).Length() > 4.5f) return; // the foot of the flight counts
        Rescued = true;
        Escorting = false;
        _label.Text = $"{NpcName} (safe)";
        EventBus.Instance.Log($"{NpcName} reaches the stairs.");
        EventBus.Instance.RaiseNpcEvent(Id, NpcEventKind.ReachedStairs);
    }

    /// <summary>Hostile enemies adjacent to an escorted NPC wound it every couple of seconds.</summary>
    private void Hazard(float dt)
    {
        _hurtTimer -= dt;
        if (_hurtTimer > 0f || GetParent() is not { } parent) return;
        _hurtTimer = 1.5f;
        Enemy? nearest = null;
        var best = 2.2f;
        foreach (var e in parent.GetChildren().OfType<Enemy>())
        {
            if (!e.IsHostile) continue;
            var d = e.GlobalPosition.DistanceTo(GlobalPosition);
            if (d < best) { best = d; nearest = e; }
        }
        if (nearest == null) return;
        var dmg = MathF.Round((nearest.Attack.MinDamage + nearest.Attack.MaxDamage) / 2f);
        Hp = Math.Max(0, Hp - dmg);
        _flash = 0.25f;
        EventBus.Instance.Log($"{nearest.DisplayName} wounds {NpcName} for {dmg:0}. ({Hp:0}/{MaxHp:0})");
        if (Hp <= 0) Die();
    }

    public DamageResult ReceiveAttack(Stats attackerStats, AttackProfile attack, HitContext ctx, Random rng, string attackerName)
    {
        if (Dead) return DamageResult.Miss(attack.Type);
        if (Downed) return DamageResult.Miss(attack.Type);
        var result = HitResolver.Resolve(attackerStats, Stats.Empty, new StatusEffectController(), attack, ctx, rng);
        if (!result.Hit) return result;
        Hp = Math.Max(0, Hp - result.Amount);
        _flash = 0.25f;
        EventBus.Instance.Log($"{attackerName} wounds {NpcName} for {result.Amount:0}. ({Hp:0}/{MaxHp:0})");
        if (Hp <= 0) { if (Mode == NpcMode.Quest) Die(); else GoDown(); }
        return result;
    }

    public void ApplyStatus(StatusApplication status) { }

    public void Die()
    {
        if (Dead) return;
        Dead = true;
        Escorting = false;
        CollisionLayer = 0;
        _label.Text = $"{NpcName} (dead)";
        _model?.Play("Death", force: true);
        EventBus.Instance.Log($"{NpcName} is dead.");
        EventBus.Instance.RaiseNpcEvent(Id, NpcEventKind.Died);
    }

    /// <summary>The NPC turns on the party: no death, no fate; they simply are not a person any more.</summary>
    public void Turn()
    {
        if (Dead || Turned) return;
        Turned = true;
        Dead = true;
        Escorting = false;
        CollisionLayer = 0;
        Visible = false;
        EventBus.Instance.Log($"{NpcName} drops the act.");
    }

    public void Restore(bool dead, bool escorting, bool rescued, float hp, Vector3? position, bool downed = false)
    {
        if (position is { } p) GlobalPosition = p;
        Hp = Math.Clamp(hp, 0, MaxHp);
        Rescued = rescued;
        if (rescued) _label.Text = $"{NpcName} (safe)";
        Escorting = escorting && !rescued;
        if (downed != Downed)
        {
            Downed = downed;
            _label.Text = downed ? $"{NpcName} (down)" : NpcName;
            _model?.Play(downed ? "Death" : "Idle", force: true);
        }
        if (dead) { Dead = true; CollisionLayer = 0; _label.Text = $"{NpcName} (dead)"; _model?.Play("Death", force: true); }
    }

    public void FaceToward(Vector3 target)
    {
        var flat = new Vector3(target.X, GlobalPosition.Y, target.Z);
        if (flat.DistanceSquaredTo(GlobalPosition) > 0.001f) LookAt(flat, Vector3.Up);
    }

    private bool ClearLine(Vector3 target)
    {
        var q = PhysicsRayQueryParameters3D.Create(GlobalPosition + Vector3.Up * 1.2f, target + Vector3.Up * 1.2f, collisionMask: 1);
        return GetWorld3D().DirectSpaceState.IntersectRay(q).Count == 0;
    }
}
