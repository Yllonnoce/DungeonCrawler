namespace DungeonCrawler.Adventure.Run;

/// <summary>What a deed widens: a clause rank, a legendary into the drop pool, a banter set, a hidden-merchant line. Never raw power.</summary>
public enum DeedUnlockKind { ClauseRank, Legendary, Banter, MerchantLine }

/// <summary>One deed: id, in-world name, the condition as the Records page prints it, and what it unlocks.</summary>
public sealed record DeedDef(string Id, string Name, string Condition, DeedUnlockKind UnlockKind, string UnlockId, string UnlockText);

/// <summary>The twenty-four deeds. Engine-free.</summary>
public static class DeedRules
{
    public const int Count = 24;
    /// <summary>The all-time counters in the profile.</summary>
    public const string ElitesTotal = "elites_total", RescuesTotal = "rescues_total", GeraldTotal = "gerald_total", DailiesTotal = "dailies_total";
    /// <summary>The thresholds, as the conditions read them.</summary>
    public const int BelowTheLeaseFloor = 25, DeepTenantFloor = 30, GeraldsWidowCount = 10, BangOnScheduleWipes = 4, ExpressLodgingsFloor = 3, ExpressLodgingsSeconds = 90,
        WainscotSecrets = 10, NosyMinSecrets = 2, BitFirstMimics = 3, DevoutShrines = 6, PelletsDips = 5, IronMannersBlocks = 25, RiposteParries = 25, ShortAudienceSeconds = 45,
        TenantEvictionElites = 10, LandlordElites = 50, PulledFreeRescues = 10, RegularDailies = 5, SmallPrintHeat = 5, WholeContractHeat = 10;

    public static readonly DeedDef[] All =
    {
        new("plumber", "Plumber", "finish the story", DeedUnlockKind.ClauseRank, "hazardpaywaived:3", "Hazard Pay Waived III"),
        new("signed_in_blood", "Signed in Blood", "finish the story under the Death Clause", DeedUnlockKind.Banter, "hardcore", "the party's talk of the Death Clause"),
        new("small_print", "Small Print", "a clear at heat 5 or more", DeedUnlockKind.ClauseRank, "fulloccupancy:2", "Full Occupancy II"),
        new("whole_contract", "Whole Contract", "a clear at heat 10 or more", DeedUnlockKind.MerchantLine, "merchant.contract", "a line at the hidden stall"),
        new("below_the_lease", "Below the Lease", "reach floor 25", DeedUnlockKind.ClauseRank, "hostiletenants:2", "Hostile Tenants II"),
        new("deep_tenant", "Deep Tenant", "reach floor 30", DeedUnlockKind.Legendary, "leg.toll_road_boots", "Toll Road Boots in the drop pool"),
        new("jammed_it", "Jammed It", "a barrel stops the Press", DeedUnlockKind.Legendary, "leg.old_ulfs_chin", "Old Ulf's Chin in the drop pool"),
        new("geralds_widow", "Gerald's Widow", "Ilex names a tenth puddle, all runs", DeedUnlockKind.Legendary, "leg.gerald", "Gerald in the drop pool"),
        new("load_bearing", "Load-Bearing", "kill an elite with Shatter", DeedUnlockKind.Legendary, "leg.gorms_molar", "Gorm's Molar in the drop pool"),
        new("bang_on_schedule", "Bang on Schedule", "wipe four times on one floor", DeedUnlockKind.Banter, "wipes", "the party's talk of dying"),
        new("express_lodgings", "Express Lodgings", "a floor from 3 down cleared in under 90 s", DeedUnlockKind.ClauseRank, "expediteddelivery:2", "Expedited Delivery II"),
        new("behind_the_wainscot", "Behind the Wainscot", "ten secrets found in one run", DeedUnlockKind.MerchantLine, "merchant.wainscot", "a line at the hidden stall"),
        new("nosy", "Nosy", "every secret on a floor that has at least two", DeedUnlockKind.Legendary, "leg.hush", "Hush in the drop pool"),
        new("bit_first", "Bit First", "three mimics sprung in one run", DeedUnlockKind.Banter, "mimic", "the party's talk of chests"),
        new("sin_eater", "Sin Eater", "take a curse at a shrine", DeedUnlockKind.MerchantLine, "merchant.sineater", "a line at the hidden stall"),
        new("devout", "Devout", "boons from six shrines in one run", DeedUnlockKind.Banter, "shrines", "the party's talk of altars"),
        new("pellets_favourite", "Pellet's Favourite", "five Lucky Dips in one run", DeedUnlockKind.Legendary, "leg.pellets_receipt", "Pellet's Receipt in the drop pool"),
        new("iron_manners", "Iron Manners", "twenty-five perfect blocks in one run", DeedUnlockKind.Banter, "guard", "the party's talk of shields"),
        new("riposte_society", "Riposte Society", "twenty-five parries in one run", DeedUnlockKind.Banter, "parry", "the party's talk of swordplay"),
        new("short_audience", "Short Audience", "a tier boss down within 45 s of being seen", DeedUnlockKind.MerchantLine, "merchant.audience", "a line at the hidden stall"),
        new("tenant_eviction", "Tenant Eviction", "ten elites slain in one run", DeedUnlockKind.ClauseRank, "hazardpaywaived:2", "Hazard Pay Waived II"),
        new("landlord", "Landlord", "fifty elites slain, all runs", DeedUnlockKind.Banter, "elites", "the party's talk of the named ones"),
        new("pulled_free", "Pulled Free", "ten Mangled members pulled free, all runs", DeedUnlockKind.Banter, "rescue", "the party's talk of rescues"),
        new("regular", "Regular", "five Daily Descents on five dates", DeedUnlockKind.MerchantLine, "merchant.regular", "a line at the hidden stall"),
    };

    private static readonly Dictionary<string, DeedDef> ById = All.ToDictionary(d => d.Id);
    public static DeedDef Def(string id) => ById[id];
    public static bool TryGet(string id, out DeedDef def) => ById.TryGetValue(id, out def!);

    /// <summary>The profile's Unlocks entry: "clauserank:hazardpaywaived:3", "legendary:leg.gerald", "banter:wipes", "merchantline:merchant.contract".</summary>
    public static string UnlockKey(DeedDef d) => $"{d.UnlockKind.ToString().ToLowerInvariant()}:{d.UnlockId}";
}
