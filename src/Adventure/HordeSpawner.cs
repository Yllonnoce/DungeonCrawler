using DungeonCrawler.Core;
using DungeonCrawler.Dungeon;
using DungeonCrawler.Dungeon.Generation;
using DungeonCrawler.Enemies;
using Godot;

namespace DungeonCrawler.Adventure;

/// <summary>
/// A horde waiting in a nest room's walls. Once triggered (the party steps in, or an alarm runner arrives anywhere on the
/// floor) it streams its creatures out of the breaches a few a second, all of them already hunting, and keeps a count on
/// the HUD until the last one is down. Spawned creatures carry no save index: a horde that was set off counts as spent.
/// </summary>
public partial class HordeSpawner : Node
{
    public const float PerSecond = 4f;
    /// <summary>Swarm creatures alive across every horde on the floor; streams pause above this.</summary>
    public const int MaxAliveOnFloor = 70;
    private static readonly List<HordeSpawner> Live = new();
    public static int AliveOnFloor => Live.Sum(s => s.Alive);
    /// <summary>A horde is streaming or still being fought somewhere on the floor (it then owns the objective line).</summary>
    public static bool AnyLoose => Live.Any(s => s.Triggered && !s.Spent);

    public HordeContents Horde { get; private set; } = null!;
    public bool Triggered { get; private set; }
    public bool Spent { get; private set; }
    public int Spawned { get; private set; }
    public int Killed { get; private set; }
    public int Alive => _alive.Count;

    private FloorLayout _layout = null!;
    private Room _room = null!;
    private int _floor;
    private float _timer;
    private float _hudTimer;
    private int _mouth;
    private readonly List<Enemy> _alive = new();
    private readonly List<GpuParticles3D> _dust = new();
    private List<string> _order = new();
    public int Count => Horde.Total;

    public void Setup(HordeContents horde, FloorLayout layout, int floor, bool spent)
    {
        Horde = horde;
        _layout = layout;
        _room = layout.Rooms[horde.Room];
        _floor = floor;
        Spent = spent;
        Name = $"Horde_{horde.Id}";
        var roster = Main.Instance?.Builder.Roster;
        _order = horde.SpawnOrder(id => roster == null || !roster.TryGetValue(id, out var p) || p.Faction == FloorMenu.HordeFaction);
        if (horde.Trigger == HordeTrigger.Alarm) EventBus.Instance.AlarmRaised += OnAlarm;
        if (horde.Trigger == HordeTrigger.Chest) EventBus.Instance.ChestOpened += OnChest;
        Live.Add(this);
    }

    public override void _ExitTree()
    {
        if (Horde?.Trigger == HordeTrigger.Alarm) EventBus.Instance.AlarmRaised -= OnAlarm;
        if (Horde?.Trigger == HordeTrigger.Chest) EventBus.Instance.ChestOpened -= OnChest;
        Live.Remove(this);
    }

    private void OnAlarm(string collectiveId) => Trigger();
    private void OnChest(Vector3 at) { if (_room.Contains(DungeonBuilder.WorldToCell(at))) Trigger(); }

    public void MarkSpent() => Spent = true;

    /// <summary>Let it loose now, whatever the trigger.</summary>
    public void Trigger()
    {
        if (Triggered || Spent) return;
        Triggered = true;
        _timer = 0.6f;
        EventBus.Instance.RaiseNarration(Horde.Line.Text.Length > 0 ? Horde.Line.Text : "The walls give way. They come.", Horde.Line.VoiceId);
        EventBus.Instance.PlaySfx("boss");
        EventBus.Instance.RaiseHordeTriggered(Horde.Id);
        Show();
        GD.Print($"[horde] {Horde.Name}: {string.Join(" + ", Horde.Members.Select(m => $"{m.Count} x {m.EnemyId}"))} from {Horde.Mouths.Count} mouths in room {Horde.Room}");
    }

