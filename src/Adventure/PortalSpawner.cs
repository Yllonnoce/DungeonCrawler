using DungeonCrawler.Core;
using DungeonCrawler.Dungeon;
using DungeonCrawler.Dungeon.Generation;
using DungeonCrawler.Enemies;
using Godot;

namespace DungeonCrawler.Adventure;

/// <summary>
/// The boss's reinforcements on one floor. After a grace period the pressure starts: portals tear open in explored
/// rooms behind the party and let a few creatures through, more often the longer the party stays (pacing in
/// <see cref="PortalRules"/>). Descending ends it; a slain tier boss silences the whole tier. Time under pressure is
/// saved with the floor so a quick load does not reset it.
/// </summary>
public partial class PortalSpawner : Node
{
    public static PortalSpawner? Instance { get; private set; }
    /// <summary>What the HUD appends while reinforcements are loose (hordes own the objective line when they are).</summary>
    public static string HudSuffix => Instance is { Alive: > 0 } p ? $"   Reinforcements: {p.Alive} hunting you" : "";

    /// <summary>Seconds the party has spent on this floor with the pressure running (grace included).</summary>
    public float PressureSeconds { get; private set; }
    public int Waves { get; private set; }
    public int Alive => _alive.Count(e => IsInstanceValid(e) && e.IsAlive);
    public bool Silenced { get; private set; }
    public string SilencedWhy { get; private set; } = "";
    public float Intensity { get; private set; } = 1f;
    public int LastPortalRoom { get; private set; } = -1;

    private FloorLayout _layout = null!;
    private int _floor;
    private Random _rng = new();
    private float _timer;
    private float _silenceCheck;
    private float _hudTimer;
    private bool _announced;
    private readonly List<Enemy> _alive = new();
    private readonly List<string> _enemyIds = new();
    // creatures still to come through the portal that just opened
    private int _pending;
    private Vector3 _portalAt;
    private float _spawnTimer;

    public void Setup(FloorLayout layout, int floor, IReadOnlyList<string> enemyIds, int seed, float pressureSeconds, int waves)
    {
        Instance = this;
        Name = "Portals";
        _layout = layout;
        _floor = floor;
        _enemyIds.AddRange(enemyIds);
        _rng = new Random(seed ^ 0x7047A1);
        PressureSeconds = pressureSeconds;
        Waves = waves;
        Intensity = Settings.Reinforcements;
        _timer = 0f;
        RefreshSilence();
    }

    public override void _ExitTree() { if (Instance == this) Instance = null; }

    public void Restore(float pressureSeconds, int waves) { PressureSeconds = Math.Max(PressureSeconds, pressureSeconds); Waves = Math.Max(Waves, waves); }

    /// <summary>Skip the grace and open the next portal within a second (tests).</summary>
    public void Hurry() { PressureSeconds = Math.Max(PressureSeconds, PortalRules.Grace(Intensity) + 1f); _timer = 0.5f; }

    /// <summary>Re-read everything that can silence the reinforcements: the setting, a debug flag, a dead tier boss, no creatures to send.</summary>
    public void RefreshSilence()
    {
        Intensity = Settings.Reinforcements;
        var bossFloor = PortalRules.BossFloorFor(_floor);
        var bossDead = (Game.Instance?.Floors.TryGetValue(bossFloor, out var s) == true && s.BossDefeated) || (bossFloor == _floor && Main.Instance?.BossDefeatedThisFloor == true);
        (Silenced, SilencedWhy) =
            !PortalRules.Enabled(Intensity) ? (true, "reinforcements are off in Settings")
            : DebugTools.Instance?.NoPortals == true ? (true, "--no-portals")
            : _enemyIds.Count == 0 ? (true, "no creatures in the roster")
            : bossDead ? (true, $"the tier's boss (floor {bossFloor}) is dead")
            : (false, "");
    }

    public override void _PhysicsProcess(double delta)
    {
        if (Net.NetSession.IsClient) return;
        var dt = (float)delta;
        _silenceCheck -= dt;
        if (_silenceCheck <= 0f) { _silenceCheck = 2f; RefreshSilence(); }
        var main = Main.Instance;
        if (main == null || !main.InGame || main.Builder.Layout != _layout) return;
        _alive.RemoveAll(e => !IsInstanceValid(e) || !e.IsAlive);
        TickSpawns(dt, main);
        TickHud(dt);
        if (Silenced) return;
        PressureSeconds += dt;
        var grace = PortalRules.Grace(Intensity);
        if (PressureSeconds < grace) return;
        _timer -= dt;
        if (_timer > 0f) return;
        if (_alive.Count + _pending >= PortalRules.MaxAliveFor(Intensity)) { _timer = 5f; return; }
        var party = main.Party.Active.Where(a => !a.Downed).Select(a => DungeonBuilder.WorldToCell(a.GlobalPosition)).ToList();
        if (main.Exploration == null || party.Count == 0) { _timer = 5f; return; }
        var site = PortalRules.ChooseSite(_layout, main.Exploration.Seen, party, _rng);
        if (site == null) { _timer = 5f; return; }
        _timer = PortalRules.Interval(PressureSeconds - grace, Intensity);
        Open(site.Value.Room, site.Value.Cell, PortalRules.GroupSize(PressureSeconds - grace, Intensity, _rng), main);
    }

