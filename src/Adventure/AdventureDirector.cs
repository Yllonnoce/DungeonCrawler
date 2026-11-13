using DungeonCrawler.Adventure.Ai;
using DungeonCrawler.Adventure.AiClients;
using DungeonCrawler.Adventure.Voice;
using DungeonCrawler.Core;
using DungeonCrawler.Dungeon.Generation;
using DungeonCrawler.Dungeon;
using DungeonCrawler.Enemies;
using DungeonCrawler.Save;
using DungeonCrawler.Items;
using Godot;

namespace DungeonCrawler.Adventure;

/// <summary>
/// Autoload that runs the adventure at play time: registers a floor's quests when it is built, feeds game events to
/// the quest machine, hands out rewards, starts hold-out waves, and keeps the HUD objective line current.
/// Planning (offline now, Claude later) lives in Main/DungeonBuilder; this class only consumes finished plans.
/// </summary>
public partial class AdventureDirector : Node
{
    public static AdventureDirector Instance { get; private set; } = null!;

    private int _currentFloor;
    private bool _hasFloor; // set once a floor is built; floor 0 (the tavern) is a floor to leave like any other
    public Dictionary<string, QuestState> Quests => Game.Instance.Adventure.Quests;

    // ---- planning ----------------------------------------------------------------------------
    public IPlanner Planner { get; private set; } = new OfflinePlanner();
    public string PlannerName { get; private set; } = "The offline designer";
    /// <summary>True while a non-offline planner is in use and has not been switched off for this run.</summary>
    public bool AiEnabled => Planner is not OfflinePlanner && Game.Instance.Adventure.AiStoppedReason == null;
    public FakePlanner? Fake => Planner as FakePlanner;
    private readonly Dictionary<int, CancellationTokenSource> _inFlight = new();
    private readonly HashSet<int> _abandoned = new();
    private CostMeter? _meter;

    /// <summary>Pick the planner from settings and debug flags. Called at New Game and after a load.</summary>
    /// <param name="offlineOnly">Play a campaign that is already written (the built-in one): no planner at all.</param>
    /// <param name="forceAi">Use the live planner even when the Settings toggle is off (a rebake from Settings, or automation).</param>
    /// <param name="noAi">Never call the planner (a plain New Game without a campaign on disk).</param>
    public void ConfigurePlanner(bool offlineOnly = false, bool forceAi = false, bool noAi = false)
    {
        foreach (var cts in _inFlight.Values) cts.Cancel();
        _inFlight.Clear();
        _abandoned.Clear();
        var debug = DebugTools.Instance;
        var spend = Game.Instance.Adventure.Spend;
        _meter = new CostMeter(spend, Settings.SpendCapUsd);
        var templates = PromptTemplates.Default;
        if (debug?.StoryBriefPath is { } briefPath)
        {
            try { templates = new PromptTemplates { Brief = File.ReadAllText(briefPath) }; GD.Print($"[Adventure] story brief: {briefPath} ({templates.Brief.Length} chars)"); }
            catch (Exception e) { GD.PushError($"[Adventure] story brief {briefPath} could not be read: {e.Message}"); }
        }
        if (offlineOnly || Game.Instance.Adventure.Mode == AdventureMode.Builtin)
        {
            Planner = new OfflinePlanner();
            PlannerName = "The built-in campaign";
        }
        else if (debug?.FakeAiDelay is { } delay)
        {
            Planner = new FakePlanner(delay);
            PlannerName = "A stand-in planner";
        }
        else if (debug?.ReplayDir is { } replay)
        {
            Planner = new ClaudePlanner(new ReplayClaudeClient(replay), _meter, templates) { Log = LogPlanning };
            PlannerName = "A recording of Claude";
        }
        else if (!noAi && debug?.NoAi != true && (Settings.AiPlanning || debug?.ForceAi == true || forceAi) && Settings.HasPlannerKey)
        {
            var viaRunware = Settings.PlannerViaRunware;
            IClaudeClient client = viaRunware ? new RunwareTextClient(Settings.RunwareKey.Trim()) { Log = LogPlanning } : new ClaudeSdkClient(Settings.ClaudeKey.Trim());
            var logDir = ProjectSettings.GlobalizePath(debug?.RecordDir ?? "user://ai/log");
            client = new RecordingClaudeClient(client, logDir);
            var model = viaRunware ? RunwareModel : Pricing.DefaultModel;
            Planner = new ClaudePlanner(client, _meter, templates) { Log = LogPlanning, Model = model };
            PlannerName = viaRunware ? $"{model} via Runware" : "Claude";
        }
        else
        {
            Planner = new OfflinePlanner();
            PlannerName = "The offline designer";
        }
        _voice.Configure();
        GD.Print($"[Adventure] planner: {PlannerName}{(Settings.HasPlannerKey ? "" : $" (no {Settings.PlannerLabel} key)")} cap=${Settings.SpendCapUsd:0.00}; voice {(_voice.Enabled ? "on" : "off")}");
    }

    /// <summary>The Runware planner model from Settings, or the default when the field is blank.</summary>
    public static string RunwareModel => string.IsNullOrWhiteSpace(Settings.RunwareTextModel) ? RunwareWire.DefaultTextModel : Settings.RunwareTextModel.Trim();

    // ---- voice ----------------------------------------------------------------------------------
    private double _lastBark = -100;
    private readonly Dictionary<(string, BarkTrigger), double> _barkCooldown = new();
    // unseeded on purpose: a fixed seed made every new game open with the same few lines, since the "said" record starts empty
    private readonly Random _barkRng = new();

    private static Characters.CharacterState? Member(int index)
    {
        var p = Main.Instance?.Party;
        return p != null && index >= 0 && index < p.Members.Length ? p.Members[index] : null;
    }

    /// <summary>Queue every spoken line of a floor plan with the right voice; ids land on the lines so saves keep them.</summary>
    public void EnqueueFloorVoice(FloorPlan plan)
    {
        var narrator = Game.Instance.Adventure.Campaign?.NarratorVoiceId ?? Voices.Narrator;
        _voice.Enqueue(plan.IntroLine, narrator, Speaker.Narrator, VoicePriority.Narrator);
        foreach (var n in plan.Narration) _voice.Enqueue(n.Line, narrator, Speaker.Narrator, VoicePriority.Narrator);
        if (plan.Boss is { } boss)
        {
            var villain = Game.Instance.Adventure.Campaign is { } c && Voice.NarrationRules.BossIsAntagonist(boss.Name, c.Antagonist.Name); // the antagonist keeps one voice as a boss
            foreach (var l in boss.AllLines())
            {
                if (villain) _voice.Enqueue(l, Voices.Villain, Speaker.Villain, VoicePriority.Boss, major: true, Game.Instance.Adventure.Campaign!.Antagonist.Persona);
                else _voice.Enqueue(l, Voices.Boss, Speaker.Boss, VoicePriority.Boss);
            }
        }
        var campaign = Game.Instance.Adventure.Campaign;
        foreach (var npc in plan.Npcs)
        {
            npc.VoiceId ??= Voices.ForNpc(npc.Name);
            var major = npc.CastId != null; // the cast gets the main model; passers-by the minor one
            var persona = npc.Persona ?? campaign?.CastFor(npc.CastId)?.Persona; // how they sound: their own words, else the cast member's
            foreach (var node in npc.Script.Nodes) _voice.Enqueue(node.Line, npc.VoiceId, Speaker.Npc, VoicePriority.Npc, major, persona);
            foreach (var aside in npc.Script.Nodes.Select(n => n.Aside).OfType<AsidePlan>()) // a companion's remark after the NPC's line, in the companion's voice
            {
                var cls = ClassIdOf(aside.Member);
                _voice.Enqueue(aside.Line, Voices.ForClass(cls), Speaker.Companion, VoicePriority.Bark, major: false, Voices.CompanionPersona(cls));
            }
            var quest = plan.Quests.FirstOrDefault(q => q.GiverNpc == npc.Id);
            if (quest != null) foreach (var l in new[] { quest.Lines.Offer, quest.Lines.Accept, quest.Lines.Decline, quest.Lines.Complete, quest.Lines.Fail }) _voice.Enqueue(l, npc.VoiceId, Speaker.Npc, VoicePriority.Npc, major, persona);
        }
        foreach (var t in plan.Taunts) EnqueueTaunt(t, campaign);
        foreach (var h in plan.Hordes) if (h.Line.Text.Length > 0) _voice.Enqueue(h.Line, narrator, Speaker.Narrator, VoicePriority.Narrator); // the nest's line is narrated when the horde pours
    }

