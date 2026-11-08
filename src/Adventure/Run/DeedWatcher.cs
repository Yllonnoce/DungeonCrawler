using DungeonCrawler.Adventure.Run;
using DungeonCrawler.Core;
using DungeonCrawler.Save.Profile;
using Godot;

namespace DungeonCrawler.Adventure;

/// <summary>
/// Listens to the EventBus for the moments the deeds are made of and feeds the <see cref="DeedTracker"/>; an earned deed
/// is saved to the profile, its unlock applied at once, toasted on the HUD and logged (a server's log reaches its clients).
/// Solo and server only: Main adds one per session.
/// </summary>
public partial class DeedWatcher : Node
{
    /// <summary>A Shatter that killed an elite is judged within this long of the hit.</summary>
    public const double ShatterWindow = 0.2;
    private DeedTracker _tracker = null!;
    private Enemies.Enemy? _shattered;
    private double _shatteredAt = double.NegativeInfinity;
    private readonly HashSet<(int Floor, int Index)> _jammed = new();
    private readonly List<Action> _unsubscribe = new();

    public override void _Ready()
    {
        _tracker = new DeedTracker(ProfileStore.Current);
        var bus = EventBus.Instance;
        bus.PartyWipedBy += OnWipedBy; _unsubscribe.Add(() => bus.PartyWipedBy -= OnWipedBy);
        bus.FloorLedgerClosed += OnLedgerClosed; _unsubscribe.Add(() => bus.FloorLedgerClosed -= OnLedgerClosed);
        bus.SecretFound += OnSecretFound; _unsubscribe.Add(() => bus.SecretFound -= OnSecretFound);
        bus.MimicSprung += OnMimic; _unsubscribe.Add(() => bus.MimicSprung -= OnMimic);
        bus.CurseTaken += OnCurse; _unsubscribe.Add(() => bus.CurseTaken -= OnCurse);
        bus.ShrineUsed += OnShrine; _unsubscribe.Add(() => bus.ShrineUsed -= OnShrine);
        bus.Gambled += OnGamble; _unsubscribe.Add(() => bus.Gambled -= OnGamble);
        bus.PerfectBlock += OnPerfectBlock; _unsubscribe.Add(() => bus.PerfectBlock -= OnPerfectBlock);
        bus.Parried += OnParried; _unsubscribe.Add(() => bus.Parried -= OnParried);
        bus.BossSeen += OnBossSeen; _unsubscribe.Add(() => bus.BossSeen -= OnBossSeen);
        bus.BossDefeated += OnBossDefeated; _unsubscribe.Add(() => bus.BossDefeated -= OnBossDefeated);
        bus.EnemyHurt += OnEnemyHurt; _unsubscribe.Add(() => bus.EnemyHurt -= OnEnemyHurt);
        bus.EliteSlain += OnEliteSlain; _unsubscribe.Add(() => bus.EliteSlain -= OnEliteSlain);
        bus.MemberRescued += OnRescued; _unsubscribe.Add(() => bus.MemberRescued -= OnRescued);
        bus.HazardPhase += OnHazardPhase; _unsubscribe.Add(() => bus.HazardPhase -= OnHazardPhase);
        bus.Bark += OnBark; _unsubscribe.Add(() => bus.Bark -= OnBark);
        bus.FloorBuilt += OnFloorBuilt; _unsubscribe.Add(() => bus.FloorBuilt -= OnFloorBuilt);
        bus.FloorChanged += OnFloorChanged; _unsubscribe.Add(() => bus.FloorChanged -= OnFloorChanged);
    }

    private int _lastDelta;
    private void OnFloorChanged(int _, int delta) => _lastDelta = delta; // raised before the ledger closes: a descent is a floor cleared

    public override void _ExitTree() { foreach (var u in _unsubscribe) u(); _unsubscribe.Clear(); }