    private void Open(Room room, GridPos cell, int count, Main main)
    {
        Waves++;
        LastPortalRoom = _layout.Rooms.IndexOf(room);
        _portalAt = main.Builder.Surface(cell) + Vector3.Up * 0.05f;
        _pending = count;
        _spawnTimer = 1.1f;
        var boss = Game.Instance.Adventure.Campaign?.Antagonist.Name;
        if (!_announced)
        {
            _announced = true;
            EventBus.Instance.RaiseNarration(string.IsNullOrWhiteSpace(boss) ? "The dungeon's master sends reinforcements. Push deeper; they will keep coming." : $"{boss} sends reinforcements. Push deeper; they will keep coming.", null);
        }
        else EventBus.Instance.Log("A portal tears open behind you.");
        EventBus.Instance.RaisePortalOpened(_portalAt);
        ShowPortal(_portalAt);
        GD.Print($"[portal] floor {_floor} wave {Waves}: {count} creature{(count == 1 ? "" : "s")} in room {LastPortalRoom} after {PressureSeconds:0} s (next in {_timer:0} s)");
    }

    private void TickSpawns(float dt, Main main)
    {
        if (_pending <= 0) return;
        _spawnTimer -= dt;
        if (_spawnTimer > 0f) return;
        _spawnTimer = 0.45f;
        _pending--;
        var jitter = new Vector3((float)(_rng.NextDouble() - 0.5) * 1.2f, 0.15f, (float)(_rng.NextDouble() - 0.5) * 1.2f);
        var id = _enemyIds[_rng.Next(_enemyIds.Count)];
        if (main.Builder.SpawnEnemy(id, _portalAt + jitter, _floor) is not { } e) { _pending = 0; return; }
        e.IsReinforcement = true;
        e.RunBoost = 1.15f;
        e.ForceAggro();
        _alive.Add(e);
    }

    private void TickHud(float dt)
    {
        _hudTimer -= dt;
        if (_hudTimer > 0f) return;
        _hudTimer = 0.5f;
        if (_alive.Count > 0 && !HordeSpawner.AnyLoose) { _wroteHud = true; EventBus.Instance.RaiseObjectiveChanged(HudSuffix.Trim()); }
        else if (_wroteHud) { _wroteHud = false; AdventureDirector.Instance?.RefreshObjective(); }
    }
    private bool _wroteHud;

    /// <summary>The rift itself: a glowing ring, a violet light and a spray of sparks that fade after a few seconds. Clients show it on the server's word.</summary>
    public void ShowPortal(Vector3 at)
    {
        var parent = GetParent();
        var root = new Node3D { Name = $"Portal{Waves}" };
        parent.AddChild(root);
        root.GlobalPosition = at + Vector3.Up * 1.1f;
        var ring = new MeshInstance3D
        {
            Mesh = new TorusMesh { InnerRadius = 0.75f, OuterRadius = 1.0f },
            MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color(0.55f, 0.2f, 0.9f), EmissionEnabled = true, Emission = new Color(0.6f, 0.25f, 1f), EmissionEnergyMultiplier = 3f, ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded },
            RotationDegrees = new Vector3(90f, 0, 0),
        };
        root.AddChild(ring);
        root.AddChild(new OmniLight3D { LightColor = new Color(0.65f, 0.3f, 1f), LightEnergy = 3.5f, OmniRange = 7f, ShadowEnabled = false });
        var sparks = new GpuParticles3D
        {
            Amount = 70, Lifetime = 1.2, Emitting = true,
            ProcessMaterial = new ParticleProcessMaterial { Direction = Vector3.Up, Spread = 180f, InitialVelocityMin = 0.5f, InitialVelocityMax = 2.5f, Gravity = Vector3.Zero, ScaleMin = 0.12f, ScaleMax = 0.35f, EmissionShape = ParticleProcessMaterial.EmissionShapeEnum.Ring, EmissionRingRadius = 0.9f, EmissionRingInnerRadius = 0.6f, EmissionRingHeight = 0.1f, EmissionRingAxis = Vector3.Up },
            DrawPass1 = new QuadMesh { Size = new Vector2(0.3f, 0.3f) },
            MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color(0.8f, 0.5f, 1f, 0.7f), AlbedoTexture = Dungeon.Props.Fire.Puff(), Transparency = BaseMaterial3D.TransparencyEnum.Alpha, BillboardMode = BaseMaterial3D.BillboardModeEnum.Particles, ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded },
        };
        root.AddChild(sparks);
        Audio.Synth.PlayAt(parent, Audio.Synth.Rumble(), at + Vector3.Up, -4f);
        root.Scale = Vector3.One * 0.05f;
        var tween = root.CreateTween();
        tween.TweenProperty(root, "scale", Vector3.One, 0.6f).SetTrans(Tween.TransitionType.Back).SetEase(Tween.EaseType.Out);
        tween.TweenInterval(4.0f);
        tween.TweenProperty(root, "scale", Vector3.One * 0.05f, 0.7f).SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.In);
        tween.TweenCallback(Callable.From(() => { if (IsInstanceValid(root)) root.QueueFree(); }));
    }
}
