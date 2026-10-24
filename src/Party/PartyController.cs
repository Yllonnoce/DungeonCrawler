using DungeonCrawler.Adventure;
using DungeonCrawler.Characters;
using DungeonCrawler.Characters.Defs;
using DungeonCrawler.Combat;
using DungeonCrawler.Core;
using DungeonCrawler.Dungeon;
using DungeonCrawler.Dungeon.Props;
using DungeonCrawler.Enemies;
using DungeonCrawler.Items;
using Godot;

namespace DungeonCrawler.Party;

/// <summary>
/// The party: the <see cref="Adventurer"/> bodies, a shared bag, and the input routing for the body this machine
/// controls (<see cref="LocalIndex"/>). In single-player the other bodies are AI companions; on a server or a client
/// they belong to other players (or are parked while their player is away). This node's own position follows the
/// <see cref="Anchor"/> so older code that asks "where is the party" keeps working.
/// </summary>
public partial class PartyController : Node3D
{
    public const string GroupName = "party";
    public const string ArrowDefId = "item.arrow";
    public const float HeavyHoldSeconds = 0.35f;
    public const float PerfectWindowStart = 0.6f;
    public const float PerfectWindowEnd = 1.0f;
    public const float BlockStaminaCost = 12f;

    private readonly Dictionary<string, AbilityProfile> _abilities = new();
    private readonly List<int> _effectVersions = new();
    private double _time;
    private float _lockpickCooldown;
    private float _trapScanTimer;
    private float _calmTimer;
    private PackedScene _lootScene = null!;
    private bool _configured;

    public CharacterState[] Members { get; private set; } = Array.Empty<CharacterState>();
    public Adventurer[] Adventurers { get; private set; } = Array.Empty<Adventurer>();
    public Inventory Bag { get; } = new();
    public Random Rng { get; } = new(7);
    /// <summary>The member this machine controls, or -1 (a dedicated server, or a client between characters).</summary>
    public int LocalIndex { get; private set; }
    /// <summary>The member the inventory, sheet and skill screens show. Possessing sets it; focusing alone does not possess.</summary>
    public int FocusedIndex { get; private set; }
    /// <summary>Single-player lets keys 1-4, F and the portraits switch bodies; a multiplayer client controls one body only.</summary>
    public bool AllowPossession { get; set; } = true;
    [Obsolete("Use LocalIndex (the body this machine controls) or Anchor.Index.")]
    public int LeadIndex => LocalIndex;

    /// <summary>The body this machine controls, if any.</summary>
    public Adventurer? Local => LocalIndex >= 0 && LocalIndex < Adventurers.Length && IsInstanceValid(Adventurers[LocalIndex]) ? Adventurers[LocalIndex] : null;
    /// <summary>Bodies that are in the world (not parked).</summary>
    public IEnumerable<Adventurer> Active => Adventurers.Where(a => IsInstanceValid(a) && !a.Parked);
    public bool HasBodies => Local != null || Active.Any();
    /// <summary>The local body, else the first living body in the world (else any body): what companions, NPCs and cameras follow.</summary>
    public Adventurer Anchor => Local ?? Active.FirstOrDefault(a => !a.Downed) ?? Active.FirstOrDefault() ?? Adventurers.First(IsInstanceValid);
    /// <summary>Older readers: the anchor body.</summary>
    public Adventurer Lead => Anchor;
    public Camera3D Camera => Anchor.Camera;
    public bool MouseCaptured => Input.MouseMode == Input.MouseModeEnum.Captured;
    public bool InputLocked { get; set; }
    /// <summary>Whether the local body is holding block.</summary>
    public bool Guarding => Local?.Guarding ?? false;
    public IInteractable? LookingAt => Local?.LookingAt;
    /// <summary>Someone in the world is still standing. False when nobody is online.</summary>
    public bool PartyAlive => Active.Any(a => !a.Downed);
    public int ArrowCount => Bag.Count(ArrowDefId);

    /// <summary>Levels and gear of the members in the world, for budgeting floors.</summary>
    public PartySnapshot Snapshot()
    {
        var online = Adventurers.Length == 0 ? Members : Active.Select(a => a.State).ToArray();
        if (online.Length == 0) return PartySnapshot.Expected(Game.Instance?.CurrentFloor ?? 1);
        var avgLevel = (float)online.Average(m => m.Level);
        var itemLevels = online.SelectMany(m => m.Equipment.Items.Select(t => (float)t.Item.ItemLevel)).ToList();
        return new PartySnapshot(avgLevel, itemLevels.Count == 0 ? avgLevel : itemLevels.Average(), online.Length);
    }

    /// <summary>The four adventurers every single-player run starts with.</summary>
    public static readonly (string Name, string ClassId, Rank Rank)[] DefaultRoster =
    {
        ("Brand", "class.warrior", Rank.Front), ("Sable", "class.rogue", Rank.Front), ("Wren", "class.ranger", Rank.Back), ("Ilex", "class.mage", Rank.Back),
    };

    public static Rank RankFor(string classId) => classId is "class.ranger" or "class.mage" ? Rank.Back : Rank.Front;

    /// <summary>Average stealth of the living members (traps and secrets use it).</summary>
    public float StealthRating => Members.Where(m => m.IsAlive).Select(m => m.Stats[Stat.Stealth]).DefaultIfEmpty(0).Average();
    /// <summary>The enemy the local player last hit; companions in Defensive stance focus it.</summary>
    public Enemy? LeadTarget { get; set; }
    public Vector2? ForcedMoveInput { get => Local?.ForcedMoveInput; set { if (Local is { } l) l.ForcedMoveInput = value; } }

