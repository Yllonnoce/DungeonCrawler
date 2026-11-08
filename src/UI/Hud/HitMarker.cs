using DungeonCrawler.Combat;
using Godot;

namespace DungeonCrawler.UI.Hud;

/// <summary>Four ticks around the crosshair that flick outward for <see cref="FeedbackRules.HitMarkerSeconds"/> when the local member lands a hit; a red X on a kill.</summary>
public partial class HitMarker : Control
{
    private const float KillSeconds = 0.25f;
    private float _left, _total;
    private bool _kill;

    public bool Active => _left > 0f;

    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Ignore;
        SetAnchorsPreset(LayoutPreset.Center);
        OffsetLeft = -20; OffsetTop = -20; OffsetRight = 20; OffsetBottom = 20;
    }

    public void Flick(bool kill)
    {
        _kill = kill;
        _left = _total = kill ? KillSeconds : FeedbackRules.HitMarkerSeconds;
        QueueRedraw();
    }

    public override void _Process(double delta)
    {
        if (_left <= 0f) return;
        _left -= (float)delta;
        QueueRedraw();
    }

    public override void _Draw()
    {
        if (_left <= 0f) return;
        var k = 1f - Mathf.Clamp(_left / Math.Max(_total, 0.01f), 0f, 1f); // 0 at the flick, 1 at the end
        var c = Size / 2f;
        if (_kill)
        {
            var red = new Color(1f, 0.2f, 0.2f, 1f - k);
            DrawLine(c + new Vector2(-9, -9), c + new Vector2(9, 9), red, 2f);
            DrawLine(c + new Vector2(-9, 9), c + new Vector2(9, -9), red, 2f);
            return;
        }
        var gap = 5f + 6f * k;
        var white = new Color(1f, 1f, 1f, 1f - k * 0.6f);
        DrawLine(c + new Vector2(-gap, 0), c + new Vector2(-gap - 10, 0), white, 2f);
        DrawLine(c + new Vector2(gap, 0), c + new Vector2(gap + 10, 0), white, 2f);
        DrawLine(c + new Vector2(0, -gap), c + new Vector2(0, -gap - 10), white, 2f);
        DrawLine(c + new Vector2(0, gap), c + new Vector2(0, gap + 10), white, 2f);
    }
}