    /// <summary>The antagonist's line: their voice, their persona, the clean twin rendered under the same id if the words are refused.</summary>
    private void EnqueueTaunt(TauntPlan t, CampaignPlan? campaign)
    {
        var line = Settings.VillainCurses || t.CleanLine == null ? t.Line : t.CleanLine;
        _voice.Enqueue(line, Voices.Villain, Speaker.Villain, VoicePriority.Boss, major: true, campaign?.Antagonist.Persona, fallbackText: t.CleanLine?.Text);
        if (!Settings.VillainCurses && t.CleanLine != null) _voice.Enqueue(t.Line, Voices.Villain, Speaker.Villain, VoicePriority.Bark, major: true, campaign?.Antagonist.Persona, fallbackText: t.CleanLine.Text);
    }

    public void EnqueueCampaignVoice(CampaignPlan campaign)
    {
        campaign.NarratorVoiceId ??= Voices.Narrator;
        foreach (var t in campaign.Tiers) _voice.Enqueue(t.EnterLine, campaign.NarratorVoiceId, Speaker.Narrator, VoicePriority.Narrator);
        Objectives.TauntBank.Fill(campaign);
        foreach (var t in campaign.Taunts) EnqueueTaunt(t, campaign);
        EnqueueBarks(campaign);
        EnqueueBanterVoice(campaign);
        EnqueueMerchantVoice(campaign);
    }

    /// <summary>The heroes' exchanges in their own class voices: the stock and unlockable banks are poured into the campaign first so the ids ship with it. Returns the line count.</summary>
    public int EnqueueBanterVoice(CampaignPlan campaign)
    {
        Voice.BanterBank.PourInto(campaign);
        var n = 0;
        foreach (var plan in campaign.Banter)
            foreach (var l in plan.Lines)
            {
                var cls = ClassIdOf(l.Member);
                l.Line.Delivery ??= Deliveries.Id(Voices.BarkDelivery(DungeonCrawler.Adventure.BarkTrigger.Travel));
                _voice.Enqueue(l.Line, Voices.ForClass(cls), Speaker.Companion, VoicePriority.Bark, major: false, Voices.CompanionPersona(cls));
                n++;
            }
        return n;
    }

    /// <summary>The merchant's lines in his cast member's voice: the briefing, every floor's safe-room lines and the gathering calls.</summary>
    public void EnqueueMerchantVoice(CampaignPlan campaign)
    {
        if (campaign.Merchant == null || campaign.MerchantCast() is not { } cast) return;
        // the persona's voice overrides the placeholder inside Enqueue
        foreach (var line in campaign.Merchant.AllLines()) _voice.Enqueue(line, Voices.Npcs[0], Speaker.Npc, VoicePriority.Npc, major: true, persona: cast.Persona);
    }

    /// <summary>A roster member's class id: the party's if it has bodies, else the default roster's, else empty.</summary>
    private static string ClassIdOf(string member)
    {
        var party = Main.Instance?.Party;
        return (party != null && GodotObject.IsInstanceValid(party) && party.Members.Length > 0
            ? party.Members.FirstOrDefault(m => m.Name == member)?.Class.Id
            : Party.PartyController.DefaultRoster.FirstOrDefault(r => r.Name == member).ClassId) ?? "";
    }

    private void EnqueueBarks(CampaignPlan campaign)
    {
        Voice.KillBarks.TopUpFighters(campaign); // the fighters' sets live in the campaign beside the party's
        foreach (var set in campaign.Barks)
        {
            string voice; Persona persona; var speaker = Speaker.Companion;
            if (Voice.KillBarks.IsFighter(set.Member)) { (voice, persona) = Voices.FighterVoice(set.Member); speaker = Speaker.Npc; }
            else
            {
                var classId = ClassIdOf(set.Member);
                voice = Voices.ForClass(classId ?? "");
                persona = Voices.CompanionPersona(classId ?? "");
                CampaignRepair.TopUpSet(set, classId ?? ""); // every stock bank (kill, idle, dance, travel, the reactive moments, the cheer) gets ids, and audio when the bank has it, like the written lines
            }
            foreach (var b in set.Lines)
            {
                b.Line.Delivery ??= Deliveries.Id(Voices.BarkDelivery(b.Trigger)); // a fight is shouted, the rest said
                // a line whose id is written in the campaign keeps it (the shipped bank was rendered under it); only a new line derives one from today's settings
                _voice.Enqueue(b.Line, voice, speaker, VoicePriority.Bark, major: false, persona);
            }
        }
    }

    /// <summary>Whether this member's body is in its victory dance (its dance line stands in for the kill bark).</summary>
    private static bool IsDancing(Characters.CharacterState m) => Main.Instance?.Party.Adventurers.FirstOrDefault(a => a.State == m)?.Dancing == true;

    /// <summary>
    /// A short in-character line from the bark bank, spoken if its audio exists; returns whether a line was said. Reactive
    /// barks are rate limited (a global gap, a per-member-per-trigger cooldown, a roll for a kill or a quick moment); a
    /// scripted line (banter, a cheer and its answers) skips the gaps but still sets the clock so nothing talks over it.
    /// </summary>
    private bool Bark(Characters.CharacterState? m, BarkTrigger trigger, bool scripted = false)
    {
        if (m == null || !Game.Instance.InGame) return false;
        var campaign = Game.Instance.Adventure.Campaign;
        if (campaign == null) return false;
        var set = campaign.Barks.FirstOrDefault(b => b.Member == m.Name);
        if (set == null) campaign.Barks.Add(set = new BarkSet { Member = m.Name }); // a hero the bank was not written for (a co-op player's own name): the stock speaks for their class
        var now = Game.Instance.PlaySeconds;
        // a kill is remarked on more freely than anything else: a quick cooldown, a roll, and a bank topped up from stock
        var kill = trigger == BarkTrigger.Kill;
        var dance = trigger == BarkTrigger.Dance; // every dance gets its word, on the kill's quick gaps and with no roll
        CampaignRepair.TopUpSet(set, m.Class.Id); // a campaign written before a trigger existed gets the stock lines for it, in this hero's voice
        if (!scripted)
        {
            if (now - _lastBark < (kill || dance ? Voice.KillBarks.GlobalGapSeconds : Voice.BarkRules.GlobalGapSeconds)) return false;
            if (_barkCooldown.TryGetValue((m.Name, trigger), out var last) && now - last < (kill || dance ? Voice.KillBarks.CooldownSeconds : Voice.BarkRules.PerTriggerCooldownSeconds)) return false;
            if (kill && _barkRng.NextDouble() >= Voice.KillBarks.Chance) return false;
            if (Voice.BarkRules.Quick.Contains(trigger) && _barkRng.NextDouble() >= Voice.BarkRules.QuickChance) return false; // a fight is full of perfect blows and parries
        }
        var lines = set.Lines.Where(l => l.Trigger == trigger && l.Line.Text != "").ToList();
        if (lines.Count == 0) return false;
        // a line said in the last two floors stays quiet; when every line for this trigger is that recent, the line said longest
        // ago comes back, except over a hero swap, which may go unremarked
        var said = Game.Instance.Adventure.BarksSaid;
        var texts = lines.Select(l => l.Line.Text).ToList();
        var text = Voice.BarkRules.UsesOldest(trigger) ? Voice.BarkRepeat.ChooseOrOldest(said, texts, _currentFloor, _barkRng) : Voice.BarkRepeat.Choose(said, texts, _currentFloor, _barkRng);
        if (text == null) return false;
        var line = lines.First(l => l.Line.Text == text).Line;
        line.Delivery ??= Deliveries.Id(Voices.BarkDelivery(trigger)); // a line topped up after the campaign loaded
        Voice.BarkRepeat.Said(said, text, _currentFloor);
        _barkCooldown[(m.Name, trigger)] = now;
        Say(m.Name, line);
        return true;
    }

    /// <summary>A companion's line goes out: the log, the bus (co-op clients hear it as a BarkMsg), the subtitle strip with the speaker's name, and the voice if it has one.</summary>
    private void Say(string speaker, Line line)
    {
        _lastBark = Game.Instance.PlaySeconds;
        EventBus.Instance.Log($"{speaker}: \"{line.Text}\"");
        EventBus.Instance.RaiseBark(speaker, line);
        Main.Instance?.Hud?.ShowBark(speaker, line.Text, Voice.BarkRules.SubtitleSeconds(line.Text));
        _voice.PlaySoon(line); // waits a few seconds for the narrator to finish rather than dropping the line
    }