    public AbilityProfile GetAbility(string id) => _abilities.TryGetValue(id, out var a) ? a : AbilityProfile.Basic;
    public AbilityProfile GetAbilityFor(CharacterState m, string id) => SkillRules.ApplyUpgrades(GetAbility(id), m, Game.Instance.Skills);
    public string AbilityName(string id) => GetAbility(id).Name;
    public float Trigger(CharacterState m, TriggerKind kind) => SkillRules.TriggerMagnitude(m, Game.Instance.Skills, kind);

    public override void _Ready()
    {
        AddToGroup(GroupName);
        _lootScene = GD.Load<PackedScene>("res://scenes/Props/LootDrop.tscn");
        _abilities[AbilityProfile.BasicId] = AbilityProfile.Basic;
        foreach (var def in Registry.Instance.All<AbilityDef>()) _abilities[def.Id] = def.ToProfile();
        if (!_configured) Configure(DefaultRoster);
        SetMouseCaptured(true);
    }

    /// <summary>Set the roster before bodies exist. Single-player passes <see cref="DefaultRoster"/>; a server starts empty.</summary>
    public void Configure(IEnumerable<(string Name, string ClassId, Rank Rank)> roster, int localIndex = 0)
    {
        _configured = true;
        Members = roster.Select(m => MakeMember(m.Name, m.ClassId, m.Rank)).ToArray();
        _effectVersions.Clear();
        _effectVersions.AddRange(Members.Select(m => m.Effects.Version));
        LocalIndex = Members.Length == 0 ? -1 : Math.Clamp(localIndex, -1, Members.Length - 1);
        FocusedIndex = Math.Max(0, LocalIndex);
        var cat = Game.Instance.Catalog;
        if (Members.Length > 0 && Bag.Items.Count() == 0)
        {
            if (cat.TryBase(ArrowDefId, out _)) Bag.TryAdd(cat.Create(ArrowDefId, 40));
            if (cat.TryBase("item.health_potion", out _)) Bag.TryAdd(cat.Create("item.health_potion", 3));
        }
    }

    /// <summary>
    /// Bodies live beside this node (not under it): this node follows the anchor, and children would be dragged along
    /// with it. Called by Main once the party node is in the tree.
    /// </summary>
    public void SpawnBodies(Node world)
    {
        FreeBodies();
        var scene = GD.Load<PackedScene>("res://scenes/Adventurer.tscn");
        Adventurers = new Adventurer[Members.Length];
        for (var i = 0; i < Members.Length; i++)
        {
            Adventurers[i] = SpawnBody(scene, world, i);
            _effectVersions[i] = Members[i].Effects.Version;
        }
        RefreshControl();
    }

    private Adventurer SpawnBody(PackedScene scene, Node world, int index)
    {
        var adv = scene.Instantiate<Adventurer>();
        adv.Party = this;
        adv.Index = index;
        adv.Name = $"Adventurer{index}";
        world.AddChild(adv);
        return adv;
    }

    /// <summary>Add a member mid-run (a player joining a server). The body starts parked; <see cref="Unpark"/> puts it in the world.</summary>
    public int AddMember(string name, string classId, Rank? rank = null)
    {
        Members = Members.Append(MakeMember(name, classId, rank ?? RankFor(classId))).ToArray();
        _effectVersions.Add(Members[^1].Effects.Version);
        var index = Members.Length - 1;
        if (GetParent() is { } world)
        {
            var body = SpawnBody(GD.Load<PackedScene>("res://scenes/Adventurer.tscn"), world, index);
            Adventurers = Adventurers.Append(body).ToArray();
            body.SetControl(ControlKind.Parked);
        }
        EventBus.Instance.RaiseRosterChanged();
        return index;
    }

    /// <summary>Take a body out of the world (its player left); the pose stays on the body for <see cref="Unpark"/>.</summary>
    public void Park(int index)
    {
        if (index < 0 || index >= Adventurers.Length) return;
        Adventurers[index].SetControl(ControlKind.Parked);
        if (index == LocalIndex) LocalIndex = -1;
        RefreshControl();
        EventBus.Instance.RaiseRosterChanged();
    }

    public void Unpark(int index, ControlKind control, Vector3? at = null, Vector3? lookTarget = null)
    {
        if (index < 0 || index >= Adventurers.Length) return;
        var adv = Adventurers[index];
        adv.SetControl(control);
        if (at is { } p) adv.Teleport(p, lookTarget);
        if (control == ControlKind.LocalHuman) { LocalIndex = index; FocusedIndex = index; }
        RefreshControl();
        EventBus.Instance.RaiseRosterChanged();
    }

    /// <summary>Which body is local and which are companions, from <see cref="LocalIndex"/>; remote and parked bodies keep their kind.</summary>
    private void RefreshControl()
    {
        for (var i = 0; i < Adventurers.Length; i++)
        {
            var a = Adventurers[i];
            if (a.Control is ControlKind.Remote or ControlKind.Parked && i != LocalIndex) continue;
            a.SetControl(i == LocalIndex ? ControlKind.LocalHuman : ControlKind.Companion);
        }
        if (HasBodies) GlobalPosition = Anchor.GlobalPosition;
    }

