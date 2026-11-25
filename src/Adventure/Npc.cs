using DungeonCrawler.Characters;
using DungeonCrawler.Combat;
using DungeonCrawler.Core;
using DungeonCrawler.Dungeon;
using DungeonCrawler.Dungeon.Generation;
using DungeonCrawler.Dungeon.Rails;
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

public partial class Npc : CharacterBody3D, IInteractable, ICombatTarget, IHazardVictim, ICartRider
{
    public const float WalkSpeed = 4.2f;
    public const float RunSpeed = 6.5f;
    public const float FollowDistance = 2.4f;

    public NpcMode Mode { get; set; } = NpcMode.Quest;
    /// <summary>At 0 HP the objective's people go down instead of dying; the party gets them up again.</summary>
    public bool Downed { get; private set; }
    /// <summary>Where the objective wants this NPC to walk (a prisoner fleeing or following a jailer); null = stand.</summary>
    public Vector3? MoveTarget { get; set; }
    /// <summary>An escorting NPC follows <see cref="MoveTarget"/> too while this is set (a rail pocket while a cart bears down). The rail encounter controller sets and clears it.</summary>
    public bool MoveOverride { get; set; }
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
    /// <summary>An escort betrayal: they ran to the enemy, alive. Gone from this floor (so <see cref="Dead"/> is set to keep them out of the world), but never a corpse.</summary>
    public bool Fled { get; private set; }
    public float Hp { get; private set; }
    public float MaxHp { get; private set; }
    /// <summary>The campaign's trader, standing in the safe room: talks through the shop dialogue, never flees, greets once per floor.</summary>
    public bool IsMerchant { get; set; }
    /// <summary>The hidden stall in a secret room (fortune): the shop opens on the stall's segment of the stock.</summary>
    public bool HiddenStall { get; set; }
    /// <summary>The merchant's lines for this floor (greeting, progress, trade); null for everyone else.</summary>
    public MerchantFloorLines? MerchantLines { get; set; }
    /// <summary>How close (metres) the local adventurer comes before the merchant greets them (0 never greets): 8 in a safe room, <see cref="TavernRules.GreetRadius"/> in the tavern.</summary>
    public float GreetRadius { get; set; } = 8f;
    private bool _greeted;
    /// <summary>Whether he has greeted the local adventurer on this floor (the safe room's line, or the tavern's briefing).</summary>
    public bool Greeted => _greeted;
    /// <summary>The merchant in the tavern on floor 0: he briefs the party instead of trading (his wares come down later).</summary>
    public bool InTavern => IsMerchant && Floor == 0;

    /// <summary>One of the railed jailbreak's crew: bleeds out when downed for <see cref="Objectives.CrewRules.BleedOutSeconds"/> unless revived.</summary>
    public bool Crew { get; set; }
    /// <summary>How long this NPC has been down (server clock, seconds); counted only for Crew, reset by a revive.</summary>
    public float DownedSeconds { get; private set; }
    private float _labelTimer;
    private int _shownLeft = -1;

    private bool CanBeRevived => Downed && !Dead && (Crew || (Mode == NpcMode.Quest && Escorting));

    public string DisplayName => NpcName;
    public bool IsAlive => !Dead && !Downed;
    public bool IsUnaware => false;
    public string Prompt => Dead ? "" : IsMerchant ? $"Talk to {NpcName}" : CanBeRevived ? $"Revive {NpcName}" : Mode switch
    {
        NpcMode.Ritualist => Downed ? $"Revive {NpcName}" : Objectives.ObjectiveController.Instance is Objectives.RitualController { Started: false } ? $"Begin with {NpcName}" : "",
        NpcMode.Prisoner => Objectives.ObjectiveController.Instance is Objectives.IPrisonerHost host ? host.PromptFor(this) : "",
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
    private bool? _talking; // in a conversation: true while its line is spoken, false while it waits; null outside one
    private MeshInstance3D _body = null!, _head = null!;
    private Label3D _label = null!;
    /// <summary>The label's place in the scene, and how far it is raised above it (<see cref="SetLabelLift"/>).</summary>
    private Vector3 _labelBase;
    private float _labelLift;
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
        _labelBase = _label.Position;
        if (_labelLift != 0f) _label.Position = _labelBase + Vector3.Up * _labelLift;
        _gravity = (float)ProjectSettings.GetSetting("physics/3d/default_gravity", 9.8f).AsDouble();
        MaxHp = Hp = TierRules.NpcHp(Floor) * (Mode == NpcMode.Fighter ? NpcFight.HpMultiplier : 1f);
        var tint = new Color(0.75f, 0.65f, 0.45f);
        _body.SetSurfaceOverrideMaterial(0, new StandardMaterial3D { AlbedoColor = tint, Roughness = 0.85f });
        _head.SetSurfaceOverrideMaterial(0, new StandardMaterial3D { AlbedoColor = new Color(0.85f, 0.7f, 0.6f) });
        var model = Models[(int)((Rng.StableHash(Id) + (ulong)Floor) % (ulong)Models.Length)]; // stable per run and peer
        _model = AnimatedModel.Attach(this, ModelPath ?? $"res://assets/models/quaternius_rpg/{model}.gltf", 1.8f, tint, ModelPath != null ? 0f : 0.1f);
        if (_model != null) { _body.Visible = false; _head.Visible = false; }
        ApplySeatPosture(); // seated before its model was built (a crew member spawned straight into a seat)
        _label.Text = NpcName;
        AddToGroup("npcs");
    }

