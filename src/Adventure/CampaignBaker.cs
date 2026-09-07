using DungeonCrawler.Adventure.Ai;
using DungeonCrawler.Adventure.Voice;
using DungeonCrawler.Characters.Defs;
using DungeonCrawler.Core;
using DungeonCrawler.Dungeon;
using DungeonCrawler.Dungeon.Generation;
using DungeonCrawler.Party;
using Godot;

namespace DungeonCrawler.Adventure;

/// <summary>
/// Rebuilds the built-in campaign from the title screen: the configured planner writes the outline and all twenty
/// floors, the result goes to <see cref="BuiltinCampaign.UserDir"/> (which the loader prefers over the shipped copy),
/// and optionally every line is rendered to the voice cache. Progress is reported through <see cref="Status"/>.
/// </summary>
public partial class CampaignBaker : Node
{
    public static CampaignBaker? Instance { get; private set; }
    /// <summary>How many lines a bake renders; the rest stay subtitles.</summary>
    public static int VoiceBudgetLines { get; set; } = VoiceBudget.Default;
    public bool Running { get; private set; }
    private string _status = "";
    private ulong _stepStarted;
    /// <summary>The current step; while a planning step is in progress the time it has taken so far is appended, since a Runware outline can poll for ten minutes with nothing else to show.</summary>
    public string Status
    {
        get => Running && _status.EndsWith("...") && !_status.StartsWith("Rendering") ? $"{_status} ({(Time.GetTicksMsec() - _stepStarted) / 60000}:{(Time.GetTicksMsec() - _stepStarted) / 1000 % 60:00} so far)" : _status;
        private set { _status = value; _stepStarted = Time.GetTicksMsec(); }
    }
    public bool LastSucceeded { get; private set; }
    private bool _voiceOnly;
    /// <summary>Set while the renderer works through the bake's lines; the status line used to be sniffed for this and stopped updating after its first rewrite.</summary>
    private bool _waitingForVoice;
    private CampaignPlan? _campaign;
    private Dictionary<int, FloorPlan> _floors = new();
    private int _nextFloor;
    private IPlanner? _planner;
    private CostMeter? _meter;
    private double _lastVoiceReport;

    public override void _Ready() { Instance = this; ProcessMode = ProcessModeEnum.Always; }

    public static string ClassName(string classId)
        => Registry.Instance.TryGet<ClassDef>(classId, out var def) ? def!.DisplayName : classId;

    private static List<PartyMemberInfo> DefaultParty()
        => PartyController.DefaultRoster.Select(m => new PartyMemberInfo(m.Name, ClassName(m.ClassId))).ToList();

    private void Report(string status) { Status = status; GD.Print($"[bake] {status}"); }

    /// <summary>Plan a whole campaign with the current planner (Claude when a key is set), then write it and render its voice.</summary>
    public bool StartCampaign(bool withVoice, ulong? seed = null)
    {
        if (Running) return false;
        if (Main.Instance == null || Main.Instance.InGame) { Report("Available from the title screen only."); return false; }
        var director = AdventureDirector.Instance;
        Game.Instance.Adventure = new Save.AdventureDto();
        director.ConfigurePlanner(forceAi: true); // a rebake is an explicit ask, whatever the New Game toggle says
        if (director.Planner is OfflinePlanner) { Report(DebugTools.Instance?.NoAi == true ? "Live planning is off for this run (start without automation flags, or add --ai)." : $"Paste a {Settings.PlannerLabel} API key and enable the planner first."); return false; }
        _planner = director.Planner;
        _meter = new CostMeter(Game.Instance.Adventure.Spend, Settings.SpendCapUsd);
        _voiceOnly = false;
        Running = true;
        LastSucceeded = false;
        _floors = new Dictionary<int, FloorPlan>();
        var gameSeed = seed ?? (ulong)Random.Shared.NextInt64();
        var builder = Main.Instance.Builder;
        var seed1 = Rng.FloorSeed(gameSeed, 1);
        var spec1 = builder.SpecFor(1);
        var layout1 = FloorGenerator.Generate(spec1, seed1);
        var req = new OutlineRequest(gameSeed, Game.Instance.TierProfiles(), builder.Roster, DefaultParty(), layout1, spec1, seed1, PartySnapshot.Expected(1), builder.ThemeProfiles());
        Report($"Writing the campaign outline and floor 1 with {director.PlannerName}...");
        var planner = _planner;
        _ = Task.Run(async () =>
        {
            OutlineResult? result = null;
            string? error = null;
            try { result = await planner.OutlineAsync(req, CancellationToken.None); } catch (Exception e) { error = e.Message; }
            Callable.From(() =>
            {
                if (result == null) { Finish(false, $"Outline failed: {error}"); return; }
                _campaign = result.Campaign;
                _campaign.GameSeed = gameSeed;
                _floors[1] = result.Floor1;
                Game.Instance.Adventure.Campaign = _campaign; // so SpecFor sees the per-floor overrides
                _nextFloor = 2;
                PlanNextFloor(withVoice);
            }).CallDeferred();
        });
        return true;
    }

