using DungeonCrawler.Combat;
using DungeonCrawler.Enemies;
using Godot;

namespace DungeonCrawler.UI.Hud;

/// <summary>A billboard health bar over any enemy hurt in the last <see cref="FeedbackRules.BarLingerSeconds"/>, with its name for bosses. Hordes get none (cost and noise).</summary>
public partial class EnemyBars : Node3D
{
    private sealed class Bar
    {
        public Node3D Root = null!;
        public MeshInstance3D Fill = null!;
        public Label3D Name = null!;
    }

    private readonly Dictionary<Enemy, float> _lastHurt = new();
    private readonly Dictionary<Enemy, Bar> _shown = new();
    private readonly Stack<Bar> _pool = new();
    private float _now;
    private static StandardMaterial3D? _backMat, _fillMat;

    public bool Showing(Enemy e) => _shown.ContainsKey(e);

    public override void _Ready()
    {
        for (var i = 0; i < FeedbackRules.BarPoolSize; i++) _pool.Push(Make());
    }

    private Bar Make()
    {
        _backMat ??= new StandardMaterial3D { ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, BillboardMode = BaseMaterial3D.BillboardModeEnum.Enabled, NoDepthTest = true, AlbedoColor = new Color(0.08f, 0.08f, 0.08f, 0.85f), Transparency = BaseMaterial3D.TransparencyEnum.Alpha, DisableReceiveShadows = true };
        _fillMat ??= new StandardMaterial3D { ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, BillboardMode = BaseMaterial3D.BillboardModeEnum.Enabled, NoDepthTest = true, AlbedoColor = new Color(0.85f, 0.15f, 0.12f), DisableReceiveShadows = true };
        var root = new Node3D { Visible = false };
        var back = new MeshInstance3D { Mesh = new QuadMesh { Size = new Vector2(0.9f, 0.08f) }, MaterialOverride = _backMat, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
        var fill = new MeshInstance3D { Mesh = new QuadMesh { Size = new Vector2(0.9f, 0.08f) }, MaterialOverride = _fillMat, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off, Position = new Vector3(0, 0, 0.001f) };
        var name = new Label3D { Billboard = BaseMaterial3D.BillboardModeEnum.Enabled, NoDepthTest = true, FontSize = 32, OutlineSize = 6, PixelSize = 0.004f, Position = new Vector3(0, 0.12f, 0), Shaded = false, Visible = false };
        root.AddChild(back); root.AddChild(fill); root.AddChild(name);
        AddChild(root);
        return new Bar { Root = root, Fill = fill, Name = name };
    }

    public void NoteHurt(Enemy e)
    {
        if (e == null || !IsInstanceValid(e) || e.IsHorde || !e.IsAlive) return;
        _lastHurt[e] = _now;
    }

    public override void _Process(double delta)
    {
        _now += (float)delta;
        foreach (var (e, when) in _lastHurt.ToArray())
        {
            var gone = !IsInstanceValid(e) || !e.IsAlive || e.IsQueuedForDeletion() || _now - when > FeedbackRules.BarLingerSeconds;
            if (gone)
            {
                _lastHurt.Remove(e);
                if (_shown.Remove(e, out var bar)) { bar.Root.Visible = false; _pool.Push(bar); }
                continue;
            }
            if (!_shown.TryGetValue(e, out var b))
            {
                if (_pool.Count == 0) continue;
                b = _pool.Pop();
                b.Root.Visible = true;
                _shown[e] = b;
            }
            b.Root.GlobalPosition = e.GlobalPosition + Vector3.Up * (0.9f * e.Profile.Scale + FeedbackRules.BarHeight);
            var frac = Mathf.Clamp(e.Hp / Math.Max(1f, e.MaxHp), 0f, 1f);
            b.Fill.Scale = new Vector3(Math.Max(0.001f, frac), 1f, 1f);
            b.Fill.Position = new Vector3(-(1f - frac) * 0.45f, 0f, 0.001f);
            b.Name.Visible = e.IsBoss;
            if (e.IsBoss) b.Name.Text = e.DisplayName;
        }
    }
}