    public void Interact(PartyController party, Adventurer actor)
    {
        if (Dead) return;
        // on a co-op client only talking is local; freeing a prisoner, starting the rite or getting a fighter up is the server's to do
        if (Net.NetSession.IsClient && (Mode is NpcMode.Ritualist or NpcMode.Prisoner || (Mode == NpcMode.Fighter && Downed) || CanBeRevived)) { Net.NetSession.Instance.Send(new Net.InteractIntent(Net.NetRefs.Npc(Id))); return; }
        if ((Mode == NpcMode.Fighter && Downed) || CanBeRevived) { TryRevive(); return; }
        switch (Mode)
        {
            case NpcMode.Ritualist:
                if (Downed) { TryRevive(); return; }
                (Objectives.ObjectiveController.Instance as Objectives.RitualController)?.Begin(this);
                return;
            case NpcMode.Prisoner:
                (Objectives.ObjectiveController.Instance as Objectives.IPrisonerHost)?.OnInteract(this, party, actor);
                return;
        }
        if (Main.Instance?.Hud is not { } hud) return; // a dedicated server has no screen; the client opened its own
        if (InTavern) hud.OpenBriefing(this); // no shop in the tavern: talking to him again replays the briefing
        else if (IsMerchant) hud.OpenMerchant(this); // trade, how it goes, leave: all local, the purchases go through the party
        else hud.OpenDialogue(this);
    }

    /// <summary>An NPC said a line through <see cref="Say"/>, on whichever role it was said (a test hook on the real path).</summary>
    public static event Action<Npc, Line>? Said;

    /// <summary>
    /// Say a line out of a conversation (the merchant's greeting and his remarks on the gathering): its voice if it has
    /// one, the subtitle and the log line either way, and the talking pose for as long as it takes to say. Local only.
    /// </summary>
    public void Say(Line line)
    {
        if (Dead || string.IsNullOrWhiteSpace(line.Text)) return;
        Said?.Invoke(this, line);
        var voiced = VoiceRenderer.Instance?.PlayLine(line, interrupt: false) == true; // the generic merchant's lines have no voice: subtitle only
        GD.Print($"[Npc] {NpcName}: \"{line.Text}\"{(voiced ? "" : " (no voice)")}");
        Main.Instance?.Hud?.ShowSubtitle($"{NpcName}: {line.Text}");
        if (Main.Instance?.Hud?.InDialogueWith(this) == true) return; // the conversation drives the pose
        Converse(true);
        var serial = ++_saySerial;
        GetTree().CreateTimer(TalkPose.EstimateSeconds(line.Text)).Timeout += () =>
        {
            if (!IsInstanceValid(this) || serial != _saySerial || Main.Instance?.Hud?.InDialogueWith(this) == true) return;
            Converse(null);
        };
    }

    private int _saySerial;

    /// <summary>The merchant greets the local adventurer the first time they come within <see cref="GreetRadius"/> on this floor.</summary>
    private void Greet()
    {
        if (_greeted || !IsMerchant || Dead || MerchantLines is not { } lines || Main.Instance?.Party is not { } party || !IsInstanceValid(party) || party.Local is not { } local) return;
        if (new Vector2(local.GlobalPosition.X - GlobalPosition.X, local.GlobalPosition.Z - GlobalPosition.Z).Length() > GreetRadius) return;
        _greeted = true;
        Say(lines.Greeting);
    }

    /// <summary>
    /// The tavern merchant opens his briefing the first time the local adventurer comes within <see cref="GreetRadius"/>,
    /// and never over another screen: while one is open he waits (this is polled each tick) and greets once it closes.
    /// Local only; a dedicated server has no screen and no local adventurer.
    /// </summary>
    private void GreetInTavern()
    {
        if (_greeted || !InTavern || Dead || GreetRadius <= 0f) return;
        if (Main.Instance is not { Hud: { } hud, Party: { } party } || !IsInstanceValid(party) || party.Local is not { } local) return;
        if (hud.DialogueOpen || hud.InventoryOpen || hud.Paused || hud.StairsPromptOpen || hud.ShopOpen || party.InputLocked) return;
        if (new Vector2(local.GlobalPosition.X - GlobalPosition.X, local.GlobalPosition.Z - GlobalPosition.Z).Length() > GreetRadius) return; // planar, like Greet
        _greeted = true;
        GD.Print($"[Npc] {NpcName} greets the party in the tavern: the briefing opens");
        hud.OpenBriefing(this);
    }

    /// <summary>
    /// Play a clip on the model and keep it there: a looping clip loops, a one-shot holds its last frame (Death stays
    /// dead) until another clip is chosen or <c>Showcase(null)</c> hands the body back to Idle/Walk. Local only: the
    /// other players in a session do not see it.
    /// </summary>
    public void Showcase(string? clip)
    {
        _showcase = clip;
        if (clip == null) _talking = null;
        if (_model == null || Dead) return;
        _model.Play(clip ?? "Idle", force: true);
    }

