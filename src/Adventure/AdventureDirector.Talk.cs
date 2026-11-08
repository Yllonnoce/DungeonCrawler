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
    private bool _floorSettled; // true from a floor's build until the next floor change: prop events before that are the save being restored

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
        bus.FloorChanged += (_, _) => _floorSettled = false; // a floor's saved state is re-applied through the same prop events: not the party's doing
        bus.PropChanged += (r, what) =>
        {
            if (!Authority || !_floorSettled || what != "opened") return;
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
        bus.MemberDamaged += (i, _) => { if (Authority && Member(i) is { } m && _lowHp.Due(i, m.Hp, m.Stats[Characters.Stat.MaxHp], _currentFloor)) EventBus.Instance.RaiseLowHp(i); };
        bus.LowHp += i => { if (Authority && Member(i) is { } m && Bark(m, BarkTrigger.LowHp)) _lowHp.Note(i, m.Hp, m.Stats[Characters.Stat.MaxHp], _currentFloor); }; // noted once said: a line held back by a gap comes at the next hit
        bus.NpcEvent += (id, k) =>
        {
            if (!Authority || !KillBarks.IsFighter(id)) return;
            if (k == NpcEventKind.Died) Taunt(Objectives.TauntTrigger.AllyDead, Kind, arg: FindNpc(id)?.NpcName ?? id);
            else if (k == NpcEventKind.Refused) Taunt(Objectives.TauntTrigger.AllyLeft, Kind, arg: FindNpc(id)?.NpcName ?? id);
        };
        bus.FloorBuilt += (_, _, _) => { _lowHp.Reset(); _floorSettled = true; };
        bus.PartyWiped += () => { if (Authority) SetRunFlag("wiped_once"); };
        bus.SafeRoomChanged += inside => { _safeRoomSince = inside ? Game.Instance.PlaySeconds : -1; _safeRoomBantered = false; };
        bus.Cheer += OnCheer;
        // what might kill the party, noted as it happens, so a wipe can say what did
        bus.MemberKilledByHazard += (_, src) => NoteDeathCause(WipeCause.FromHazardSource(src));
        bus.ObjectiveEvent += (_, t, _) => { if (t == Objectives.ObjectiveTrigger.GasLethal) NoteDeathCause(WipeCause.Gas); };
        bus.HordeTriggered += _ => NoteDeathCause(WipeCause.Horde);
        bus.MimicSprung += _ => NoteDeathCause(WipeCause.Mimic);
        bus.EliteSeen += _ => NoteDeathCause(WipeCause.Elite);
        bus.MemberFell += _ => NoteDeathCause(WipeCause.Fall);
        bus.BossSeen += _ => _bossAlive = true;
        bus.BossDefeated += _ => _bossAlive = false;
        bus.FloorBuilt += (_, _, _) => { _causeNotes.Clear(); _bossAlive = false; };
        bus.PartyWiped += OnPartyWipedForEpitaph; // wired after _Ready's own handlers, so the record has counted this wipe
    }

    // ---- the wipe's cause and epitaph ----
    /// <summary>What a wipe came to: the floor, the inferred cause (a <see cref="WipeCause"/> id), the wipe count on that floor, and the death card's line.</summary>
    public sealed record WipeInfo(int Floor, string Cause, int Count, string Epitaph);
    /// <summary>The last wipe's card, set as <c>PartyWiped</c> fires (run-structure's death card reads it on <c>PartyWipedBy</c>).</summary>
    public WipeInfo? LastWipe { get; private set; }
    private readonly List<(string Cause, double At)> _causeNotes = new();
    private bool _bossAlive;
    private const int MaxCauseNotes = 16;

    /// <summary>Something that could kill the party just happened (a bomber's blast, an elite's blow, the press, the gas): the wipe's cause is the newest note still valid.</summary>
    public void NoteDeathCause(string cause)
    {
        _causeNotes.Add((cause, Game.Instance.PlaySeconds));
        if (_causeNotes.Count > MaxCauseNotes) _causeNotes.RemoveAt(0);
    }

    private void OnPartyWipedForEpitaph()
    {
        if (!Authority) return;
        var cause = WipeCause.Infer(_causeNotes, Game.Instance.PlaySeconds, _bossAlive);
        var count = Math.Max(1, Memory.For(_currentFloor).Wipes);
        LastWipe = new WipeInfo(_currentFloor, cause, count, Epitaphs.Pick(cause, count, _currentFloor));
        EventBus.Instance.Log(LastWipe.Epitaph);
        EventBus.Instance.RaisePartyWipedBy(_currentFloor, cause);
    }

    // ---- banter ----
    private BanterRun? _banter;
    private double _safeRoomSince = -1;
    private bool _safeRoomBantered;
    private double _tavernNext = double.MaxValue;

    /// <summary>An exchange is in flight (its lines are still coming).</summary>
    public bool BanterPlaying => _banter is { Done: false };

    /// <summary>
    /// Start an exchange (server/solo only): one chosen for the living speakers, the floor, the tier, the run's flags and
    /// gag counters, or the one named (a test's hook: its gating is ignored). Marks it said, bumps its counter, and
    /// returns whether anything started.
    /// </summary>
    public bool StartBanter(string? id = null)
    {
        if (!Authority || !Game.Instance.InGame || Main.Instance?.Party is not { } party || !party.HasBodies) return false;
        if (_banter is { Done: false }) return false; // one exchange at a time: a travel tick never cuts a safe-room or tavern one short
        var campaign = Game.Instance.Adventure.Campaign;
        var bank = BanterBank.Stock.Concat(campaign?.Banter ?? new List<BanterPlan>()).ToList();
        var living = party.Active.Where(a => !a.Downed).Select(a => a.State.Name).ToList();
        var said = Game.Instance.Adventure.BarksSaid;
        var plan = id != null
            ? bank.FirstOrDefault(b => b.Id == id)
            : BanterPicker.Choose(bank, living, _currentFloor, Dungeon.TierRules.TierIndex(Math.Max(1, _currentFloor)), Memory.Flags, said, GagCounts, _barkRng);
        if (plan == null) return false;
        if (plan.OncePerRun) BarkRepeat.SaidForever(said, BanterRules.SaidKey(plan)); else BarkRepeat.Said(said, BanterRules.SaidKey(plan), _currentFloor);
        var count = BanterPicker.CountFor(plan, GagCounts);
        if (plan.Bumps && plan.Counter != null) Bump(plan.Counter);
        _banter = new BanterRun(plan, Game.Instance.PlaySeconds, BanterRules.LineGapSeconds, count);
        GD.Print($"[Banter] {plan.Id}");
        return true;
    }

    /// <summary>A line whose words are fixed (banter, a cheer's answer): the written line when the text is untouched, else a fresh unvoiced one (a rendered token means no audio), in the trigger's delivery.</summary>
    private void SayScripted(Characters.CharacterState m, Line source, string text, BarkTrigger trigger)
    {
        var line = text == source.Text ? source : new Line(text) { Delivery = source.Delivery };
        line.Delivery ??= Deliveries.Id(Voices.BarkDelivery(trigger));
        Say(m.Name, line);
    }

    private void TickTalk()
    {
        if (!Authority || !Game.Instance.InGame || Main.Instance is not { } main || main.Party is not { } party || !party.HasBodies) return;
        var now = Game.Instance.PlaySeconds;
        var living = party.Active.Where(a => !a.Downed).Select(a => a.State.Name).ToList();
        // (a) the exchange in flight: its next line when due, dropped the moment a speaker is down or a fight starts
        if (_banter != null)
        {
            if (Audio.MusicDirector.AnyoneFighting(main)) { _banter.Abort(); _banter = null; }
            else
            {
                if (_banter.Next(now, name => living.Contains(name)) is { } next && party.Members.FirstOrDefault(m => m.Name == next.Member) is { } who)
                    SayScripted(who, _banter.Plan.Lines[_banter.Index - 1].Line, next.Text, BarkTrigger.Travel);
                if (_banter.Done) _banter = null;
            }
        }
        // (b) a rest in the safe room earns one exchange
        if (_safeRoomSince >= 0 && !_safeRoomBantered && now - _safeRoomSince >= BanterRules.SafeRoomRestSeconds && _banter == null && StartBanter()) _safeRoomBantered = true;
        // (c) the tavern: one soon after arriving, then now and again, never over the merchant's talk or his shop
        if (_currentFloor == 0 && now >= _tavernNext && _banter == null && main.Hud is { DialogueOpen: false, ShopOpen: false })
        {
            StartBanter();
            _tavernNext = now + BanterRules.TavernRepeatSeconds;
        }
        // (d) the cheer's answers, one each, as their moments come
        for (var i = _cheerAnswers.Count - 1; i >= 0; i--)
        {
            var (at, member) = _cheerAnswers[i];
            if (at > now) continue;
            _cheerAnswers.RemoveAt(i);
            if (Member(member) is { IsAlive: true } m && living.Contains(m.Name) && Bark(m, BarkTrigger.CheerAnswer, scripted: true)) CheerBody(member);
        }
    }

    // ---- the cheer ----
    private double _lastCheer = -100;
    private readonly List<(double At, int Member)> _cheerAnswers = new();

    /// <summary>A member pressed Cheer (server/solo): their line, their body's cheer, and every other living hero's answer in turn; once per <see cref="CheerRules.CooldownSeconds"/>.</summary>
    private void OnCheer(int member)
    {
        if (!Authority || !Game.Instance.InGame || Main.Instance?.Party is not { } party) return;
        var now = Game.Instance.PlaySeconds;
        if (now - _lastCheer < CheerRules.CooldownSeconds) return;
        var m = Member(member);
        if (m is not { IsAlive: true }) return;
        _lastCheer = now;
        Bark(m, BarkTrigger.Cheer, scripted: true);
        CheerBody(member);
        var living = party.Active.Where(a => !a.Downed).Select(a => a.State.Name).ToList();
        var i = 0;
        foreach (var who in CheerRules.Answerers(living, m.Name))
        {
            var index = Array.FindIndex(party.Members, x => x.Name == who);
            if (index >= 0) _cheerAnswers.Add((now + CheerRules.AnswerDelay(i++), index));
        }
    }

    /// <summary>The body's Cheer clip here and, from a server, on every client.</summary>
    private static void CheerBody(int member)
    {
        var party = Main.Instance?.Party;
        if (party == null || member < 0 || member >= party.Adventurers.Length) return;
        party.Adventurers[member].PlayCheer();
        if (Net.NetSession.IsServer) Net.NetSession.Instance.Server?.Broadcast(new Net.EmoteEvt(member, "cheer"));
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