    private void OnWipedBy(int floor, string _) => Note(new DeedEvent(DeedSignal.Wipe, Floor: floor));
    private void OnLedgerClosed(int floor, int seconds)
    {
        Note(new DeedEvent(DeedSignal.FloorClosed, Floor: floor, Amount: seconds, Flag: _lastDelta > 0));
        var r = Game.Instance.Adventure.Memory.For(floor);
        if (r.SecretsTotal >= DeedRules.NosyMinSecrets && r.SecretsFound >= r.SecretsTotal) Note(new DeedEvent(DeedSignal.FloorSecretsComplete, Floor: floor));
    }
    private void OnSecretFound(int floor) => Note(new DeedEvent(DeedSignal.SecretFound, Floor: floor));
    private void OnMimic(Vector3 _) => Note(new DeedEvent(DeedSignal.MimicSprung));
    private void OnCurse(string _) => Note(new DeedEvent(DeedSignal.CurseTaken));
    private void OnShrine(int _, bool isBoon) { if (isBoon) Note(new DeedEvent(DeedSignal.ShrineBoon)); }
    private void OnGamble(int _, bool __) => Note(new DeedEvent(DeedSignal.Gamble));
    private void OnPerfectBlock(int _) => Note(new DeedEvent(DeedSignal.PerfectBlock));
    private void OnParried(int _) => Note(new DeedEvent(DeedSignal.Parried));
    private void OnBossSeen(Enemies.Enemy _) => Note(new DeedEvent(DeedSignal.BossSeen, At: Game.Instance.PlaySeconds));
    private void OnBossDefeated(int floor) => Note(new DeedEvent(DeedSignal.BossDown, Floor: floor, At: Game.Instance.PlaySeconds));
    private void OnEnemyHurt(Enemies.Enemy e, float _, Combat.HitFlavor flavor, int __) { if (flavor == Combat.HitFlavor.Shatter) { _shattered = e; _shatteredAt = Game.Instance.PlaySeconds; } }
    private void OnEliteSlain(Enemies.Enemy e)
    {
        Note(new DeedEvent(DeedSignal.EliteSlain));
        if (ReferenceEquals(e, _shattered) && Game.Instance.PlaySeconds - _shatteredAt <= ShatterWindow) Note(new DeedEvent(DeedSignal.EliteShattered));
    }
    private void OnRescued(int _, int __) => Note(new DeedEvent(DeedSignal.Rescue));
    private void OnHazardPhase(int index, byte _)
    {
        var floor = Game.Instance.CurrentFloor;
        if (_jammed.Contains((floor, index))) return;
        if (Main.Instance?.Builder.Hazards.OfType<Dungeon.Props.Press>().FirstOrDefault(p => p.Index == index) is not { Jammed: true }) return;
        _jammed.Add((floor, index));
        Note(new DeedEvent(DeedSignal.PressJammed, Floor: floor));
    }
    private void OnBark(string member, Line line) { if (member == "Ilex" && line.Text.Contains("Gerald", StringComparison.OrdinalIgnoreCase)) Note(new DeedEvent(DeedSignal.GeraldNamed)); }
    private void OnFloorBuilt(int floor, int _, bool __) => Note(new DeedEvent(DeedSignal.FloorReached, Floor: floor));

    /// <summary>A new run under this watcher (the per-run counters start over).</summary>
    public void NewRun() { _tracker.NewRun(); _jammed.Clear(); }

    /// <summary>Feed a signal; every newly earned deed is saved, applied, toasted and logged. Returns what was earned.</summary>
    public IReadOnlyList<DeedDef> Note(DeedEvent e)
    {
        var earned = _tracker.Note(e);
        if (earned.Count == 0) return earned;
        ProfileStore.Save();
        ProfileStore.ApplyUnlocks(Game.Instance.Catalog);
        foreach (var d in earned)
        {
            Main.Instance?.Hud?.ShowDeedToast(d.Name, d.UnlockText);
            EventBus.Instance.Log($"Deed: {d.Name} — {d.UnlockText}");
            GD.Print($"[deed] {d.Id} earned ({d.Condition}); unlocked {DeedRules.UnlockKey(d)}");
        }
        return earned;
    }
}
