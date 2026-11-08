using DungeonCrawler.Core;
using DungeonCrawler.Net;
using Godot;

namespace DungeonCrawler.UI.Hud;

/// <summary>
/// The world side of a ping (co-op): a <see cref="Label3D"/> with the pinger's name where they looked, for
/// <see cref="PingRules.LifetimeSeconds"/>, under the floor root so it leaves with the floor. <see cref="Board"/> is the
/// engine-free record the minimap draws from.
/// </summary>
public partial class PingMarkers : Node
{
    public static PingMarkers? Instance { get; private set; }
    public PingBoard Board { get; } = new();
    private static readonly Color PingColor = new(0.45f, 0.9f, 1f);
    private readonly Dictionary<string, Label3D> _labels = new();

    public override void _EnterTree() => Instance = this;
    public override void _ExitTree() { if (Instance == this) Instance = null; }

    /// <summary>A ping by <paramref name="name"/> at <paramref name="at"/>: one mark per name, the newest wins.</summary>
    public void Place(string name, Vector3 at)
    {
        Board.Place(name, at.X, at.Y, at.Z, Now());
        if (_labels.TryGetValue(name, out var old) && IsInstanceValid(old)) old.QueueFree();
        _labels.Remove(name);
        if (Main.Instance?.Builder.FloorRoot is not { } root) return;
        var label = new Label3D
        {
            Name = $"Ping_{name}",
            Text = name,
            Modulate = PingColor,
            Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
            NoDepthTest = true,
            FontSize = 40,
            OutlineSize = 8,
            Position = at + Vector3.Up * 0.6f,
        };
        root.AddChild(label);
        _labels[name] = label;
    }

    public override void _Process(double delta)
    {
        Board.Prune(Now());
        if (_labels.Count == 0) return;
        var rootAlive = Main.Instance?.Builder.FloorRoot is { } root && IsInstanceValid(root);
        foreach (var (name, label) in _labels.ToList())
        {
            if (rootAlive && Board.Active.Any(m => m.Name == name)) continue;
            if (IsInstanceValid(label)) label.QueueFree();
            _labels.Remove(name);
        }
        if (!rootAlive) Board.Clear();
    }

    private static double Now() => Time.GetTicksMsec() / 1000.0;
}
