using DungeonCrawler.Core;
using DungeonCrawler.Dungeon;
using DungeonCrawler.Dungeon.Generation;
using DungeonCrawler.Dungeon.Props;
using DungeonCrawler.Save;
using Godot;

namespace DungeonCrawler.Adventure.Objectives;

/// <summary>
/// The rising gas. The server keeps the clock and poisons whoever stands in a gassed cell once a second; clients get the
/// clock every couple of seconds and run it forward between, so the minimap and the air agree everywhere. There is
/// nothing to unlock: leaving is the objective.
/// </summary>
public partial class ToxicController : ObjectiveController
{
    public const float TickSeconds = 1f;

    public float Seconds { get; private set; }
    public GasField Field { get; private set; } = null!;
    private float _tick;
    private int _worstSeen;

    public override void Setup(FloorLayout layout, ObjectiveContents contents, int floor, int seed)
    {
        base.Setup(layout, contents, floor, seed);
        Field = GasField.Build(layout);
    }

    /// <summary>The gas level where a body stands, 0..1.</summary>
    public float LevelAt(Vector3 world) => Field.LevelAt(DungeonBuilder.WorldToCell(world), Seconds);
    public float LevelAt(GridPos cell) => Field.LevelAt(cell, Seconds);
    public int DangerAt(GridPos cell) => Field.DangerAt(cell, Seconds);

    /// <summary>Seconds until the cell the party stands in starts to gas (0 when it already has).</summary>
    public float SecondsUntil(GridPos cell) => Math.Max(0f, Field.ArrivalAt(cell) - Seconds);

    public override string HudText
    {
        get
        {
            var anchor = Main.Instance?.Party?.Anchor;
            if (anchor == null) return "The gas is rising. The stairs lie low and far.";
            var cell = DungeonBuilder.WorldToCell(anchor.GlobalPosition);
            var danger = DangerAt(cell);
            var here = danger == 0 ? (SecondsUntil(cell) is var wait && !float.IsInfinity(wait) ? $"clear here for {wait:0} s" : "clear here") : $"{GasRules.Describe(danger)} here";
            return $"The gas is rising: {here}. The stairs lie low and far.";
        }
    }

    protected override void Tick(float dt, Main main)
    {
        Seconds += dt;
        TintAir(dt);
        _tick -= dt;
        if (_tick > 0f) return;
        _tick = TickSeconds;
        var worst = 0;
        foreach (var adv in main.Party.Active)
        {
            if (adv.Downed) continue;
            var danger = DangerAt(DungeonBuilder.WorldToCell(adv.GlobalPosition));
            worst = Math.Max(worst, danger);
            if (danger == 0) continue;
            adv.ReceiveAttack(Trap.TrapStats, GasRules.Tick(Floor, danger), "The gas");
        }
        if (worst > _worstSeen)
        {
            _worstSeen = worst;
            if (worst >= 3) { EventBus.Instance.Log("The gas is lethal here. Get out."); Raise(ObjectiveTrigger.GasLethal); }
            else if (worst == 2) { EventBus.Instance.Log("The gas thickens around you."); Raise(ObjectiveTrigger.GasThickens); }
            else EventBus.Instance.Log("A thin green haze creeps along the floor.");
        }
    }

    protected override void ClientTick(float dt) { Seconds += dt; TintAir(dt); }

    private float _tint;
    /// <summary>The air the local player breathes, twice a second.</summary>
    private void TintAir(float dt)
    {
        _tint -= dt;
        if (_tint > 0f) return;
        _tint = 0.5f;
        if (Main.Instance is not { InGame: true } main || main.Party?.Local is not { } me) return;
        main.ApplyGasTint(LevelAt(me.GlobalPosition));
    }

    public override ObjectiveStateDto Capture() { var dto = base.Capture(); dto.Seconds = Seconds; return dto; }
    public override void Restore(ObjectiveStateDto dto) { base.Restore(dto); Seconds = Math.Max(Seconds, dto.Seconds); }
    public override void ApplyNet(ObjectiveStateDto dto) { base.Restore(dto); Seconds = dto.Seconds; }
    /// <summary>Jump the clock to the moment the last room fills (tests).</summary>
    public override void Hurry() => Seconds = Math.Max(Seconds, GasRules.FullAt(Field.RoomArrivals));
}
