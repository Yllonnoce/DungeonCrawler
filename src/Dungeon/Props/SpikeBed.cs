using DungeonCrawler.Adventure;
using DungeonCrawler.Combat;
using DungeonCrawler.Core;
using DungeonCrawler.Dungeon.Generation;
using DungeonCrawler.Enemies;
using DungeonCrawler.Party;
using Godot;

namespace DungeonCrawler.Dungeon.Props;

/// <summary>
/// The Spike Bed: a patch of holed floor. Pressure beds fire when stepped on (a click, a hand's-breadth of pre-raise,
/// then the spikes); periodic beds fire on a metronome from the server tick, out of step with their neighbours.
/// Whoever has their feet on the patch when the spikes reach full height is impaled; a body in the air is not.
/// The server owns the state machine (<see cref="HazardRules"/>); clients animate what they are told.
/// </summary>
public partial class SpikeBed : Node3D
{
    public const string SourceName = "The spikes";
    private const float SpikeDown = -0.45f;
    private const float SpikeUp = 1.0f;

    public int Index { get; set; }
    public HazardPlacement Placement { get; set; } = null!;
    public IReadOnlyList<GridPos> Cells => Placement.Cells;
    public HazardVariant Variant { get; private set; }
    public int Floor { get; set; } = 1;
    public SpikePhase Phase { get; private set; } = SpikePhase.Down;
    public float PhaseTime { get; private set; }
    public int Spent { get; private set; }
    /// <summary>At rest: a pressure bed waiting in Down, or a periodic bed anywhere in its Down phase.</summary>
    public bool Armed => Phase == SpikePhase.Down;
    public HazardPlate Plate { get; private set; } = null!;
    public event Action<SpikeBed, SpikePhase>? PhaseChanged;

    private readonly List<MeshInstance3D> _spikes = new();
    private readonly List<MeshInstance3D> _glows = new();
    private Material? _stone;
    private DungeonBuilder? _builder;
    private float _raise = SpikeDown;

    public void Configure(HazardPlacement placement, int floor, Material stone, DungeonBuilder builder)
    {
        Placement = placement; Index = placement.Index; Variant = placement.Variant; Floor = floor; _stone = stone; _builder = builder;
    }

    /// <summary>Testing only: swap the variant after the fact.</summary>
    public void ForceVariant(HazardVariant variant) { Variant = variant; Phase = SpikePhase.Down; PhaseTime = 0f; Plate.Enabled = variant == HazardVariant.Pressure; }

    public override void _Ready()
    {
        GlobalPosition = _builder!.Surface(Placement.Cells[0]);
        foreach (var c in Placement.Cells)
        {
            var at = _builder.Surface(c) - GlobalPosition;
            // the holed tile: a dark slab with a grid of holes drawn as small dark boxes
            var tile = new MeshInstance3D { Mesh = new BoxMesh { Size = new Vector3(DungeonBuilder.CellSize - 0.1f, 0.05f, DungeonBuilder.CellSize - 0.1f) }, Position = at + new Vector3(0, 0.025f, 0) };
            tile.SetSurfaceOverrideMaterial(0, new StandardMaterial3D { AlbedoColor = new Color(0.16f, 0.15f, 0.14f), Roughness = 1f });
            AddChild(tile);
            var glow = new MeshInstance3D { Mesh = new BoxMesh { Size = new Vector3(DungeonBuilder.CellSize - 0.6f, 0.02f, DungeonBuilder.CellSize - 0.6f) }, Position = at + new Vector3(0, 0.06f, 0), Visible = false };
            glow.SetSurfaceOverrideMaterial(0, new StandardMaterial3D { AlbedoColor = new Color(0.6f, 0.15f, 0.1f), EmissionEnabled = true, Emission = new Color(0.9f, 0.2f, 0.1f), EmissionEnergyMultiplier = 1.5f });
            AddChild(glow);
            _glows.Add(glow);
            for (var i = 0; i < 9; i++)
            {
                var ox = (i % 3 - 1) * 1.1f; var oz = (i / 3 - 1) * 1.1f;
                var spike = new MeshInstance3D { Mesh = new CylinderMesh { TopRadius = 0.0f, BottomRadius = 0.09f, Height = 1.3f }, Position = at + new Vector3(ox, _raise, oz) };
                spike.SetSurfaceOverrideMaterial(0, new StandardMaterial3D { AlbedoColor = new Color(0.35f, 0.33f, 0.3f), Metallic = 0.6f, Roughness = 0.5f });
                AddChild(spike);
                _spikes.Add(spike);
            }
        }
        Plate = new HazardPlate { Name = "Plate" };
        AddChild(Plate);
        Plate.Position = _builder.Surface(Placement.Plate) - GlobalPosition;
        Plate.Enabled = Variant == HazardVariant.Pressure;
        Plate.Pressed += _ => Trigger();
        if (Variant == HazardVariant.Pressure) Plate.Reveal(); // the holes are always visible; the plate reads as part of the bed
    }