    /// <summary>
    /// The pose of a conversation: Talk while <paramref name="speaking"/>, a slow HipRock while waiting for an answer
    /// (<see cref="TalkPose"/>); null when the dialogue closes hands the body back to Idle. A move picked from the moves
    /// grid plays out first. Local only, like <see cref="Showcase"/>.
    /// </summary>
    public void Converse(bool? speaking)
    {
        if (speaking == null) { if (_talking != null) Showcase(null); return; }
        _talking = speaking;
    }

    /// <summary>
    /// A revive of this NPC would be taken now (<see cref="Objectives.RitualRules.ReviveTaken"/>: down, alive, no living enemy aware of the party
    /// within the clear radius but a hijacker spared on its own wagon's rim, <see cref="NearestHostile"/>), on every peer from its copies: what <see cref="TryRevive"/> refuses by, and what a wagon's "Revive {name}" is
    /// offered by. Awareness is <see cref="Enemy.IsUnaware"/>, which a client reads from the server's snapshot, so a client judges as the server
    /// does. An NPC that is not down asks nothing more (no scan).
    /// </summary>
    public bool ReviveTaken => Downed && !Dead && Objectives.RitualRules.ReviveTaken(Downed, Dead, NearestHostile());

    /// <summary>
    /// The distance to the nearest enemy counting against a revive (<see cref="Objectives.RitualRules.CountsAgainstRevive(bool, bool, bool, bool)"/>:
    /// alive and aware, but not a boarder riding this NPC's own wagon while the train is at its stop or hijacked, the spared hijacker no siege
    /// fight clears; plan 16 final fix wave) among this NPC's siblings, +∞ for none. Every enemy, a client's copies included
    /// (<c>DungeonBuilder.SpawnEnemy</c>), lives under the floor root, as the crew and the floor's NPCs do, and a client's copy is carried by the
    /// wagon the server's rim list names (<c>RailNet</c>), so every peer judges alike. No list is made.
    /// </summary>
    private float NearestHostile()
    {
        var nearest = float.PositiveInfinity;
        if (GetParent() is not { } parent) return nearest;
        var wagon = Cart as TrainWagon;
        var atStop = wagon != null && RailController.Instance is { } rails && IsInstanceValid(rails)
            && Objectives.RitualRules.TrainAtStop(rails.State.Mode == CartMode.Held, rails.Halted, rails.HijackLive);
        for (int i = 0, n = parent.GetChildCount(); i < n; i++)
            if (parent.GetChild(i) is Enemy e && Objectives.RitualRules.CountsAgainstRevive(e.IsAlive, !e.IsUnaware, wagon != null && e.Carrier == wagon, atStop))
                nearest = MathF.Min(nearest, e.GlobalPosition.DistanceTo(GlobalPosition));
        return nearest;
    }

    /// <summary>Get a downed NPC up, if nothing hostile is close enough to make it pointless (<see cref="ReviveTaken"/>).</summary>
    public bool TryRevive()
    {
        if (!Downed || Dead) return false;
        if (!ReviveTaken)
        {
            EventBus.Instance.Log(Crew || (Mode == NpcMode.Quest && Escorting) ? "Not with enemies this close." : $"Not with them at {NpcName}'s throat. Clear the shrine first.");
            return false;
        }
        Downed = false;
        Hp = MathF.Round(MaxHp * (Mode is NpcMode.Ritualist or NpcMode.Fighter ? Objectives.RitualRules.ReviveHpFraction : Objectives.CrewRules.ReviveHpFraction));
        DownedSeconds = 0f;
        _shownLeft = -1;
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
        DownedSeconds = 0f;
        _labelTimer = 0f;
        _shownLeft = -1;
        _label.Text = Crew ? $"{NpcName} (down {Objectives.CrewRules.BleedOutSeconds:0}s)" : $"{NpcName} (down)";
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
        var changed = (s.Flags ^ _netFlags) & ~Net.NpcState.FlagSeated; // the seat bit alone restores nothing (ClientTick reads it)
        _netFlags = s.Flags;
        if (changed != 0)
        {
            var wasDowned = Downed;
            Restore((s.Flags & Net.NpcState.FlagDead) != 0, (s.Flags & Net.NpcState.FlagEscorting) != 0, (s.Flags & Net.NpcState.FlagRescued) != 0, Hp, null, (s.Flags & Net.NpcState.FlagDowned) != 0, (s.Flags & Net.NpcState.FlagFled) != 0);
            Captured = (s.Flags & Net.NpcState.FlagCaptured) != 0;
            Jailed = (s.Flags & Net.NpcState.FlagJailed) != 0;
            Freed = Escorting && Mode == NpcMode.Prisoner;
            if (Downed && !wasDowned) { DownedSeconds = 0f; _shownLeft = -1; } // just gone down: its clock starts full until the next objective state says how far it ran
        }
    }

