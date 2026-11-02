using DungeonCrawler.Adventure.Objectives;
using DungeonCrawler.Adventure.Voice;
using DungeonCrawler.Dungeon.Generation;

namespace DungeonCrawler.Adventure;

/// <summary>
/// What every campaign gets before it is used, whoever wrote it: taunts for every trigger (stock ones where the
/// planner left gaps), a clean twin for every taunt, personas within bounds, deliveries that exist. Engine-free.
/// </summary>
public static class CampaignRepair
{
    public const int MaxLine = 220;

    public static List<string> Apply(CampaignPlan campaign)
    {
        var repairs = new List<string>();
        campaign.Taunts.RemoveAll(t =>
        {
            if (!TauntTriggers.TryParse(t.Trigger, out var trigger)) { repairs.Add($"taunt with unknown trigger \"{t.Trigger}\" dropped"); return true; }
            t.Trigger = TauntTriggers.Id(trigger);
            if (string.IsNullOrWhiteSpace(t.Line.Text)) { repairs.Add($"empty {t.Trigger} taunt dropped"); return true; }
            if (t.Objective != null && !FloorObjectives.TryParse(t.Objective, out _)) { t.Objective = null; repairs.Add($"{t.Trigger} taunt: unknown objective cleared"); }
            else if (t.Objective != null && FloorObjectives.TryParse(t.Objective, out var k)) t.Objective = k == Dungeon.Generation.FloorObjectiveKind.None ? null : FloorObjectives.Id(k);
            t.Line.Text = Clip(t.Line.Text);
            if (t.CleanLine != null) t.CleanLine.Text = Clip(t.CleanLine.Text);
            t.RequiresFlag = FlagRules.Clean(t.RequiresFlag); t.ForbidsFlag = FlagRules.Clean(t.ForbidsFlag);
            RepairLine(t.Line, repairs, $"{t.Trigger} taunt");
            if (t.CleanLine != null) RepairLine(t.CleanLine, repairs, $"{t.Trigger} taunt (clean)");
            return false;
        });
        RepairMerchant(campaign, repairs);
        var added = TauntBank.Fill(campaign);
        if (added > 0) repairs.Add($"{added} stock taunts added for triggers the campaign had none for");
        RepairPersona(campaign.Antagonist.Persona, repairs, "antagonist");
        foreach (var c in campaign.Cast) RepairPersona(c.Persona, repairs, c.Name);
        foreach (var t in campaign.Tiers) RepairLine(t.EnterLine, repairs, $"tier {t.Index} entrance");
        foreach (var set in campaign.Barks) foreach (var b in set.Lines) RepairLine(b.Line, repairs, $"{set.Member} bark");
        return repairs;
    }

    /// <summary>Exactly one cast member keeps the shop, hides nothing, and has lines to speak; a campaign without one gets the generic merchant.</summary>
    private static void RepairMerchant(CampaignPlan campaign, List<string> repairs)
    {
        if (!campaign.Cast.Any(c => c.Merchant)) { campaign.Cast.Add(MerchantLineBank.GenericCast()); repairs.Add("generic merchant added"); }
        var first = campaign.Cast.First(c => c.Merchant);
        var extra = false;
        foreach (var c in campaign.Cast) if (c.Merchant && c != first) { c.Merchant = false; extra = true; }
        if (extra) repairs.Add("extra merchant flags cleared");
        if (!string.IsNullOrEmpty(first.Secret)) { first.Secret = ""; repairs.Add("merchant secret cleared"); }
        if (campaign.Merchant is null or { IsEmpty: true }) { campaign.Merchant = MerchantLineBank.Generic(campaign.Title, campaign.Antagonist.Name); repairs.Add("generic merchant lines added"); }
    }

