using DungeonCrawler.Core;
using DungeonCrawler.Dungeon;
using DungeonCrawler.Dungeon.Generation;
using DungeonCrawler.Save;
using Godot;

namespace DungeonCrawler.Adventure.Objectives;

/// <summary>
/// Runs one floor's objective: a server-only node under the floor root (a client only shows what the server says).
/// Subclasses tick the rules, build their props, and call <see cref="Complete"/> when the gate may open. State goes
/// through <see cref="ObjectiveStateDto"/> for saves, late joiners and the 2 s state broadcast.
/// </summary>
public abstract partial class ObjectiveController : Node
{
    public static ObjectiveController? Instance { get; private set; }

    public FloorObjectiveKind Kind => Layout.Objective?.Kind ?? FloorObjectiveKind.None;
    public bool Completed { get; protected set; }
    /// <summary>What the HUD's objective line shows for this objective right now.</summary>
    public abstract string HudText { get; }
    public int Floor { get; private set; }
    public FloorLayout Layout { get; private set; } = null!;
    public ObjectiveContents Contents { get; private set; } = null!;
    protected Random Rng { get; private set; } = new();

    private float _hudTimer;
    private string _lastHud = "";

    public virtual void Setup(FloorLayout layout, ObjectiveContents contents, int floor, int seed)
    {
        Instance = this;
        Name = "Objective";
        Layout = layout;
        Contents = contents;
        Floor = floor;
        Rng = new Random(seed ^ 0x0B7);
    }

    public override void _ExitTree() { if (Instance == this) Instance = null; }

    /// <summary>Spawn the objective's own props (gates, levers, plaques, people) once the floor's ordinary props stand.</summary>
    public virtual void Build(DungeonBuilder builder, Node3D root) { }

    /// <summary>The prefix the HUD puts before <see cref="HudText"/>: whose trial this is.</summary>
    public static string Prefix => ObjectiveText.HudPrefix(Game.Instance?.Adventure?.Campaign?.Antagonist?.Name);

    public virtual ObjectiveStateDto Capture() => new() { Kind = FloorObjectives.Id(Kind), Completed = Completed };
    public virtual void Restore(ObjectiveStateDto dto) { Completed = dto.Completed; }
    /// <summary>A client applying the server's state.</summary>
    public virtual void ApplyNet(ObjectiveStateDto dto) => Restore(dto);
    /// <summary>The server said it is done (client).</summary>
    public void MarkCompletedFromServer() => Completed = true;
    /// <summary>Skip the waiting (tests).</summary>
    public virtual void Hurry() { }
    /// <summary>An objective lever (index from <see cref="FloorContents.ExtraIndexBase"/>) was pulled or put back (server).</summary>
    public virtual void OnLever(int index, bool pulled) { }

    public override void _PhysicsProcess(double delta)
    {
        if (Net.NetSession.IsClient) { ClientTick((float)delta); return; }
        if (Main.Instance is not { InGame: true } main || main.Builder.Layout != Layout) return;
        var dt = (float)delta;
        Tick(dt, main);
        _hudTimer -= dt;
        if (_hudTimer <= 0f) { _hudTimer = 0.5f; RefreshHud(); }
    }

    protected abstract void Tick(float dt, Main main);
    /// <summary>A client running the server's state forward between updates (a clock, an animation); nothing that decides anything.</summary>
    protected virtual void ClientTick(float dt) { }

    protected void RefreshHud()
    {
        if (Net.NetSession.IsClient) return;
        var text = HudText;
        if (text == _lastHud) return;
        _lastHud = text;
        AdventureDirector.Instance?.RefreshObjective();
    }

    protected void Complete()
    {
        if (Completed) return;
        Completed = true;
        Raise(ObjectiveTrigger.ObjectiveDone);
        Main.Instance?.OnObjectiveCompleted(Floor, Kind);
        RefreshHud();
    }

    /// <summary>Something happened the villain (and the tests) may care about.</summary>
    protected void Raise(ObjectiveTrigger trigger, string? arg = null) => EventBus.Instance.RaiseObjectiveEvent(Kind, trigger, arg);
}