    /// <summary>Something stepped on a pressure bed (server).</summary>
    public void Trigger()
    {
        if (Net.NetSession.IsClient || Variant != HazardVariant.Pressure || Phase != SpikePhase.Down) return;
        Enter(SpikePhase.Click);
    }

    public override void _PhysicsProcess(double delta)
    {
        if (Net.NetSession.IsClient) { Animate((float)delta); return; }
        var dt = (float)delta;
        if (Variant == HazardVariant.Periodic)
        {
            var phase = HazardRules.PeriodicPhaseAt(_builder?.FloorTick ?? 0, Index, out var into);
            PhaseTime = into;
            if (phase != Phase) Enter(phase);
            else { WriteDanger(); SetRaise(); }
            return;
        }
        if (Phase == SpikePhase.Down) return;
        PhaseTime += dt;
        WriteDanger();
        SetRaise();
        if (PhaseTime >= HazardRules.SpikeDuration(Phase, Variant)) Enter(HazardRules.NextSpike(Phase));
    }

    private void Enter(SpikePhase next)
    {
        var from = Phase;
        Phase = next;
        PhaseTime = 0f;
        switch (next)
        {
            case SpikePhase.Click:
                Plate.Enabled = false;
                EventBus.Instance.PlaySfx("ui_click");
                foreach (var g in _glows) g.Visible = true;
                break;
            case SpikePhase.Up:
                foreach (var g in _glows) g.Visible = false;
                if (from == SpikePhase.Click) Impale();
                break;
            case SpikePhase.Down:
                Plate.Enabled = Variant == HazardVariant.Pressure;
                break;
        }
        WriteDanger();
        SetRaise();
        PhaseChanged?.Invoke(this, next);
        EventBus.Instance.RaiseHazardPhase(Index, (byte)next);
    }

    private void SetRaise()
    {
        var t = Phase switch
        {
            SpikePhase.Click => 0f,
            SpikePhase.Up => 1f,
            SpikePhase.Retract => 1f - Math.Clamp(PhaseTime / HazardRules.SpikeDuration(SpikePhase.Retract, Variant), 0f, 1f),
            _ => 0f,
        };
        var pre = Phase == SpikePhase.Click ? HazardRules.SpikePreRaiseMetres : 0f;
        _raise = Mathf.Lerp(SpikeDown, SpikeUp, t) + pre;
        foreach (var s in _spikes) s.Position = new Vector3(s.Position.X, _raise, s.Position.Z);
    }

    private void WriteDanger()
    {
        if (_builder == null) return;
        var value = Phase switch
        {
            SpikePhase.Down => Variant == HazardVariant.Periodic ? Math.Clamp((PhaseTime - 2f) / 1f, 0f, 1f) : 0f, // the last second before the click
            SpikePhase.Click or SpikePhase.Up => 1f,
            SpikePhase.Retract => 0.3f,
            _ => 0f,
        };
        foreach (var c in Placement.Cells) _builder.SetDanger(c, value);
    }

