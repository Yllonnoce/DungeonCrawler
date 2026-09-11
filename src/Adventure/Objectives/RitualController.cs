using DungeonCrawler.Core;
using DungeonCrawler.Dungeon;
using DungeonCrawler.Dungeon.Generation;
using DungeonCrawler.Dungeon.Props;
using DungeonCrawler.Enemies;
using DungeonCrawler.Items;
using DungeonCrawler.Save;
using Godot;

namespace DungeonCrawler.Adventure.Objectives;

/// <summary>
/// The rite at the shrine. The party begins it by speaking to the ritualist; from then on the breaches around the
/// shrine pour creatures that hunt the ritualist until the rite is done. Levers by each breach seal it under rubble;
/// with every breach sealed a slower trickle comes in by the doors. The ritualist goes down at 0 HP and the party
/// gets them up; a party wipe puts the rite back to the start. Done = the stairs open.
/// </summary>
public partial class RitualController : ObjectiveController
{
    public bool Started { get; private set; }
    public float Progress { get; private set; }
    public float Duration => RitualRules.Duration(Floor);
    public Npc? Ritualist { get; private set; }
    public IReadOnlyList<bool> Sealed => _sealed;
    public bool AllSealed => _sealed.Count > 0 && _sealed.All(s => s);
    public int Alive => _alive.Count(e => IsInstanceValid(e) && e.IsAlive);
    /// <summary>The rite is under way and not yet done: portals stand down while it runs.</summary>
    public bool Active => Started && !Completed;

    private readonly List<bool> _sealed = new();
    private readonly List<Lever> _levers = new();
    private readonly List<Enemy> _alive = new();
    private readonly List<string> _fodder = new(), _regulars = new();
    private float _waveTimer;
    private int _waves, _mouthCursor;
    private bool _wasDown;

    public override string HudText
    {
        get
        {
            if (Completed) return "The rite is done. Take the stairs.";
            var name = Ritualist?.NpcName ?? "the ritualist";
            if (!Started) return $"Speak to {name} at the shrine to begin the ritual. Lay traps and find the breach levers first.";
            var down = Ritualist is { Downed: true } ? $" {name} is down: get them up." : "";
            return $"Hold the shrine: {Progress:0}/{Duration:0} s, {Alive} attacking.{down}";
        }
    }

    public override void Setup(FloorLayout layout, ObjectiveContents contents, int floor, int seed)
    {
        base.Setup(layout, contents, floor, seed);
        foreach (var _ in layout.Objective!.BreachLevers) _sealed.Add(false);
        EventBus.Instance.PartyWiped += OnPartyWiped;
    }

    public override void _ExitTree()
    {
        EventBus.Instance.PartyWiped -= OnPartyWiped;
        base._ExitTree();
    }

    public override void Build(DungeonBuilder builder, Node3D root)
    {
        var objective = Layout.Objective!;
        if (objective.Altar is { } altar) builder.SpawnAltar(root, altar);
        if (Contents.Ritualist is { } r) Ritualist = builder.SpawnObjectiveNpc(root, r.Id, r.Name, r.Cell, NpcMode.Ritualist);
        for (var i = 0; i < objective.BreachLevers.Count; i++)
            _levers.Add(builder.SpawnLever(root, objective.BreachLevers[i].Lever, FloorContents.ExtraIndexBase + i, toggle: false, label: "breach lever"));
        // who comes through: the tier's swarm first, the floor's own creatures for the rest
        var tier = TierRules.TierIndex(Floor);
        foreach (var p in builder.Roster.Values.Where(p => p.Faction == FloorMenu.HordeFaction && !p.IsBoss && (p.TierIndex < 0 || p.TierIndex == tier)).OrderBy(p => p.Id)) _fodder.Add(p.Id);
        foreach (var id in (builder.Contents?.Enemies ?? new List<EnemyPlacement>()).Select(e => e.EnemyId).Distinct())
            if (builder.Roster.TryGetValue(id, out var p) && !p.IsBoss && p.Faction != FloorMenu.HordeFaction) _regulars.Add(id);
        if (_fodder.Count == 0 && _regulars.Count == 0) _regulars.Add(LegacyPopulator.FallbackEnemy);
    }