    /// <summary>
    /// Client: the server's objective state says this downed crew member has <paramref name="secondsLeft"/> to bleed. The label counts down
    /// from there each frame (<see cref="ClientBleed"/>) until the next state refreshes it; the server alone lets them die.
    /// </summary>
    public void ApplyBleedLeft(float secondsLeft)
    {
        if (!Net.NetSession.IsClient || !Crew || !Downed || Dead || !float.IsFinite(secondsLeft)) return;
        DownedSeconds = Math.Clamp(Objectives.CrewRules.BleedOutSeconds - secondsLeft, 0f, Objectives.CrewRules.BleedOutSeconds);
        ShowBleedLabel();
    }

    /// <summary>Client, each physics tick: a downed crew member's clock runs on here, as on the server, and the label shows the whole seconds left. No allocation unless the number changes.</summary>
    private void ClientBleed(float dt)
    {
        if (!Downed || !Crew || Dead) return;
        DownedSeconds = Math.Min(DownedSeconds + dt, Objectives.CrewRules.BleedOutSeconds);
        ShowBleedLabel();
    }

    /// <summary>The bleed label ("{name} (down 42s)"), rebuilt only when the whole seconds left change.</summary>
    private void ShowBleedLabel()
    {
        var left = (int)MathF.Ceiling(Objectives.CrewRules.BleedOutSeconds - DownedSeconds);
        if (left == _shownLeft) return;
        _shownLeft = left;
        _label.Text = $"{NpcName} (down {left}s)";
    }

    /// <summary>The name label's text now (tests: the RIDE pass reads a downed crewman's countdown).</summary>
    internal string LabelText => _label?.Text ?? "";

