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
    private readonly Random _barkRng = new(11);

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
            _voice.Enqueue(boss.IntroLine, Voices.Boss, Speaker.Boss, VoicePriority.Boss);
            _voice.Enqueue(boss.DeathLine, Voices.Boss, Speaker.Boss, VoicePriority.Boss);
            if (boss.AltDeathLine != null) _voice.Enqueue(boss.AltDeathLine, Voices.Boss, Speaker.Boss, VoicePriority.Boss);
        }
        var campaign = Game.Instance.Adventure.Campaign;
        foreach (var npc in plan.Npcs)
        {
            npc.VoiceId ??= Voices.ForNpc(npc.Name);
            var major = npc.CastId != null; // the cast gets the main model; passers-by the minor one
            var persona = npc.Persona ?? campaign?.CastFor(npc.CastId)?.Persona; // how they sound: their own words, else the cast member's
            foreach (var node in npc.Script.Nodes) _voice.Enqueue(node.Line, npc.VoiceId, Speaker.Npc, VoicePriority.Npc, major, persona);
            var quest = plan.Quests.FirstOrDefault(q => q.GiverNpc == npc.Id);
            if (quest != null) foreach (var l in new[] { quest.Lines.Offer, quest.Lines.Accept, quest.Lines.Decline, quest.Lines.Complete, quest.Lines.Fail }) _voice.Enqueue(l, npc.VoiceId, Speaker.Npc, VoicePriority.Npc, major, persona);
        }
        foreach (var t in plan.Taunts) EnqueueTaunt(t, campaign);
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
    }

    private void EnqueueBarks(CampaignPlan campaign)
    {
        var party = Main.Instance?.Party;
        Voice.KillBarks.TopUpFighters(campaign); // the fighters' sets live in the campaign beside the party's
        foreach (var set in campaign.Barks)
        {
            string voice; Persona persona; var speaker = Speaker.Companion;
            if (Voice.KillBarks.IsFighter(set.Member)) { (voice, persona) = Voices.FighterVoice(set.Member); speaker = Speaker.Npc; }
            else
            {
                var classId = party != null && GodotObject.IsInstanceValid(party) && party.Members.Length > 0
                    ? party.Members.FirstOrDefault(m => m.Name == set.Member)?.Class.Id
                    : Party.PartyController.DefaultRoster.FirstOrDefault(r => r.Name == set.Member).ClassId;
                voice = Voices.ForClass(classId ?? "");
                persona = Voices.CompanionPersona(classId ?? "");
                Voice.KillBarks.TopUp(set, classId ?? ""); // the stock victory lines get ids (and audio, when the bank has it) like the written ones
            }
            foreach (var b in set.Lines)
            {
                b.Line.Delivery ??= Deliveries.Id(Voices.BarkDelivery(b.Trigger)); // a fight is shouted, the rest said
                // a line whose id is written in the campaign keeps it (the shipped bank was rendered under it); only a new line derives one from today's settings
                _voice.Enqueue(b.Line, voice, speaker, VoicePriority.Bark, major: false, persona);
            }
        }
    }

    /// <summary>A short in-character line from the bark bank, rate limited, spoken if its audio exists.</summary>
    private void Bark(Characters.CharacterState? m, BarkTrigger trigger)
    {
        if (m == null || !Game.Instance.InGame) return;
        var campaign = Game.Instance.Adventure.Campaign;
        var set = campaign?.Barks.FirstOrDefault(b => b.Member == m.Name);
        if (set == null) return;
        var now = Game.Instance.PlaySeconds;
        // a kill is remarked on more freely than anything else: a quick cooldown, a roll, and a bank topped up from stock
        var kill = trigger == BarkTrigger.Kill;
        if (kill) Voice.KillBarks.TopUp(set, m.Class.Id);
        if (now - _lastBark < (kill ? Voice.KillBarks.GlobalGapSeconds : 5)) return;
        if (_barkCooldown.TryGetValue((m.Name, trigger), out var last) && now - last < (kill ? Voice.KillBarks.CooldownSeconds : 25)) return;
        if (kill && _barkRng.NextDouble() >= Voice.KillBarks.Chance) return;
        var lines = set.Lines.Where(l => l.Trigger == trigger && l.Line.Text != "").ToList();
        if (lines.Count == 0) return;
        // a line said in the last two floors stays quiet; when every line for this trigger is that recent, so does the companion,
        // except over a kill, where the line said longest ago comes back
        var said = Game.Instance.Adventure.BarksSaid;
        var texts = lines.Select(l => l.Line.Text).ToList();
        var text = kill ? Voice.BarkRepeat.ChooseOrOldest(said, texts, _currentFloor, _barkRng) : Voice.BarkRepeat.Choose(said, texts, _currentFloor, _barkRng);
        if (text == null) return;
        var line = lines.First(l => l.Line.Text == text).Line;
        Voice.BarkRepeat.Said(said, text, _currentFloor);
        _lastBark = now;
        _barkCooldown[(m.Name, trigger)] = now;
        EventBus.Instance.Log($"{m.Name}: \"{line.Text}\"");
        EventBus.Instance.RaiseBark(m.Name, line);
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
        _lastBark = now;
        _barkCooldown[(npc.Id, BarkTrigger.Kill)] = now;
        var line = barkLines.First(l => l.Line.Text == text).Line; // carries the id the bank was rendered under
        EventBus.Instance.Log($"{npc.NpcName}: \"{text}\"");
        EventBus.Instance.RaiseBark(npc.NpcName, line);
        _voice.PlaySoon(line);
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

    /// <summary>A client voicing a bark the server chose.</summary>
    public void PlayBark(Line line) => _voice.PlayLine(line, interrupt: false);

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
    private double _floorStart;
    private string _leadName = "";
    private readonly Dictionary<int, List<string>> _pendingRepairs = new();

    public string MemorySummary(int upToFloor) => Adventure.MemorySummary.Build(Memory, upToFloor);

    private void OpenRecord(int floor)
    {
        _record = Memory.For(floor);
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
    }

    public override void _Process(double delta)
    {
        TickIdleTaunt();
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
        bus.BossSeen += boss => { if (Main.Instance?.Builder.Contents?.Plan?.Boss is { } b && b.IntroLine.Text != "") EventBus.Instance.RaiseNarration($"{b.Name}: {b.IntroLine.Text}", b.IntroLine.VoiceId); Bark(Main.Instance?.Party?.Lead.State, BarkTrigger.BossSeen); };
        bus.BossDefeated += _ => { if (Main.Instance?.Builder.Contents?.Plan?.Boss is { } b && b.DeathLineFor(Memory.Flags) is { } last && last.Text != "") EventBus.Instance.RaiseNarration($"{b.Name}: {last.Text}", last.VoiceId); };
        bus.MemberKill += m => Bark(m, BarkTrigger.Kill);
        bus.NpcKill += FighterBark;
        bus.MemberDamaged += (i, amount) => { var m = Member(i); if (m != null && amount >= m.Stats[Characters.Stat.MaxHp] * 0.15f) Bark(m, BarkTrigger.Hurt); };
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
        bus.PartyWiped += () => Taunt(Objectives.TauntTrigger.PartyWiped, Main.Instance?.Builder.Layout?.Objective?.Kind ?? FloorObjectiveKind.None, urgent: true);
        bus.ObjectiveEvent += OnObjectiveEvent;
        bus.Narration += (_, _) => _taunts.NoteNarration(Game.Instance.PlaySeconds);
        bus.MemberRevived += i => Bark(Member(i), BarkTrigger.Revived);
        bus.EnemyDied += OnEnemyDied;
        bus.QuestItemPicked += id => Apply(new QuestItemPicked(QuestKeys.QuestOf(id))); // clue items carry an ordinal after the quest key
        bus.NpcEvent += OnNpcEvent;
        bus.DialogueEffectChosen += OnDialogueEffect;
        bus.FloorChanged += OnFloorChanged;
        bus.HitLanded += (m, r) => { if (_record == null) return; _record.DamageDealt += r.Amount; if (r.Sneak) _record.SneakHits++; };
        bus.MemberDamaged += (_, amount) => { if (_record != null) _record.DamageTaken += amount; };
        bus.MemberDowned += _ => { if (_record != null) _record.Downs++; };
        bus.PartyWiped += () => { if (_record != null) _record.Wipes++; };
        bus.AbilityUsed += (_, kind) => { if (_record == null) return; if (kind is Combat.AbilityKind.WeaponAttack or Combat.AbilityKind.MeleeStrike) _record.WeaponAttacks++; else _record.SpellCasts++; };
        bus.LeadChanged += index => { var p = Main.Instance?.Party; if (p != null && index < p.Members.Length) _leadName = p.Members[index].Name; };
        bus.BossDefeated += _ => { if (_record != null) _record.BossSeconds = (float)(Game.Instance.PlaySeconds - _floorStart); };
    }

    private void OnFloorChanged(int floor, int delta)
    {
        if (_currentFloor > 0 && _currentFloor != floor && Game.Instance.InGame)
        {
            Apply(new FloorLeft(_currentFloor));
            CloseRecord();
            if (delta > 0 && Objectives.ObjectiveController.Instance is { Completed: true } done) _pendingDescend = done.Kind;
        }
        _currentFloor = floor;
    }

    private FloorObjectiveKind? _pendingDescend;

    /// <summary>Called by Main once a floor's nodes exist and saved state has been applied.</summary>
    public void OnFloorBuilt(FloorContents contents, int floor, bool firstVisit)
    {
        _currentFloor = floor;
        OpenRecord(floor);
        if (_pendingDescend is { } left) { _pendingDescend = null; Taunt(Objectives.TauntTrigger.Descend, left); }
        // an objective floor opens with the villain's line instead of the narrator's, plan or no plan
        var objectiveKind = Main.Instance?.Builder.Layout?.Objective?.Kind ?? FloorObjectiveKind.None;
        var villainSpoke = firstVisit && objectiveKind != FloorObjectiveKind.None && Taunt(Objectives.TauntTrigger.FloorEnter, objectiveKind, urgent: true);
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
            if (firstVisit && !villainSpoke && plan.IntroLine.Text != "") EventBus.Instance.RaiseNarration(plan.IntroLine.Text, plan.IntroLine.VoiceId);
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

    // ---- the villain --------------------------------------------------------------------------

    private readonly Objectives.TauntDirector _taunts = new();
    private double _lastObjectiveActivity;

    /// <summary>
    /// The antagonist says something about what just happened, if the bank has a fresh line for it and enough time has
    /// passed since anyone spoke. Server only (clients hear it over the wire). True when a line was said.
    /// </summary>
    public bool Taunt(Objectives.TauntTrigger trigger, FloorObjectiveKind kind = FloorObjectiveKind.None, string? arg = null, bool urgent = false)
    {
        if (Net.NetSession.IsClient || !Game.Instance.InGame) return false;
        var campaign = Game.Instance.Adventure.Campaign;
        if (campaign == null) return false;
        if (campaign.Taunts.Count == 0 || !campaign.Taunts.Any(t => t.Trigger == Objectives.TauntTriggers.Id(trigger))) Objectives.TauntBank.Fill(campaign); // a campaign baked before a trigger existed gets the stock line for it
        var bank = campaign.Taunts.Concat(Main.Instance?.Builder.Contents?.Plan?.Taunts ?? new List<TauntPlan>()).ToList();
        var now = Game.Instance.PlaySeconds;
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
        _lastObjectiveActivity = now;
        return true;
    }

    private void OnObjectiveEvent(FloorObjectiveKind kind, Objectives.ObjectiveTrigger trigger, string? arg)
    {
        _lastObjectiveActivity = Game.Instance.PlaySeconds;
        var urgent = trigger is Objectives.ObjectiveTrigger.ObjectiveDone or Objectives.ObjectiveTrigger.AllFreed or Objectives.ObjectiveTrigger.RitualDone or Objectives.ObjectiveTrigger.VaultOpened;
        if (trigger == Objectives.ObjectiveTrigger.ObjectiveDone && kind is FloorObjectiveKind.Jailbreak or FloorObjectiveKind.Ritual or FloorObjectiveKind.ShiftingVault) return; // its own last word (AllFreed, RitualDone, VaultOpened) was just said
        Taunt(Objectives.TauntTriggers.From(trigger), kind, arg, urgent);
    }

    /// <summary>A party dawdling on an objective floor hears about it.</summary>
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
                break;
            case QuestStatus.Failed:
                Memory.For(q.Floor).QuestOutcomes.Add($"{q.Title} failed");
                EventBus.Instance.Log($"Quest failed: {q.Title}.{(q.FailLine != "" ? $" \"{q.FailLine}\"" : "")}");
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

    /// <summary>The NPC turns: they vanish and their creatures stand where they stood, the first wearing their name.</summary>
    private void Betray(string npcId, QuestState? quest)
    {
        var main = Main.Instance;
        var npc = FindNpc(npcId);
        var plan = main?.Builder.Contents?.Plan?.Npcs.FirstOrDefault(n => n.Id == npcId);
        if (main == null || npc == null || npc.Turned || plan?.Betrayal is not { } b) return;
        var at = npc.GlobalPosition;
        npc.Turn();
        _record?.NpcFates.Add($"{npc.NpcName} turned on the party");
        EventBus.Instance.PlaySfx("boss");
        for (var i = 0; i < b.Count; i++)
        {
            var offset = new Vector3(Mathf.Cos(i * 2.1f) * 0.9f * i, 0.2f, Mathf.Sin(i * 2.1f) * 0.9f * i);
            var e = main.Builder.SpawnEnemy(b.EnemyId, at + offset, _currentFloor);
            if (e == null) continue;
            e.QuestGroupId = QuestMachine.BetrayalGroup(npcId);
            if (i == 0) e.NameOverride = npc.NpcName;
            e.ForceAggro();
        }
        if (quest is { Template: QuestTemplate.Betrayal }) Apply(new BetrayalStarted(quest.Id));
        else EventBus.Instance.Log($"{npc.NpcName} was never on your side.");
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
        CurrentObjective = Objectives.ObjectiveLine.Compose(objective, HordeSpawner.HudText, quest, PortalSpawner.HudSuffix);
        EventBus.Instance.RaiseObjectiveChanged(CurrentObjective);
    }
}
