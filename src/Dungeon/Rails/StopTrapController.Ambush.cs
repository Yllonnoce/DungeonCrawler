using DungeonCrawler.Adventure;
using DungeonCrawler.Adventure.Objectives;
using DungeonCrawler.Core;
using DungeonCrawler.Dungeon.Generation;
using DungeonCrawler.Net;
using Godot;

namespace DungeonCrawler.Dungeon.Rails;

/// <summary>
/// The stop trap's wall ambushers (plan 16 Task 4, spec section 2). On every role the cage stops' wall points are placed
/// (<see cref="StopAmbushRules.Place"/>, seeded on the floor's seed, so every peer holds the same list) and appended to the floor's
/// <see cref="AmbushSpawner"/> after its corridor points, only when the corridor ambushes are on (Ruling 2: <c>Settings.Ambushes</c> and no
/// <c>NoAmbush</c>) and never on the RIDE pass. The server and solo arm a stop's points on a real <see cref="CartEventKind.Arrived"/> at stops 1-4
/// (a spare's takeover's arrival too, never a restore) and spring them <see cref="StopAmbushRules.SpringAfterStepOff"/> s after the first hero
/// at the stop gets out while the train is held, or <see cref="StopAmbushRules.SpringAfterArrival"/> s after the arrival if nobody does (Ruling 7).
/// A point springs once ever: its index joins the spawner's <c>Sprung</c>, saved as <c>SprungAmbushes</c>, so a revisit, a load or a wipe's
/// rise leaves it quiet. The departure, a wreck or the rest on foot before it is due disarms the stop's points unsprung.
/// </summary>
public partial class StopTrapController
{
    /// <summary>The spawner's index of the first stop-wall point, or −1 when none were placed (ambushes off, the RIDE pass, no spawner).</summary>
    public int AmbushFirst { get; private set; } = -1;
    /// <summary>The stop-wall points placed on this floor, in stop order.</summary>
    public IReadOnlyList<AmbushPoint> AmbushPoints { get; private set; } = Array.Empty<AmbushPoint>();
    /// <summary>The stop whose walls are armed (arrived at, not yet sprung), or −1.</summary>
    public int AmbushArmedStop => _ambushStop;

    private AmbushSpawner? _ambushes;
    private int _ambushStop = -1;
    private float _ambushSinceArrival;
    /// <summary>Seconds since the first hero got out at the armed stop; NaN while nobody has.</summary>
    private float _ambushSinceStepOff = float.NaN;

    /// <summary>The wall points, after the cages stand (their cells keep the points clear), on every role.</summary>
    private void BuildAmbushes(DungeonBuilder builder, FloorLayout layout)
    {
        if (!Settings.Ambushes || DebugTools.Instance?.NoAmbush == true) return; // Ruling 2: only where the corridor ambushes are
        if (NetSession.Instance?.TestRide == true) { GD.Print("[ambush] no stop-wall ambushers: the RIDE pass (Ruling 2)"); return; }
        if (layout.Rail is not { } rail || AmbushSpawner.Instance is not { } spawner || !IsInstanceValid(spawner) || spawner.GetParent() != builder.FloorRoot) return;
        var cages = new Dictionary<int, GridPos>();
        if (ObjectiveController.Instance is CrewRescueController crew && IsInstanceValid(crew) && crew.GetParent() == builder.FloorRoot)
            foreach (var c in crew.Cages) if (IsInstanceValid(c)) cages[c.Stop] = c.Cell;
        var points = StopAmbushRules.Place(layout, rail.Stops, cages, layout.Seed);
        _ambushes = spawner;
        AmbushPoints = points;
        AmbushFirst = spawner.AddStopPoints(points);
        GD.Print($"[ambush] stop walls: {points.Count} point(s) from index {AmbushFirst}: [{string.Join(", ", points.GroupBy(p => p.Stop).Select(g => $"stop {g.Key}: {g.Count()}"))}]");
    }

    /// <summary>The floor's spawner, while it stands.</summary>
    private AmbushSpawner? Ambushes => _ambushes != null && IsInstanceValid(_ambushes) ? _ambushes : null;

    /// <summary>The cart's events as the wall points hear them (server and solo; called from <see cref="OnCartEvent"/>).</summary>
    private void OnAmbushCartEvent(CartEvent e)
    {
        switch (e.Kind)
        {
            case CartEventKind.Arrived:
                _ambushStop = -1;
                if (StopAmbushRules.Count(e.Stop) == 0 || Ambushes is not { } spawner || !HasUnsprung(spawner, e.Stop)) return;
                _ambushStop = e.Stop;
                _ambushSinceArrival = 0f;
                _ambushSinceStepOff = float.NaN;
                break;
            case CartEventKind.Unseated:
                if (_ambushStop < 1 || !float.IsNaN(_ambushSinceStepOff) || !IsHero(e.Rider)) return;
                if (_rails.State.Mode != CartMode.Held || _rails.StopIndex != _ambushStop) return;
                _ambushSinceStepOff = 0f;
                break;
            case CartEventKind.Departed or CartEventKind.Wrecked or CartEventKind.OnFoot or CartEventKind.Restored:
                _ambushStop = -1; // the train left (or was put back) before the walls opened: they wait for a real arrival
                break;
        }
    }

    private static bool HasUnsprung(AmbushSpawner spawner, int stop)
    {
        for (var i = 0; i < spawner.Points.Count; i++)
            if (spawner.Points[i].Stop == stop && !spawner.Sprung.Contains(i)) return true;
        return false;
    }

    /// <summary>The rider is one of the party's heroes (a crew member or Grisel stepping out is not a hero stepping off).</summary>
    private static bool IsHero(int riderId)
    {
        if (Main.Instance?.Party is not { } party) return false;
        foreach (var a in party.Adventurers)
            if (IsInstanceValid(a) && a.RiderId == riderId) return true;
        return false;
    }

    /// <summary>Each physics tick on the server and in solo: the armed stop's clocks, and its walls burst when due.</summary>
    private void TickAmbush(float dt)
    {
        if (_ambushStop < 1) return;
        _ambushSinceArrival += dt;
        if (!float.IsNaN(_ambushSinceStepOff)) _ambushSinceStepOff += dt;
        if (!StopAmbushRules.Due(_ambushSinceArrival, _ambushSinceStepOff)) return;
        var stop = _ambushStop;
        _ambushStop = -1;
        if (Ambushes is not { } spawner) return;
        var stepped = !float.IsNaN(_ambushSinceStepOff);
        var n = spawner.SpringStop(stop);
        GD.Print(FormattableString.Invariant($"[ambush] stop {stop}: {n} burst from the walls {(stepped ? _ambushSinceStepOff : _ambushSinceArrival):0.0} s after {(stepped ? "the first step-off" : "the arrival")}"));
    }
}
