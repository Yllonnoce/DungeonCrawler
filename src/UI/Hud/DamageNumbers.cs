using DungeonCrawler.Combat;
using DungeonCrawler.Core;
using DungeonCrawler.Enemies;
using Godot;

namespace DungeonCrawler.UI.Hud;

/// <summary>
/// Floating damage numbers over enemies: a pool of <see cref="Label3D"/>s, each rising and fading over <see cref="FeedbackRules.NumberSeconds"/>;
/// numbers within <see cref="FeedbackRules.MergeSeconds"/> on one target merge (<see cref="DamageNumberMerger"/>). Hidden by <see cref="Settings.DamageNumbers"/>.
/// </summary>
public partial class DamageNumbers : Node3D
{
    private const float Rise = 0.6f;
    private readonly DamageNumberMerger _merger = new();
    private readonly Dictionary<DamageNumberMerger.Entry, Label3D> _labels = new();
    private readonly Dictionary<DamageNumberMerger.Entry, Vector3> _anchors = new();
    private readonly Stack<Label3D> _pool = new();
    private float _now;

    public int ActiveCount => _labels.Count;

    public override void _Ready()
    {
        for (var i = 0; i < FeedbackRules.NumberPoolSize; i++) _pool.Push(MakeLabel());
    }

    private Label3D MakeLabel()
    {
        var l = new Label3D
        {
            Billboard = BaseMaterial3D.BillboardModeEnum.Enabled, NoDepthTest = true, FixedSize = false, FontSize = 48, OutlineSize = 8,
            Shaded = false, DoubleSided = true, Visible = false, PixelSize = 0.005f, TextureFilter = BaseMaterial3D.TextureFilterEnum.Linear,
        };
        AddChild(l);
        return l;
    }

    public void Show(Enemy e, float amount, HitFlavor flavor)
    {
        if (!Settings.DamageNumbers || e == null || !IsInstanceValid(e)) return;
        if (amount <= 0f && flavor != HitFlavor.Blocked) return;
        var entry = _merger.Add(e.NetId, amount, flavor, _now);
        _anchors[entry] = e.GlobalPosition + Vector3.Up * (1.8f * e.Profile.Scale + 0.2f);
        if (!_labels.ContainsKey(entry))
        {
            if (_pool.Count == 0) return; // the pool is the rate limit
            var l = _pool.Pop();
            l.Visible = true;
            _labels[entry] = l;
        }
        Paint(entry, _labels[entry]);
    }

    private static void Paint(DamageNumberMerger.Entry entry, Label3D l)
    {
        l.Text = FeedbackRules.Text(entry.Flavor, entry.Amount);
        l.Modulate = new Color(FeedbackRules.ColorHex(entry.Flavor));
        l.OutlineModulate = new Color(0, 0, 0, 0.9f);
        l.PixelSize = 0.005f * FeedbackRules.Scale(entry.Flavor);
        entry.Dirty = false;
    }

    public override void _Process(double delta)
    {
        var dt = (float)delta;
        _now += dt;
        _merger.Update(dt);
        var live = new HashSet<DamageNumberMerger.Entry>(_merger.Entries);
        foreach (var (entry, l) in _labels.ToArray())
        {
            if (!live.Contains(entry)) { l.Visible = false; _pool.Push(l); _labels.Remove(entry); _anchors.Remove(entry); continue; }
            if (entry.Dirty) Paint(entry, l);
            var t = Mathf.Clamp(entry.Age / FeedbackRules.NumberSeconds, 0f, 1f);
            l.GlobalPosition = _anchors[entry] + Vector3.Up * (Rise * t);
            var c = l.Modulate; c.A = 1f - t * t; l.Modulate = c;
            var o = l.OutlineModulate; o.A = 0.9f * (1f - t * t); l.OutlineModulate = o;
        }
    }
}