    /// <summary>A recruited fighter's word over a kill, on the kill-bark rules; fighters have no voiced bank, so it is a log line and a subtitle.</summary>
    private void FighterBark(Npc npc)
    {
        if (Net.NetSession.IsClient || !Game.Instance.InGame) return;
        var campaign = Game.Instance.Adventure.Campaign;
        if (campaign == null || !Voice.KillBarks.IsFighter(npc.Id)) return;
        if (Voice.KillBarks.TopUpFighters(campaign) > 0) EnqueueBarks(campaign); // a campaign written before the fighters had lines
        var set = campaign.Barks.FirstOrDefault(b => b.Member == npc.Id);
        var barkLines = set?.Lines.Where(l => l.Trigger == BarkTrigger.Kill && l.Line.Text != "").ToList() ?? new List<BarkLine>();
        var lines = barkLines.Select(l => l.Line.Text).ToList();
        if (lines.Count == 0) return;
        var now = Game.Instance.PlaySeconds;
        if (now - _lastBark < Voice.KillBarks.GlobalGapSeconds) return;
        if (_barkCooldown.TryGetValue((npc.Id, BarkTrigger.Kill), out var last) && now - last < Voice.KillBarks.CooldownSeconds) return;
        if (_barkRng.NextDouble() >= Voice.KillBarks.Chance) return;
        var said = Game.Instance.Adventure.BarksSaid;
        var text = Voice.BarkRepeat.ChooseOrOldest(said, lines, _currentFloor, _barkRng);
        if (text == null) return;
        Voice.BarkRepeat.Said(said, text, _currentFloor);
        _barkCooldown[(npc.Id, BarkTrigger.Kill)] = now;
        var line = barkLines.First(l => l.Line.Text == text).Line; // carries the id the bank was rendered under
        Say(npc.NpcName, line);
    }

    /// <summary>
    /// Queue every kill line for rendering: each party member's whole Kill set (written and stock) in the class voice,
    /// and each fighter's stock in their own. Returns how many lines were queued. Used by --bake-kill-barks.
    /// </summary>
    public int EnqueueKillBarkVoice(CampaignPlan campaign)
    {
        EnqueueBarks(campaign); // tops up the party's and the fighters' sets and gives every bark line its id
        return campaign.Barks.Sum(set => set.Lines.Count(l => l.Line.VoiceId != null));
    }

    /// <summary>A client voicing a bark the server chose: the speaker's name on the subtitle strip, the voice if the line has one.</summary>
    public void PlayBark(int member, Line line)
    {
        var members = Main.Instance?.Party?.Members;
        var name = members != null && member >= 0 && member < members.Length ? members[member].Name : "";
        Main.Instance?.Hud?.ShowBark(name, line.Text, Voice.BarkRules.SubtitleSeconds(line.Text));
        _voice.PlayLine(line, interrupt: false);
    }

    private void LogPlanning(string line)
    {
        GD.Print($"[Adventure] {line}");
        if (Planner is ClaudePlanner { LastFailure: { } failure } && failure.Contains("cap"))
            Game.Instance.Adventure.AiStoppedReason = failure;
    }

    public bool IsPlanning(int floor) => _inFlight.ContainsKey(floor);

    /// <summary>The floor is being built without its AI plan; drop the result when it arrives.</summary>
    public void AbandonPlanning(int floor, string why)
    {
        if (!_inFlight.Remove(floor, out var cts)) return;
        _abandoned.Add(floor);
        cts.Cancel();
        GD.Print($"[Adventure] floor {floor}: plan abandoned ({why}); designed offline instead");
    }

    private static List<PartyMemberInfo> PartyInfo()
        => Main.Instance?.Party?.Members.Select(m => new PartyMemberInfo(m.Name, m.Class.Name)).ToList() ?? new List<PartyMemberInfo>();

    /// <summary>Outline + floor 1 on a worker thread; the callback runs on the main thread.</summary>
    public void PrepareCampaign(Action<string> status, Action<CampaignPlan, FloorPlan?> done)
    {
        var game = Game.Instance;
        var builder = Main.Instance!.Builder;
        var seed1 = Rng.FloorSeed(game.GameSeed, 1);
        var spec1 = builder.SpecFor(1);
        var layout1 = FloorGenerator.Generate(spec1, seed1);
        var tiers = game.TierProfiles();
        var req = new OutlineRequest(game.GameSeed, tiers, builder.Roster, PartyInfo(), layout1, spec1, seed1, Main.Instance.Party.Snapshot(), builder.ThemeProfiles());
        var planner = Planner;
        var cts = new CancellationTokenSource();
        _inFlight[1] = cts;
        var started = Time.GetTicksMsec();
        _ = Task.Run(async () =>
        {
            OutlineResult result;
            try { result = await planner.OutlineAsync(req, cts.Token); }
            catch (Exception e)
            {
                GD.PushWarning($"[Adventure] outline failed: {e.Message}");
                result = await new OfflinePlanner().OutlineAsync(req, CancellationToken.None);
            }
            Callable.From(() =>
            {
                _inFlight.Remove(1);
                var secs = (Time.GetTicksMsec() - started) / 1000f;
                GD.Print($"[Adventure] campaign \"{result.Campaign.Title}\" ({result.Campaign.Source}) and floor 1 ({result.Floor1.Source}) ready in {secs:0.0} s; {result.Floor1Report}");
                status($"Ready in {secs:0.0} s.");
                foreach (var r in CampaignRepair.Apply(result.Campaign)) GD.Print($"[Adventure] campaign repaired: {r}");
                done(result.Campaign, result.Floor1);
                EnqueueCampaignVoice(result.Campaign);
                if (planner is not OfflinePlanner) FetchBarks(result.Campaign);
            }).CallDeferred();
        });
    }

    /// <summary>Write (or rewrite) the bark bank for a campaign in the background; a resumed bake needs it.</summary>
    public void RequestBarks(CampaignPlan campaign) => FetchBarks(campaign);

    private void FetchBarks(CampaignPlan campaign)
    {
        var planner = Planner;
        var party = PartyInfo();
        _ = Task.Run(async () =>
        {
            List<BarkSet> barks;
            try { barks = await planner.BarksAsync(campaign, party, CancellationToken.None); }
            catch (Exception e) { GD.PushWarning($"[Adventure] barks failed: {e.Message}"); return; }
            Callable.From(() => { if (Game.Instance.Adventure.Campaign == campaign) { campaign.Barks = barks; EnqueueBarks(campaign); GD.Print($"[Adventure] bark bank ready: {barks.Sum(b => b.Lines.Count)} lines"); } }).CallDeferred();
        });
    }

    /// <summary>Start writing a floor's plan in the background if nothing is stored or running for it.</summary>
    public void Prefetch(int floor)
    {
        var game = Game.Instance;
        if (!AiEnabled || floor < 1 || floor > Dungeon.TierRules.FinalFloor) return;
        if (game.Adventure.FloorPlans.ContainsKey(floor) || _inFlight.ContainsKey(floor) || game.Adventure.Campaign == null) return;
        var builder = Main.Instance!.Builder;
        var seed = Rng.FloorSeed(game.GameSeed, floor);
        var spec = builder.SpecFor(floor);
        var layout = FloorGenerator.Generate(spec, seed);
        var req = new FloorRequest(game.Adventure.Campaign, layout, spec, builder.TierFor(floor)?.ToProfile(), builder.Roster, floor, seed, Main.Instance.Party.Snapshot(), MemorySummary(floor));
        var planner = Planner;
        var cts = new CancellationTokenSource();
        _inFlight[floor] = cts;
        _abandoned.Remove(floor);
        var started = Time.GetTicksMsec();
        GD.Print($"[Adventure] floor {floor}: planning started ({PlannerName})");
        _ = Task.Run(async () =>
        {
            FloorResult? result = null;
            string? error = null;
            try { result = await planner.PlanFloorAsync(req, cts.Token); }
            catch (OperationCanceledException) { error = "cancelled"; }
            catch (Exception e) { error = e.Message; }
            Callable.From(() =>
            {
                _inFlight.Remove(floor);
                if (_abandoned.Remove(floor) || error == "cancelled") return;
                if (result == null) { GD.PushWarning($"[Adventure] floor {floor}: planning failed: {error}"); return; }
                if (game.Adventure.FloorPlans.ContainsKey(floor)) return;
                game.Adventure.FloorPlans[floor] = result.Plan;
                EnqueueFloorVoice(result.Plan);
                if (result.Report.Repairs.Count > 0) _pendingRepairs[floor] = result.Report.Repairs.ToList();
                GD.Print($"[Adventure] floor {floor}: plan ready ({result.Plan.Source}) in {(Time.GetTicksMsec() - started) / 1000f:0.0} s; {result.Report}");
            }).CallDeferred();
        });
    }

    // ---- campaign memory -----------------------------------------------------------------------
    private CampaignMemoryDto Memory => Game.Instance.Adventure.Memory;
    private FloorRecord? _record;
    private (int Floor, Run.FloorCardModel Card)? _pendingFloorCard;
    /// <summary>A session is torn down: nothing of its ledger or its pending floor card carries into the next.</summary>
    public void ForgetSession() { _pendingFloorCard = null; _record = null; _hasFloor = false; _heldRecap = null; }
    /// <summary>The last floor card shown (tests).</summary>
    public Run.FloorCardModel? LastFloorCard { get; private set; }
    private double _floorStart;
    private string _leadName = "";
    private readonly Dictionary<int, List<string>> _pendingRepairs = new();

    public string MemorySummary(int upToFloor) => Adventure.MemorySummary.Build(Memory, upToFloor);