    private void PlanNextFloor(bool withVoice)
    {
        if (_nextFloor > TierRules.FinalFloor) { FetchBarksThenWrite(withVoice); return; }
        var floor = _nextFloor;
        var builder = Main.Instance!.Builder;
        var seed = Rng.FloorSeed(_campaign!.GameSeed, floor);
        var spec = builder.SpecFor(floor);
        var layout = FloorGenerator.Generate(spec, seed);
        var req = new FloorRequest(_campaign, layout, spec, builder.TierFor(floor)?.ToProfile(), builder.Roster, floor, seed, PartySnapshot.Expected(floor), "");
        Report($"Floor {floor} of {TierRules.FinalFloor}... (spent ${Game.Instance.Adventure.Spend.Usd:0.00})");
        var planner = _planner!;
        _ = Task.Run(async () =>
        {
            FloorResult? result = null;
            string? error = null;
            try { result = await planner.PlanFloorAsync(req, CancellationToken.None); } catch (Exception e) { error = e.Message; }
            Callable.From(() =>
            {
                if (result == null) { Finish(false, $"Floor {floor} failed: {error}"); return; }
                _floors[floor] = result.Plan;
                _nextFloor++;
                PlanNextFloor(withVoice);
            }).CallDeferred();
        });
    }

    private void FetchBarksThenWrite(bool withVoice)
    {
        Report("Writing the companions' bark bank...");
        var planner = _planner!;
        var party = DefaultParty();
        var campaign = _campaign!;
        _ = Task.Run(async () =>
        {
            List<BarkSet>? barks = null;
            try { barks = await planner.BarksAsync(campaign, party, CancellationToken.None); } catch { }
            Callable.From(() =>
            {
                campaign.Barks = barks ?? party.Select(p => OfflineDesigner.DefaultBarks(p.Name, p.ClassName)).ToList();
                WriteAndMaybeVoice(withVoice);
            }).CallDeferred();
        });
    }

    private void WriteAndMaybeVoice(bool withVoice)
    {
        var dir = ProjectSettings.GlobalizePath(BuiltinCampaign.UserDir);
        try { BuiltinCampaign.Write(dir, _campaign!, _floors); }
        catch (Exception e) { Finish(false, $"Could not write the campaign: {e.Message}"); return; }
        var spend = Game.Instance.Adventure.Spend;
        if (withVoice) StartVoiceFor(_campaign!, _floors, $"Campaign written (${spend.Usd:0.00}). ");
        else Finish(true, $"Campaign \"{_campaign!.Title}\" written: {_floors.Count} floors, ${spend.Usd:0.00} over {spend.Calls} calls.");
    }

    /// <summary>Render every line of the current built-in campaign (user copy first, else the shipped one) into the voice cache.</summary>
    public bool StartVoice()
    {
        if (Running) return false;
        if (Main.Instance == null || Main.Instance.InGame) { Report("Available from the title screen only."); return false; }
        if (BuiltinCampaign.Load() is not { } baked) { Report("There is no built-in campaign to voice yet."); return false; }
        var director = AdventureDirector.Instance;
        director.ConfigurePlanner(offlineOnly: true); // also configures the voice synth from settings
        if (VoiceRenderer.Instance is not { Enabled: true }) { Report($"Paste a {Settings.VoiceLabel} API key and enable voice first."); return false; }
        Running = true;
        LastSucceeded = false;
        _voiceOnly = true;
        _campaign = baked.Campaign;
        _floors = baked.Floors;
        StartVoiceFor(_campaign, _floors, "");
        return true;
    }

    private void StartVoiceFor(CampaignPlan campaign, Dictionary<int, FloorPlan> floors, string prefix)
    {
        var director = AdventureDirector.Instance;
        if (VoiceRenderer.Instance is not { Enabled: true })
        {
            Finish(true, prefix + $"No {Settings.VoiceLabel} key, so no voice was rendered.");
            return;
        }
        // only so many lines get audio: the plot first, barks last; the rest stay subtitles
        var picks = VoiceBudget.Select(campaign, floors, VoiceBudgetLines);
        var chosen = new HashSet<Line>(picks.Select(p => p.Line), ReferenceEqualityComparer.Instance);
        var candidates = VoiceBudget.Candidates(campaign, floors).Count;
        VoiceRenderer.Instance.Filter = line => chosen.Contains(line);
        director.EnqueueCampaignVoice(campaign);
        foreach (var plan in floors.Values) director.EnqueueFloorVoice(plan);
        VoiceRenderer.Instance.Filter = null;
        GD.Print($"[bake] {VoiceBudget.Describe(picks, candidates)}");
        // ids are now on the lines: write the files again so the shipped JSON references its audio
        try { BuiltinCampaign.Write(ProjectSettings.GlobalizePath(BuiltinCampaign.UserDir), campaign, floors); } catch { }
        _waitingForVoice = true;
        _lastVoiceReport = Time.GetTicksMsec();
        Report(prefix + $"Rendering {VoiceRenderer.Instance.Pending} voice lines ({VoiceBudget.Describe(picks, candidates)})...");
    }

    public override void _Process(double delta)
    {
        if (!Running || VoiceRenderer.Instance is not { } vr) return;
        if (!_waitingForVoice) return;
        if (vr.StoppedReason is { } stopped) { Finish(false, $"{stopped} ({vr.Rendered} lines rendered so far.)"); return; }
        if (vr.Pending > 0)
        {
            if (Time.GetTicksMsec() - _lastVoiceReport > 3000) { _lastVoiceReport = Time.GetTicksMsec(); Status = $"Rendering voice: {vr.Pending} lines left, {vr.Rendered} done{(vr.Failed > 0 ? $", {vr.Failed} failed" : "")}..."; }
            return;
        }
        Finish(true, $"Voice rendered: {vr.Rendered} lines{(vr.Failed > 0 ? $", {vr.Failed} failed" : "")}. The built-in campaign now speaks.");
    }

    private void Finish(bool ok, string status)
    {
        Running = false;
        _waitingForVoice = false;
        LastSucceeded = ok;
        Report(status);
        if (!_voiceOnly) Game.Instance.Adventure = new Save.AdventureDto();
        Main.Instance?.Menu?.RefreshSubtitle();
    }
}