    public void FreeBodies()
    {
        foreach (var a in Adventurers) if (IsInstanceValid(a)) a.QueueFree();
        Adventurers = Array.Empty<Adventurer>();
    }

    public override void _ExitTree() => FreeBodies();

    private static CharacterState MakeMember(string name, string classId, Rank rank)
    {
        var cls = Registry.Instance.Get<ClassDef>(classId).ToProfile();
        var cat = Game.Instance.Catalog;
        ItemInstance? weapon = cls.StartingWeaponId != "" && cat.TryBase(cls.StartingWeaponId, out var wb) ? ItemInstance.FromBase(wb) : null;
        var c = CharacterState.Create(name, cls, rank, weapon);
        foreach (var id in cls.StartingItems)
        {
            if (!cat.TryBase(id, out var b)) continue;
            var item = ItemInstance.FromBase(b);
            if (c.Equipment.PreferredSlot(c, item) is { } slot) c.Equipment.Equip(slot, item);
        }
        c.Recalculate();
        c.FullRestore();
        return c;
    }

    // ---- possession and formation ------------------------------------------------------------

    /// <summary>Single-player: take control of another member. Focus follows.</summary>
    public void Possess(int index)
    {
        if (!AllowPossession || index < 0 || index >= Adventurers.Length || index == LocalIndex || !Members[index].IsAlive) return;
        if (Adventurers[index].Control is ControlKind.Remote or ControlKind.Parked) return;
        Local?.CancelHold();
        LocalIndex = index;
        FocusedIndex = index;
        RefreshControl();
        EventBus.Instance.Log($"You are now {Members[index].Name}.");
        EventBus.Instance.PlaySfx("ui_click");
        EventBus.Instance.RaiseLeadChanged(index);
        for (var i = 0; i < Members.Length; i++) EventBus.Instance.RaiseMemberChanged(i);
    }

    /// <summary>Show another member in the inventory, sheet and skill screens without taking control of them.</summary>
    public void SetFocus(int index)
    {
        if (index < 0 || index >= Members.Length || index == FocusedIndex) return;
        FocusedIndex = index;
        for (var i = 0; i < Members.Length; i++) EventBus.Instance.RaiseMemberChanged(i);
    }

    /// <summary>Formation offset (right, back) in metres relative to the anchor. Melee classes take the near slots.</summary>
    public Vector2 FormationSlot(int index)
    {
        var anchor = HasBodies ? Anchor.Index : LocalIndex;
        var companions = Enumerable.Range(0, Members.Length).Where(i => i != anchor)
            .OrderBy(i => Members[i].WeaponProfile.IsRanged || Members[i].Class.Id == "class.mage" ? 1 : 0).ThenBy(i => i).ToList();
        var pos = companions.IndexOf(index);
        return pos switch { 0 => new Vector2(-1.3f, 1.5f), 1 => new Vector2(1.3f, 1.5f), 2 => new Vector2(-0.9f, 3.2f), _ => new Vector2(0.9f, 3.2f) };
    }

    public void CycleStance(int index)
    {
        var adv = Adventurers[index];
        adv.Stance = (Stance)(((int)adv.Stance + 1) % 3);
        EventBus.Instance.Log($"{Members[index].Name}: {adv.Stance}.");
        EventBus.Instance.RaiseMemberChanged(index);
    }

    public void CycleAllStances()
    {
        var companions = Adventurers.Where(a => a.Control == ControlKind.Companion).ToList();
        if (companions.Count == 0) return;
        var next = (Stance)(((int)companions[0].Stance + 1) % 3);
        foreach (var a in Adventurers) a.Stance = next;
        EventBus.Instance.Log($"Companions: {next}.");
        for (var i = 0; i < Members.Length; i++) EventBus.Instance.RaiseMemberChanged(i);
    }

    public IEnumerable<Adventurer> AdventurersWithin(Vector3 point, float radius)
        => Active.Where(a => !a.Downed && a.GlobalPosition.DistanceTo(point) <= radius);

    public Adventurer? NearestAlive(Vector3 point)
        => Active.Where(a => !a.Downed).OrderBy(a => a.GlobalPosition.DistanceSquaredTo(point)).FirstOrDefault();

    public void SetMouseCaptured(bool captured) => Input.MouseMode = captured ? Input.MouseModeEnum.Captured : Input.MouseModeEnum.Visible;

    public void ApplySettings() { foreach (var a in Adventurers) a.ApplySettings(); }

    /// <summary>Place the anchor at a position facing a point; companions fall in around them.</summary>
    public void Teleport(Vector3 position, Vector3? lookTarget = null)
    {
        if (!HasBodies) { GlobalPosition = position; return; }
        var anchor = Anchor;
        anchor.Teleport(position, lookTarget);
        GlobalPosition = position;
        var layout = Main.Instance?.Builder.Layout;
        foreach (var adv in Active)
        {
            if (adv == anchor) continue;
            var slot = FormationSlot(adv.Index);
            var world = position + anchor.GlobalTransform.Basis * new Vector3(slot.X, 0, slot.Y);
            if (layout != null && !layout.IsWalkable(DungeonBuilder.WorldToCell(world))) world = position + new Vector3(0.3f * adv.Index, 0, 0.3f * adv.Index);
            adv.Teleport(world, lookTarget);
        }
    }

    // ---- input -------------------------------------------------------------------------------

