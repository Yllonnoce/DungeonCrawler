using DungeonCrawler.Core;
using Godot;

namespace DungeonCrawler.Net;

/// <summary>
/// The server's stairs ready-check (co-op, pillar 7). With two or more players online a Yes on a landing starts a vote
/// (<see cref="ReadyCheck"/>); every HUD reads "Descend? n/m ready · s" from <see cref="StairsReadyMsg"/>; the floor
/// changes when all living players are ready or 15 s pass with a majority; a No calls it off. One player online takes
/// the stairs at once, as a solo game does.
/// </summary>
public partial class ServerWorld
{
    public ReadyCheck StairsCheck { get; } = new();
    private Dungeon.Stairs? _readyStairs;
    private int _readyDirection;

    /// <summary>
    /// A player's Yes or No on <paramref name="stairs"/>. True when the ready-check took the decision (two or more online);
    /// false when the caller should change floor itself (one player online).
    /// </summary>
    public bool StairsVote(Dungeon.Stairs stairs, int member, bool yes)
    {
        if (!ReadyCheckRules.Needed(Registry.OnlineCount)) { Trace.Add("stairs:immediate"); return false; }
        var party = Main.Instance?.Party;
        if (party == null) return false;
        var netRef = NetRefResolver.RefOf(stairs) ?? "";
        var name = Registry.ByMember(member)?.Name ?? (member >= 0 && member < party.Members.Length ? party.Members[member].Name : "Someone");
        var living = LivingPlayers();
        var now = NetSession.Instance.Now;
        var startsNew = !StairsCheck.Active || StairsCheck.StairsRef != netRef;
        if (!living.Contains(member)) return true; // a downed or absent player neither starts a check nor vetoes one
        if (!yes && (!StairsCheck.Active || StairsCheck.StairsRef != netRef)) return true; // a No answers the running check on these stairs, or is nothing
        var verb = stairs.Direction < 0 ? "climb" : "descend";
        var verdict = StairsCheck.Vote(member, yes, netRef, stairs.Direction, living, now);
        if (yes)
        {
            _readyStairs = stairs;
            _readyDirection = stairs.Direction;
            if (verdict == ReadyVerdict.Pending)
            {
                var count = StairsCheck.ReadyCount(living);
                if (startsNew) { Trace.Add("ready-check"); EventBus.Instance.Log($"{name} is ready to {verb}. Stand on the landing and say yes."); }
                else EventBus.Instance.Log($"{name} is ready ({count}/{living.Count}).");
                BroadcastReady(count, living.Count, Mathf.CeilToInt(StairsCheck.SecondsLeft(now)), stairs.Direction);
                return true;
            }
        }
        else EventBus.Instance.Log($"{name} is not ready. The {(stairs.Direction < 0 ? "climb" : "descent")} is called off.");
        OnReadyVerdict(verdict);
        return true;
    }

    /// <summary>Once a second while a vote runs: the HUD count, and the verdict at 15 s.</summary>
    private void TickReadyCheck(float dt)
    {
        if (!StairsCheck.Active) return;
        if (_readyStairs == null || !GodotObject.IsInstanceValid(_readyStairs)) { StairsCheck.Clear(); OnReadyVerdict(ReadyVerdict.Cancelled); return; }
        var living = LivingPlayers();
        var now = NetSession.Instance.Now;
        var verdict = StairsCheck.Tick(living, now);
        if (verdict == ReadyVerdict.Pending)
        {
            if (NetSession.Instance.Tick % 60 == 0) BroadcastReady(StairsCheck.ReadyCount(living), living.Count, Mathf.CeilToInt(StairsCheck.SecondsLeft(now)), _readyDirection);
            return;
        }
        if (verdict == ReadyVerdict.Cancelled) EventBus.Instance.Log($"Nobody agreed. The {(_readyDirection < 0 ? "climb" : "descent")} is called off.");
        OnReadyVerdict(verdict);
    }

    private void OnReadyVerdict(ReadyVerdict v)
    {
        var stairs = _readyStairs;
        _readyStairs = null;
        BroadcastReady(0, 0, 0, _readyDirection);
        if (v != ReadyVerdict.Go) { if (v == ReadyVerdict.Cancelled) Trace.Add("ready-cancel"); return; }
        if (stairs == null || !GodotObject.IsInstanceValid(stairs)) { Trace.Add("ready-cancel"); return; }
        if (stairs.Locked) { EventBus.Instance.Log(stairs.LockReason); Trace.Add("ready-cancel"); return; } // sealed again since the first Yes
        Trace.Add("ready-go");
        Game.Instance.ChangeFloor(stairs.Direction);
    }

    // ---- pings -------------------------------------------------------------------------------------------
    private readonly Dictionary<int, double> _lastPingAt = new();

    /// <summary>A client's ping: refused inside the 1 s cooldown or farther than <see cref="PingRules.MaxRangeFromBody"/> from its body; otherwise everyone gets the mark.</summary>
    private void HandlePing(PlayerSlot slot, Party.Adventurer body, PingIntent pi)
    {
        var now = NetSession.Instance.Now;
        var at = new Vector3(pi.At.X, pi.At.Y, pi.At.Z);
        if (!PingRules.CanPing(_lastPingAt.GetValueOrDefault(slot.MemberIndex, -1), now)) return;
        if (!PingRules.Accepts(at.DistanceTo(body.GlobalPosition))) return;
        _lastPingAt[slot.MemberIndex] = now;
        Broadcast(new PingEvt(slot.Name, pi.At));
        Trace.Add("ping");
    }

    private void BroadcastReady(int count, int total, int secondsLeft, int direction)
    {
        Broadcast(new StairsReadyMsg(count, total, secondsLeft, direction));
        if (Main.Instance?.Hud is { } hud) { if (total == 0) hud.HideReadyCheck(); else hud.ShowReadyCheck(count, total, secondsLeft, direction); }
    }

    /// <summary>The players whose vote counts: online, on this floor, with a standing body (a downed player is not waited for).</summary>
    private IReadOnlyCollection<int> LivingPlayers()
    {
        var party = Main.Instance?.Party;
        if (party == null) return Array.Empty<int>();
        var living = new List<int>();
        foreach (var s in Registry.Online)
        {
            if (!s.Ready || s.MemberIndex < 0 || s.MemberIndex >= party.Adventurers.Length) continue;
            var body = party.Adventurers[s.MemberIndex];
            if (!GodotObject.IsInstanceValid(body) || body.Parked || body.Downed) continue;
            living.Add(s.MemberIndex);
        }
        return living;
    }
}
