namespace DungeonCrawler.Adventure.Run;

// Shared surface as found on 2026-11-08 (every sibling Depths plan had landed):
// - EventBus: every contract event exists (EliteSeen .. FloorLedgerClosed); run-structure adds MemberRescued and GoldFound.
// - Writing: Adventure.Core.Voice.WipeCause (ids), Epitaphs.Pick(cause, wipesOnFloor, floor), AdventureDirector.NoteDeathCause/LastWipe.
// - Elites: EliteRules.CountFor(floor, extra), Enemy.IsElite/IsBoss/IsHorde, EnemyProfile.Bomber/EliteName, DungeonBuilder.ExtraElitesPerFloor (static int; Hostile Tenants adds its rank there).
// - Fortune: Curses.Scar ("scar", MaxHp*0.9, transient), Prices.BuyFactor set in FortuneDirector.Refresh via FortuneRules.PriceFactor(adv, clauseFactor),
//   ShrineRules.OfferCount/OfferCountFinePrint, Chest.IsMimic + MimicRules.EliteName on the brute, TierRules.HazardFactor (fortune's Brittle; not ours).
// - Items: legendary ids leg.gerald, leg.gorms_molar, leg.old_ulfs_chin, leg.hush, leg.pellets_receipt, leg.toll_road_boots; LootDrop.Interact -> PartyController.AddFoundGold.
// - Combat: DamageResult.Shatter; EnemyHurt carries HitFlavor.Shatter.
// - Save: SaveFormat.CurrentVersion is already 11; run-structure adds defaulted v11 fields only. Protocol 8/88 -> 9/91.

/// <summary>The ten clauses of the factor's contract, in the order the card lists them.</summary>
public enum Clause { DeathClause, ConvenienceFee, InventoryAudit, FullOccupancy, HostileTenants, ExpeditedDelivery, HazardPayWaived, NoRest, OpenDoor, FinePrint }

/// <summary>One clause: its key on the wire and in saves, its name and the factor's description, heat per rank, the ranks it has, and the deed each rank above the first needs (null = always available).</summary>
public sealed record ClauseDef(Clause Id, string Key, string Name, string Description, int HeatPerRank, int MaxRank, string?[] RankUnlockedByDeed);

/// <summary>The contract's numbers, verbatim from the spec (section 5), and the clause set the Godot side reads. Engine-free.</summary>
public static class ClauseRules
{
    /// <summary>Heat multiplies gold and XP found by 1 + HeatStep × heat.</summary>
    public const float HeatStep = 0.1f;
    public const float ConvenienceFeePrices = 1.4f;
    public const float FullOccupancyPerRank = 0.25f;
    public const float ExpeditedGraceSeconds = 45f;
    public const float HazardPayPerRank = 0.2f;
    /// <summary>The Scar curse: −10% max HP until the next floor (fortune's Curses.Scar carries the same number).</summary>
    public const float ScarMaxHp = 0.9f;
    public const int OpenDoorAmbushFactor = 2, FinePrintOffers = 2;
    /// <summary>The tier gates at which the Inventory Audit takes an affix.</summary>
    public static readonly int[] AuditFloors = { 6, 11, 16 };