    /// <summary>
    /// The merchant repair for a campaign read from disk (the shipped one, a user's rebake, a bake in progress): he and his
    /// lines are there in memory, and what was changed is kept on <see cref="CampaignPlan.LoadRepair"/> so that
    /// <see cref="ForWriting"/> hands a writer the campaign as the file had it. The other repairs are not applied here.
    /// </summary>
    public static List<string> MerchantOnLoad(CampaignPlan campaign)
    {
        var undo = new MerchantLoadRepair
        {
            Merchant = campaign.Merchant,
            Flags = campaign.Cast.ToDictionary<CastMember, CastMember, (bool, string)>(c => c, c => (c.Merchant, c.Secret), ReferenceEqualityComparer.Instance),
        };
        var before = campaign.Cast.ToHashSet(ReferenceEqualityComparer.Instance);
        var repairs = new List<string>();
        RepairMerchant(campaign, repairs);
        if (repairs.Count == 0) return repairs;
        undo.Added = campaign.Cast.Where(c => !before.Contains(c)).Select(c => c.Id).ToList();
        campaign.LoadRepair = undo;
        return repairs;
    }

    /// <summary>
    /// The campaign to write: itself when nothing was repaired on load, else a copy with the load's merchant repair taken
    /// back out (the generic merchant and his lines removed, flags and secret as they were). Everything else that changed
    /// since the load, voice ids and barks among them, is kept.
    /// </summary>
    public static CampaignPlan ForWriting(CampaignPlan campaign)
    {
        if (campaign.LoadRepair is not { } undo) return campaign;
        var copy = PlanFormat.Deserialize<CampaignPlan>(PlanFormat.Serialize(campaign));
        copy.Merchant = undo.Merchant;
        for (var i = 0; i < campaign.Cast.Count && i < copy.Cast.Count; i++)
            if (undo.Flags.TryGetValue(campaign.Cast[i], out var was)) { copy.Cast[i].Merchant = was.Merchant; copy.Cast[i].Secret = was.Secret; }
        copy.Cast.RemoveAll(c => undo.Added.Contains(c.Id));
        return copy;
    }

    /// <summary>A line's delivery must be one of ours; anything else falls back to the character's default.</summary>
    public static void RepairLine(Line line, List<string> repairs, string what)
    {
        if (line.Delivery == null) return;
        if (Deliveries.TryParse(line.Delivery, out var d)) line.Delivery = Deliveries.Id(d);
        else { repairs.Add($"{what}: unknown delivery \"{line.Delivery}\" cleared"); line.Delivery = null; }
    }

    /// <summary>A persona's words are clipped, its delivery and voice must exist.</summary>
    public static void RepairPersona(Persona? persona, List<string> repairs, string who)
    {
        if (persona == null) return;
        if (persona.VoiceStyle.Length > Persona.MaxStyle) { persona.VoiceStyle = persona.VoiceStyle[..Persona.MaxStyle].Trim(); repairs.Add($"{who}: voice style clipped"); }
        if (persona.DefaultDelivery != null)
        {
            if (Deliveries.TryParse(persona.DefaultDelivery, out var d)) persona.DefaultDelivery = Deliveries.Id(d);
            else { repairs.Add($"{who}: unknown delivery \"{persona.DefaultDelivery}\" cleared"); persona.DefaultDelivery = null; }
        }
        if (persona.Voice != null)
        {
            if (Voices.Canonical(persona.Voice) is { } known) persona.Voice = known;
            else { repairs.Add($"{who}: unknown voice \"{persona.Voice}\" cleared"); persona.Voice = null; }
        }
    }

    private static string Clip(string s) => s.Length <= MaxLine ? s.Trim() : s[..MaxLine].Trim();
}

/// <summary>What a load-time merchant repair changed, to take it back out before the campaign is written.</summary>
public sealed class MerchantLoadRepair
{
    /// <summary>The merchant section as the file had it (null, or an empty one).</summary>
    public MerchantPlan? Merchant { get; set; }
    /// <summary>Each cast member read from the file, with its merchant flag and secret as they were.</summary>
    public Dictionary<CastMember, (bool Merchant, string Secret)> Flags { get; set; } = new(ReferenceEqualityComparer.Instance);
    /// <summary>The ids of the cast members the repair added.</summary>
    public List<string> Added { get; set; } = new();
}
