using DungeonCrawler.Adventure;
using DungeonCrawler.Combat;
using DungeonCrawler.Core;
using DungeonCrawler.Dungeon.Generation;
using DungeonCrawler.Enemies;
using DungeonCrawler.Party;
using Godot;

namespace DungeonCrawler.Dungeon.Props;

/// <summary>
/// The Press: a three-cell corridor whose side walls close. A plate in the middle cell (anyone's step) starts a
/// warning, then the slabs squeeze in and crush whatever is still in the run: players and allies go down Mangled,
/// enemies die, bosses stagger. A barrel left in the run jams it. The server owns the state machine
/// (<see cref="HazardRules"/>); clients only animate what they are told.
/// </summary>
public partial class Press : Node3D
{
    public const string SourceName = "The press";
    private const float SlabThickness = 0.5f;
    private const float SlabHeight = DungeonBuilder.LevelHeight;

    public int Index { get; set; }
    public HazardPlacement Placement { get; set; } = null!;
    public int Floor { get; set; } = 1;
    public PressPhase Phase { get; private set; } = PressPhase.Armed;
    public float PhaseTime { get; private set; }
    public bool Jammed { get; private set; }
    public int Spent { get; private set; }
    public bool Armed => Phase == PressPhase.Armed;
    public HazardPlate Plate { get; private set; } = null!;
    public event Action<Press, PressPhase>? PhaseChanged;

    private AnimatableBody3D _slabA = null!, _slabB = null!;
    private MeshInstance3D _decal = null!;
    private Vector3 _centre, _side, _along;
    private float _gap = 3.5f;
    private Material? _stone;
    private DungeonBuilder? _builder;

    public void Configure(HazardPlacement placement, int floor, Material stone, DungeonBuilder builder)
    {
        Placement = placement; Index = placement.Index; Floor = floor; _stone = stone; _builder = builder;
    }

    public override void _Ready()
    {
        _centre = _builder!.Surface(Placement.Cells[1]);
        _along = Placement.Horizontal ? Vector3.Right : Vector3.Back;
        _side = Placement.Horizontal ? Vector3.Back : Vector3.Right;
        GlobalPosition = _centre;
        _slabA = MakeSlab("SlabA");
        _slabB = MakeSlab("SlabB");
        _decal = new MeshInstance3D { Name = "Decal", Mesh = new PlaneMesh { Size = new Vector2(2.2f, 1.2f) }, Position = new Vector3(0, 0.03f, 0), Visible = false };
        _decal.SetSurfaceOverrideMaterial(0, new StandardMaterial3D { AlbedoColor = new Color(0.25f, 0.05f, 0.04f), Roughness = 1f });
        if (!Placement.Horizontal) _decal.RotateY(Mathf.Pi / 2f);
        AddChild(_decal);
        Plate = new HazardPlate { Name = "Plate" };
        AddChild(Plate);
        Plate.Position = Vector3.Zero;
        Plate.Pressed += _ => Trigger();
        LayoutSlabs();
    }