    private void OpenRecord(int floor)
    {
        _record = Memory.For(floor);
        _record.SecretsTotal = Main.Instance?.Builder.Layout?.Secrets.Count ?? 0; // the floor card's "Secrets 1/3" (fortune)
        _floorStart = Game.Instance.PlaySeconds;
        if (_pendingRepairs.Remove(floor, out var repairs)) _record.Repairs = repairs;
        var party = Main.Instance?.Party;
        _leadName = party != null && party.HasBodies ? party.Lead.State.Name : "";
    }

    private void CloseRecord()
    {
        if (_record == null) return;
        var party = Main.Instance?.Party;
        _record.Seconds += (float)(Game.Instance.PlaySeconds - _floorStart);
        _floorStart = Game.Instance.PlaySeconds;
        if (party != null && party.Members.Length > 0)
            _record.ExitHpFraction = party.Members.Sum(m => m.Hp) / Math.Max(1f, party.Members.Sum(m => m.Stats[Characters.Stat.MaxHp]));
        _record.Completed = true;
        _record.WorstMoment = Run.LedgerRules.WorstMoment(_record);
        EventBus.Instance.RaiseFloorLedgerClosed(_record.Floor, (int)_record.Seconds);
        if (_record.Floor >= 1 && !Net.NetSession.IsClient)
        {   // the floor card for the floor just left, shown once the next (deeper) floor stands
            var lead = Game.Instance.Adventure.Campaign?.Barks.FirstOrDefault(b => b.Member == _leadName);
            var reaction = Run.RecapBuilder.Reaction(lead, Game.Instance.Adventure.BarksSaid, _record.Floor, _barkRng);
            _pendingFloorCard = (_record.Floor, Run.RecapBuilder.FloorCard(_record, _record.Title != "" ? _record.Title : $"Floor {_record.Floor}", reaction));
        }
    }

    // ---- floor narration: the entries a floor's plan keeps for rooms, the boss and the quests ----------------------
    private readonly List<(double At, string Text, string? Voice, string Key)> _narrationQueue = new();
    private readonly HashSet<string> _narrationQueued = new();
    private double _narrationFree, _roomNarrationCheck;
    private readonly Voice.SpeechClock _speech = new(); // every line raised as narration holds the one voice channel for its recording's length (HeardSeconds)

    /// <summary>How long a line holds the voice channel: its recording's real length and a breath, else the estimate.</summary>
    private double Heard(string text, string? voiceId) => Voice.NarrationRules.HeardSeconds(text, _voice.LengthOf(voiceId));
    private (double? DrainedAt, Action Show)? _heldRecap; // the victory recap, waiting for the closing narration; DrainedAt once the last entry is raised

    /// <summary>Nothing queued survives a floor change, a load or a wipe: the line was not heard, so its key was never recorded.</summary>
    private void ClearNarrationQueue() { _deferred.Clear(); _narrationQueue.Clear(); _narrationQueued.Clear(); _narrationFree = 0; _speech.Reset(); }

    /// <summary>
    /// The last boss is dead: when its death queued narration (the closing lines), the victory recap waits until that has
    /// drained and been heard (at most <see cref="Voice.NarrationRules.RecapCapSeconds"/> after the drain). False when nothing waits: show it now.
    /// Server and solo: clients get the narration and then the recap over the wire, as before.
    /// </summary>
    public bool HoldRecapForNarration(Action show)
    {
        if (Net.NetSession.IsClient || !Voice.NarrationRules.HoldsRecap(_narrationQueue.Count)) return false;
        _heldRecap = (null, show);
        GD.Print($"[Adventure] the victory recap waits for {_narrationQueue.Count} closing line(s)");
        return true;
    }

    /// <summary>Nothing queued, deferred or being said: the floor's opening has been heard (tests).</summary>
    public bool NarrationIdle => _deferred.Count == 0 && _narrationQueue.Count == 0 && _speech.Free(Game.Instance.PlaySeconds);

    /// <summary>Whether a victory recap is waiting for the closing narration (tests).</summary>
    public bool RecapHeld => _heldRecap != null;

    /// <summary>Queue the plan's entries due for this trigger (for RoomEnter, any of the rooms), in plan order. Server and solo only: clients hear them as narration messages. The key is recorded when the line plays.</summary>
    private void PlayNarration(NarrationTrigger trigger, IReadOnlyList<int?> rooms, double lead = 0)
    {
        if (Net.NetSession.IsClient || !Game.Instance.InGame || Main.Instance?.Builder is not { Contents.Plan: { } plan } builder || plan.Narration.Count == 0) return;
        if (builder.CurrentFloor != _currentFloor) return; // the old floor is still built while the next one is written: it is not this floor's to speak
        var said = new HashSet<string>(Game.Instance.Adventure.BarksSaid.Keys);
        said.UnionWith(_narrationQueued);
        foreach (var room in rooms)
            foreach (var n in Voice.NarrationRules.Due(plan, trigger, room, Memory.Flags, said, builder.CurrentFloor))
            {
                var key = Voice.NarrationRules.Key(builder.CurrentFloor, n);
                said.Add(key);
                _narrationQueued.Add(key);
                var at = Math.Max(Game.Instance.PlaySeconds + lead, _narrationFree);
                _narrationFree = at + Heard(n.Line.Text, n.Line.VoiceId);
                _narrationQueue.Add((at, n.Line.Text, n.Line.VoiceId, key));
            }
    }

    /// <summary>How long the boss's own intro or death line takes to be heard: its narration waits that long.</summary>
    private double BossSpeakSeconds(bool death)
    {
        if (Main.Instance?.Builder.Contents?.Plan?.Boss is not { } b) return 0;
        var line = death ? b.DeathLineFor(Memory.Flags) : b.IntroLine;
        return line == null || line.Text == "" ? 0 : Heard($"{b.Name}: {line.Text}", line.VoiceId);
    }

    private static readonly int?[] NoRoom = { null };

    private void TickNarration()
    {
        var now = Game.Instance.PlaySeconds;
        for (var i = 0; i < _deferred.Count; i++)
            if (_deferred[i].At <= now) { var d = _deferred[i].Do; _deferred.RemoveAt(i--); d(); }
        // one line at a time: an entry waits until whatever was raised last (a taunt, a boss line, the entry before it) has been heard
        while (_narrationQueue.Count > 0 && _narrationQueue[0].At <= now && _speech.Free(now))
        {
            var (_, text, voice, key) = _narrationQueue[0];
            _narrationQueue.RemoveAt(0);
            _narrationQueued.Remove(key);
            Game.Instance.Adventure.BarksSaid[key] = Voice.BarkRepeat.Forever; // heard now: it never plays again this run
            EventBus.Instance.RaiseNarration(text, voice);
        }
        if (_heldRecap is { } held)
        {
            if (held.DrainedAt == null && _narrationQueue.Count == 0) _heldRecap = held = (now, held.Show);
            if (Voice.NarrationRules.RecapDue(now, held.DrainedAt, _narrationQueue.Count, _speech.Until))
            {
                _heldRecap = null;
                if (Game.Instance.InGame) held.Show();
            }
        }
        if (Net.NetSession.IsClient || !Game.Instance.InGame || now < _roomNarrationCheck || Main.Instance is not { } main || main.Party is not { HasBodies: true } party || main.Builder.Layout is not { } layout || main.Builder.Contents?.Plan is not { } plan) return;
        _roomNarrationCheck = now + 0.25;
        if (!plan.Narration.Any(n => n.Trigger == NarrationTrigger.RoomEnter)) return;
        var rooms = party.Active.Where(a => !a.Downed).Select(a => layout.RoomAt(DungeonBuilder.WorldToCell(a.GlobalPosition))).OfType<Dungeon.Generation.Room>().Select(r => (int?)layout.Rooms.IndexOf(r)).Distinct().ToList();
        if (rooms.Count > 0) PlayNarration(NarrationTrigger.RoomEnter, rooms);
    }

    public override void _Process(double delta)
    {
        TickNarration();
        TickIdleTaunt();
        TickIdleRazz();
        TickTravel();
        TickTalk();
        if (_record == null || !Game.Instance.InGame || _leadName == "") return;
        _record.PossessSeconds[_leadName] = _record.PossessSeconds.GetValueOrDefault(_leadName) + (float)delta;
    }