    private bool OnBed(Node3D body)
    {
        var cell = DungeonBuilder.WorldToCell(body.GlobalPosition);
        if (!Placement.Cells.Contains(cell)) return false;
        return Math.Abs(body.GlobalPosition.Y - GlobalPosition.Y) < DungeonBuilder.LevelHeight * 0.75f;
    }

    private void Impale()
    {
        if (GetParent() is not { } parent) return;
        var lethality = TierRules.Lethality;
        var any = false;
        var victims = new List<(Node3D Body, bool OnFloor)>();
        if (Main.Instance?.Party is { } party) victims.AddRange(party.Active.Where(OnBed).Select(a => ((Node3D)a, a.IsOnFloor())));
        victims.AddRange(parent.GetChildren().OfType<Enemy>().Where(e => e.IsAlive && OnBed(e)).Select(e => ((Node3D)e, e.IsOnFloor())));
        victims.AddRange(parent.GetChildren().OfType<Npc>().Where(n => n.IsAlive && OnBed(n)).Select(n => ((Node3D)n, true)));
        foreach (var (body, onFloor) in victims)
        {
            if (body is not IHazardVictim victim) continue;
            if (HazardRules.CanDodgeSpikes(onFloor)) { EventBus.Instance.Log($"The spikes miss {(body is Adventurer a ? a.State.Name : body.Name)} in the air."); continue; }
            victim.ReceiveHazard(HazardDamage.Impale, HazardRules.Outcome(victim.HazardVictimKind, lethality), SourceName, Index);
            any = true;
            if (body is Adventurer { Downed: true } or Npc { Downed: true }) body.GlobalPosition = NearestExit(body.GlobalPosition);
        }
        if (any) { Spent++; EventBus.Instance.PlaySfx("hit_flesh"); }
    }

    /// <summary>The nearest walkable cell beside the patch, so a downed body is not impaled again next cycle.</summary>
    public Vector3 NearestExit(Vector3 from)
    {
        var layout = _builder?.Layout;
        if (layout == null) return from;
        GridPos? best = null; var bestD = float.MaxValue;
        foreach (var c in Placement.Cells) foreach (var n in c.Neighbours4())
        {
            if (Placement.Cells.Contains(n) || !layout.Contains(n) || !layout.IsWalkable(n)) continue;
            var d = _builder!.Surface(n).DistanceSquaredTo(from);
            if (d < bestD) { bestD = d; best = n; }
        }
        return best is { } b ? _builder!.Surface(b) + Vector3.Up * 0.2f : from;
    }

    // ---- client side -------------------------------------------------------------------------

    private SpikePhase _shownPhase = SpikePhase.Down;
    private float _shownTime;

    public void ApplyPhase(SpikePhase phase, float secondsIn, int spent)
    {
        Phase = phase; PhaseTime = secondsIn; Spent = spent;
        _shownPhase = phase; _shownTime = secondsIn;
        foreach (var g in _glows) g.Visible = phase == SpikePhase.Click;
        SetRaise();
    }

    private void Animate(float dt)
    {
        _shownTime += dt;
        PhaseTime = _shownTime;
        if (_shownPhase == SpikePhase.Retract) SetRaise();
    }

    /// <summary>Restore from a save: resume the phase with its elapsed time.</summary>
    public void Restore(SpikePhase phase, float phaseTime, int spent)
    {
        Phase = phase; PhaseTime = phaseTime; Spent = spent;
        Plate.Enabled = Variant == HazardVariant.Pressure && phase == SpikePhase.Down;
        foreach (var g in _glows) g.Visible = phase == SpikePhase.Click;
        SetRaise();
        WriteDanger();
    }

    public override void _ExitTree() => _builder?.ClearDanger(Placement.Cells);
}
