using DungeonCrawler.Save.Profile;

namespace DungeonCrawler.Adventure.Run;

/// <summary>What the game tells the tracker. Floor, Amount, Flag and At mean what each signal says.</summary>
public enum DeedSignal
{
    StoryCleared, HeatClear, FloorReached, PressJammed, GeraldNamed, EliteShattered, Wipe, FloorClosed, SecretFound, FloorSecretsComplete, MimicSprung,
    CurseTaken, ShrineBoon, Gamble, PerfectBlock, Parried, BossSeen, BossDown, EliteSlain, Rescue, DailyDone,
}

/// <summary>One signal: StoryCleared's Flag is "under the Death Clause"; HeatClear's Amount the heat; FloorClosed's Amount the seconds and its Flag "left by the down stairs" (cleared); BossSeen/BossDown's At the clock.</summary>
public sealed record DeedEvent(DeedSignal Signal, int Floor = 0, int Amount = 0, bool Flag = false, double At = 0);

/// <summary>
/// Turns signals into earned deeds, writing them (and their unlock keys) into the profile. Per-run counters live here and
/// clear with <see cref="NewRun"/>; all-time counters live in <c>profile.Counters</c>. Engine-free.
/// </summary>
public sealed class DeedTracker
{
    private readonly ProfileDto _profile;
    private readonly Dictionary<int, int> _wipesOnFloor = new();
    private int _secrets, _mimics, _boons, _dips, _blocks, _parries, _elites;
    private double _bossSeenAt = double.NegativeInfinity;

    public DeedTracker(ProfileDto profile) { _profile = profile; }

    /// <summary>A new run: the per-run counters start over.</summary>
    public void NewRun()
    {
        _wipesOnFloor.Clear();
        _secrets = _mimics = _boons = _dips = _blocks = _parries = _elites = 0;
        _bossSeenAt = double.NegativeInfinity;
    }

    /// <summary>The deeds this signal earns that were not earned before, written into the profile.</summary>
    public IReadOnlyList<DeedDef> Note(DeedEvent e)
    {
        var earned = new List<DeedDef>();
        void Earn(string id) { if (Grant(id) is { } d) earned.Add(d); }
        switch (e.Signal)
        {
            case DeedSignal.StoryCleared: Earn("plumber"); if (e.Flag) Earn("signed_in_blood"); break;
            case DeedSignal.HeatClear: if (e.Amount >= DeedRules.SmallPrintHeat) Earn("small_print"); if (e.Amount >= DeedRules.WholeContractHeat) Earn("whole_contract"); break;
            case DeedSignal.FloorReached: if (e.Floor >= DeedRules.BelowTheLeaseFloor) Earn("below_the_lease"); if (e.Floor >= DeedRules.DeepTenantFloor) Earn("deep_tenant"); break;
            case DeedSignal.PressJammed: Earn("jammed_it"); break;
            case DeedSignal.GeraldNamed: if (Bump(DeedRules.GeraldTotal) >= DeedRules.GeraldsWidowCount) Earn("geralds_widow"); break;
            case DeedSignal.EliteShattered: Earn("load_bearing"); break;
            case DeedSignal.Wipe: _wipesOnFloor[e.Floor] = _wipesOnFloor.GetValueOrDefault(e.Floor) + 1; if (_wipesOnFloor[e.Floor] >= DeedRules.BangOnScheduleWipes) Earn("bang_on_schedule"); break;
            case DeedSignal.FloorClosed: if (e.Flag && e.Floor >= DeedRules.ExpressLodgingsFloor && e.Amount < DeedRules.ExpressLodgingsSeconds) Earn("express_lodgings"); break; // cleared: the party took the stairs down
            case DeedSignal.SecretFound: if (++_secrets >= DeedRules.WainscotSecrets) Earn("behind_the_wainscot"); break;
            case DeedSignal.FloorSecretsComplete: Earn("nosy"); break;
            case DeedSignal.MimicSprung: if (++_mimics >= DeedRules.BitFirstMimics) Earn("bit_first"); break;
            case DeedSignal.CurseTaken: Earn("sin_eater"); break;
            case DeedSignal.ShrineBoon: if (++_boons >= DeedRules.DevoutShrines) Earn("devout"); break;
            case DeedSignal.Gamble: if (++_dips >= DeedRules.PelletsDips) Earn("pellets_favourite"); break;
            case DeedSignal.PerfectBlock: if (++_blocks >= DeedRules.IronMannersBlocks) Earn("iron_manners"); break;
            case DeedSignal.Parried: if (++_parries >= DeedRules.RiposteParries) Earn("riposte_society"); break;
            case DeedSignal.BossSeen: _bossSeenAt = e.At; break;
            case DeedSignal.BossDown: if (e.At - _bossSeenAt <= DeedRules.ShortAudienceSeconds) Earn("short_audience"); _bossSeenAt = double.NegativeInfinity; break;
            case DeedSignal.EliteSlain: if (++_elites >= DeedRules.TenantEvictionElites) Earn("tenant_eviction"); if (Bump(DeedRules.ElitesTotal) >= DeedRules.LandlordElites) Earn("landlord"); break;
            case DeedSignal.Rescue: if (Bump(DeedRules.RescuesTotal) >= DeedRules.PulledFreeRescues) Earn("pulled_free"); break;
            case DeedSignal.DailyDone: if (Bump(DeedRules.DailiesTotal) >= DeedRules.RegularDailies) Earn("regular"); break;
        }
        return earned;
    }

    private int Bump(string counter) => _profile.Counters[counter] = _profile.Counters.GetValueOrDefault(counter) + 1;

    private DeedDef? Grant(string id)
    {
        if (_profile.Deeds.ContainsKey(id)) return null;
        var d = DeedRules.Def(id);
        _profile.Deeds[id] = DateTime.UtcNow.ToString("o");
        var key = DeedRules.UnlockKey(d);
        if (!_profile.Unlocks.Contains(key)) _profile.Unlocks.Add(key);
        return d;
    }
}