    public override void _UnhandledInput(InputEvent @event)
    {
        if (InputLocked || !PartyAlive || Local is not { } local) return;

        if (@event is InputEventMouseMotion motion && MouseCaptured) { local.Look(motion.Relative); return; }

        if (@event is InputEventMouseButton { Pressed: true } mb)
        {
            if (!MouseCaptured) { SetMouseCaptured(true); return; }
            if (mb.ButtonIndex == MouseButton.WheelUp) { CycleAbility(LocalIndex, -1); return; }
            if (mb.ButtonIndex == MouseButton.WheelDown) { CycleAbility(LocalIndex, +1); return; }
        }
        if (!MouseCaptured) return;

        if (@event.IsActionPressed("interact")) local.TryInteract();
        else if (@event.IsActionPressed("focus_next")) Possess(NextAlive(LocalIndex));
        else if (@event.IsActionPressed("ability_next")) CycleAbility(LocalIndex, +1);
        else if (@event.IsActionPressed("swap_rank")) CycleAllStances();
        else if (@event.IsActionPressed("attack_1")) Possess(0);
        else if (@event.IsActionPressed("attack_2")) Possess(1);
        else if (@event.IsActionPressed("attack_3")) Possess(2);
        else if (@event.IsActionPressed("attack_4")) Possess(3);
        else if (@event.IsActionPressed("attack_primary")) local.BeginAttack(_time);
        else if (@event.IsActionReleased("attack_primary")) local.EndAttack(_time);
    }

    private int NextAlive(int from)
    {
        for (var step = 1; step < Members.Length; step++)
        {
            var i = (from + step) % Members.Length;
            if (Members[i].IsAlive && i < Adventurers.Length && Adventurers[i].Control is ControlKind.Companion or ControlKind.LocalHuman) return i;
        }
        return from;
    }

    public void CycleAbility(int index, int dir)
    {
        if (index < 0 || index >= Members.Length) return;
        var m = Members[index];
        m.CycleAbility(dir);
        if (Net.NetSession.IsClient && index == LocalIndex) Net.NetSession.Instance.Send(new Net.CycleAbilityIntent(dir));
        EventBus.Instance.Log($"{m.Name}: {AbilityName(m.ActiveAbilityId)}");
        EventBus.Instance.RaiseMemberChanged(index);
    }

    /// <summary>Automation and portrait buttons: member <paramref name="index"/> uses their active ability now.</summary>
    public void Attack(int index, bool heavy = false, bool perfect = false)
    {
        if (index < 0 || index >= Adventurers.Length) return;
        var adv = Adventurers[index];
        if (!adv.IsLocal && Main.Instance.Builder.FloorRoot is { } root)
        {
            var nearest = root.GetChildren().OfType<Enemy>().Where(e => e.IsAlive).OrderBy(e => e.GlobalPosition.DistanceSquaredTo(adv.GlobalPosition)).FirstOrDefault();
            if (nearest != null) adv.FaceToward(nearest.GlobalPosition);
        }
        adv.Attack(heavy, perfect);
    }

    // ---- upkeep --------------------------------------------------------------------------------

    public override void _PhysicsProcess(double delta)
    {
        var dt = (float)delta;
        if (Adventurers.Length == 0) return;
        if (Local is { } local) local.Intent = LocalInput.Read(MouseCaptured, InputLocked, local.ForcedMoveInput);
        if (HasBodies)
        {
            GlobalPosition = Anchor.GlobalPosition;
            Rotation = Anchor.Rotation;
        }
        if (_lockpickCooldown > 0) _lockpickCooldown -= dt;
        ScanForTrapsAndSecrets(dt);
    }

    public override void _Process(double delta)
    {
        if (Adventurers.Length == 0) return;
        var dt = (float)delta;
        _time += delta;
        if (Net.NetSession.IsClient) return; // vitals, effects and falls all come from the server
        for (var i = 0; i < Members.Length; i++)
        {
            var m = Members[i];
            var body = i < Adventurers.Length ? Adventurers[i] : null;
            if (body == null || body.Parked) continue;
            m.TickCooldowns(dt);
            m.Regenerate(dt, body.Guarding && m.Class.CanBlock);
            var wasAlive = m.IsAlive;
            foreach (var tick in m.Effects.Update(dt))
            {
                if (!m.IsAlive) break;
                if (tick.IsHeal) m.Heal(tick.Amount);
                else m.TakeDamage(DamagePipeline.Mitigate(tick.Amount, tick.Type, m.Stats));
            }
            if (wasAlive && !m.IsAlive) { EventBus.Instance.Log($"{m.Name} falls!"); body.CallDeferred(nameof(Adventurer.Revive)); m.Hp = 0; OnMemberDowned(i); }
            if (m.Effects.Version != _effectVersions[i]) { _effectVersions[i] = m.Effects.Version; m.Recalculate(); }
        }
        if (Active.Any() && MangledRules.IsWipe(Active.Select(a => (a.Downed, a.State.Mangled)).ToList()) && Main.Instance?.Builder.Layout != null) { OnPartyWiped(); return; }
        AutoRevive(dt);
        TickRescues(dt);
    }

    public void OnMemberDowned(int index)
    {
        EventBus.Instance.RaiseMemberDowned(index);
        if (index == LocalIndex && AllowPossession && PartyAlive)
        {
            var next = NextAlive(index);
            if (next != index) { EventBus.Instance.Log($"{Members[index].Name} is down. Control passes to {Members[next].Name}."); Possess(next); }
        }
    }