    /// <summary>The party said yes at the shrine.</summary>
    public void Begin(Npc who)
    {
        if (Started || Completed || Ritualist == null || who != Ritualist) return;
        Started = true;
        _waveTimer = RitualRules.FirstWave;
        EventBus.Instance.Log($"{Ritualist.NpcName} begins the rite. The walls answer.");
        EventBus.Instance.PlaySfx("lever");
        Raise(ObjectiveTrigger.RitualStart);
        RefreshHud();
    }

    private void OnPartyWiped()
    {
        if (!Started || Completed) return;
        Progress = 0f;
        EventBus.Instance.Log("The rite falters with no one to hold the shrine. It must begin again.");
    }

    protected override void Tick(float dt, Main main)
    {
        _alive.RemoveAll(e => !IsInstanceValid(e) || !e.IsAlive);
        if (!Started || Completed || Ritualist == null || !IsInstanceValid(Ritualist)) return;
        if (Ritualist.Dead) return;
        if (Ritualist.Downed && !_wasDown) { _wasDown = true; Raise(ObjectiveTrigger.RitualistDown); }
        if (!Ritualist.Downed && _wasDown) _wasDown = false;
        if (RitualRules.Advances(Started, Ritualist.Downed, Ritualist.Dead))
        {
            Progress += dt;
            if (Progress >= Duration) { Finish(main); return; }
        }
        _waveTimer -= dt;
        if (_waveTimer > 0f) return;
        if (Alive >= RitualRules.MaxAlive(Floor)) { _waveTimer = 2f; return; }
        Wave(main);
    }

    private void Wave(Main main)
    {
        var progress = Progress / Duration;
        _waveTimer = RitualRules.WaveInterval(progress, AllSealed);
        var count = Math.Min(RitualRules.WaveSize(progress, Floor), RitualRules.MaxAlive(Floor) - Alive);
        if (count <= 0) return;
        _waves++;
        for (var i = 0; i < count; i++)
        {
            var (at, facing) = SpawnPoint(main.Builder);
            var fodder = _fodder.Count > 0 && (_regulars.Count == 0 || Rng.NextDouble() < RitualRules.FodderShare);
            var pool = fodder ? _fodder : _regulars;
            var id = pool[Rng.Next(pool.Count)];
            var jitter = new Vector3((float)(Rng.NextDouble() - 0.5) * 1.2f, 0.15f, (float)(Rng.NextDouble() - 0.5) * 1.2f);
            if (main.Builder.SpawnEnemy(id, at + facing * 0.6f + jitter, Floor) is not { } e) continue;
            if (fodder) e.SetSwarmDetail();
            e.IsReinforcement = true;
            e.RunBoost = 1.1f;
            e.NpcTarget = Ritualist;
            e.ChangeState(new Enemies.Ai.HuntNpcState());
            _alive.Add(e);
        }
        EventBus.Instance.Log(AllSealed ? $"Creatures slip in past the rubble ({count})." : $"The breaches spill {count} creature{(count == 1 ? "" : "s")}!");
        Raise(ObjectiveTrigger.WaveBreak, _waves.ToString());
    }

