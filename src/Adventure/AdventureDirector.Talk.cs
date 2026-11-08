using DungeonCrawler.Adventure.Voice;
using DungeonCrawler.Core;
using DungeonCrawler.Dungeon.Generation;
using Godot;

namespace DungeonCrawler.Adventure;

/// <summary>
/// The party's talk beyond the kill and the grumble: the Depths update's moments wired to barks and taunts, the run
/// flags and gag counters the banter reads, the banter player, the cheer, and the wipe's cause and epitaph. Every
/// handler speaks only on the server or in solo: a client hears what the server picked as a BarkMsg.
/// </summary>
public partial class AdventureDirector
{
    private readonly LowHpWatch _lowHp = new();

    private static bool Authority => !Net.NetSession.IsClient;
    private static FloorObjectiveKind Kind => Main.Instance?.Builder.Layout?.Objective?.Kind ?? FloorObjectiveKind.None;

    private void WireTalk(EventBus bus)
    {
        bus.TrapSprung += i => { if (Authority) Bark(Member(i), BarkTrigger.TrapSprung); };
        bus.MemberFell += i => { if (Authority) Bark(Member(i), BarkTrigger.Fell); };
        bus.PerfectBlow += i => { if (Authority) Bark(Member(i), BarkTrigger.PerfectBlow); };
        bus.Parried += i => { if (Authority) Bark(Member(i), BarkTrigger.Parried); };
        bus.PerfectBlock += i => { if (Authority) Bark(Member(i), BarkTrigger.Parried); }; // a perfect block shares the parry bank (the spec lists no PerfectBlock bark)
        bus.MimicSprung += at => { if (!Authority) return; Bark(Nearest(at, 12f), BarkTrigger.Mimic); SetRunFlag("seen_mimic"); Taunt(Objectives.TauntTrigger.MimicSprung, Kind); };
        bus.SecretFound += _ => { if (Authority) Bark(Main.Instance?.Party?.Lead.State, BarkTrigger.SecretFound); };
        bus.PropChanged += (r, what) =>
        {
            if (!Authority || what != "opened") return;
            if (r.StartsWith(Net.NetRefs.SecretKind + ":")) EventBus.Instance.RaiseSecretFound(_currentFloor); // until fortune's plan raises it from the wall itself
            else if (r.StartsWith(Net.NetRefs.DoorKind + ":")) Bump(BanterRules.DoorsCounter);
        };
        bus.EliteSeen += e => { if (Authority) Bark(Nearest(e.GlobalPosition, 15f), BarkTrigger.EliteSeen); };
        bus.EliteSlain += e => { if (Authority) Taunt(Objectives.TauntTrigger.EliteSlain, Kind, arg: e.DisplayName); };
        bus.BrokeAtShop += i => { if (Authority) Bark(Member(i), BarkTrigger.Broke); };
        bus.ShrineUsed += (i, boon) => { if (!Authority) return; Bark(Member(i), boon ? BarkTrigger.ShrineBoon : BarkTrigger.ShrineCurse); SetRunFlag(boon ? "took_boon" : "took_curse"); Taunt(Objectives.TauntTrigger.ShrineUsed, Kind, arg: Member(i)?.Name); };
        bus.BoonTaken += _ => { if (Authority) SetRunFlag("took_boon"); };
        bus.CurseTaken += _ => { if (Authority) SetRunFlag("took_curse"); };
        bus.Gambled += (i, win) => { if (!Authority) return; Bark(Member(i), win ? BarkTrigger.GambleWin : BarkTrigger.GambleDud); SetRunFlag("gambled"); Taunt(Objectives.TauntTrigger.Gamble, Kind); };
        bus.MemberDamaged += (i, _) => { if (Authority && Member(i) is { } m && _lowHp.Note(i, m.Hp, m.Stats[Characters.Stat.MaxHp], _currentFloor)) EventBus.Instance.RaiseLowHp(i); };
        bus.LowHp += i => { if (Authority) Bark(Member(i), BarkTrigger.LowHp); };
        bus.NpcEvent += (id, k) =>
        {
            if (!Authority || !KillBarks.IsFighter(id)) return;
            if (k == NpcEventKind.Died) Taunt(Objectives.TauntTrigger.AllyDead, Kind, arg: FindNpc(id)?.NpcName ?? id);
            else if (k == NpcEventKind.Refused) Taunt(Objectives.TauntTrigger.AllyLeft, Kind, arg: FindNpc(id)?.NpcName ?? id);
        };
        bus.FloorBuilt += (_, _, _) => _lowHp.Reset();
        bus.PartyWiped += () => { if (Authority) SetRunFlag("wiped_once"); };
    }

    private void TickTalk()
    {
    }

    /// <summary>A flag the run sets for itself (a mimic seen, a wipe, a shrine, a gamble): banter gates on it; unlike <see cref="SetFlag"/> it is nobody's choice and goes in no record.</summary>
    public void SetRunFlag(string flag)
    {
        if (Memory.Flags.Contains(flag)) return;
        Memory.Flags.Add(flag);
        GD.Print($"[Adventure] run flag: {flag}");
    }

    private static Dictionary<string, int> GagCounts => Game.Instance.Adventure.GagCounts;

    /// <summary>One more on a running-gag counter (doors opened, quiet rooms, puddles named).</summary>
    private static void Bump(string counter) => GagCounts[counter] = GagCounts.GetValueOrDefault(counter) + 1;

    /// <summary>The living, present body nearest a point within so many metres, else the lead: who remarks on a thing that happened somewhere.</summary>
    private static Characters.CharacterState? Nearest(Vector3 at, float within)
    {
        var party = Main.Instance?.Party;
        if (party == null || !party.HasBodies) return null;
        var near = party.Active.Where(a => !a.Downed).OrderBy(a => a.GlobalPosition.DistanceSquaredTo(at)).FirstOrDefault();
        return near != null && near.GlobalPosition.DistanceTo(at) <= within ? near.State : party.Lead.State;
    }
}
