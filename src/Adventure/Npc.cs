using DungeonCrawler.Characters;
using DungeonCrawler.Combat;
using DungeonCrawler.Core;
using DungeonCrawler.Dungeon;
using DungeonCrawler.Dungeon.Generation;
using DungeonCrawler.Enemies;
using DungeonCrawler.Party;
using Godot;

namespace DungeonCrawler.Adventure;

/// <summary>
/// A quest giver placed by the floor plan. Talk with E; when escorting, follows the lead and can be hurt by hostile
/// enemies standing next to it (the party cannot harm NPCs). Reaching the down stairs while escorted completes an escort.
/// </summary>
/// <summary>What an NPC is on this floor: a quest giver from the plan, or one of the floor objective's people.</summary>
public enum NpcMode { Quest, Ritualist, Prisoner, Fighter }

public partial class Npc : CharacterBody3D, IInteractable, ICombatTarget, IHazardVictim
{
    public const float WalkSpeed = 4.2f;
    public const float RunSpeed = 6.5f;
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
    /// <summary>Down to a lethal hazard: an ally waits to be pulled free rather than revived with a word.</summary>
    public bool Mangled { get; private set; }
    public int MangledBy { get; private set; } = -1;
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
        NpcMode.Fighter when Downed => $"Revive {NpcName}",
        _ => Rescued ? $"{NpcName} waits by the stairs" : $"Talk to {NpcName}",
    };

    private static readonly string[] Models = { "Cleric", "Monk" };
    /// <summary>A glTF to use instead of the stock party models (a character exported from the 3dObject viewer).</summary>
    public string? ModelPath { get; set; }
    /// <summary>True once a glTF model is attached (false while the capsule stand-in shows).</summary>
    public bool HasModel => _model != null;
    /// <summary>Put one of the game's weapons (a KayKit item name) in the model's right hand; false when the model has no grip bones (see <see cref="WeaponGrip"/>).</summary>
    public bool HoldWeapon(string weapon, float scale = 1f) => _model != null && WeaponGrip.Attach(_model.Root, weapon, "Right", scale) != null;
    /// <summary>True for a character whose dialogue also offers every clip of its model to play (the floor-1 viewer cast).</summary>
    public bool ShowMoves { get; set; }
    /// <summary>The clips the attached model can play, in the model's own order; empty without a model.</summary>
    public IReadOnlyList<string> ModelClips => _model?.Player?.GetAnimationList() ?? Array.Empty<string>();
    private string? _showcase;
    private MeshInstance3D _body = null!, _head = null!;
    private Label3D _label = null!;
    private AnimatedModel? _model;
    private readonly PathMover _mover = new();
    /// <summary>Running from an enemy that came within <see cref="NpcFlight.FleeRange"/> (quest NPCs standing about); walks back to its spot once the enemy has been gone a while.</summary>
    public bool Fleeing { get; private set; }
    private Vector3? _home;       // where this NPC stood before anything scared it
    private Vector3? _fleeGoal;
    /// <summary>The cell this NPC is running to, so neighbours pick other cells; null when not running.</summary>
    public GridPos? FleeCell { get; private set; }
    private float _calm, _repick, _stuck;
    private readonly HashSet<GridPos> _badCells = new();   // flee cells that turned out unreachable this scare
    private float _gravity;
    private float _hurtTimer;
    private float _flash;

    public override void _Ready()
    {
        _body = GetNode<MeshInstance3D>("Body");
        _head = GetNode<MeshInstance3D>("Head");
        _label = GetNode<Label3D>("Label");
        _gravity = (float)ProjectSettings.GetSetting("physics/3d/default_gravity", 9.8f).AsDouble();
        MaxHp = Hp = TierRules.NpcHp(Floor) * (Mode == NpcMode.Fighter ? NpcFight.HpMultiplier : 1f);
        var tint = new Color(0.75f, 0.65f, 0.45f);
        _body.SetSurfaceOverrideMaterial(0, new StandardMaterial3D { AlbedoColor = tint, Roughness = 0.85f });
        _head.SetSurfaceOverrideMaterial(0, new StandardMaterial3D { AlbedoColor = new Color(0.85f, 0.7f, 0.6f) });
        var model = Models[(int)((Rng.StableHash(Id) + (ulong)Floor) % (ulong)Models.Length)]; // stable per run and peer
        _model = AnimatedModel.Attach(this, ModelPath ?? $"res://assets/models/quaternius_rpg/{model}.gltf", 1.8f, tint, ModelPath != null ? 0f : 0.1f);
        if (_model != null) { _body.Visible = false; _head.Visible = false; }
        _label.Text = NpcName;
        AddToGroup("npcs");
    }

    public void Interact(PartyController party, Adventurer actor)
    {
        if (Dead) return;
        if (Mode == NpcMode.Fighter && Downed) { if (!Net.NetSession.IsClient) TryRevive(); return; }
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

    /// <summary>
    /// Play a clip on the model and keep it there: a looping clip loops, a one-shot holds its last frame (Death stays
    /// dead) until another clip is chosen or <c>Showcase(null)</c> hands the body back to Idle/Walk. Local only: the
    /// other players in a session do not see it.
    /// </summary>
    public void Showcase(string? clip)
    {
        _showcase = clip;
        if (_model == null || Dead) return;
        _model.Play(clip ?? "Idle", force: true);
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
        Mangled = false;
        MangledBy = -1;
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
            ShowGait(new Vector2(planar.X, planar.Z).Length(), dt);
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
        var speed = WalkSpeed;
        if (!Dead && !Downed && !Escorting && Mode == NpcMode.Quest && MoveTarget == null && Flight(dt, out var away, out speed))
        {
            if (away is { } d) { FaceToward(d); planar = PathMover.Planar(GlobalPosition, d) * speed; }
        }
        else if (!Dead && !Downed && Mode == NpcMode.Fighter && MoveTarget == null && Fight(dt, out var at, out speed))
        {
            if (at is { } d) { FaceToward(d); planar = PathMover.Planar(GlobalPosition, d) * speed; }
        }
        else if (!Dead && !Downed && !Escorting && MoveTarget is { } goal)
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
            var gap = new Vector2(lead.GlobalPosition.X - GlobalPosition.X, lead.GlobalPosition.Z - GlobalPosition.Z).Length() - FollowDistance;
            var dest = _mover.Next(this, lead.GlobalPosition, dt, FollowDistance, ClearLine, out var straight);
            // speed follows the gap: a trailing follower settles at a steady pace instead of lurching between full speed
            // and a dead stop every frame; well behind, it runs to catch up
            if (dest is { } d && gap > 0.25f)
            {
                if (!straight) FaceToward(d);
                planar = PathMover.Planar(GlobalPosition, d) * Math.Clamp(gap * 2.5f, 1.2f, gap > 3f ? RunSpeed : WalkSpeed);
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
        var actual = new Vector2(Velocity.X, Velocity.Z).Length();   // what the slide allowed, not what was asked for
        if (Fleeing && planar.Length() > 0.5f)
        {
            // pushing but not moving: this refuge is behind something; give it up and pick another
            _stuck = actual < 0.5f ? _stuck + dt : 0f;
            if (_stuck > 0.6f) { if (FleeCell is { } bad) _badCells.Add(bad); _fleeGoal = null; _repick = 0f; _stuck = 0f; }
        }
        else _stuck = 0f;
        if (_model != null)
        {
            if (_flash > 0f) { _flash -= dt; _model.Flash(Math.Max(0f, _flash * 2f)); }
            ShowGait(actual, dt);   // a blocked NPC stands instead of running on the spot
        }
    }

    /// <summary>Idle, Walk or Run for this ground speed; a viewer-rigged model's gait is played at the pace the ground passes so its feet stay planted.</summary>
    private void ShowGait(float groundSpeed, float dt)
    {
        if (_model == null) return;
        var current = _model.Current is "Walk" or "Run" ? _model.Current : null;
        var gait = ModelPath != null ? ViewerGait.GaitFor(1.8f, groundSpeed, current) : groundSpeed > 0.5f ? (groundSpeed > WalkSpeed + 0.5f ? "Run" : "Walk") : "Idle";
        if (!Dead && _showcase == null && !_model.IsPlayingOneShot) _model.Play(gait);
        if (ModelPath != null && _model.Player != null) _model.Player.SpeedScale = _model.IsPlayingOneShot || gait == "Idle" ? 1f : ViewerGait.SpeedScaleFor(gait, 1.8f, groundSpeed);
    }

    /// <summary>
    /// Run from the nearest alive enemy within <see cref="NpcFlight.FleeRange"/>, re-picking the way every
    /// <see cref="NpcFlight.RepickSeconds"/>; once it has been gone for <see cref="NpcFlight.CalmSeconds"/>, walk back to the
    /// spot. True while there is somewhere to go (or breath to catch); <paramref name="dest"/> is the next point to head for.
    /// </summary>
    private bool Flight(float dt, out Vector3? dest, out float speed)
    {
        dest = null;
        speed = WalkSpeed;
        _home ??= GlobalPosition;
        // a new scare starts inside FleeRange; once scared, anything inside SafeRange keeps the NPC wary (hysteresis, so it
        // does not run, calm down at the edge of the range, walk back into it and run again)
        var watch = Fleeing ? NpcFlight.SafeRange : NpcFlight.FleeRange;
        var threat = NearestEnemy(GlobalPosition, watch, out var dist);
        if (threat != null)
        {
            _calm = 0f;
            _repick -= dt;
            if (!Fleeing) { Fleeing = true; _repick = 0f; EventBus.Instance.Log($"{NpcName} runs from {threat.DisplayName}!"); GD.Print($"[Npc] {NpcName} runs from {threat.DisplayName} ({dist:0.0} m away) at {GlobalPosition:0.0}"); }
            var near = dist < NpcFlight.FleeRange;
            var arrived = _fleeGoal == null || GlobalPosition.DistanceTo(_fleeGoal.Value) < 0.8f;
            if (near && (arrived || _repick <= 0f))
            {
                _repick = NpcFlight.RepickSeconds;
                if (Main.Instance?.Builder is { Layout: { } layout } builder)
                {
                    var others = GetTree().GetNodesInGroup("npcs").OfType<Npc>().Where(o => o != this && o.IsAlive).ToList();
                    bool Taken(GridPos c) => others.Any(o => o.FleeCell == c || DungeonBuilder.WorldToCell(o.GlobalPosition) == c);
                    // a refuge must be walkable AND reachable by a path: the mover walks straight at an unreachable goal, which plants the NPC against a wall
                    var myCell = DungeonBuilder.WorldToCell(GlobalPosition);
                    var me = new PathNode(myCell, builder.IsUnderAt(GlobalPosition));
                    bool Reachable(GridPos c) => layout.IsWalkable(c) && !builder.IsCellBlocked(c) && !_badCells.Contains(c)
                        && GridPathfinder.FindPath(layout, me, new PathNode(c, false), builder.IsCellBlocked, builder.IsSecretOpen) != null;
                    var cell = NpcFlight.PickFleeCell(myCell, DungeonBuilder.WorldToCell(threat.GlobalPosition), Reachable, NpcFlight.FleeSteps, Taken, c => Main.Instance?.Builder.DangerAt(c) ?? 0f);
                    var changed = cell != FleeCell;
                    FleeCell = cell;
                    _fleeGoal = cell is { } c2 ? builder.Surface(c2) : null;
                    if (_fleeGoal != null && changed) { _mover.Reset(); GD.Print($"[Npc] {NpcName} runs to {cell}"); }
                }
            }
            else if (!near && arrived) { _fleeGoal = null; FleeCell = null; }   // far enough: hold here and keep an eye on it
            speed = RunSpeed;
            if (_fleeGoal is { } g) dest = _mover.Next(this, g, dt, 0.6f, ClearLine, out _);
            else FaceToward(threat.GlobalPosition);
            return true;
        }
        if (!Fleeing) return false;
        _badCells.Clear();
        _fleeGoal = null;
        FleeCell = null;
        _calm += dt;
        if (_calm < NpcFlight.CalmSeconds) return true;   // stands a moment, catching breath
        var home = _home.Value;
        if (NearestEnemy(home, NpcFlight.FleeRange, out _) != null) return true;   // home is not clear yet: wait here
        if (new Vector2(home.X - GlobalPosition.X, home.Z - GlobalPosition.Z).Length() < 0.8f) { Fleeing = false; _mover.Reset(); GD.Print($"[Npc] {NpcName} is back at its spot"); return false; }
        dest = _mover.Next(this, home, dt, 0.6f, ClearLine, out _);
        return true;
    }

    private Enemy? _foe;
    private float _swing;
    private bool _struck;
    private int _asks;

    /// <summary>Asked to join the party: a fighter decides (<see cref="NpcFight.Decides"/>) and, if willing, follows and fights beside it. True when they come along.</summary>
    public bool AskToJoin()
    {
        if (Dead || Downed || Mode != NpcMode.Fighter) return false;
        if (Escorting) return true;
        _asks++;
        var yes = NpcFight.Decides(Game.Instance?.GameSeed ?? 0, Id, _asks);
        GD.Print($"[Npc] {NpcName} asked to join (ask {_asks}): {(yes ? "yes" : "no")}");
        if (yes) StartFollowing();
        return yes;
    }

    /// <summary>Carry hit points across a floor change.</summary>
    public void RestoreHp(float fraction) => Hp = Math.Clamp(MaxHp * fraction, 1f, MaxHp);

    /// <summary>
    /// A fighter goes for the nearest enemy within <see cref="NpcFight.SightRange"/>, swings at it in the windup/recover
    /// rhythm enemies use, and walks back to its spot when none is left. True while there is somewhere to go or someone to hit.
    /// </summary>
    private bool Fight(float dt, out Vector3? dest, out float speed)
    {
        dest = null;
        speed = WalkSpeed;
        _home ??= GlobalPosition;
        var foe = NearestEnemy(GlobalPosition, NpcFight.SightRange, out var dist);
        if (foe != null)
        {
            if (_foe != foe) { _foe = foe; _swing = 0f; _struck = false; EventBus.Instance.Log($"{NpcName} squares up to {foe.DisplayName}!"); GD.Print($"[Npc] {NpcName} squares up to {foe.DisplayName} ({dist:0.0} m away)"); }
            if (dist > NpcFight.AttackRange)
            {
                _swing = 0f; _struck = false;
                dest = _mover.Next(this, foe.GlobalPosition, dt, NpcFight.AttackRange * 0.85f, ClearLine, out _);
                speed = Math.Clamp((dist - NpcFight.AttackRange * 0.85f) * 3f, 1.0f, WalkSpeed);   // ease up to the foe instead of slamming to a stop
                return true;
            }
            FaceToward(foe.GlobalPosition);
            _swing += dt;
            if (!_struck && _swing >= NpcFight.Windup) { _struck = true; Strike(foe); }
            if (_swing >= NpcFight.Windup + NpcFight.Recover) { _swing = 0f; _struck = false; }
            return true;
        }
        _foe = null; _swing = 0f; _struck = false;
        if (Escorting) return false;   // nobody to hit: fall in behind the party
        var home = _home.Value;
        if (new Vector2(home.X - GlobalPosition.X, home.Z - GlobalPosition.Z).Length() < 0.8f) return false;
        dest = _mover.Next(this, home, dt, 0.6f, ClearLine, out _);
        return true;
    }

    private void Strike(Enemy foe)
    {
        _model?.Play("Punch", force: true);
        if (GlobalPosition.DistanceTo(foe.GlobalPosition) > NpcFight.AttackRange * 1.15f) return;
        var rng = Main.Instance?.Party?.Rng ?? new Random();
        var r = foe.ReceiveAttack(NpcFight.Stats, NpcFight.AttackFor(Floor), new HitContext(), rng, NpcName);
        EventBus.Instance.Log(r.Hit ? $"{NpcName} hits {foe.DisplayName} for {r.Amount:0}." : $"{NpcName} misses {foe.DisplayName}.");
        if (r.Hit) GD.Print($"[Npc] {NpcName} hits {foe.DisplayName} for {r.Amount:0}");
        if (foe.IsAlive && foe.NpcTarget == null) foe.NpcTarget = this;   // it has someone to answer
    }

    /// <summary>The nearest alive enemy within <paramref name="range"/> metres of <paramref name="of"/> (flat distance), or null.</summary>
    private Enemy? NearestEnemy(Vector3 of, float range, out float distance)
    {
        Enemy? best = null;
        var bestD2 = range * range;
        foreach (var n in GetTree().GetNodesInGroup("enemies"))
        {
            if (n is not Enemy e || !e.IsAlive) continue;
            var d2 = new Vector2(e.GlobalPosition.X - of.X, e.GlobalPosition.Z - of.Z).LengthSquared();
            if (d2 < bestD2) { bestD2 = d2; best = e; }
        }
        distance = MathF.Sqrt(bestD2);
        return best;
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
        if (Hp <= 0) GoDown(); // wounds put an escort down; only the dungeon's own hazards and quest-mode blows kill
    }

    public VictimKind HazardVictimKind => Mode == NpcMode.Fighter && Escorting ? VictimKind.Ally : VictimKind.QuestNpc;

    /// <summary>A lethal hazard caught this NPC. Allies go down Mangled; everyone else goes down and waits for the party.</summary>
    public void ReceiveHazard(HazardDamage kind, HazardOutcome outcome, string sourceName, int hazardIndex)
    {
        if (Dead || Downed) return;
        if (outcome.Lethal || outcome.HpFraction >= 1f)
        {
            EventBus.Instance.Log($"{sourceName} {(kind == HazardDamage.Crush ? "crushes" : "impales")} {NpcName}!");
            if (outcome.Lethal) { Mangled = true; MangledBy = hazardIndex; }
            GoDown();
            return;
        }
        var amount = MaxHp * outcome.HpFraction;
        Hp = Math.Max(0, Hp - amount);
        _flash = 0.25f;
        EventBus.Instance.Log($"{sourceName} hits {NpcName} for {amount:0}. ({Hp:0}/{MaxHp:0})");
        if (Hp <= 0) GoDown();
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