    /// <summary>Downed members get up at 1 HP once no enemy has been fighting the party for a few seconds.</summary>
    private void AutoRevive(float dt)
    {
        var down = Active.Where(a => a.Downed && MangledRules.CanAutoRevive(a.State.Mangled, hazardArmed: true)).ToList();
        if (down.Count == 0) { _calmTimer = 0; return; }
        var fighting = false;
        if (Main.Instance.Builder.FloorRoot is { } root)
            foreach (var e in root.GetChildren().OfType<Enemy>())
                if (e.IsAlive && e.IsHostile && Active.Any(a => !a.Downed && a.GlobalPosition.DistanceTo(e.GlobalPosition) < 20f)) { fighting = true; break; }
        _calmTimer = fighting ? 0 : _calmTimer + dt;
        if (_calmTimer < 3f) return;
        foreach (var a in down)
        {
            var i = a.Index;
            Members[i].Hp = 1;
            Members[i].Effects.Clear();
            a.Revive();
            EventBus.Instance.Log($"{Members[i].Name} gets back up.");
            EventBus.Instance.RaiseMemberChanged(i);
        }
        _calmTimer = 0;
    }

    private sealed class Rescue { public Adventurer Target = null!; public Adventurer Rescuer = null!; public Func<bool> HazardArmed = () => true; public Vector3 Start; public float Hp; public float Progress; }
    private readonly List<Rescue> _rescues = new();

    /// <summary>Start (or keep) pulling a Mangled member free: the hazard that took them must be at rest and the rescuer within reach. Progress runs in <see cref="TickRescues"/>.</summary>
    public bool TryRescue(Adventurer target, Adventurer rescuer, Func<bool> hazardArmed)
    {
        if (!target.State.Mangled || !target.Downed) return false;
        var distance = rescuer.GlobalPosition.DistanceTo(target.GlobalPosition);
        if (!MangledRules.RescueAllowed(hazardArmed(), distance, rescuer.Downed))
        {
            if (!hazardArmed()) EventBus.Instance.Log($"{target.State.Name} cannot be reached until it stops moving.");
            return false;
        }
        if (_rescues.Any(r => r.Target == target)) return true;
        _rescues.Add(new Rescue { Target = target, Rescuer = rescuer, HazardArmed = hazardArmed, Start = rescuer.GlobalPosition, Hp = rescuer.State.Hp });
        EventBus.Instance.Log($"{rescuer.State.Name} pulls at {target.State.Name}...");
        return true;
    }

    /// <summary>0..1 of the rescue in progress for this member, 0 when none.</summary>
    public float RescueProgress(int index) => _rescues.FirstOrDefault(r => r.Target.Index == index)?.Progress / MangledRules.RescueSeconds ?? 0f;

    private void TickRescues(float dt)
    {
        for (var i = _rescues.Count - 1; i >= 0; i--)
        {
            var r = _rescues[i];
            var interrupted = !IsInstanceValid(r.Rescuer) || r.Rescuer.Downed || !r.HazardArmed()
                || r.Rescuer.GlobalPosition.DistanceTo(r.Start) > MangledRules.RescueInterruptMove
                || r.Rescuer.GlobalPosition.DistanceTo(r.Target.GlobalPosition) > MangledRules.RescueRange
                || r.Rescuer.State.Hp < r.Hp;
            if (interrupted || !r.Target.State.Mangled) { _rescues.RemoveAt(i); continue; }
            r.Progress += dt;
            if (r.Progress < MangledRules.RescueSeconds) continue;
            _rescues.RemoveAt(i);
            var m = r.Target.State;
            m.Hp = MangledRules.ReviveHp;
            m.Effects.Clear();
            r.Target.Revive();
            EventBus.Instance.Log($"{m.Name} is pulled free.");
            EventBus.Instance.RaiseMemberChanged(r.Target.Index);
        }
    }

    private void OnPartyWiped()
    {
        EventBus.Instance.Log("The party has fallen. The dungeon shifts...");
        EventBus.Instance.RaisePartyWiped();
        foreach (var m in Members) m.FullRestore();
        foreach (var a in Adventurers) a.Revive();
        for (var i = 0; i < Members.Length; i++) EventBus.Instance.RaiseMemberChanged(i);
        Game.Instance.ReloadFloor();
    }

    public void ApplyBuff(AbilityProfile a, IEnumerable<CharacterState> targets)
    {
        foreach (var t in targets)
        {
            foreach (var e in a.Effects) t.Effects.Apply(e);
            t.Recalculate();
        }
    }