    /// <summary>The rumble, the shake and the dust at every breach: what a client shows when the server lets a horde loose.</summary>
    public void Show()
    {
        Triggered = true;
        Dungeon.Props.Tremor.ShakeOnly(0.07f, 1.8f);
        var builder = Main.Instance!.Builder;
        for (var i = 0; i < Horde.Mouths.Count; i++)
        {
            var at = builder.Surface(Horde.Mouths[i]) + Vector3.Up * 1.2f;
            Audio.Synth.PlayAt(GetParent(), Audio.Synth.Rumble(), at, -2f);
            var dust = new GpuParticles3D
            {
                Amount = 40, Lifetime = 1.6, OneShot = false, Explosiveness = 0.1f,
                ProcessMaterial = new ParticleProcessMaterial { Direction = new Vector3(Horde.Outward[i].X, 0.3f, Horde.Outward[i].Y), Spread = 35f, InitialVelocityMin = 1.5f, InitialVelocityMax = 3.5f, Gravity = new Vector3(0, -1.5f, 0), ScaleMin = 0.4f, ScaleMax = 1.2f, EmissionShape = ParticleProcessMaterial.EmissionShapeEnum.Box, EmissionBoxExtents = new Vector3(1.2f, 1.2f, 0.3f) },
                DrawPass1 = new QuadMesh { Size = new Vector2(1.1f, 1.1f) },
                MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color(0.4f, 0.36f, 0.3f, 0.16f), AlbedoTexture = Dungeon.Props.Fire.Puff(), Transparency = BaseMaterial3D.TransparencyEnum.Alpha, BillboardMode = BaseMaterial3D.BillboardModeEnum.Particles, ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded },
            };
            GetParent().AddChild(dust);
            dust.GlobalPosition = at;
            _dust.Add(dust);
        }
    }

    /// <summary>The last one fell (a client hearing it from the server): the dust settles.</summary>
    public void ShowSpent()
    {
        Spent = true;
        foreach (var d in _dust) d.Emitting = false;
        _dust.Clear();
    }

    public override void _PhysicsProcess(double delta)
    {
        if (Spent || Net.NetSession.IsClient) return;
        var dt = (float)delta;
        var main = Main.Instance;
        if (main == null) return;
        if (!Triggered)
        {
            if (Horde.Trigger != HordeTrigger.Enter) return;
            foreach (var adv in main.Party.Adventurers)
            {
                if (adv.Downed) continue;
                var cell = DungeonBuilder.WorldToCell(adv.GlobalPosition);
                // a cell in from the edge, so peeking through the door does not set it off
                if (cell.X > _room.X && cell.X < _room.Right && cell.Y > _room.Y && cell.Y < _room.Bottom && Math.Abs(adv.GlobalPosition.Y - main.Builder.Surface(cell).Y) < 2f) { Trigger(); break; }
            }
            return;
        }
        _alive.RemoveAll(e => { if (!IsInstanceValid(e) || !e.IsAlive) { Killed++; return true; } return false; });
        if (Spawned < Count)
        {
            _timer -= dt;
            while (_timer <= 0f && Spawned < Count && _alive.Count < 50 && AliveOnFloor < MaxAliveOnFloor)
            {
                _timer += 1f / PerSecond;
                SpawnOne(main.Builder);
            }
        }
        else if (_dust.Count > 0)
        {
            foreach (var d in _dust) d.Emitting = false;
            _dust.Clear();
        }
        _hudTimer -= dt;
        if (_hudTimer <= 0f)
        {
            _hudTimer = 0.4f;
            // one line for every horde loose on the floor
            var loose = Live.Where(s => s.Triggered && !s.Spent).ToList();
            EventBus.Instance.RaiseObjectiveChanged(string.Join("   ", loose.Select(s => $"{s.Horde.Name}: {s.Count - s.Killed} remain")) + PortalSpawner.HudSuffix);
        }
        if (Spawned >= Count && _alive.Count == 0)
        {
            Spent = true;
            EventBus.Instance.RaiseHordeSpent(Horde.Id);
            EventBus.Instance.Log($"The last of {Horde.Name} falls.");
            AdventureDirector.Instance?.RefreshObjective();
        }
    }

    private void SpawnOne(DungeonBuilder builder)
    {
        var i = _mouth++ % Horde.Mouths.Count;
        var mouth = Horde.Mouths[i];
        var outward = new Vector3(Horde.Outward[i].X, 0, Horde.Outward[i].Y);
        var side = new Vector3(-outward.Z, 0, outward.X);
        var jitter = ((Spawned * 7919) % 100 / 100f - 0.5f) * 1.6f;
        var pos = builder.Surface(mouth) + outward * 0.6f + side * jitter + Vector3.Up * 0.15f;
        var id = Spawned < _order.Count ? _order[Spawned] : _order[^1];
        if (builder.SpawnEnemy(id, pos, _floor) is not { } e) { Spawned = Count; return; }
        e.SetSwarmDetail();
        e.HordeId = Horde.Id;
        e.RunBoost = 1.1f;
        e.ForceAggro();
        _alive.Add(e);
        Spawned++;
    }
}