    /// <summary>Settings screen: a tiny structured request with the given key through the given provider; the result text arrives on the main thread.</summary>
    public void TestConnection(string key, bool viaRunware, Action<string> result)
    {
        if (key.Trim().Length < 10) { result($"Paste a {(viaRunware ? "Runware" : "Claude")} API key first."); return; }
        IClaudeClient client = viaRunware ? new RunwareTextClient(key.Trim()) : new ClaudeSdkClient(key.Trim());
        if (client is RunwareTextClient rw) rw.Log = line => GD.Print($"[planner] {line}");
        var request = new ClaudeRequest("ping", viaRunware ? RunwareModel : Pricing.DefaultModel, Array.Empty<PromptBlock>(), "Reply with a JSON object whose Reply field is the single word ok.", PlanSchemas.For<Ping>(), "low", viaRunware ? 256 : 64);
        _ = Task.Run(async () =>
        {
            var r = await client.CreateAsync(request, CancellationToken.None);
            var text = r.Error != null ? $"Failed: {r.Error}" : r.StopReason == "refusal" ? "The request was refused." : $"Connected ({r.Usage.InputTokens} in / {r.Usage.OutputTokens} out tokens, {Pricing.Cost(r.Usage) * 100:0.00} cents).";
            Callable.From(() => result(text)).CallDeferred();
        });
    }

    public sealed class Ping { public string Reply { get; set; } = ""; }

    private VoiceRenderer _voice = null!;

    public override void _Ready()
    {
        Instance = this;
        _voice = new VoiceRenderer { Name = "Voice" };
        AddChild(_voice);
        AddChild(new CampaignBaker { Name = "Baker" });
        var bus = EventBus.Instance;
        bus.BossSeen += boss =>
        {
            var b = Main.Instance?.Builder.Contents?.Plan?.Boss;
            if (b is { } spoken && spoken.IntroLine.Text != "") EventBus.Instance.RaiseNarration($"{spoken.Name}: {spoken.IntroLine.Text}", spoken.IntroLine.VoiceId);
            if (b == null || b.IntroLine.Text != "" || !NarrationDue(NarrationTrigger.BossSeen)) Bark(Main.Instance?.Party?.Lead.State, BarkTrigger.BossSeen); // a silent boss's sighting is the narrator's at once: no bark for it to cut
            PlayNarration(NarrationTrigger.BossSeen, NoRoom, BossSpeakSeconds(death: false));
        };
        bus.BossDefeated += _ => { if (Main.Instance?.Builder.Contents?.Plan?.Boss is { } b && b.DeathLineFor(Memory.Flags) is { } last && last.Text != "") EventBus.Instance.RaiseNarration($"{b.Name}: {last.Text}", last.VoiceId); PlayNarration(NarrationTrigger.BossDead, NoRoom, BossSpeakSeconds(death: true)); };
        bus.BossHalf += _ => { if (Main.Instance?.Builder.Contents?.Plan?.Boss is { } b && b.HalfLineFor(Memory.Flags) is { } half && half.Text != "") EventBus.Instance.RaiseNarration($"{b.Name}: {half.Text}", half.VoiceId); };
        bus.SecretFound += _ => { if (_record != null) _record.SecretsFound++; }; // fortune: the floor card's count
        bus.MemberKill += m => { if (!IsDancing(m)) Bark(m, BarkTrigger.Kill); }; // a dancer has already crowed
        bus.MemberDance += i => { var members = Main.Instance?.Party.Members; if (members != null && i >= 0 && i < members.Length) Bark(members[i], BarkTrigger.Dance); };
        bus.NpcKill += FighterBark;
        bus.MemberDamaged += (i, amount) => { var m = Member(i); if (m != null && amount >= m.Stats[Characters.Stat.MaxHp] * Voice.BarkRules.HurtFraction && !_lowHp.Due(i, m.Hp, m.Stats[Characters.Stat.MaxHp], _currentFloor)) Bark(m, BarkTrigger.Hurt); }; // a hit that leaves them badly hurt is LowHp's to remark on
        bus.ItemPickedUp += _ => Bark(Main.Instance?.Party?.Lead.State, BarkTrigger.Loot);
        bus.LevelUp += i => Bark(Member(i), BarkTrigger.LevelUp);
        bus.LeadChanged += i => Bark(Member(i), BarkTrigger.Possess);
        bus.MemberDowned += i => { Bark(Member(i), BarkTrigger.Downed); if (Member(i) is not { Mangled: true }) Taunt(Objectives.TauntTrigger.MemberDowned, Main.Instance?.Builder.Layout?.Objective?.Kind ?? FloorObjectiveKind.None, Member(i)?.Name); }; // a crushing gets its own gloat
        bus.MemberKilledByHazard += (i, _) => Taunt(Objectives.TauntTrigger.Crushed, Main.Instance?.Builder.Layout?.Objective?.Kind ?? FloorObjectiveKind.None, Member(i)?.Name, urgent: true);
        bus.EnemyKilledByHazard += (e, _) =>
        {
            var witness = Main.Instance?.Party?.Active.Where(a => !a.Downed).OrderBy(a => a.GlobalPosition.DistanceSquaredTo(e.GlobalPosition)).FirstOrDefault();
            if (witness != null && witness.GlobalPosition.DistanceTo(e.GlobalPosition) <= 10f) Bark(witness.State, BarkTrigger.Kill);
        };
        bus.PartyWiped += ClearNarrationQueue;
        bus.PartyWiped += () => Taunt(Objectives.TauntTrigger.PartyWiped, Main.Instance?.Builder.Layout?.Objective?.Kind ?? FloorObjectiveKind.None, urgent: true);
        bus.ObjectiveEvent += OnObjectiveEvent;
        bus.Narration += (text, voiceId) => { var at = Game.Instance.PlaySeconds; var heard = Heard(text, voiceId); _taunts.NoteNarration(at, heard); _speech.Spoke(at, heard); }; // the villain waits until a line has been heard, and so does the next queued entry
        bus.MemberRevived += i => Bark(Member(i), BarkTrigger.Revived);
        bus.EnemyDied += OnEnemyDied;
        bus.QuestItemPicked += id => Apply(new QuestItemPicked(QuestKeys.QuestOf(id))); // clue items carry an ordinal after the quest key
        bus.NpcEvent += OnNpcEvent;
        bus.DialogueEffectChosen += OnDialogueEffect;
        bus.DialogueNode += OnDialogueNode;
        bus.FloorChanged += OnFloorChanged;
        bus.Gathered += OnGathered;
        bus.HitLanded += (m, r) => { if (_record == null) return; _record.DamageDealt += r.Amount; if (r.Sneak) _record.SneakHits++; _record.DamageBy[m.Name] = _record.DamageBy.GetValueOrDefault(m.Name) + r.Amount; };
        bus.MemberDamaged += (_, amount) => { if (_record != null) _record.DamageTaken += amount; };
        bus.MemberDowned += _ => { if (_record != null) _record.Downs++; };
        // run-structure's ledger: who did what, what hurt most, who fell to what, what wiped the party
        bus.MemberKill += m => { if (_record != null) _record.KillsBy[m.Name] = _record.KillsBy.GetValueOrDefault(m.Name) + 1; };
        bus.MemberDowned += i => { if (_record == null || Member(i) is not { } m) return; _record.DownsBy[m.Name] = _record.DownsBy.GetValueOrDefault(m.Name) + 1; _record.Falls.Add(new FallRecord { Member = m.Name, Cause = m.LastHarm?.CauseId ?? Run.HarmCauses.Generic, Killer = m.LastHarm?.Name ?? "" }); };
        bus.MemberDamaged += (i, amount) => { if (_record == null || Member(i) is not { } m || amount <= _record.BiggestHit) return; _record.BiggestHit = amount; _record.BiggestHitOn = m.Name; _record.BiggestHitBy = m.LastHarm?.Name ?? ""; };
        bus.MemberRescued += (_, rescuer) => { if (_record == null) return; _record.Rescues++; if (Member(rescuer) is { } r) _record.RescuesBy[r.Name] = _record.RescuesBy.GetValueOrDefault(r.Name) + 1; };
        bus.PartyWipedBy += (floor, cause) => { if (_record != null && floor == _record.Floor) _record.WipeCauses.Add(cause); };
        bus.GoldFound += n => { if (_record != null) _record.GoldFound += n; };
        bus.PartyWiped += () => { if (_record != null) _record.Wipes++; };
        bus.AbilityUsed += (_, kind) => { if (_record == null) return; if (kind is Combat.AbilityKind.WeaponAttack or Combat.AbilityKind.MeleeStrike) _record.WeaponAttacks++; else _record.SpellCasts++; };
        bus.LeadChanged += index => { var p = Main.Instance?.Party; if (p != null && index < p.Members.Length) _leadName = p.Members[index].Name; };
        bus.BossDefeated += _ => { if (_record != null) _record.BossSeconds = (float)(Game.Instance.PlaySeconds - _floorStart); };
        WireTalk(bus); // last: its PartyWiped handler reads the record's wipe count after the line above has counted this one
    }

    /// <summary>The tavern merchant's briefing reached the deal: the seed money, once. The server's (or solo's) to pay; a client's gold follows.</summary>
    private void OnDialogueNode(string npcId, string nodeId)
    {
        if (Net.NetSession.IsClient || Game.Instance.CurrentFloor != 0 || npcId != "merchant") return;
        if (TavernRules.ShouldPaySeed(Game.Instance.Adventure.SeedMoneyPaid, nodeId, descending: false)) PaySeedMoney();
    }

