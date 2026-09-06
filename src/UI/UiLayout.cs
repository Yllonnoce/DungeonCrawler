using Godot;

namespace DungeonCrawler.UI;

/// <summary>Anchor helpers. Explicit anchors are used because SetAnchorsPreset on code-built controls left them unsized.</summary>
public static class UiLayout
{
    public static T FillParent<T>(this T c) where T : Control
    {
        c.AnchorLeft = 0; c.AnchorTop = 0; c.AnchorRight = 1; c.AnchorBottom = 1;
        c.OffsetLeft = 0; c.OffsetTop = 0; c.OffsetRight = 0; c.OffsetBottom = 0;
        c.GrowHorizontal = Control.GrowDirection.Both;
        c.GrowVertical = Control.GrowDirection.Both;
        return c;
    }
}
