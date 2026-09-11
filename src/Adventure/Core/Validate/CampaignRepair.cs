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
        var added = TauntBank.Fill(campaign);
        if (added > 0) repairs.Add($"{added} stock taunts added for triggers the campaign had none for");
        RepairPersona(campaign.Antagonist.Persona, repairs, "antagonist");
        foreach (var c in campaign.Cast) RepairPersona(c.Persona, repairs, c.Name);
        foreach (var t in campaign.Tiers) RepairLine(t.EnterLine, repairs, $"tier {t.Index} entrance");
        foreach (var set in campaign.Barks) foreach (var b in set.Lines) RepairLine(b.Line, repairs, $"{set.Member} bark");
        return repairs;
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