    /// <summary>The party goes down from the tavern: if they never struck the deal, they are staked anyway.</summary>
    public void OnLeavingTavern()
    {
        if (Net.NetSession.IsClient) return;
        if (TavernRules.ShouldPaySeed(Game.Instance.Adventure.SeedMoneyPaid, null, descending: true)) PaySeedMoney();
    }

    private static void PaySeedMoney()
    {
        if (Main.Instance?.Party is not { } party) return;
        party.AddGold(TavernRules.SeedGold);
        Game.Instance.Adventure.SeedMoneyPaid = true;
        EventBus.Instance.Log("The seed money: 150 gold, counted twice.");
    }

    private void OnFloorChanged(int floor, int delta)
    {
        if (_hasFloor && _currentFloor != floor && Game.Instance.InGame)
        {
            Apply(new FloorLeft(_currentFloor));
            CloseRecord();
            if (delta > 0 && Objectives.ObjectiveController.Instance is { Completed: true } done) _pendingDescend = done.Kind;
            if (delta > 0 && Game.Instance.Adventure.FloorPlans.TryGetValue(_currentFloor, out var left)) _leftDescend = Objectives.TauntDirector.DescendBank(left.Taunts, Memory.Flags);
        }
        _currentFloor = floor;
        _hasFloor = true;
    }

    private FloorObjectiveKind? _pendingDescend;
    private double _lastTauntSeconds; // how long the last taunt raised holds the channel: what the next floor's opening waits for
    private readonly List<(double At, Action Do)> _deferred = new(); // what opens a floor, held until the parting taunt has been heard
    private List<TauntPlan> _leftDescend = new(); // the floor just left: its own Descend taunts the flags allow

    private int _gatheringRemarks; // how many of this floor build's remarks he has made; reset in OnFloorBuilt (the gathered count restarts there too)

    /// <summary>More creatures gathered outside the safe room: the merchant says the next of his lines, each once per floor.</summary>
    private void OnGathered(int creaturesTotal)
    {
        var due = SafeRoomRules.GatheringRemarkAt.Count(t => creaturesTotal >= t);
        if (due <= _gatheringRemarks) return;
        _gatheringRemarks = due; // a jump past two totals at once says only the latest
        var campaign = Game.Instance.Adventure.Campaign;
        if (campaign == null || Main.Instance?.Builder.FloorRoot?.GetNodeOrNull<Npc>("Npc_merchant") is not { } merchant) return;
        var plan = campaign.MerchantOrGeneric();
        if (due - 1 >= plan.Gathering.Count) return;
        var line = plan.Gathering[due - 1];
        merchant.Say(line);
        if (Net.NetSession.IsServer) Net.NetSession.Instance.Server?.Broadcast(new Net.NpcSay("merchant", line)); // the clients hear him too
    }

    /// <summary>Called by Main once a floor's nodes exist and saved state has been applied.</summary>
    public void OnFloorBuilt(FloorContents contents, int floor, bool firstVisit)
    {
        _currentFloor = floor;
        _hasFloor = true;
        ClearNarrationQueue(); // a descent, a load or a wipe's rebuild: lines queued for the floor before were never heard
        _gatheringRemarks = 0; // every build or load starts the gathering count again
        _tavernNext = floor == 0 ? Game.Instance.PlaySeconds + Voice.BanterRules.TavernFirstSeconds : double.MaxValue; // the tavern's first exchange
        OpenRecord(floor);
        if (_pendingFloorCard is { } pending)
        {
            _pendingFloorCard = null;
            if (floor > pending.Floor)
            {   // a descent: the card for the floor left behind, on every screen
                LastFloorCard = pending.Card;
                Main.Instance?.Hud?.ShowFloorCard(pending.Card);
                if (Net.NetSession.IsServer) Net.NetSession.Instance.Server?.Broadcast(new Net.FloorCardEvt(pending.Card));
                GD.Print($"[Adventure] floor card: {pending.Card.Title} {pending.Card.Time} kills={pending.Card.Kills} elites={pending.Card.Elites} secrets={pending.Card.Secrets} downs={pending.Card.Downs} gold={pending.Card.GoldFound} worst=\"{pending.Card.WorstMoment}\" reaction={pending.Card.Reaction}");
            }
        }
        var own = _leftDescend; _leftDescend = new();
        var ownSpoke = own.Count > 0 && Taunt(Objectives.TauntTrigger.Descend, urgent: true, bankOverride: own); // the left floor's own line, objective or not
        var partingSpoke = ownSpoke;
        if (_pendingDescend is { } left) { _pendingDescend = null; if (!ownSpoke) partingSpoke = Taunt(Objectives.TauntTrigger.Descend, left); }
        // an objective floor opens with the villain's line instead of the narrator's, plan or no plan
        var objectiveKind = Main.Instance?.Builder.Layout?.Objective?.Kind ?? FloorObjectiveKind.None;
        // the order on arrival: the parting taunt of the floor left, then (on a tier's first floor, once a run) the tier's
        // entry line, then the floor's opening; each waits until the line before it has been heard
        var now = Game.Instance.PlaySeconds;
        var tier = firstVisit ? TierLineDue(floor) : null;
        var (tierAt, openAt) = Voice.NarrationRules.Arrival(now, partingSpoke ? _lastTauntSeconds : null, tier == null ? null : Heard(tier.EnterLine.Text, tier.EnterLine.VoiceId));
        if (tier != null && tierAt is { } at)
        {
            if (at <= now) SayTierLine(tier);
            else _deferred.Add((at, () => { if (_currentFloor == floor) SayTierLine(tier); }));
        }
        var holdOpening = firstVisit && openAt > now;
        var villainSpoke = !holdOpening && firstVisit && OpeningTaunt(objectiveKind, contents.Plan);
        if (holdOpening)
        {
            var intro = contents.Plan?.IntroLine;
            var plan0 = contents.Plan;
            _deferred.Add((openAt, () =>
            {
                if (_currentFloor != floor) return;
                if (OpeningTaunt(objectiveKind, plan0)) return;
                if (intro != null && intro.Text != "") EventBus.Instance.RaiseNarration(intro.Text, intro.VoiceId);
            }));
        }
        if (contents.Plan is { } plan)
        {
            _record!.Title = plan.Title;
            _record.Source = plan.Source;
            EnqueueFloorVoice(plan);
            foreach (var q in plan.Quests)
            {
                var key = QuestKeys.For(floor, q.Id);
                if (Quests.ContainsKey(key)) continue;
                if (!FlagRules.Allowed(q.RequiresFlag, q.ForbidsFlag, Memory.Flags)) continue;
                if (!contents.Npcs.Any(n => n.Id == q.GiverNpc)) continue; // the giver stayed away (flag-gated)
                var target = contents.Collectives.FirstOrDefault(c => c.Id == q.Params.TargetCollective);
                var state = QuestMachine.Create(q, plan, floor, target?.MemberSpawnIndices.Count ?? 1);
                state.Id = key;
                Quests[key] = state;
            }
            if (firstVisit && !villainSpoke && !holdOpening && plan.IntroLine.Text != "") EventBus.Instance.RaiseNarration(plan.IntroLine.Text, plan.IntroLine.VoiceId);
            foreach (var q in Quests.Values.Where(q => q.Floor == floor && q.Status == QuestStatus.Active))
            {
                if (q.Template == QuestTemplate.Escort && FindNpc(q.EscortNpc) is { Escorting: false, Rescued: false, Dead: false } npc) npc.StartFollowing();
                if (q.Template == QuestTemplate.HoldOut) StartWaves(q);
            }
        }
        RefreshObjective();
        Prefetch(floor + 1);
        _lastObjectiveActivity = Game.Instance.PlaySeconds;
    }

    /// <summary>This floor's tier entry line when it opens a tier and has not been heard this run; null otherwise.</summary>
    private TierPlan? TierLineDue(int floor)
        => Game.Instance.Adventure.Campaign is { } c && Voice.NarrationRules.TierEntry(c, floor) is { } t && !Game.Instance.Adventure.BarksSaid.ContainsKey(Voice.NarrationRules.TierKey(t)) ? t : null;

    /// <summary>The narrator's tier line, in its own recording; remembered forever so a revisit or a reload stays quiet.</summary>
    private void SayTierLine(TierPlan tier)
    {
        Game.Instance.Adventure.BarksSaid[Voice.NarrationRules.TierKey(tier)] = Voice.BarkRepeat.Forever;
        EventBus.Instance.RaiseNarration(tier.EnterLine.Text, tier.EnterLine.VoiceId);
    }

    /// <summary>An objective floor's opening taunt: from the floor's own FloorEnter lines when it has any the objective and flags allow, else from the campaign's.</summary>
    private bool OpeningTaunt(FloorObjectiveKind kind, FloorPlan? plan)
    {
        if (kind == FloorObjectiveKind.None) return false;
        var own = Objectives.TauntDirector.FloorEnterBank(plan?.Taunts ?? new List<TauntPlan>(), kind, Memory.Flags);
        return Taunt(Objectives.TauntTrigger.FloorEnter, kind, urgent: true, bankOverride: own.Count > 0 ? own : null);
    }