    /// <summary>Skill triggers that fire when one of ours lands a hit: poison on crit, rewards on kill.</summary>
    public void OnHitLanded(CharacterState m, ICombatTarget target, DamageResult r)
    {
        if (!r.Hit) return;
        EventBus.Instance.RaiseHitLanded(m, r);
        if (r.Crit && Trigger(m, TriggerKind.VenomCrit) is > 0 and var venom)
        {
            target.ApplyStatus(new StatusApplication(StatusKind.Poison, 6, venom));
            EventBus.Instance.Log($"{m.Name}'s blade envenoms {target.DisplayName}.");
        }
        if (target.IsAlive || !target.IsCreature) return; // a smashed crate is not a kill
        EventBus.Instance.RaiseMemberKill(m);
        var index = Array.IndexOf(Members, m);
        if (Trigger(m, TriggerKind.Bloodlust) is > 0 and var heal) { m.Heal(heal); EventBus.Instance.Log($"{m.Name} feeds on the kill (+{heal:0} HP)."); }
        if (Trigger(m, TriggerKind.SoulSiphon) is > 0 and var mana) { m.Mana = Math.Min(m.Stats[Stat.MaxMana], m.Mana + mana); EventBus.Instance.Log($"{m.Name} siphons {mana:0} mana."); }
        if (Trigger(m, TriggerKind.Scavenger) is > 0 and var arrows && m.WeaponProfile.IsRanged && Game.Instance.Catalog.TryBase(ArrowDefId, out _))
        {
            Bag.TryAdd(Game.Instance.Catalog.Create(ArrowDefId, (int)arrows));
            EventBus.Instance.Log($"{m.Name} recovers {(int)arrows} arrow{((int)arrows == 1 ? "" : "s")}.");
        }
        if (Trigger(m, TriggerKind.Momentum) is > 0 and var haste) { m.Effects.Apply(new StatusApplication(StatusKind.Haste, 3f * haste, 1)); m.Recalculate(); }
        if (index >= 0) EventBus.Instance.RaiseMemberChanged(index);
    }

    public void LogResult(string attacker, string target, DamageResult r, bool heavy, bool perfect)
    {
        if (!r.Hit) { EventBus.Instance.Log($"{attacker} misses {target}."); EventBus.Instance.PlaySfx("miss"); return; }
        EventBus.Instance.PlaySfx(r.Type == DamageType.Physical ? (heavy || r.Crit ? "hit_heavy" : "hit_flesh") : "cast_hit");
        var verb = r.Sneak ? "ambushes" : r.Crit ? "critically hits" : perfect ? "perfectly smashes" : heavy ? "smashes" : "hits";
        var text = $"{attacker} {verb} {target} for {r.Amount:0}";
        if (r.Type != DamageType.Physical) text += $" {r.Type.ToString().ToLower()}";
        if (r.Blocked) text += " (blocked)";
        if (r.Applied.Count > 0) text += $" [{string.Join(", ", r.Applied.Select(s => s.Kind))}]";
        EventBus.Instance.Log(text + ".");
    }

    public void AwardXp(int xp)
    {
        for (var i = 0; i < Members.Length; i++)
        {
            var m = Members[i];
            var gained = Leveling.AddXp(m, xp);
            if (gained > 0)
            {
                EventBus.Instance.Log($"{m.Name} reaches level {m.Level}! (+{gained * Leveling.StatPointsPerLevel} stat, +{gained * Leveling.SkillPointsPerLevel} skill points)");
                EventBus.Instance.PlaySfx("level_up");
                EventBus.Instance.RaiseLevelUp(i);
            }
            EventBus.Instance.RaiseMemberChanged(i);
        }
    }

    // ---- items, traps, locks -------------------------------------------------------------------

    public bool PickUp(ItemInstance item)
    {
        if (!Bag.TryAdd(item)) { EventBus.Instance.Log("The bag is full."); return false; }
        EventBus.Instance.RaiseItemPickedUp(item);
        EventBus.Instance.Log($"Picked up {item}.");
        EventBus.Instance.PlaySfx(item.DefId == "item.iron_key" ? "coins" : "pickup");
        return true;
    }

    /// <summary>The living member in the world with the highest stat (falls back to any living member).</summary>
    public CharacterState BestBy(Stat stat)
    {
        var pool = Active.Where(a => !a.Downed).Select(a => a.State).ToList();
        if (pool.Count == 0) pool = Members.Where(m => m.IsAlive).ToList();
        return pool.OrderByDescending(m => m.Stats[stat]).First();
    }

    public bool TryUnlock(string what)
    {
        if (Bag.Count("item.iron_key") > 0)
        {
            Bag.Consume("item.iron_key", 1);
            EventBus.Instance.Log($"An iron key opens {what}.");
            return true;
        }
        if (_lockpickCooldown > 0) return false;
        var m = BestBy(Stat.Dexterity);
        var chance = TrapRules.LockpickChance(m.Stats[Stat.Dexterity]);
        if (Rng.NextDouble() < chance) { EventBus.Instance.Log($"{m.Name} picks the lock on {what}."); return true; }
        _lockpickCooldown = 1.2f;
        EventBus.Instance.PlaySfx("lock_fail");
        EventBus.Instance.Log($"{m.Name} fumbles the lock ({chance:P0} chance). Try again, or find a key.");
        return false;
    }

    /// <summary>The party's best hands try the trap; a slip springs it on whoever reached for it.</summary>
    public void TryDisarm(Trap trap, Adventurer actor)
    {
        var m = BestBy(Stat.Dexterity);
        var chance = TrapRules.DisarmChance(m.Stats[Stat.Dexterity]);
        if (Rng.NextDouble() < chance) { trap.Disarm(); EventBus.Instance.Log($"{m.Name} disarms the {trap.Kind.ToString().ToLower()} trap."); }
        else { EventBus.Instance.Log($"{m.Name} slips ({chance:P0} chance)..."); trap.Spring(actor); }
    }