    private void ClientTick(float dt)
    {
        ClientBleed(dt);
        if (Dead && Visible && InTheDark()) Visible = false; // the server hid it as it fell (CrevasseWatch.Take): its copy here is hidden too
        var before = GlobalPosition;
        // a seat here (the server's SeatMsg) carries it: its ride's owner calls FollowSeat each rendered frame. One the server says is
        // seated whose SeatMsg has not come yet stays put rather than slide 100 ms behind a moving cart
        if (!Seated && (_netFlags & Net.NpcState.FlagSeated) == 0 && _net?.At(Net.NetSession.Instance.Now - Net.Protocol.InterpolationMs / 1000.0) is { } s)
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
        if (!Net.NetSession.IsServer) { Greet(); GreetInTavern(); } // every player's own screen: a dedicated server has nobody to greet
        TickHide(dt);
        if (Net.NetSession.IsClient) { ClientTick(dt); return; }
        BleedOut(dt);
        if (Seated)
        {
            // the ride carries it (FollowSeat, called by the ride's owner): no walking, no gravity, no wounding hazard
            if (Mode == NpcMode.Quest && Escorting && !Downed && !Dead) CheckStairs();
            if (_model != null)
            {
                if (_flash > 0f) { _flash -= dt; _model.Flash(Math.Max(0f, _flash * 2f)); }
                ShowGait(0f, dt);
            }
            return;
        }
        var v = Velocity;
        // a throw's launch (plan 14 Task 5 fix round 1): IsOnFloor is the last MoveAndSlide's, and a seated body made none, so one that stood on
        // the ground before it sat would read "on the floor" on its first tick out and lose the throw; for ThrowLaunch it flies whatever it says
        var launching = _launchLeft > 0f;
        if (launching) _launchLeft -= dt;
        var grounded = IsOnFloor() && !launching;
        v.Y = grounded ? 0f : v.Y - _gravity * dt;
        var planar = Vector3.Zero;
        var party = Main.Instance?.Party;
        var speed = WalkSpeed;
        if (!Dead && !Downed && !Escorting && !IsMerchant && Mode == NpcMode.Quest && MoveTarget == null && Flight(dt, out var away, out speed))
        {
            if (away is { } d) { FaceToward(d); planar = PathMover.Planar(GlobalPosition, d) * speed; }
        }
        else if (!Dead && !Downed && Mode == NpcMode.Fighter && MoveTarget == null && Fight(dt, out var at, out speed))
        {
            if (at is { } d) { FaceToward(d); planar = PathMover.Planar(GlobalPosition, d) * speed; }
        }
        else if (!Dead && !Downed && (!Escorting || MoveOverride) && MoveTarget is { } goal)
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
            var layout = Main.Instance?.Builder.Layout;
            // the side gate, read every frame (not only when a catch-up is due): back on its lead's side (set down beside it, or the lead come across), the hold is over
            if (_catchUpHeld && (layout?.Rail?.Island == null || InIsland(layout, GlobalPosition) == InIsland(layout, lead.GlobalPosition))) _catchUpHeld = false;
            // held across the island's edge (Ruling 35): it holds where it is, walking nowhere; its path search runs only to tell when the
            // hold may end (a way found across), at most every HeldSearchSeconds (a path search per frame cost too much), the first at once
            Vector3? dest = null;
            var straight = true;
            if (!_catchUpHeld) dest = _mover.Next(this, lead.GlobalPosition, dt, FollowDistance, ClearLine, out straight);
            else if ((_heldSearchIn -= dt) <= 0f)
            {
                _heldSearchIn = HeldSearchSeconds;
                _mover.Next(this, lead.GlobalPosition, dt, FollowDistance, ClearLine, out var searchedStraight);
                if (_mover.Unreachable <= 0f && !searchedStraight) _catchUpHeld = false; // a way found across: the hold is over (it walks from the next frame)
            }
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
                if (!lead.Seated)
                {
                    var behind = lead.GlobalPosition + lead.GlobalTransform.Basis * new Vector3(0, 0, 1.5f);
                    // on a crevasse floor never over a drop nor off a plank's boards: the spot when the ground to it holds, else the lead's footing
                    if (layout is { ChasmCount: > 0 } drops)
                    {
                        var spot = CrevasseRules.SetDownNear(drops, DungeonBuilder.CellSize, new(lead.GlobalPosition.X, lead.GlobalPosition.Z), new(behind.X, behind.Z));
                        behind = new Vector3(spot.X, lead.GlobalPosition.Y, spot.Y);
                    }
                    if (CatchUpAllowed(layout, lead, behind)) GlobalPosition = behind;
                }
                // the lead rides: never into the tub or onto the line; far behind, beside the cart (as a rider gets out), else hold here. The spot is on
                // the follower's own side of the exit island, passed explicitly (a bridge cell is outside: ExitBridgeRules.SideOf), never the nearer end's
                else if (gap + FollowDistance > SeatedLeadCatchUp && RailController.Instance is { } rails && rails.StandBesideCart(GlobalPosition) is var beside
                    && CatchUpAllowed(layout, lead, beside)) GlobalPosition = beside;
                _mover.Reset();
            }
            if (Mode == NpcMode.Quest) CheckStairs();
            Hazard(dt);
        }
        if (Thrown && _hideLeft > 0f && !grounded) planar = new Vector3(v.X, 0f, v.Z); // thrown: it keeps its outward way until it lands
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

    /// <summary>
    /// The catch-up is held across the island's edge (<see cref="CatchUpAllowed"/>: the follower and its lead on two sides): the hold lasts until a
    /// catch-up is allowed, the follower is back on its lead's side (read every frame), or it finds a way across.
    /// </summary>
    private bool _catchUpHeld;
    /// <summary>While held (standing still), the follower looks for a way to its lead at most this often (a search per frame cost too much; the unheld follower's trigger is unchanged).</summary>
    private const float HeldSearchSeconds = 0.5f;
    private float _heldSearchIn;
    /// <summary>A refusal by the set-down spot's side alone (the follower and its lead on one side) has been told: once, until a catch-up runs (not a log line per frame).</summary>
    private bool _holdTold;
    /// <summary>The catch-up is held across the island's edge now (the flows read it).</summary>
    internal bool CatchUpHeld => _catchUpHeld;

    private static bool InIsland(FloorLayout layout, Vector3 at) => layout.InIsland(DungeonBuilder.WorldToCell(at));

    /// <summary>Holds that started on a rail cell and stepped off it (plan 15 Task 8; the flows read it).</summary>
    internal int HoldStepsOff { get; private set; }

    /// <summary>Test hook (--test-island=grisel): the hold is let go where the follower stands, so its next one starts there (on a lane cell the flow put it on).</summary>
    internal void DebugReleaseHold() { _catchUpHeld = false; _holdTold = false; _mover.Reset(); }

    /// <summary>
    /// A hold that starts on a rail cell (plan 15 Task 8: a follower held on the lane would stand where a cart runs) first steps the body to the nearest cell off the rails on
    /// its own side (<see cref="ExitBridgeRules.HoldStepOff"/>): then it holds where it is, never inside the lift standing again at its place nor in a
    /// cart's way. Off the rails, or with no such cell near, it holds where it stands. Server and solo.
    /// </summary>
    private void StepOffTheLine(FloorLayout layout, bool inside)
    {
        var here = DungeonBuilder.WorldToCell(GlobalPosition);
        if (!layout.IsRail(here) || Main.Instance?.Builder is not { } builder) return;
        // never onto another body's cell (an NPC on foot, a hero up), as the sibling set-downs keep their taken sets
        var taken = new HashSet<GridPos>();
        if (GetParent() is { } parent)
            for (int i = 0, n = parent.GetChildCount(); i < n; i++)
                if (parent.GetChild(i) is Npc { Dead: false, Seated: false } other && other != this) taken.Add(DungeonBuilder.WorldToCell(other.GlobalPosition));
        if (Main.Instance.Party is { } party)
            foreach (var hero in party.Adventurers)
                if (IsInstanceValid(hero) && !hero.Seated && !hero.Parked) taken.Add(DungeonBuilder.WorldToCell(hero.GlobalPosition));
        if (ExitBridgeRules.HoldStepOff(layout, here, inside, builder.IsCellBlocked, taken) is not { } cell) return;
        GlobalPosition = builder.Surface(cell) + Vector3.Up * 0.1f;
        Velocity = Vector3.Zero;
        HoldStepsOff++;
        GD.Print($"[npc] {NpcName}: held on the line at {here}: steps off to {cell}");
    }

    /// <summary>
    /// Plan 15 Ruling 35: on a floor with an exit island, the follower's catch-up to <paramref name="dest"/> runs only with the follower, its
    /// <paramref name="lead"/> and the spot all on one side of the island's edge (<see cref="Objectives.CrewRules.CatchUpAllowed"/>; a bridge cell is
    /// outside); otherwise it holds where it is, printing so once per hold. Always true off an island floor.
    /// </summary>
    private bool CatchUpAllowed(FloorLayout? layout, Node3D lead, Vector3 dest)
    {
        if (layout?.Rail?.Island == null) return true;
        bool self = InIsland(layout, GlobalPosition), led = InIsland(layout, lead.GlobalPosition);
        if (Objectives.CrewRules.CatchUpAllowed(true, self, led, InIsland(layout, dest))) { _catchUpHeld = false; _holdTold = false; return true; }
        if (self != led)
        {
            // across the edge: a hold, told as it starts, its search throttled until it ends
            if (!_catchUpHeld) { GD.Print($"[npc] {NpcName}: catch-up held (the lead is across the island's edge)"); _catchUpHeld = true; _heldSearchIn = 0f; StepOffTheLine(layout, self); } // its first search at once
        }
        else if (!_holdTold) { GD.Print($"[npc] {NpcName}: catch-up held (the set-down spot is across the island's edge)"); _holdTold = true; } // refused by the spot's side alone: told once until a catch-up runs
        return false;
    }

    /// <summary>Test hook (--test-crew): set a downed crew member's clock, to run the bleed-out on without waiting it out.</summary>
    internal void DebugBleed(float seconds) { if (Downed && Crew && !Dead) DownedSeconds = seconds; }

    /// <summary>A downed crew member's clock (server only): the label counts the seconds left; at the end they are gone.</summary>
    private void BleedOut(float dt)
    {
        if (!Downed || !Crew || Dead) return;
        DownedSeconds += dt;
        if (DownedSeconds >= Objectives.CrewRules.BleedOutSeconds) { Die(); return; }
        _labelTimer += dt;
        if (_labelTimer < 0.5f) return;
        _labelTimer = 0f;
        var left = (int)MathF.Ceiling(Objectives.CrewRules.BleedOutSeconds - DownedSeconds);
        if (left == _shownLeft) return;
        _shownLeft = left;
        _label.Text = $"{NpcName} (down {left}s)";
    }

    // ---- riding ----------------------------------------------------------------------------------

    /// <summary>What this NPC rides (a cart, a train wagon) or stands in (a platform cage), null on foot.</summary>
    public Node3D? Ride { get; private set; }
    public bool Seated => Ride != null;
    /// <summary>A follower stuck this far (flat) from a lead who rides the cart is set down beside the cart; nearer, it holds where it is.</summary>
    public const float SeatedLeadCatchUp = 30f;
    private Func<Transform3D>? _seatAt;
    private uint _layerOnFoot;
    private int _riderId = -1;
    private float _seatYaw;

    /// <summary>The seat book's id for this NPC: 100 + its place among the floor's NPCs, fixed for the ride.</summary>
    public int RiderId
    {
        get
        {
            if (_riderId < 0)
            {
                var index = 0;
                if (GetParent() is { } parent)
                    foreach (var n in parent.GetChildren())
                    {
                        if (n == this) break;
                        if (n is Npc) index++;
                    }
                _riderId = 100 + index;
            }
            return _riderId;
        }
    }

    /// <summary>The cart this NPC sits in (null on foot or on another kind of ride).</summary>
    public ICartBody? Cart { get; private set; }
    public int Seat { get; private set; } = -1;
    public float SeatYaw { get => _seatYaw; set => _seatYaw = Mathf.Clamp(value, -Mathf.Pi, Mathf.Pi); }

    /// <summary>Sit on <paramref name="ride"/> at the pose <paramref name="seat"/> gives (world space, −Z forward); its owner calls <see cref="FollowSeat"/> every rendered frame.</summary>
    public void SitIn(Node3D ride, Func<Transform3D> seat)
    {
        if (!Seated) _layerOnFoot = CollisionLayer;
        Ride = ride;
        _seatAt = seat;
        _seatYaw = 0f;
        CollisionLayer = 0;
        Velocity = Vector3.Zero;
        Fleeing = false;
        MoveTarget = null;
        _mover.Reset();
        FollowSeat();
    }

    /// <summary>
    /// Raise the name label <paramref name="metres"/> above its place in the scene (0: back in place): a cage (or a wagon) stands its two crew's
    /// names one above the other (<see cref="Objectives.CrewRules.LabelLift"/>).
    /// </summary>
    public void SetLabelLift(float metres)
    {
        _labelLift = metres;
        if (_label != null && IsInstanceValid(_label)) _label.Position = _labelBase + Vector3.Up * metres;
    }

    /// <summary>Take the booked <paramref name="seat"/> of a cart (<see cref="RiderId"/> in its book).</summary>
    public void SitIn(ICartBody cart, int seat)
    {
        if (Cart != null && Cart != cart) LeaveSeat();
        Cart = cart;
        Seat = seat;
        SitIn((Node3D)cart, () => cart.SeatTransform(seat));
        ApplySeatPosture();
        cart.Admit(this); // last: whoever hears of it sees the body seated
    }

    /// <summary>The body's height standing (the model is built to it).</summary>
    public const float StandHeight = 1.8f;
    /// <summary>A body sitting in a cart's or a wagon's seat is folded to this share of its height, as a seated hero's (<c>Adventurer.ApplyPosture</c>).</summary>
    public const float SitScale = 0.7f;
    /// <summary>
    /// Sitting: seated in a cart's or a wagon's seat (the bench, plan 14 Task 2), never standing in a cage (<see cref="Dungeon.Rails.PlatformCage"/>),
    /// whose crew stand behind the bars.
    /// </summary>
    public bool Sitting => Cart != null && Cart is not Dungeon.Rails.PlatformCage;
    /// <summary>The top of the head above the body's origin: <see cref="StandHeight"/>, folded by <see cref="SitScale"/> while <see cref="Sitting"/>.</summary>
    public float HeadHeight => Sitting ? StandHeight * SitScale : StandHeight;

    private float _modelScaleY = float.NaN, _modelPosY;

    /// <summary>The model (or the stand-in body and head) folded to <see cref="SitScale"/> while <see cref="Sitting"/>, else at its full height.</summary>
    private void ApplySeatPosture()
    {
        var k = Sitting ? SitScale : 1f;
        if (_model != null && IsInstanceValid(_model.Root))
        {
            if (float.IsNaN(_modelScaleY)) { _modelScaleY = _model.Root.Scale.Y; _modelPosY = _model.Root.Position.Y; }
            _model.Root.Scale = _model.Root.Scale with { Y = _modelScaleY * k };
            _model.Root.Position = _model.Root.Position with { Y = _modelPosY * k };
        }
        else if (_body != null && IsInstanceValid(_body) && _head != null && IsInstanceValid(_head))
        {
            _body.Scale = new Vector3(1f, k, 1f);
            _body.Position = new Vector3(0f, 0.8f * k, 0f);
            _head.Position = new Vector3(0f, 1.72f * k, 0f);
        }
    }

    /// <summary>Upright at the seat, facing its heading turned by <see cref="SeatYaw"/>.</summary>
    public void FollowSeat()
    {
        if (_seatAt == null) return;
        if (Ride == null || !IsInstanceValid(Ride)) { LeaveSeat(); return; }
        var seat = _seatAt();
        var f = -seat.Basis.Z;
        var heading = Mathf.Atan2(-f.X, -f.Z);
        GlobalTransform = new Transform3D(new Basis(Vector3.Up, heading + _seatYaw), seat.Origin);
    }

    /// <summary>Out of the seat and onto the ground at <paramref name="at"/>; an escort keeps following.</summary>
    public void GetOut(Vector3 at) => GetOut(at, Vector3.Zero);

    public void GetOut(Vector3 at, Vector3 velocity)
    {
        LeaveSeat();
        GlobalPosition = at;
        Velocity = velocity;
        _mover.Reset();
    }

    private void LeaveSeat()
    {
        var forward = -GlobalTransform.Basis.Z;
        if (Cart is { } cart && cart is GodotObject o && IsInstanceValid(o)) cart.Release(this);
        var wasSeated = Seated;
        Cart = null;
        Seat = -1;
        Ride = null;
        _seatAt = null;
        _riderId = -1;
        _seatYaw = 0f;
        if (wasSeated) ApplySeatPosture();
        if (wasSeated) CollisionLayer = Dead ? 0u : _layerOnFoot;
        if (new Vector2(forward.X, forward.Z).LengthSquared() > 1e-6f) GlobalBasis = new Basis(Vector3.Up, Mathf.Atan2(-forward.X, -forward.Z));
    }

    /// <summary>Idle, Walk or Run for this ground speed; a viewer-rigged model's gait is played at the pace the ground passes so its feet stay planted.</summary>
    private void ShowGait(float groundSpeed, float dt)
    {
        if (_model == null) return;
        if (_talking is { } speaking && !Dead)
        {
            if (_showcase != null && _model.IsPlayingOneShot) return; // a move from the grid plays through first
            _showcase = null;
            var (clip, speed) = TalkPose.Choose(speaking);
            if (_model.Current != clip) GD.Print($"[Talk] {Id}: {clip} x{speed:0.0}");
            _model.Play(clip, loop: true, speed: speed);
            return;
        }
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
        if (yes) StartFollowing(); else EventBus.Instance.RaiseNpcEvent(Id, NpcEventKind.Refused);
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
        // nobody of the party about (a server floor built before anyone joined): stand down rather than settle the bout alone
        var partyNear = Main.Instance?.Party is { } p && NpcFight.PartyNear(p.Active.Where(a => !a.Downed).Select(a => new Vector2(a.GlobalPosition.X - GlobalPosition.X, a.GlobalPosition.Z - GlobalPosition.Z).Length()));
        var dist = 0f;
        var foe = partyNear ? NearestEnemy(GlobalPosition, NpcFight.SightRange, out dist) : null;
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
        if (r.Hit && !foe.IsAlive) EventBus.Instance.RaiseNpcKill(this);
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
            EventBus.Instance.Log($"{sourceName} {HazardRules.Verb(kind)} {NpcName}!");
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
        LeaveRide();
        EventBus.Instance.Log(Crew ? $"{NpcName} is gone." : $"{NpcName} is dead.");
        EventBus.Instance.RaiseNpcEvent(Id, NpcEventKind.Died);
    }

    /// <summary>Thrown from a wagon by a boarder (plan 14 Task 5): dead at once, gone (hidden and <see cref="Fled"/>) <see cref="Dungeon.Rails.BoarderRules.ThrownHide"/> later. A save writes it as fled from the throw on.</summary>
    public bool Thrown { get; private set; }
    /// <summary>Seconds until the body is hidden (a throw's tumble, or a client's copy told of one), or 0 when none is pending.</summary>
    private float _hideLeft;
    /// <summary>A throw's body flies this long whatever <c>IsOnFloor</c> says (its last value is from before it sat): long enough to leave the ground.</summary>
    public const float ThrowLaunch = 0.1f;
    private float _launchLeft;

    /// <summary>
    /// Thrown from a wagon by a boarder alone with the prisoners (plan 14 Task 5, Ruling 17), server and solo: the death with no corpse. Dead and out
    /// of the escort at once, the death clip; unseated (its seat's book freed, the usual seat message) at <paramref name="at"/> (over the wagon's rim)
    /// with <paramref name="velocity"/>, it flies (for <see cref="ThrowLaunch"/> whatever its stale floor state says) and lands beside the lane; <see cref="Dungeon.Rails.BoarderRules.ThrownHide"/> later it is hidden
    /// and <see cref="Fled"/> (gone, not a corpse), so a save, a load and a client keep it gone. Logs nothing: the controller logs.
    /// </summary>
    public void Throw(Vector3 at, Vector3 velocity)
    {
        if (Dead) return;
        Dead = true;
        Thrown = true;
        Escorting = false;
        CollisionLayer = 0;
        _label.Text = $"{NpcName} (dead)";
        _model?.Play("Death", force: true);
        GetOut(at, velocity); // LeaveSeat keeps it on no layer (dead)
        _launchLeft = ThrowLaunch;
        HideAfter(Dungeon.Rails.BoarderRules.ThrownHide);
        EventBus.Instance.RaiseNpcEvent(Id, NpcEventKind.Died);
    }

    /// <summary>The body is hidden <paramref name="seconds"/> from now (a throw's tumble; a client's copy on the server's word that it was thrown).</summary>
    public void HideAfter(float seconds)
    {
        if (!Visible) return;
        _hideLeft = Math.Max(seconds, 1e-4f);
    }

    /// <summary>A pending hide counts down; at nought the body is hidden, and a thrown one (on the server or solo) is <see cref="Fled"/> from then on.</summary>
    private void TickHide(float dt)
    {
        if (_hideLeft <= 0f) return;
        _hideLeft -= dt;
        if (_hideLeft > 0f) return;
        _hideLeft = 0f;
        Visible = false;
        Velocity = Vector3.Zero;
        if (Thrown && !Net.NetSession.IsClient) Fled = true; // gone, not a corpse, as a load restores a fled one
    }

    /// <summary>The NPC turns on the party: no death, no fate; they simply are not a person any more. <paramref name="fled"/>: they ran to the enemy instead, and stay gone (not dead) across a save or on a client.</summary>
    public void Turn(bool fled = false)
    {
        if (Dead || Turned) return;
        Turned = true;
        Fled = fled;
        Dead = true;
        Escorting = false;
        CollisionLayer = 0;
        Visible = false;
        LeaveRide();
        EventBus.Instance.Log(BetrayalRules.TurnLog(fled, NpcName));
    }

    /// <summary>A seated NPC who dies or turns gives up its seat: the body drops to the floor of the cell it was carried over.</summary>
    private void LeaveRide()
    {
        if (!Seated) return;
        var at = GlobalPosition;
        GetOut(Main.Instance?.Builder is { Layout: not null } builder ? builder.Surface(DungeonBuilder.WorldToCell(at)) : at);
    }

    public void Restore(bool dead, bool escorting, bool rescued, float hp, Vector3? position, bool downed = false, bool fled = false)
    {
        if (position is { } p) GlobalPosition = p;
        if (fled) { Turned = true; Fled = true; Dead = true; Escorting = false; CollisionLayer = 0; Visible = false; return; } // ran to the enemy: gone, not a corpse
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
        if (Dead && position != null && !Net.NetSession.IsClient && InTheDark())
        {
            // a save's body gone into a crevasse stays there, unseen and still, as the fall left it: out of the seat the build gave it, where
            // it lies (a crew controller's "seated dead get out at the floor of their cell" would lift it to the lip, for clients and the next save)
            if (Seated) GetOut(GlobalPosition);
            Visible = false;
            Velocity = Vector3.Zero;
            SetPhysicsProcess(false);
        }
    }

    /// <summary>
    /// The body lies below the lip of the drop it is over (<see cref="CrevasseRules.Fell"/>): a crevasse took it. A dead one there is
    /// hidden on every role: the server's fall hides it (<see cref="CrevasseWatch.Take"/>), a client's copy and a load hide it by this.
    /// </summary>
    internal bool InTheDark()
    {
        if (Main.Instance?.Builder is not { Layout: { } layout } builder) return false;
        var at = GlobalPosition;
        var cell = DungeonBuilder.WorldToCell(at);
        return CrevasseRules.IsDrop(layout, cell) && CrevasseRules.Fell(layout, cell, at.Y, builder.Surface(cell).Y);
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