    // ---- the villain --------------------------------------------------------------------------

    private readonly Objectives.TauntDirector _taunts = new();
    private double _lastObjectiveActivity;

    /// <summary>
    /// The antagonist says something about what just happened, if the bank has a fresh line for it and enough time has
    /// passed since anyone spoke. Server only (clients hear it over the wire). True when a line was said.
    /// </summary>
    public bool Taunt(Objectives.TauntTrigger trigger, FloorObjectiveKind kind = FloorObjectiveKind.None, string? arg = null, bool urgent = false, List<TauntPlan>? bankOverride = null)
    {
        if (Net.NetSession.IsClient || !Game.Instance.InGame) return false;
        var campaign = Game.Instance.Adventure.Campaign;
        if (campaign == null) return false;
        if (campaign.Taunts.Count == 0 || !campaign.Taunts.Any(t => t.Trigger == Objectives.TauntTriggers.Id(trigger))) Objectives.TauntBank.Fill(campaign); // a campaign baked before a trigger existed gets the stock line for it
        var bank = bankOverride ?? campaign.Taunts.Concat(Main.Instance?.Builder.Contents?.Plan?.Taunts ?? new List<TauntPlan>()).ToList();
        var now = Game.Instance.PlaySeconds;
        if (Objectives.TauntDirector.HoldSeconds(trigger, urgent) is { } hold && !_speech.Free(now))
        {   // the objective's last word (as long as it takes) or a rite's opening (half a minute) waits for the line being said rather than cutting it off or being dropped
            if (Objectives.TauntDirector.Held(now, _speech.Until, now + hold) == Objectives.TauntDirector.HoldVerdict.Drop) return false;
            HoldTaunt(trigger, kind, arg, bankOverride, now + hold);
            return true;
        }
        var pick = _taunts.Pick(bank, trigger, kind, Game.Instance.Adventure.BarksSaid, Memory.Flags, _currentFloor, now, _barkRng, urgent);
        if (pick == null) return false;
        Voice.BarkRepeat.Said(Game.Instance.Adventure.BarksSaid, pick.Line.Text, _currentFloor);
        var partyName = Main.Instance?.Party is { } party && party.Members.Length > 0 ? party.Members[0].Name : null;
        var name = Objectives.ObjectiveText.Villain(campaign.Antagonist.Name);
        var coarse = $"{name}: \"{Objectives.TauntDirector.Render(pick.Line.Text, partyName, _currentFloor, arg)}\"";
        var clean = pick.CleanLine == null ? null : $"{name}: \"{Objectives.TauntDirector.Render(pick.CleanLine.Text, partyName, _currentFloor, arg)}\"";
        var (text, voice) = Settings.VillainCurses || clean == null ? (coarse, pick.Line.VoiceId) : (clean, pick.CleanLine!.VoiceId);
        if (trigger == Objectives.TauntTrigger.FloorEnter) EventBus.Instance.PlaySfx("boss");
        EventBus.Instance.RaiseNarration(text, voice, clean, pick.CleanLine?.VoiceId);
        _lastTauntSeconds = Heard(text, voice);
        _lastObjectiveActivity = now;
        return true;
    }

    /// <summary>A held taunt, tried again once the channel frees: said (as urgent: it has waited its turn), held again, or dropped past its deadline.</summary>
    private void HoldTaunt(Objectives.TauntTrigger trigger, FloorObjectiveKind kind, string? arg, List<TauntPlan>? bankOverride, double deadline)
    {
        var floor = _currentFloor;
        _deferred.Add((_speech.Until, () =>
        {
            if (_currentFloor != floor) return;
            switch (Objectives.TauntDirector.Held(Game.Instance.PlaySeconds, _speech.Until, deadline))
            {
                case Objectives.TauntDirector.HoldVerdict.Play: Taunt(trigger, kind, arg, urgent: true, bankOverride); break;
                case Objectives.TauntDirector.HoldVerdict.Wait: HoldTaunt(trigger, kind, arg, bankOverride, deadline); break;
            }
        }));
    }

    /// <summary>Whether this floor has narration due for a trigger (not yet said, flags allowing).</summary>
    private bool NarrationDue(NarrationTrigger trigger)
        => Main.Instance?.Builder is { Contents.Plan: { } plan } builder
           && Voice.NarrationRules.Due(plan, trigger, null, Memory.Flags, new HashSet<string>(Game.Instance.Adventure.BarksSaid.Keys), builder.CurrentFloor).Count > 0;

    private void OnObjectiveEvent(FloorObjectiveKind kind, Objectives.ObjectiveTrigger trigger, string? arg)
    {
        _lastObjectiveActivity = Game.Instance.PlaySeconds;
        var urgent = trigger is Objectives.ObjectiveTrigger.ObjectiveDone or Objectives.ObjectiveTrigger.AllFreed or Objectives.ObjectiveTrigger.RitualDone or Objectives.ObjectiveTrigger.VaultOpened;
        if (trigger == Objectives.ObjectiveTrigger.ObjectiveDone && kind is FloorObjectiveKind.Jailbreak or FloorObjectiveKind.Ritual or FloorObjectiveKind.ShiftingVault) return; // its own last word (AllFreed, RitualDone, VaultOpened) was just said
        Taunt(Objectives.TauntTriggers.From(trigger), kind, arg, urgent);
    }

    /// <summary>A party dawdling on an objective floor hears about it.</summary>
    private readonly Voice.IdleWatch _idle = new();
    private int _razzTurn;
    private readonly Voice.TravelWatch _travel = new();
    private int _travelTurn;

    /// <summary>A grumble from whoever's turn it is once the party has walked a long quiet way (server/solo only; clients hear it as a bark).</summary>
    private void TickTravel()
    {
        if (Net.NetSession.IsClient || !Game.Instance.InGame || Main.Instance is not { } main || main.Party is not { } party || !party.HasBodies || party.Local is not { } lead) return;
        var hud = main.Hud;
        var action = !party.PartyAlive || lead.Downed || party.InputLocked || hud == null || hud.Paused || hud.DialogueOpen || hud.InventoryOpen || Audio.MusicDirector.AnyoneFighting(main);
        if (!_travel.Update(Game.Instance.PlaySeconds, lead.DistanceTravelled, action)) return;
        Bump(Voice.BanterRules.EmptyCounter); // Sable's tally of empty rooms: a long quiet way is one
        if (_travelTurn % Voice.BanterRules.TravelShare == 0 && StartBanter()) { _travelTurn++; return; } // one grumble in three is an exchange instead
        var living = party.Active.Where(a => !a.Downed).Select(a => a.State.Name).ToList();
        if (Voice.TravelBarks.Speaker(living, _travelTurn++) is { } who)
            Bark(party.Members.FirstOrDefault(m => m.Name == who), BarkTrigger.Travel);
    }

    /// <summary>The player has stood in one place for ten seconds with nothing going on: a companion (never the one being played) razzes them, another one twenty seconds later.</summary>
    private void TickIdleRazz()
    {
        if (Net.NetSession.IsClient || !Game.Instance.InGame || Main.Instance is not { } main || main.Party is not { } party || !party.HasBodies || party.Local is not { } lead) return;
        var hud = main.Hud;
        var busy = !party.PartyAlive || lead.Downed || party.InputLocked || hud == null || hud.Paused || hud.DialogueOpen || hud.InventoryOpen || Audio.MusicDirector.AnyoneFighting(main);
        var at = lead.GlobalPosition;
        if (!_idle.Update(at.X, at.Z, Game.Instance.PlaySeconds, busy)) return;
        var standing = party.Active.Where(a => !a.Downed).Select(a => a.State.Name).ToList();
        if (Voice.IdleBarks.Razzer(standing, lead.State.Name, _razzTurn++) is { } who)
            Bark(party.Members.FirstOrDefault(m => m.Name == who), BarkTrigger.Idle);
    }

    private void TickIdleTaunt()
    {
        if (Net.NetSession.IsClient || !Game.Instance.InGame || Objectives.ObjectiveController.Instance is not { Completed: false } oc) return;
        var now = Game.Instance.PlaySeconds;
        if (now - _lastObjectiveActivity < Objectives.TauntDirector.IdleAfter) return;
        _lastObjectiveActivity = now; // whether or not it had a line, wait another stretch
        Taunt(Objectives.TauntTrigger.Idle, oc.Kind);
    }

    public QuestState? QuestGivenBy(string npcId) => Quests.Values.FirstOrDefault(q => q.Floor == _currentFloor && q.GiverNpc == npcId);