    public static readonly ClauseDef[] All =
    {
        new(Clause.DeathClause, "deathclause", "Death Clause", "Should the whole party fall, the contract is void and the bodies stay where they lie. No second signature.", 3, 1, new string?[] { null }),
        new(Clause.ConvenienceFee, "conveniencefee", "Convenience Fee", "Every price in my ledger carries a forty-per-cent handling charge.", 1, 1, new string?[] { null }),
        new(Clause.InventoryAudit, "inventoryaudit", "Inventory Audit", "At each tier gate the meanest piece each of you wears is assessed, and one of its virtues is retained against costs.", 2, 1, new string?[] { null }),
        new(Clause.FullOccupancy, "fulloccupancy", "Full Occupancy", "The council lets the lower rooms to a fuller house: a quarter more tenants on every floor, per rank.", 1, 2, new string?[] { null, "small_print" }),
        new(Clause.HostileTenants, "hostiletenants", "Hostile Tenants", "One more of the named sort on every floor, per rank. They have references.", 1, 2, new string?[] { null, "below_the_lease" }),
        new(Clause.ExpeditedDelivery, "expediteddelivery", "Expedited Delivery", "The guardians' reinforcements arrive after three-quarters of a minute, and the intervals shorten twice as fast, per rank.", 1, 2, new string?[] { null, "express_lodgings" }),
        new(Clause.HazardPayWaived, "hazardpaywaived", "Hazard Pay Waived", "You bear a fifth more of every blow, per rank. The council thanks you for your economy.", 1, 3, new string?[] { null, "tenant_eviction", "plumber" }),
        new(Clause.NoRest, "norest", "No Rest for the Hired", "No locked rooms below. I shall wait by the stairs, in the open, and think of you.", 2, 1, new string?[] { null }),
        new(Clause.OpenDoor, "opendoor", "Open-Door Policy", "Twice the number lie in wait along the corridors. The doors, you understand, are the council's.", 1, 1, new string?[] { null }),
        new(Clause.FinePrint, "fineprint", "Fine Print", "The altars below offer two favours, not three. The third was never in the contract.", 1, 1, new string?[] { null }),
    };

    private static readonly Dictionary<Clause, ClauseDef> ById = All.ToDictionary(d => d.Id);
    private static readonly Dictionary<string, ClauseDef> ByKey = All.ToDictionary(d => d.Key, StringComparer.OrdinalIgnoreCase);

    public static ClauseDef Def(Clause c) => ById[c];
    public static ClauseDef? ByKeyOrNull(string key) => ByKey.TryGetValue(key, out var d) ? d : null;

    /// <summary>The entry for a clause at a rank: the key alone at rank 1, "key:rank" above it.</summary>
    public static string Entry(ClauseDef def, int rank) => rank <= 1 ? def.Key : $"{def.Key}:{rank}";

    /// <summary>Split "key" or "key:rank" into the def and the rank (clamped to 1..MaxRank); null for an unknown key or a malformed rank.</summary>
    public static (ClauseDef Def, int Rank)? ParseEntry(string raw)
    {
        var text = raw.Trim();
        if (text.Length == 0) return null;
        var sep = text.IndexOf(':');
        var key = sep < 0 ? text : text[..sep];
        var def = ByKeyOrNull(key);
        if (def == null) return null;
        var rank = 1;
        if (sep >= 0 && !int.TryParse(text[(sep + 1)..], out rank)) return null;
        return (def, Math.Clamp(rank, 1, def.MaxRank));
    }

    /// <summary>Unknown keys dropped, ranks clamped, duplicates keep the higher rank, sorted in <see cref="Clause"/> order.</summary>
    public static List<string> Normalize(IEnumerable<string> raw)
    {
        var best = new Dictionary<Clause, int>();
        foreach (var r in raw)
            if (ParseEntry(r) is { } e && e.Rank > best.GetValueOrDefault(e.Def.Id)) best[e.Def.Id] = e.Rank;
        return best.OrderBy(kv => kv.Key).Select(kv => Entry(Def(kv.Key), kv.Value)).ToList();
    }