    private AnimatableBody3D MakeSlab(string name)
    {
        var length = DungeonBuilder.CellSize * Placement.Cells.Count;
        var size = Placement.Horizontal ? new Vector3(length, SlabHeight, SlabThickness) : new Vector3(SlabThickness, SlabHeight, length);
        var body = new AnimatableBody3D { Name = name, CollisionLayer = 1, CollisionMask = 0, SyncToPhysics = true };
        body.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = size } });
        var mesh = new MeshInstance3D { Mesh = new BoxMesh { Size = size } };
        if (_stone != null) mesh.SetSurfaceOverrideMaterial(0, _stone);
        body.AddChild(mesh);
        // grooves: three dark stripes down the inner face, the tell that this wall moves
        for (var i = -1; i <= 1; i++)
        {
            var stripe = new MeshInstance3D { Mesh = new BoxMesh { Size = Placement.Horizontal ? new Vector3(0.12f, SlabHeight * 0.9f, 0.02f) : new Vector3(0.02f, SlabHeight * 0.9f, 0.12f) } };
            stripe.SetSurfaceOverrideMaterial(0, new StandardMaterial3D { AlbedoColor = new Color(0.08f, 0.08f, 0.08f), Roughness = 1f });
            stripe.Position = Placement.Horizontal ? new Vector3(i * length / 3f, 0, 0) : new Vector3(0, 0, i * length / 3f);
            body.AddChild(stripe);
        }
        AddChild(body);
        return body;
    }

    private void LayoutSlabs()
    {
        var half = _gap / 2f + SlabThickness / 2f;
        var y = SlabHeight / 2f;
        _slabA.Position = _side * half + Vector3.Up * y;
        _slabB.Position = -_side * half + Vector3.Up * y;
        // the stripes face inward: nothing to do, the slab is symmetric
    }

    /// <summary>Something stepped on the plate (server). Nothing happens unless the press is waiting.</summary>
    public void Trigger()
    {
        if (Net.NetSession.IsClient || Phase != PressPhase.Armed) return;
        Enter(PressPhase.Warning);
    }

    public override void _PhysicsProcess(double delta)
    {
        if (Net.NetSession.IsClient) { Animate((float)delta); return; }
        if (Phase == PressPhase.Armed) return;
        var dt = (float)delta;
        PhaseTime += dt;
        if (Phase == PressPhase.Closing)
        {
            var t = PhaseTime / HazardRules.PressDuration(PressPhase.Closing);
            var gap = HazardRules.PressGap(t);
            if (!Jammed && gap <= HazardRules.JamGap && JamCheck()) Jammed = true;
            _gap = Jammed ? Math.Max(gap, HazardRules.JamGap) : gap;
            LayoutSlabs();
        }
        else if (Phase == PressPhase.Retract)
        {
            _gap = Mathf.Lerp(Jammed ? HazardRules.JamGap : HazardRules.PressGap(1f), HazardRules.PressGap(0f), Math.Clamp(PhaseTime / HazardRules.PressDuration(PressPhase.Retract), 0f, 1f));
            LayoutSlabs();
        }
        WriteDanger();
        if (PhaseTime >= HazardRules.PressDuration(Phase)) Enter(HazardRules.NextPress(Phase));
    }

    private void Enter(PressPhase next)
    {
        Phase = next;
        PhaseTime = 0f;
        switch (next)
        {
            case PressPhase.Warning:
                Plate.Enabled = false;
                EventBus.Instance.PlaySfx("boss");
                EventBus.Instance.Log("Stone grinds against stone.");
                break;
            case PressPhase.Crush:
                if (!Jammed) Crush();
                break;
            case PressPhase.Retract:
                break;
            case PressPhase.Armed:
                Jammed = false;
                _gap = HazardRules.PressGap(0f);
                LayoutSlabs();
                Plate.Enabled = true;
                break;
        }
        WriteDanger();
        PhaseChanged?.Invoke(this, next);
        EventBus.Instance.RaiseHazardPhase(Index, (byte)next);
    }

    private void WriteDanger()
    {
        if (_builder == null) return;
        var value = Phase switch
        {
            PressPhase.Armed => 0f,
            PressPhase.Warning => Math.Clamp(PhaseTime / HazardRules.PressDuration(PressPhase.Warning), 0f, 1f),
            PressPhase.Closing or PressPhase.Crush or PressPhase.Hold => 1f,
            PressPhase.Retract => 0.3f,
            _ => 0f,
        };
        foreach (var c in Placement.Cells) _builder.SetDanger(c, value);
    }

    /// <summary>Is anything solid still standing in the run that stops the slabs?</summary>
    private bool JamCheck()
    {
        if (GetParent() is not { } parent) return false;
        foreach (var b in parent.GetChildren().OfType<Breakable>())
        {
            if (!b.IsAlive || !Inside(b.GlobalPosition)) continue;
            if (++_jamSqueezes >= 2) { b.Vanish(); _jamSqueezes = 0; EventBus.Instance.Log("The barrel splinters between the stones."); return false; }
            EventBus.Instance.Log("The press jams on the barrel.");
            return true;
        }
        return false;
    }
    private int _jamSqueezes;

    private bool Inside(Vector3 world)
    {
        var cell = DungeonBuilder.WorldToCell(world);
        if (!Placement.Cells.Contains(cell)) return false;
        return Math.Abs(world.Y - _centre.Y) < DungeonBuilder.LevelHeight * 0.75f; // the run's own level, not a cellar under it
    }

    private void Crush()
    {
        if (GetParent() is not { } parent) return;
        var lethality = TierRules.Lethality;
        var victims = new List<Node3D>();
        if (Main.Instance?.Party is { } party) victims.AddRange(party.Active.Where(a => Inside(a.GlobalPosition)));
        victims.AddRange(parent.GetChildren().OfType<Enemy>().Where(e => e.IsAlive && Inside(e.GlobalPosition)));
        victims.AddRange(parent.GetChildren().OfType<Npc>().Where(n => n.IsAlive && Inside(n.GlobalPosition)));
        var any = false;
        foreach (var v in victims)
        {
            if (v is not IHazardVictim victim) continue;
            victim.ReceiveHazard(HazardDamage.Crush, HazardRules.Outcome(victim.HazardVictimKind, lethality), SourceName, Index);
            any = true;
            // a body the press put down is shoved to the nearest cell outside the run, so the rescue never happens inside it
            if (v is Adventurer { Downed: true } shoved) shoved.Shove(NearestExit(v.GlobalPosition));
            else if (v is Npc { Downed: true }) v.GlobalPosition = NearestExit(v.GlobalPosition);
        }
        if (any) { Spent++; _decal.Visible = true; EventBus.Instance.PlaySfx("hit_flesh"); }
    }

    /// <summary>The surface of the walkable cell nearest to a point and outside the run (the cells before and after it).</summary>
    public Vector3 NearestExit(Vector3 from)
    {
        var layout = _builder?.Layout;
        if (layout == null) return from;
        var first = Placement.Cells[0]; var last = Placement.Cells[^1];
        var dir = Placement.Horizontal ? new GridPos(1, 0) : new GridPos(0, 1);
        var candidates = new[] { first - dir, last + dir }.Where(c => layout.Contains(c) && layout.IsWalkable(c)).ToList();
        if (candidates.Count == 0) return from;
        var best = candidates.OrderBy(c => _builder!.Surface(c).DistanceSquaredTo(from)).First();
        return _builder!.Surface(best) + Vector3.Up * 0.2f;
    }

    // ---- client side -------------------------------------------------------------------------

    private PressPhase _shownPhase = PressPhase.Armed;
    private float _shownTime;

    /// <summary>A client was told the phase the server is in and how far into it; it animates from there.</summary>
    public void ApplyPhase(PressPhase phase, float secondsIn, bool jammed, int spent)
    {
        Phase = phase; PhaseTime = secondsIn; Jammed = jammed; Spent = spent;
        _shownPhase = phase; _shownTime = secondsIn;
        _decal.Visible = spent > 0;
        if (phase == PressPhase.Armed) { _gap = HazardRules.PressGap(0f); LayoutSlabs(); }
    }

    private void Animate(float dt)
    {
        _shownTime += dt;
        PhaseTime = _shownTime;
        if (_shownPhase == PressPhase.Closing)
        {
            var gap = HazardRules.PressGap(_shownTime / HazardRules.PressDuration(PressPhase.Closing));
            _gap = Jammed ? Math.Max(gap, HazardRules.JamGap) : gap;
            LayoutSlabs();
        }
        else if (_shownPhase == PressPhase.Retract)
        {
            _gap = Mathf.Lerp(Jammed ? HazardRules.JamGap : HazardRules.PressGap(1f), HazardRules.PressGap(0f), Math.Clamp(_shownTime / HazardRules.PressDuration(PressPhase.Retract), 0f, 1f));
            LayoutSlabs();
        }
    }

    /// <summary>Restore from a save: resume the phase with its elapsed time (the kill check only ever runs on entering Crush).</summary>
    public void Restore(PressPhase phase, float phaseTime, bool jammed, int spent)
    {
        Phase = phase; PhaseTime = phaseTime; Jammed = jammed; Spent = spent;
        _decal.Visible = spent > 0;
        Plate.Enabled = phase == PressPhase.Armed;
        _gap = phase switch
        {
            PressPhase.Closing => jammed ? Math.Max(HazardRules.PressGap(phaseTime / HazardRules.PressDuration(PressPhase.Closing)), HazardRules.JamGap) : HazardRules.PressGap(phaseTime / HazardRules.PressDuration(PressPhase.Closing)),
            PressPhase.Crush or PressPhase.Hold => jammed ? HazardRules.JamGap : HazardRules.PressGap(1f),
            _ => HazardRules.PressGap(0f),
        };
        LayoutSlabs();
        WriteDanger();
    }

    public override void _ExitTree()
    {
        _builder?.ClearDanger(Placement.Cells);
    }
}