    public void Apply(QuestEvent e)
    {
        foreach (var q in Quests.Values.ToList())
        {
            if (e is NpcDied or NpcReachedStairs && q.Floor != _currentFloor) continue; // NPC ids repeat per floor
            var before = q.Status;
            if (!QuestMachine.Apply(q, e)) continue;
            if (q.Status != before) OnStatusChanged(q);
            EventBus.Instance.RaiseQuestChanged(q);
        }
        RefreshObjective();
    }

    private void OnStatusChanged(QuestState q)
    {
        switch (q.Status)
        {
            case QuestStatus.Active:
                EventBus.Instance.Log($"Quest accepted: {q.Title}.");
                EventBus.Instance.PlaySfx("ui_open");
                if (q.Template == QuestTemplate.Escort) FindNpc(q.EscortNpc)?.StartFollowing();
                if (q.Template == QuestTemplate.HoldOut) StartWaves(q);
                break;
            case QuestStatus.ReadyToTurnIn:
                EventBus.Instance.Log($"{q.Title}: return to {q.GiverName}.");
                break;
            case QuestStatus.Completed:
                Memory.For(q.Floor).QuestOutcomes.Add($"{q.Title} completed");
                EventBus.Instance.Log($"Quest complete: {q.Title}.");
                EventBus.Instance.PlaySfx("level_up");
                if (q.Template == QuestTemplate.Escort && q.CompleteLine != "") EventBus.Instance.RaiseNarration($"{q.GiverName}: {q.CompleteLine}", null);
                GrantReward(q);
                if (q.Floor == _currentFloor) PlayNarration(NarrationTrigger.QuestDone, NoRoom, 2);
                break;
            case QuestStatus.Failed:
                Memory.For(q.Floor).QuestOutcomes.Add($"{q.Title} failed");
                EventBus.Instance.Log($"Quest failed: {q.Title}.{(q.FailLine != "" ? $" \"{q.FailLine}\"" : "")}");
                if (q.Floor == _currentFloor) PlayNarration(NarrationTrigger.QuestFailed, NoRoom, 2);
                break;
        }
    }

    private void GrantReward(QuestState q)
    {
        if (q.RewardGranted) return;
        q.RewardGranted = true;
        var party = Main.Instance?.Party;
        if (party == null) return;
        if (q.RewardXp > 0) party.AwardXp(q.RewardXp);
        var cat = Game.Instance.Catalog;
        if (q.RewardTable != null && cat.Tables.TryGetValue(q.RewardTable, out var table))
        {
            var at = FindNpc(q.GiverNpc)?.GlobalPosition ?? party.Lead.GlobalPosition;
            var drops = LootRoller.RollMany(cat, table with { DropChance = 1f }, TierRules.ItemLevel(q.Floor), party.Rng);
            for (var i = 0; i < drops.Count; i++) party.SpawnLoot(drops[i], at + new Vector3(-0.5f + i * 0.5f, 0.4f, 1.0f));
            if (drops.Count > 0) EventBus.Instance.Log($"{q.GiverName} hands over {drops.Count} item{(drops.Count == 1 ? "" : "s")}.");
        }
    }

    private void StartWaves(QuestState q)
    {
        var builder = Main.Instance?.Builder;
        if (builder?.FloorRoot is not { } root || builder.Contents == null) return;
        if (root.GetChildren().OfType<WaveSpawner>().Any(w => !w.Done)) return;
        var template = builder.Contents.Collectives.FirstOrDefault(c => c.Id == q.WaveCollective) ?? builder.Contents.Collectives.FirstOrDefault();
        if (template == null) return;
        var around = FindNpc(q.GiverNpc)?.GlobalPosition ?? Main.Instance!.Party.Lead.GlobalPosition;
        var spawner = new WaveSpawner { Name = "Waves" };
        root.AddChild(spawner);
        spawner.Start(q, template, around, q.Progress);
    }

    private void OnEnemyDied(Enemy e)
    {
        if (_record != null && e.Floor == _currentFloor) _record.Kills++;
        if (_record != null && e.Floor == _currentFloor && e.IsElite) _record.Elites++;
        Apply(new EnemyDiedEvent(e.Group?.Id ?? e.QuestGroupId, e.Profile.Id, e.Floor));
    }

    private void OnNpcEvent(string npcId, NpcEventKind kind)
    {
        var name = FindNpc(npcId)?.NpcName ?? npcId;
        if (kind == NpcEventKind.Died) _record?.NpcFates.Add($"{name} died");
        if (kind == NpcEventKind.ReachedStairs) _record?.NpcFates.Add($"{name} rescued");
        switch (kind)
        {
            case NpcEventKind.Died: Apply(new NpcDied(npcId)); break;
            case NpcEventKind.ReachedStairs: Apply(new NpcReachedStairs(npcId)); break;
        }
    }

    private void OnDialogueEffect(string npcId, DialogueEffect effect, string? flag)
    {
        var q = QuestGivenBy(npcId);
        switch (effect)
        {
            case DialogueEffect.AcceptQuest: if (q != null) Apply(new QuestAccepted(q.Id)); break;
            case DialogueEffect.DeclineQuest: if (q != null) Apply(new QuestDeclined(q.Id)); break;
            case DialogueEffect.TurnIn: if (q != null) Apply(new QuestTurnedIn(q.Id)); break;
            case DialogueEffect.Choose:
                if (FlagRules.Clean(flag) is not { } f) break;
                SetFlag(f, FindNpc(npcId)?.NpcName ?? npcId);
                if (q is { Template: QuestTemplate.Decide }) Apply(new ChoiceMade(q.Id, f));
                break;
            case DialogueEffect.Betray: Betray(npcId, q); break;
            case DialogueEffect.Join: FindNpc(npcId)?.AskToJoin(); break;
        }
    }

    /// <summary>A choice the run remembers: later NPCs, quests and lines gate on it, and the planner hears about it.</summary>
    public void SetFlag(string flag, string byWhom)
    {
        if (Memory.Flags.Contains(flag)) return;
        Memory.Flags.Add(flag);
        _record?.Choices.Add($"{flag} (with {byWhom} on floor {_currentFloor})");
        GD.Print($"[Adventure] flag set: {flag}");
    }

    /// <summary>The NPC turns: they vanish and their creatures stand where they stood, the first wearing their name. An escort betrayal: the NPC flees to those creatures instead, and none wears their name.</summary>
    private void Betray(string npcId, QuestState? quest)
    {
        var main = Main.Instance;
        var npc = FindNpc(npcId);
        var plan = main?.Builder.Contents?.Plan?.Npcs.FirstOrDefault(n => n.Id == npcId);
        if (main == null || npc == null || npc.Turned || plan?.Betrayal is not { } b) return;
        var at = npc.GlobalPosition;
        npc.Turn(fled: b.Escort);
        _record?.NpcFates.Add(BetrayalRules.Fate(b.Escort, npc.NpcName));
        EventBus.Instance.PlaySfx("boss");
        for (var i = 0; i < b.Count; i++)
        {
            var offset = new Vector3(Mathf.Cos(i * 2.1f) * 0.9f * i, 0.2f, Mathf.Sin(i * 2.1f) * 0.9f * i);
            var e = main.Builder.SpawnEnemy(b.EnemyId, at + offset, _currentFloor);
            if (e == null) continue;
            e.QuestGroupId = QuestMachine.BetrayalGroup(npcId);
            if (BetrayalRules.CreatureName(b, npc.NpcName, i) is { } wears) e.NameOverride = wears;
            e.ForceAggro();
        }
        if (quest is { Template: QuestTemplate.Betrayal }) Apply(new BetrayalStarted(quest.Id));
        else if (BetrayalRules.NoQuestLog(b.Escort, npc.NpcName) is { } never) EventBus.Instance.Log(never);
        RefreshObjective();
    }

    public Npc? FindNpc(string? id)
    {
        if (id == null || Main.Instance?.Builder.FloorRoot is not { } root) return null;
        return root.GetChildren().OfType<Npc>().FirstOrDefault(n => n.Id == id);
    }

    /// <summary>The HUD's one-line objective: the first open quest on this floor.</summary>
    /// <summary>The objective line the HUD shows now (a joining client gets it in its Welcome).</summary>
    public string CurrentObjective { get; private set; } = "";

    public void RefreshObjective()
    {
        var q = Quests.Values.Where(q => q.Floor == _currentFloor && q.IsOpen).OrderBy(q => q.Status == QuestStatus.ReadyToTurnIn ? 0 : 1).FirstOrDefault();
        var quest = q == null ? "" : $"{q.Title}: {q.ObjectiveText}";
        var objective = Objectives.ObjectiveController.Instance is { Completed: false } oc ? $"{Objectives.ObjectiveController.Prefix} {oc.HudText}" : null;
        CurrentObjective = Objectives.ObjectiveLine.Compose(objective, HordeSpawner.HudText, quest, PortalSpawner.HudSuffix, SafeRoomController.HudText);
        EventBus.Instance.RaiseObjectiveChanged(CurrentObjective);
    }
}