    public void PlaceTrap(TrapKind kind, Adventurer? actor = null)
    {
        actor ??= Anchor;
        if (Main.Instance.Builder.FloorRoot is not { } root) return;
        var forward = -actor.GlobalTransform.Basis.Z; forward.Y = 0;
        var pos = actor.GlobalPosition + forward.Normalized() * 2.0f;
        var trap = GD.Load<PackedScene>("res://scenes/Props/Trap.tscn").Instantiate<Trap>();
        trap.Kind = kind;
        trap.Floor = Game.Instance.CurrentFloor;
        trap.PlacedByParty = true;
        root.AddChild(trap);
        trap.GlobalPosition = new Vector3(pos.X, actor.GlobalPosition.Y, pos.Z);
        EventBus.Instance.Log($"{BestBy(Stat.Dexterity).Name} sets a {kind.ToString().ToLower()} trap.");
        EventBus.Instance.RaiseTrapPlaced(trap);
    }

    private void ScanForTrapsAndSecrets(float dt)
    {
        _trapScanTimer -= dt;
        if (_trapScanTimer > 0 || Main.Instance.Builder.FloorRoot is not { } root) return;
        _trapScanTimer = 0.25f;
        // every standing member looks with their own Perception: a ranger sees a plate from across the room, a warrior a step short of it
        var lookers = Active.Where(a => !a.Downed).Select(a => (Body: a, Radius: TrapRules.DetectRadiusCells(a.State.Stats[Stat.Perception]) * DungeonBuilder.CellSize)).ToList();
        if (lookers.Count == 0) return;
        Adventurer? Spotter(Vector3 at, float extra) => lookers.Where(l => at.DistanceTo(l.Body.GlobalPosition) <= l.Radius + extra).OrderByDescending(l => l.Radius).Select(l => l.Body).FirstOrDefault();
        foreach (var trap in root.GetChildren().OfType<Trap>())
        {
            if (trap.Detected || trap.PlacedByParty || !trap.Armed) continue;
            if (Spotter(trap.GlobalPosition, 0f) is not { } who) continue;
            trap.Reveal();
            EventBus.Instance.Log($"{who.State.Name} spots a {trap.Kind.ToString().ToLower()} trap!");
        }
        foreach (var secret in root.GetChildren().OfType<SecretWall>())
        {
            if (secret.Detected || Spotter(secret.GlobalPosition, 1f) is not { } who) continue;
            secret.Reveal();
            EventBus.Instance.Log($"{who.State.Name} notices loose stones in the wall.");
        }
        foreach (var door in root.GetChildren().OfType<Door>())
        {
            if (door.Detected || door.IsOpen || Spotter(door.GlobalPosition, 1f) is not { } who) continue;
            door.Reveal();
            EventBus.Instance.Log($"{who.State.Name} makes out {(door.Kind == Dungeon.Generation.VaultKind.KeyDoor ? "a locked door" : "the seams of a gate")} in the wall.");
        }
    }

    public LootDrop? SpawnLoot(ItemInstance item, Vector3 at, bool droppedByParty = false)
    {
        if (Main.Instance.Builder.FloorRoot is not { } root) return null;
        var drop = _lootScene.Instantiate<LootDrop>();
        drop.Item = item;
        drop.DroppedByParty = droppedByParty;
        root.AddChild(drop);
        drop.GlobalPosition = at;
        EventBus.Instance.RaiseLootSpawned(drop);
        return drop;
    }

    public void SpawnArrowPickup(Vector3 at, float chance)
    {
        if (Rng.NextDouble() >= chance || !Game.Instance.Catalog.TryBase(ArrowDefId, out _)) return;
        SpawnLoot(Game.Instance.Catalog.Create(ArrowDefId), at);
    }

    /// <summary>Run a bag move for <paramref name="actor"/> (the anchor when null) and carry out what it asks for.</summary>
    public BagResult BagMove(BagSlot from, BagSlot to, Adventurer? actor = null)
    {
        if (Net.NetSession.IsClient) { Net.NetSession.Instance.Send(new Net.BagMoveIntent(from, to)); return new BagResult(); }
        return ApplyBag(BagActions.Move(Bag, Members, from, to), actor);
    }

    public BagResult BagQuick(BagSlot slot, int focused, Adventurer? actor = null)
    {
        if (Net.NetSession.IsClient) { Net.NetSession.Instance.Send(new Net.QuickActionIntent(slot)); return new BagResult(); }
        return ApplyBag(BagActions.QuickAction(Bag, Members, slot, focused, Game.Instance.Skills), actor);
    }

    /// <summary>Spend a stat point (the sheet). On a client the server does it and syncs the member back.</summary>
    public bool AllocateStat(int index, Stat stat)
    {
        if (index < 0 || index >= Members.Length) return false;
        if (Net.NetSession.IsClient) { Net.NetSession.Instance.Send(new Net.AllocateStatIntent(stat.ToString())); return false; }
        if (!Members[index].AllocateStat(stat)) return false;
        EventBus.Instance.RaiseMemberChanged(index);
        return true;
    }

    /// <summary>Learn a skill (the tree). On a client the server does it and syncs the member back.</summary>
    public bool LearnSkill(int index, SkillProfile skill)
    {
        if (index < 0 || index >= Members.Length) return false;
        var m = Members[index];
        var why = SkillRules.WhyCannotLearn(m, skill);
        if (why != null) { EventBus.Instance.Log(why + "."); return false; }
        if (Net.NetSession.IsClient) { Net.NetSession.Instance.Send(new Net.LearnSkillIntent(skill.Id)); return false; }
        SkillRules.Learn(m, skill, Game.Instance.Skills);
        EventBus.Instance.PlaySfx("skill");
        EventBus.Instance.Log($"{m.Name} learns {skill.Name}.");
        EventBus.Instance.RaiseMemberChanged(index);
        return true;
    }

