using DungeonCrawler.Adventure;
using DungeonCrawler.Dungeon;
using Godot;

namespace DungeonCrawler.Core;

/// <summary>--bake-all-voice=DIR: every spoken line the shipped campaign can say, rendered into DIR and the ids written back into the plans.</summary>
public partial class DebugTools
{
    private string? _bakeAllVoiceDir;
    private int _bakeAllVoiceQueued = -1;

    /// <summary>
    /// With --builtin --ai and the voice flags: queues the campaign (tier lines, every taunt, every bark set topped up from every stock bank, the
    /// banter poured into the campaign, the merchant) and every floor (intro, narration, boss, cast and asides, taunts, horde lines) with no filter
    /// and no budget; lines the bank already holds cost nothing. When the queue drains, the campaign and the twenty plans are written with their ids
    /// to DIR/all-plans, to copy over data/adventures/builtin.
    /// </summary>
    private void TickBakeAllVoice()
    {
        if (_bakeAllVoiceDir == null || Main.Instance?.InGame != true || Game.Instance.Adventure.Campaign is not { } campaign || _elapsed < 2.0) return;
        var vr = VoiceRenderer.Instance;
        var plans = Game.Instance.Adventure.FloorPlans.Where(kv => kv.Key >= 1 && kv.Key <= TierRules.FinalFloor).ToDictionary(kv => kv.Key, kv => kv.Value);
        if (_bakeAllVoiceQueued < 0)
        {
            if (vr == null || AdventureDirector.Instance is not { } director) { GD.Print("[all-voice] RESULT FAIL: no voice renderer or director"); _done = true; GetTree().Quit(1); return; }
            if (plans.Count < TierRules.FinalFloor) { GD.Print($"[all-voice] RESULT FAIL: only {plans.Count} floor plans loaded"); _done = true; GetTree().Quit(1); return; }
            vr.CacheDir = _bakeAllVoiceDir; vr.Filter = null;
            director.EnqueueCampaignVoice(campaign);
            foreach (var plan in plans.Values) director.EnqueueFloorVoice(plan);
            _bakeAllVoiceQueued = vr.Pending;
            GD.Print($"[all-voice] queued: {vr.Pending} to render into {vr.CacheDir} with {vr.CurrentModel}, {VoiceRenderer.MaxConcurrent} at a time; banter exchanges in the campaign: {campaign.Banter.Count}; bark lines: {campaign.Barks.Sum(s => s.Lines.Count)}; taunts: {campaign.Taunts.Count}{(vr.Enabled ? "" : " (voice is off: nothing renders)")}");
        }
        else if (vr != null && (vr.Pending == 0 || vr.StoppedReason != null))
        {
            var ok = vr.Failed == 0 && vr.StoppedReason == null;
            GD.Print($"[all-voice] rendered {vr.Rendered}, failed {vr.Failed}{(vr.StoppedReason != null ? ", stopped: " + vr.StoppedReason : "")}; per model: {string.Join(", ", vr.RenderedByModel.Select(kv => $"{kv.Key} x{kv.Value}"))}");
            var outDir = Path.Combine(_bakeAllVoiceDir, "all-plans");
            BuiltinCampaign.Write(outDir, campaign, plans); // written even after a stop: the ids of what did render are worth keeping
            GD.Print($"[all-voice] wrote the campaign and {plans.Count} plans with their voice ids to {outDir}");
            GD.Print($"[all-voice] RESULT {(ok ? "ok" : "FAIL")}");
            _done = true; GetTree().Quit(ok ? 0 : 1);
        }
        else if (vr != null && (int)_elapsed % 30 == 0 && _elapsed - Math.Floor(_elapsed) < 0.02) GD.Print($"[all-voice] {vr.Pending} pending, {vr.Rendered} done, {vr.Failed} failed");
    }
}
