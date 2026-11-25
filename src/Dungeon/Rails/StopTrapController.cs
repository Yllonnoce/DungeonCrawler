using DungeonCrawler.Adventure;
using DungeonCrawler.Adventure.Objectives;
using DungeonCrawler.Adventure.Voice;
using DungeonCrawler.Core;
using DungeonCrawler.Dungeon.Generation;
using DungeonCrawler.Net;
using DungeonCrawler.Party;
using Godot;

namespace DungeonCrawler.Dungeon.Rails;

/// <summary>
/// The stop trap (plan 16): what makes a cage stop a fight to win while the crew are freed. A node on every railed floor whose line
/// has three stops or more, on every role; only the server and solo act (it listens to <see cref="RailController.CartEvents"/>, which
/// a client never hears). This part is the siege (Task 3): the train's arrival at a cage stop starts <see cref="PortalSpawner"/>'s
/// stop mode with the row's first-wave delay; its departure, a wreck or the rest on foot ends it; a restore (a load, a wipe's rise)
/// ends it and, with the train held at a cage stop, starts it again from wave 1 after the restart's grace (Ruling 3). On a stop's
/// first siege Grisel, or else the nearest up freed crew member, calls a warning. The RIDE pass's server runs no siege (Ruling 2).
/// Later tasks add their parts (T4 the wall ambushers, T6 the hijack) as further files of this partial class.
/// </summary>
public partial class StopTrapController : Node
{
    public static StopTrapController? Instance { get; private set; }

    private RailController _rails = null!;
    /// <summary>The stop whose first-siege warning is still to be said, or −1.</summary>
    private int _warnStop = -1;
    private float _warnClock;

    public void Setup(DungeonBuilder builder, FloorLayout layout, RailController rails)
    {
        Instance = this;
        Name = "StopTrap";
        _rails = rails;
        _rails.CartEvents += OnCartEvent;
    }

    public override void _ExitTree()
    {
        if (IsInstanceValid(_rails)) _rails.CartEvents -= OnCartEvent;
        if (Instance == this) Instance = null;
    }

    /// <summary>This floor's portal spawner (none on a floor without one).</summary>
    private PortalSpawner? Portals => PortalSpawner.Instance is { } p && IsInstanceValid(p) && p.GetParent() == GetParent() ? p : null;

    private void OnCartEvent(CartEvent e)
    {
        if (NetSession.IsClient) return;
        switch (e.Kind)
        {
            case CartEventKind.Arrived:
                if (StopSiegeRules.Sieges(e.Stop, _rails.StopS.Length)) BeginSiege(e.Stop, StopSiegeRules.For(e.Stop).FirstAfter);
                break;
            case CartEventKind.Departed: Portals?.EndSiege("the train left"); break;
            case CartEventKind.Wrecked: Portals?.EndSiege("the cart was wrecked"); break;
            case CartEventKind.OnFoot: Portals?.EndSiege("the rest is on foot"); break;
            case CartEventKind.Restored:
                Portals?.EndSiege("the cart was restored");
                if (_rails.State.Mode == CartMode.Held && StopSiegeRules.Sieges(_rails.StopIndex, _rails.StopS.Length))
                    BeginSiege(_rails.StopIndex, StopSiegeRules.RestartGrace(Main.Instance?.RoseAtCart == true));
                break;
        }
    }

    private void BeginSiege(int stop, float firstDelay)
    {
        if (NetSession.Instance?.TestRide == true) { GD.Print($"[stoptrap] no siege at stop {stop}: the RIDE pass's server (Ruling 2)"); return; }
        if (Portals is not { } portals) return;
        portals.StartSiege(stop, firstDelay, Humans());
        if (portals.SiegeStop != stop) return; // silenced
        _warnStop = stop;
        _warnClock = 0f;
        TryWarn();
    }

    /// <summary>The heroes a human drives: solo 1; on a server, each connected player's hero (a host's own too).</summary>
    private static int Humans()
    {
        var n = 0;
        if (Main.Instance?.Party is { } party)
            foreach (var a in party.Adventurers)
                if (IsInstanceValid(a) && a.Control is ControlKind.LocalHuman or ControlKind.Remote) n++;
        return Math.Max(1, n);
    }

    public override void _PhysicsProcess(double delta)
    {
        if (NetSession.IsClient || _warnStop < 1) return;
        _warnClock += (float)delta;
        if (_warnClock < 1f) return;
        _warnClock = 0f;
        TryWarn();
    }

    /// <summary>
    /// The stop's first-siege warning (once a run per stop: the call's key <c>siege:{stop}</c>), while its siege runs: Grisel's, or when
    /// she is absent, dead or downed, the nearest up freed crew member's. Nobody to say it: tried again each second of the siege.
    /// </summary>
    private void TryWarn()
    {
        if (_warnStop < 1) return;
        if (Portals is not { } portals || portals.SiegeStop != _warnStop) { _warnStop = -1; return; }
        var root = GetParent();
        Npc? speaker = null;
        string? text = null;
        foreach (var child in root.GetChildren())
            if (child is Npc { Id: CrewRescueController.GriselId, Dead: false, Downed: false } g && IsInstanceValid(g)) { speaker = g; text = StopTrapLines.GriselSiegeWarning; break; }
        if (speaker == null && ObjectiveController.Instance is CrewRescueController crew && IsInstanceValid(crew))
        {
            var at = _rails.Cart.GlobalPosition;
            var best = float.MaxValue;
            foreach (var n in crew.Crew)
            {
                if (!IsInstanceValid(n) || n.Dead || n.Downed || crew.StateOf(n) != CrewState.Freed) continue;
                var d = n.GlobalPosition.DistanceSquaredTo(at);
                if (d < best) { best = d; speaker = n; }
            }
            if (speaker != null) text = StopTrapLines.CrewSiegeWarning;
        }
        if (speaker == null || text == null) return;
        var stop = _warnStop;
        _warnStop = -1;
        var queued = AdventureDirector.Instance?.SayAs(speaker, StopTrapLines.Say(text), $"siege:{stop}") == true;
        GD.Print($"[stoptrap] stop {stop}'s first siege: {speaker.NpcName} warns ({(queued ? "queued" : "said before, or not now")})");
    }
}