    /// <summary>A client adopting the server's copy of a member.</summary>
    public void ApplyMemberDto(int index, Save.CharacterDto dto)
    {
        if (index < 0 || index >= Members.Length) return;
        ClassProfile? Classes(string id) => Registry.Instance.TryGet<ClassDef>(id, out var def) ? def.ToProfile() : null;
        Members[index] = Save.SaveMapper.FromDto(dto, Classes, Game.Instance.Catalog, Game.Instance.Skills);
        if (index < _effectVersions.Count) _effectVersions[index] = Members[index].Effects.Version;
        EventBus.Instance.RaiseMemberChanged(index);
    }

    public void ApplyBagDto(List<Save.BagCellDto> dto)
    {
        Save.SaveMapper.Restore(Bag, dto, Game.Instance.Catalog);
        Main.Instance?.Hud?.Refresh();
    }

    private BagResult ApplyBag(BagResult r, Adventurer? actor)
    {
        foreach (var line in r.Log) EventBus.Instance.Log(line);
        if (r.Sfx != null) EventBus.Instance.PlaySfx(r.Sfx);
        if (r.Dropped != null) DropItem(r.Dropped, actor, quiet: true);
        if (r.PlaceTrap != "" && Enum.TryParse<TrapKind>(r.PlaceTrap, true, out var kind)) PlaceTrap(kind, actor);
        foreach (var i in r.ChangedMembers) EventBus.Instance.RaiseMemberChanged(i);
        return r;
    }

    public void DropItem(ItemInstance item, Adventurer? actor = null, bool quiet = false)
    {
        actor ??= Anchor;
        var forward = -actor.GlobalTransform.Basis.Z;
        SpawnLoot(item, actor.GlobalPosition + forward * 1.3f + Vector3.Up * 0.4f, droppedByParty: true);
        if (!quiet) EventBus.Instance.Log($"Dropped {item}.");
    }

    // ---- save ----------------------------------------------------------------------------------

    public Save.PartyDto CaptureState()
    {
        var anchor = HasBodies ? Anchor : null;
        return new()
        {
            X = anchor?.GlobalPosition.X ?? GlobalPosition.X, Y = anchor?.GlobalPosition.Y ?? GlobalPosition.Y, Z = anchor?.GlobalPosition.Z ?? GlobalPosition.Z,
            Yaw = anchor?.Rotation.Y ?? 0f, FocusedIndex = Math.Max(0, LocalIndex),
            Poses = Adventurers.Select(a => { var p = a.IsInsideTree() ? a.GlobalPosition : a.Position; return new Save.PoseDto { X = p.X, Y = p.Y, Z = p.Z, Yaw = a.Rotation.Y }; }).ToList(),
            Stances = Adventurers.Select(a => a.Stance.ToString()).ToList(),
            Members = Members.Select(Save.SaveMapper.ToDto).ToList(),
            Bag = Save.SaveMapper.ToDto(Bag),
        };
    }

    /// <param name="keepControl">Leave LocalIndex and every body's ControlKind alone (a client restoring the server's party).</param>
    public void RestoreState(Save.PartyDto dto, bool keepControl = false)
    {
        var cat = Game.Instance.Catalog;
        ClassProfile? Classes(string id) => Registry.Instance.TryGet<ClassDef>(id, out var def) ? def.ToProfile() : null;
        if (dto.Members.Count > 0) Members = dto.Members.Select(m => Save.SaveMapper.FromDto(m, Classes, cat, Game.Instance.Skills)).ToArray();
        Save.SaveMapper.Restore(Bag, dto.Bag, cat);
        if (Members.Length != Adventurers.Length && GetParent() is { } world) SpawnBodies(world);
        _effectVersions.Clear();
        _effectVersions.AddRange(Members.Select(m => m.Effects.Version));
        if (!keepControl && (LocalIndex >= 0 || Members.Length > 0 && Adventurers.All(a => a.Control != ControlKind.Remote)))
        {
            LocalIndex = Math.Clamp(dto.FocusedIndex, 0, Members.Length - 1);
            if (!Members[LocalIndex].IsAlive) LocalIndex = NextAlive(LocalIndex);
        }
        if (!keepControl) FocusedIndex = Math.Max(0, LocalIndex);
        var leadPose = new Vector3(dto.X, dto.Y, dto.Z);
        for (var i = 0; i < Adventurers.Length; i++)
        {
            var pose = i < dto.Poses.Count ? dto.Poses[i] : null;
            var pos = pose != null ? new Vector3(pose.X, pose.Y, pose.Z) : leadPose;
            Adventurers[i].CancelHold();
            Adventurers[i].Teleport(pos);
            Adventurers[i].Rotation = new Vector3(0, pose?.Yaw ?? dto.Yaw, 0);
            if (i < dto.Stances.Count && Enum.TryParse<Stance>(dto.Stances[i], out var st)) Adventurers[i].Stance = st;
            if (Members[i].IsAlive) Adventurers[i].Revive(); else Adventurers[i].CollisionLayer = 0;
        }
        RefreshControl();
        for (var i = 0; i < Members.Length; i++) EventBus.Instance.RaiseMemberChanged(i);
    }
}