    /// <summary>The server's <c>--clauses=a,b:2</c>: a comma list, normalized.</summary>
    public static List<string> Parse(string flag) => Normalize(flag.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

    /// <summary>The highest rank the profile's deeds allow: each rank above the first needs its own deed and every rank below it.</summary>
    public static int MaxRankAllowed(ClauseDef def, IReadOnlySet<string> deeds)
    {
        var rank = 1;
        while (rank < def.MaxRank && def.RankUnlockedByDeed[rank] is { } deed && deeds.Contains(deed)) rank++;
        return rank;
    }

    /// <summary>The clauses the running game plays under; the Godot side reads it wherever a clause turns a knob.</summary>
    public static ClauseSet Active { get; private set; } = ClauseSet.None;

    /// <summary>Make these the active clauses (null or empty: none). Side statics the clauses drive are read live off <see cref="Active"/>.</summary>
    public static void Activate(IEnumerable<string>? clauses)
    {
        var list = clauses == null ? new List<string>() : Normalize(clauses);
        Active = list.Count == 0 ? ClauseSet.None : new ClauseSet(list);
        Dungeon.TierRules.ClauseDamageMultiplier = Active.DamageTakenMultiplier; // Hazard Pay Waived, at the party's three damage sites
    }

    public static void Reset() => Activate(null);
}

/// <summary>A signed set of clauses and the knobs they turn.</summary>
public sealed class ClauseSet
{
    public static readonly ClauseSet None = new(Array.Empty<string>());
    private readonly Dictionary<Clause, int> _ranks = new();

    public ClauseSet(IEnumerable<string> clauses)
    {
        Clauses = ClauseRules.Normalize(clauses);
        foreach (var c in Clauses) if (ClauseRules.ParseEntry(c) is { } e) _ranks[e.Def.Id] = e.Rank;
    }

    /// <summary>The normalized entries, in <see cref="Clause"/> order.</summary>
    public IReadOnlyList<string> Clauses { get; }
    public int Rank(Clause c) => _ranks.GetValueOrDefault(c);
    public bool Has(Clause c) => Rank(c) > 0;
    public int Heat => _ranks.Sum(kv => ClauseRules.Def(kv.Key).HeatPerRank * kv.Value);
    public float RewardMultiplier => 1f + ClauseRules.HeatStep * Heat;
    /// <summary>Gold or XP found, scaled by the heat and rounded to the nearest whole.</summary>
    public int ScaleReward(int value) => (int)Math.Round(value * (double)RewardMultiplier, MidpointRounding.AwayFromZero);
    public float PriceMultiplier => Has(Clause.ConvenienceFee) ? ClauseRules.ConvenienceFeePrices : 1f;
    public float EnemyCountMultiplier => 1f + ClauseRules.FullOccupancyPerRank * Rank(Clause.FullOccupancy);
    public int ExtraElites => Rank(Clause.HostileTenants);
    public int ExpeditedRank => Rank(Clause.ExpeditedDelivery);
    public float DamageTakenMultiplier => 1f + ClauseRules.HazardPayPerRank * Rank(Clause.HazardPayWaived);
    public bool NoSafeRoom => Has(Clause.NoRest);
    public bool AmbushesDoubled => Has(Clause.OpenDoor);
    public int ShrineOffers(int baseCount) => Has(Clause.FinePrint) ? Math.Max(ClauseRules.FinePrintOffers, baseCount - 1) : baseCount;
    public bool Hardcore => Has(Clause.DeathClause);
    public bool AuditsAt(int floor) => Has(Clause.InventoryAudit) && Array.IndexOf(ClauseRules.AuditFloors, floor) >= 0;

    /// <summary>"Convenience Fee, Hazard Pay Waived II · heat 3 · ×1.3 gold and XP", or "No clauses signed."</summary>
    public string Summary
    {
        get
        {
            if (_ranks.Count == 0) return "No clauses signed.";
            var names = _ranks.OrderBy(kv => kv.Key).Select(kv => kv.Value > 1 ? $"{ClauseRules.Def(kv.Key).Name} {Roman(kv.Value)}" : ClauseRules.Def(kv.Key).Name);
            return $"{string.Join(", ", names)} · heat {Heat} · ×{RewardMultiplier.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture)} gold and XP";
        }
    }

    private static string Roman(int n) => n switch { 1 => "I", 2 => "II", 3 => "III", _ => n.ToString() };
}
