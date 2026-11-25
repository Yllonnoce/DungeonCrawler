using DungeonCrawler.Adventure;
using DungeonCrawler.Dungeon.Generation;
using DungeonCrawler.Dungeon.Rails;
using DungeonCrawler.Party;
using Godot;

namespace DungeonCrawler.Core;

// ---- --test-stop-trap[=siege|ambush|roll|wonback|gotaway|limits|pockets|alcove] (plan 16) ---------------------------------------
// The stop trap's flows, one variant a run, each added by the task that builds it (T3: =siege, the default; T4: =ambush). Every variant runs on the
// built-in campaign's floor 12 at lethality 1 (ForcedLethality ??= 1), with no gathering and no AI trace; every hero is topped up to full
// HP each second (a downed one stood up first) unless the flow is taking them down. NoPortals is off for =siege, =wonback and =gotaway;
// NoAmbush is off for =ambush, =pockets and =alcove (Ruling 22). The cull frees (kills, as the other floor-12 flows do) every enemy but the
// variant's own: siege creatures, stop guards and the stop walls' ambushers here; the later tasks spare their hijackers and alcove shooters. Prints
// [stoptrap] lines and [stoptrap] RESULT ok ({variant}) or [stoptrap] RESULT FAIL ({variant}): {why}.
public partial class DebugTools
{
    private bool _testStopTrap;
    private string _stVariant = "siege";
    private int _stStep;
    private float _stClock, _stTopUp;
    /// <summary>The top-up is off (the flow takes the party down on purpose).</summary>
    private bool _stNoTopUp;
    private int _stRevived;
    /// <summary>The variants built so far (each task adds its own).</summary>
    private static readonly string[] StopTrapVariants = { "siege", "ambush" };

    /// <summary>The flag: the variant, its switches and its budget (Ruling 22).</summary>
    private void StopTrapArgs(string arg)
    {
        var v = arg.Contains('=') ? arg[(arg.IndexOf('=') + 1)..].Trim().ToLowerInvariant() : "siege";
        _testStopTrap = true; _stVariant = v; _builtin = true; _startFloor = 12; _floorGiven = true; ForcedLethality ??= 1f; NoGathering = true; TraceAi = false;
        NoPortals = v is not ("siege" or "wonback" or "gotaway");
        NoAmbush = v is not ("ambush" or "pockets" or "alcove");
        _quitAfter = v switch { "siege" => 330, "ambush" => 200, "roll" => 200, "wonback" => 260, "gotaway" => 300, "limits" => 200, "pockets" => 30, "alcove" => 360, _ => 30 };
    }

    /// <summary>Physics time (seconds of fixed ticks since start-up): the siege's own clock, frame for frame.</summary>
    private static double StNow => Engine.GetPhysicsFrames() / (double)Engine.PhysicsTicksPerSecond;

    private void StopTrapFail(string why)
    {
        if (_done) return;
        GD.Print($"[stoptrap] RESULT FAIL ({_stVariant}): {why}");
        StopTrapRestore();
        _done = true;
        GetTree().Quit(1);
    }

    private void StopTrapOk(string summary)
    {
        if (_done) return;
        GD.Print($"[stoptrap] RESULT ok ({_stVariant}){(summary.Length > 0 ? $": {summary}" : "")}");
        StopTrapRestore();
        _done = true;
        GetTree().Quit(0);
    }

    /// <summary>What a variant changed in memory goes back (the setting it moved).</summary>
    private void StopTrapRestore() { if (_stReinforcementsWas is { } was) Settings.Reinforcements = was; }
    /// <summary>The setting as the flow found it (null until a variant reads it: an early failure leaves the setting alone).</summary>
    private float? _stReinforcementsWas;

    private void RunStopTrapTest()
    {
        var main = Main.Instance;
        if (_stStep == 0 && _stClock == 0f && Array.IndexOf(StopTrapVariants, _stVariant) < 0) { StopTrapFail($"unknown or not yet built variant \"{_stVariant}\" ({string.Join(", ", StopTrapVariants)})"); return; }
        if (main?.Builder.Layout is not { } layout || main.Builder.FloorRoot is not { } root || !main.InGame || Game.Instance.CurrentFloor != 12 || main.Rebuilding) return;
        var dt = (float)GetProcessDeltaTime();
        _stClock += dt;
        HoldTheRunaway("stoptrap"); // not this flow's business
        if (RailController.Instance is not { } rc || !IsInstanceValid(rc) || rc.GetParent() != root) { if (_stClock > 3f) StopTrapFail("floor 12 has no line"); return; }
        if (PortalSpawner.Instance is not { } portals || !IsInstanceValid(portals) || portals.GetParent() != root) { if (_stClock > 3f) StopTrapFail("floor 12 has no portal spawner"); return; }
        StopTrapTopUp(main.Party, dt);
        StopTrapCull(main, layout, root, portals);
        switch (_stVariant)
        {
            case "siege": RunStopTrapSiege(main, layout, root, rc, portals); break;
            case "ambush": RunStopTrapAmbush(main, layout, root, rc, portals); break;
        }
    }

    /// <summary>Every second, every hero back to full HP (a downed one, not fallen, stood up first): nobody is lost to the flow's crowd.</summary>
    private void StopTrapTopUp(PartyController party, float dt)
    {
        if (_stNoTopUp || (_stTopUp -= dt) > 0f) return;
        _stTopUp = 1f;
        foreach (var a in party.Adventurers)
        {
            if (!IsInstanceValid(a) || a.Parked || a.State.Fallen) continue;
            if (a.Downed) { a.Revive(); _stRevived++; }
            a.State.Hp = a.State.Stats[Characters.Stat.MaxHp];
        }
    }

    /// <summary>
    /// Every frame: every living enemy that is not the variant's own dies by the test's hand, as the floor-12 flows cull (the floor's
    /// own, the rides' set pieces, a floor-wide portal's, a corridor ambusher): the stop guards (they hold the gates), the siege's creatures and the
    /// stop walls' ambushers stay.
    /// </summary>
    private void StopTrapCull(Main main, FloorLayout layout, Node3D root, PortalSpawner portals)
    {
        if (_stStep == 0) return;
        _stCull.Clear();
        var walls = root.GetChildren().OfType<AmbushSpawner>().FirstOrDefault();
        foreach (var e in Enemies.Enemy.Live)
        {
            if (!IsInstanceValid(e) || !e.IsAlive || e.GetParent() != root) continue;
            if (portals.IsSiegeCreature(e) || HoldsAStop(main, layout, e) || walls?.IsStopAmbusher(e) == true) continue;
            _stCull.Add(e);
        }
        foreach (var e in _stCull) e.Kill();
    }
    private readonly List<Enemies.Enemy> _stCull = new();

    /// <summary>The heroes up now, by cell (the sites' clearances are judged from these, as the siege picks them).</summary>
    private static List<GridPos> StopTrapHeroCells(Main main)
        => main.Party.Active.Where(a => !a.Downed).Select(a => Dungeon.DungeonBuilder.WorldToCell(a.GlobalPosition)).ToList();

    /// <summary>Cell centre to cell centre, in metres (the siege's own measure).</summary>
    private static float StopTrapMetres(GridPos a, GridPos b) => Mathf.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y)) * 4f;
}
