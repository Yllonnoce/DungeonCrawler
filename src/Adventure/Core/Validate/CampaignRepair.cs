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
    /// <summary>The voice a merchant without a known one is given: the stock merchant's (Achird, Inworld Oliver, a man).</summary>
    public const string MerchantVoice = "Achird";

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
        // the merchant never falls back to the placeholder voice (Kore, a woman): an empty or unknown voice gets the stock merchant's
        if (campaign.Cast.FirstOrDefault(c => c.Merchant) is { } shop && !Voices.IsKnown(shop.Persona?.Voice))
        {
            shop.Persona ??= new Persona();
            shop.Persona.Voice = MerchantVoice;
            repairs.Add($"{shop.Name}: merchant voice set to {MerchantVoice}");
        }
        foreach (var t in campaign.Tiers) RepairLine(t.EnterLine, repairs, $"tier {t.Index} entrance");
        foreach (var set in campaign.Barks) foreach (var b in set.Lines) RepairLine(b.Line, repairs, $"{set.Member} bark");
        return repairs;
    }

    /// <summary>
    /// Exactly one cast member keeps the shop, hides nothing, and has lines to speak; a campaign without one gets the generic
    /// merchant. A section that skips floors gets the generic lines for floors 1 to <see cref="MerchantFloors"/> it lacks, and
    /// his floor and gathering lines are clipped and their deliveries checked like every other line. What a fill or a clip
    /// changed is noted on <paramref name="undo"/> when there is one, for <see cref="ForWriting"/> to take back out.
    /// </summary>
    private static void RepairMerchant(CampaignPlan campaign, List<string> repairs, MerchantLoadRepair? undo = null)
    {
        if (!campaign.Cast.Any(c => c.Merchant)) { campaign.Cast.Add(MerchantLineBank.GenericCast()); repairs.Add("generic merchant added"); }
        var first = campaign.Cast.First(c => c.Merchant);
        var extra = false;
        foreach (var c in campaign.Cast) if (c.Merchant && c != first) { c.Merchant = false; extra = true; }
        if (extra) repairs.Add("extra merchant flags cleared");
        if (!string.IsNullOrEmpty(first.Secret)) { first.Secret = ""; repairs.Add("merchant secret cleared"); }
        if (campaign.Merchant is null or { IsEmpty: true }) { campaign.Merchant = MerchantLineBank.Generic(campaign.Title, campaign.Antagonist.Name); repairs.Add("generic merchant lines added"); return; }

        var merchant = campaign.Merchant;
        MerchantPlan? generic = null;
        var filled = 0;
        for (var floor = 1; floor <= MerchantFloors; floor++)
        {
            if (merchant.FloorLines.Any(f => f.Floor == floor)) continue;
            generic ??= MerchantLineBank.Generic(campaign.Title, campaign.Antagonist.Name);
            if (generic.FloorLines.FirstOrDefault(f => f.Floor == floor) is not { } stock) continue;
            merchant.FloorLines.Add(stock);
            undo?.AddedFloors.Add(stock);
            filled++;
        }
        if (filled > 0) repairs.Add($"{filled} merchant floor lines added from the generic bank");
        foreach (var f in merchant.FloorLines)
        {
            RepairMerchantLine(f.Greeting, repairs, $"merchant floor {f.Floor} greeting", undo);
            RepairMerchantLine(f.Progress, repairs, $"merchant floor {f.Floor} progress", undo);
            RepairMerchantLine(f.Trade, repairs, $"merchant floor {f.Floor} trade", undo);
        }
        for (var i = 0; i < merchant.Gathering.Count; i++) RepairMerchantLine(merchant.Gathering[i], repairs, $"merchant gathering line {i + 1}", undo);
    }

    /// <summary>The floors the merchant has authored lines for; deeper floors reuse the last (<see cref="MerchantPlan.For"/>).</summary>
    public const int MerchantFloors = 20;

    private static void RepairMerchantLine(Line line, List<string> repairs, string what, MerchantLoadRepair? undo)
    {
        var (text, delivery) = (line.Text, line.Delivery);
        line.Text = Clip(line.Text);
        if (line.Text != text) repairs.Add($"{what} clipped");
        RepairLine(line, repairs, what);
        if (undo != null && (line.Text != text || line.Delivery != delivery) && !undo.Lines.ContainsKey(line)) undo.Lines[line] = (text, delivery);
    }

    /// <summary>
    /// The merchant repair for a campaign read from disk (the shipped one, a user's rebake, a bake in progress): he and his
    /// lines are there in memory, and what was changed is kept on <see cref="CampaignPlan.LoadRepair"/> so that
    /// <see cref="ForWriting"/> hands a writer the campaign as the file had it. The other repairs are not applied here.
    /// </summary>
    public static List<string> MerchantOnLoad(CampaignPlan campaign)
    {
        var merchant = campaign.Merchant;
        var was = campaign.Cast.Select(c => (Member: c, c.Merchant, c.Secret)).ToList();
        var repairs = new List<string>();
        var undo = new MerchantLoadRepair { Merchant = merchant };
        RepairMerchant(campaign, repairs, undo);
        if (repairs.Count == 0) return repairs;
        undo.LinesReplaced = !ReferenceEquals(merchant, campaign.Merchant);
        foreach (var (member, flag, secret) in was)
            if (member.Merchant != flag || member.Secret != secret) undo.Changed[member] = (flag, secret);
        undo.Added.AddRange(campaign.Cast.Where(c => !was.Any(w => ReferenceEquals(w.Member, c))));
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
        if (undo.LinesReplaced) copy.Merchant = undo.Merchant;
        else if (campaign.Merchant is { } merchant && copy.Merchant is { } written)
        {
            // the copy's merchant section lines up with the campaign's by index: put back clipped or cleared lines, drop the floors the fill added
            void Restore(Line from, Line to) { if (undo.Lines.TryGetValue(from, out var was)) { to.Text = was.Text; to.Delivery = was.Delivery; } }
            var dropFloors = new List<MerchantFloorLines>();
            for (var i = 0; i < merchant.FloorLines.Count && i < written.FloorLines.Count; i++)
            {
                var (from, to) = (merchant.FloorLines[i], written.FloorLines[i]);
                if (undo.AddedFloors.Any(a => ReferenceEquals(a, from))) { dropFloors.Add(to); continue; }
                Restore(from.Greeting, to.Greeting); Restore(from.Progress, to.Progress); Restore(from.Trade, to.Trade);
            }
            for (var i = 0; i < merchant.Gathering.Count && i < written.Gathering.Count; i++) Restore(merchant.Gathering[i], written.Gathering[i]);
            foreach (var d in dropFloors) written.FloorLines.Remove(d);
        }
        // the copy's cast lines up with the campaign's by index: undo only the members the repair touched, drop only the one it added
        var drop = new List<CastMember>();
        for (var i = 0; i < campaign.Cast.Count && i < copy.Cast.Count; i++)
        {
            var member = campaign.Cast[i];
            if (undo.Added.Any(a => ReferenceEquals(a, member))) drop.Add(copy.Cast[i]);
            else if (undo.Changed.TryGetValue(member, out var was)) { copy.Cast[i].Merchant = was.Merchant; copy.Cast[i].Secret = was.Secret; }
        }
        foreach (var d in drop) copy.Cast.Remove(d);
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

    /// <summary>Pour every stock bank into one party member's bark set (kill, idle, dance, travel, the reactive moments, the cheer); returns how many lines were added.</summary>
    public static int TopUpSet(BarkSet set, string classId)
        => Voice.KillBarks.TopUp(set, classId) + Voice.IdleBarks.TopUp(set, classId) + Voice.DanceBarks.TopUp(set, classId)
         + Voice.TravelBarks.TopUp(set, classId) + Voice.ReactiveBarks.TopUp(set, classId) + Voice.CheerBarks.TopUp(set, classId);

    /// <summary>Top up every party member's set in a campaign (fighters keep their own stock; a member whose class is unknown is left alone); returns how many lines were added.</summary>
    public static int TopUpBarks(CampaignPlan campaign, Func<string, string?> classIdOf)
    {
        var added = 0;
        foreach (var set in campaign.Barks)
        {
            if (Voice.KillBarks.IsFighter(set.Member)) continue;
            if (classIdOf(set.Member) is not { } classId) continue;
            added += TopUpSet(set, classId);
        }
        return added;
    }
}

/// <summary>What a load-time merchant repair changed, to take it back out before the campaign is written.</summary>
public sealed class MerchantLoadRepair
{
    /// <summary>The merchant section as the file had it (null, or an empty one); put back only when <see cref="LinesReplaced"/>.</summary>
    public MerchantPlan? Merchant { get; set; }
    /// <summary>True when the repair put the generic lines in place of the file's section.</summary>
    public bool LinesReplaced { get; set; }
    /// <summary>The cast members whose merchant flag or secret the repair changed, with the values the file had.</summary>
    public Dictionary<CastMember, (bool Merchant, string Secret)> Changed { get; } = new(ReferenceEqualityComparer.Instance);
    /// <summary>The cast members the repair added (by reference: an authored member sharing the id is never dropped).</summary>
    public List<CastMember> Added { get; } = new();
    /// <summary>The generic floor rows the repair added to a section that skipped floors (by reference).</summary>
    public List<MerchantFloorLines> AddedFloors { get; } = new();
    /// <summary>The merchant's lines the repair clipped or whose delivery it cleared, with the text and delivery the file had.</summary>
    public Dictionary<Line, (string Text, string? Delivery)> Lines { get; } = new(ReferenceEqualityComparer.Instance);
}
