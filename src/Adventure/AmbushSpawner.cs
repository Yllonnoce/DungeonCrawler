using DungeonCrawler.Core;
using DungeonCrawler.Dungeon;
using DungeonCrawler.Dungeon.Generation;
using DungeonCrawler.Enemies;
using Godot;

namespace DungeonCrawler.Adventure;

/// <summary>
/// The floor's ambushers: creatures hidden in rubble mounds and wall niches along the corridors (placed by
/// <see cref="AmbushRules"/>) that burst out, already hunting, when an adventurer steps within a few metres. A sprung
/// point stays sprung in the save; the creature itself is dynamic like a horde's and is not kept.
/// </summary>
public partial class AmbushSpawner : Node
{
    public static AmbushSpawner? Instance { get; private set; }
    /// <summary>Metres from the trigger cell's centre at which an ambush springs.</summary>
    public const float TriggerRadius = 3.2f;

    public IReadOnlyList<AmbushPoint> Points { get; private set; } = Array.Empty<AmbushPoint>();
    public HashSet<int> Sprung { get; } = new();
    public int Alive => _alive.Count(e => IsInstanceValid(e) && e.IsAlive);

    private FloorLayout _layout = null!;
    private int _floor;
    private float _timer;
    private readonly List<Enemy> _alive = new();

    public void Setup(FloorLayout layout, int floor, IReadOnlyList<AmbushPoint> points, IEnumerable<int>? sprung)
    {
        Instance = this;
        Name = "Ambushes";
        _layout = layout;
        _floor = floor;
        Points = points;
        if (sprung != null) foreach (var i in sprung) Sprung.Add(i);
    }

    public override void _ExitTree() { if (Instance == this) Instance = null; }

    public void Restore(IEnumerable<int> sprung) { foreach (var i in sprung) Sprung.Add(i); }

    public override void _PhysicsProcess(double delta)
    {
        if (Net.NetSession.IsClient || Points.Count == 0 || Sprung.Count >= Points.Count) return;
        _timer -= (float)delta;
        if (_timer > 0f) return;
        _timer = 0.15f;
        var main = Main.Instance;
        if (main?.Builder.Layout == null || !main.InGame) return;
        for (var i = 0; i < Points.Count; i++)
        {
            if (Sprung.Contains(i)) continue;
            var at = main.Builder.Surface(Points[i].Trigger);
            foreach (var adv in main.Party.Active)
            {
                if (adv.Downed) continue;
                var d = adv.GlobalPosition - at;
                if (Math.Abs(d.Y) < 2f && new Vector2(d.X, d.Z).Length() < TriggerRadius) { Spring(i); break; }
            }
        }
    }

    /// <summary>Let point <paramref name="index"/> loose now: the creature appears out of its cover and goes for the party.</summary>
    public Enemy? Spring(int index)
    {
        if (index < 0 || index >= Points.Count || !Sprung.Add(index)) return null;
        var main = Main.Instance;
        if (main == null) return null;
        var p = Points[index];
        var trigger = main.Builder.Surface(p.Trigger);
        var cover = main.Builder.Surface(p.Cover);
        var toCover = new Vector3(cover.X - trigger.X, 0, cover.Z - trigger.Z);
        if (toCover.Length() > 0.01f) toCover = toCover.Normalized();
        var pos = trigger + toCover * 0.7f + Vector3.Up * 0.15f;
        var e = main.Builder.SpawnEnemy(p.EnemyId, pos, _floor);
        if (e == null) return null;
        e.RunBoost = 1.2f;
        e.ForceAggro();
        _alive.Add(e);
        EventBus.Instance.Log($"{e.DisplayName} bursts from the {(p.FromRubble ? "rubble" : "wall")}!");
        EventBus.Instance.RaiseAmbushSprung(index);
        ShowBurst(index);
        return e;
    }

    /// <summary>The dust, the thud and the shake of a creature breaking cover; what a client shows when the server springs one.</summary>
    public void ShowBurst(int index)
    {
        if (index < 0 || index >= Points.Count || Main.Instance == null) return;
        Sprung.Add(index);
        var p = Points[index];
        var trigger = Main.Instance.Builder.Surface(p.Trigger);
        var cover = Main.Instance.Builder.Surface(p.Cover);
        var toCover = new Vector3(cover.X - trigger.X, 0, cover.Z - trigger.Z);
        if (toCover.Length() > 0.01f) toCover = toCover.Normalized();
        var at = trigger + toCover * 0.9f + Vector3.Up * 0.9f;
        Audio.Synth.PlayAt(GetParent(), Audio.Synth.Thud(), at, -1f);
        Dungeon.Props.Tremor.ShakeOnly(0.035f, 0.5f);
        var dust = new GpuParticles3D
        {
            Amount = 50, Lifetime = 1.4, OneShot = true, Explosiveness = 0.95f, Emitting = true,
            ProcessMaterial = new ParticleProcessMaterial { Direction = -toCover + Vector3.Up * 0.4f, Spread = 50f, InitialVelocityMin = 1.5f, InitialVelocityMax = 4f, Gravity = new Vector3(0, -1.8f, 0), ScaleMin = 0.35f, ScaleMax = 1.1f, EmissionShape = ParticleProcessMaterial.EmissionShapeEnum.Box, EmissionBoxExtents = new Vector3(0.6f, 0.8f, 0.6f) },
            DrawPass1 = new QuadMesh { Size = new Vector2(1f, 1f) },
            MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color(0.42f, 0.38f, 0.32f, 0.2f), AlbedoTexture = Dungeon.Props.Fire.Puff(), Transparency = BaseMaterial3D.TransparencyEnum.Alpha, BillboardMode = BaseMaterial3D.BillboardModeEnum.Particles, ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded },
        };
        GetParent().AddChild(dust);
        dust.GlobalPosition = at;
        GetTree().CreateTimer(2.5).Timeout += () => { if (IsInstanceValid(dust)) dust.QueueFree(); };
    }
}