    /// <summary>An open breach mouth (round robin), or a doorway of the shrine when every mouth is sealed.</summary>
    private (Vector3 at, Vector3 facing) SpawnPoint(DungeonBuilder builder)
    {
        var objective = Layout.Objective!;
        var open = Enumerable.Range(0, _sealed.Count).Where(i => !_sealed[i]).ToList();
        if (open.Count > 0 && Layout.NestAt(objective.ShrineRoom!) is { } nest)
        {
            var i = open[_mouthCursor++ % open.Count];
            var mouth = objective.BreachLevers[i].Mouth;
            var m = nest.Mouths.IndexOf(mouth);
            var outward = m >= 0 ? new Vector3(nest.Outward[m].X, 0, nest.Outward[m].Y) : Vector3.Zero;
            return (builder.Surface(mouth), outward);
        }
        var entrances = FloorGenerator.Entrances(Layout, objective.ShrineRoom!);
        var door = entrances.Count > 0 ? entrances[_mouthCursor++ % entrances.Count] : objective.ShrineRoom!.Center;
        var centre = builder.Surface(objective.ShrineRoom!.Center);
        var at = builder.Surface(door);
        var toward = centre - at; toward.Y = 0;
        return (at, toward.Length() > 0.01f ? toward.Normalized() : Vector3.Zero);
    }

    public override void OnLever(int index, bool pulled)
    {
        if (index < 0 || index >= _sealed.Count || _sealed[index] || !pulled) return;
        Seal(index, silent: false);
    }

    private void Seal(int index, bool silent)
    {
        _sealed[index] = true;
        var mouth = Layout.Objective!.BreachLevers[index].Mouth;
        Main.Instance?.Builder.SealMouth((GetParent() as Node3D) ?? Main.Instance.Builder.FloorRoot!, mouth, silent);
        if (!silent) EventBus.Instance.Log(AllSealed ? "The last breach falls shut. They will find another way in, slower." : "The breach collapses in a fall of stone.");
        RefreshHud();
    }

    private void Finish(Main main)
    {
        Progress = Duration;
        EventBus.Instance.Log($"{Ritualist?.NpcName ?? "The ritualist"} finishes the rite. The creatures falter.");
        foreach (var e in _alive.Where(e => IsInstanceValid(e) && e.IsAlive)) { e.NpcTarget = null; e.ForceAggro(); }
        Reward(main);
        Raise(ObjectiveTrigger.RitualDone);
        Complete();
    }

    private void Reward(Main main)
    {
        var party = main.Party;
        party.AwardXp(60 + 12 * Floor);
        var cat = Game.Instance.Catalog;
        if (cat.Tables.TryGetValue("table.chest", out var table) && Layout.Objective!.Altar is { } altar)
        {
            var at = main.Builder.Surface(altar) + Vector3.Up * 0.8f;
            var drops = LootRoller.RollMany(cat, table with { DropChance = 1f }, TierRules.ItemLevel(Floor), party.Rng);
            for (var i = 0; i < drops.Count; i++) party.SpawnLoot(drops[i], at + new Vector3(-0.5f + i * 0.5f, 0.2f, 0.9f));
            if (drops.Count > 0) EventBus.Instance.Log($"The altar yields {drops.Count} item{(drops.Count == 1 ? "" : "s")}.");
        }
    }

    public override ObjectiveStateDto Capture()
    {
        var dto = base.Capture();
        dto.RitualStarted = Started;
        dto.RitualProgress = Progress;
        dto.RitualistDowned = Ritualist?.Downed == true;
        dto.SealedMouths = Enumerable.Range(0, _sealed.Count).Where(i => _sealed[i]).ToList();
        return dto;
    }

    public override void Restore(ObjectiveStateDto dto)
    {
        base.Restore(dto);
        Started = dto.RitualStarted || Completed;
        Progress = Completed ? Duration : dto.RitualProgress;
        _waveTimer = RitualRules.FirstWave;
        foreach (var i in dto.SealedMouths) if (i >= 0 && i < _sealed.Count && !_sealed[i]) { Seal(i, silent: true); _levers[i].SetPulled(true); }
    }

    public override void ApplyNet(ObjectiveStateDto dto)
    {
        base.Restore(dto);
        Started = dto.RitualStarted || Completed;
        Progress = dto.RitualProgress;
    }

    /// <summary>Begin, and put the rite a breath from done (tests).</summary>
    public override void Hurry()
    {
        if (Ritualist != null && !Started) Begin(Ritualist);
        Progress = Math.Max(Progress, Duration - 1f);
    }
}
