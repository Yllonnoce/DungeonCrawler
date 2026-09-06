using DungeonCrawler.Items;
using Godot;

namespace DungeonCrawler.UI.Inventory;

/// <summary>One cell of the inventory screen. Draws its item, supports drag-and-drop, double/right click for quick actions.</summary>
public partial class ItemSlotControl : Control
{
    public static readonly Vector2 CellSize = new(58, 58);

    public InventoryScreen Screen { get; set; } = null!;
    public SlotRef Ref { get; set; }
    public ItemInstance? Item { get; set; }
    public string Caption { get; set; } = "";
    public bool Highlight { get; set; }

    public override void _Ready()
    {
        ItemIcons.Changed += QueueRedraw;
        CustomMinimumSize = CellSize;
        MouseFilter = MouseFilterEnum.Stop;
    }

    public override void _ExitTree() => ItemIcons.Changed -= QueueRedraw;

    public override void _Draw()
    {
        var rect = new Rect2(Vector2.Zero, Size);
        DrawRect(rect, Ref.IsDestroy ? new Color(0.05f, 0.05f, 0.05f, 0.9f) : Ref.IsDrop ? new Color(0.3f, 0.1f, 0.1f, 0.8f) : new Color(0.1f, 0.1f, 0.12f, 0.9f));
        var border = Item != null ? new Color(RarityInfo.ColorHex(Item.Rarity)) : Highlight ? new Color(1f, 0.9f, 0.5f) : new Color(0.35f, 0.35f, 0.4f);
        DrawRect(rect, border, filled: false, width: Item != null && Item.Rarity >= Rarity.Rare ? 2f : 1f);

        var font = GetThemeDefaultFont();
        if (Item != null)
        {
            if (ItemIcons.Get(Item) is { } icon)
            {
                var pad = 3f;
                DrawTextureRect(icon, new Rect2(new Vector2(pad, pad), Size - new Vector2(pad * 2, pad * 2)), false);
            }
            else
            {
                DrawRect(new Rect2(new Vector2(10, 8), Size - new Vector2(20, 26)), new Color(Item.Color));
                var abbr = Abbreviation(Item);
                DrawString(font, new Vector2(4, Size.Y - 6), abbr, HorizontalAlignment.Left, Size.X - 8, 11, new Color(0.9f, 0.9f, 0.9f));
            }
            if (Item.StackCount > 1)
            {
                var count = Item.StackCount.ToString();
                DrawString(font, new Vector2(1, 15), count, HorizontalAlignment.Right, Size.X - 3, 12, Colors.Black);
                DrawString(font, new Vector2(0, 14), count, HorizontalAlignment.Right, Size.X - 3, 12, Colors.White);
            }
        }
        else if (Caption != "")
        {
            DrawString(font, new Vector2(0, Size.Y / 2 + 4), Caption, HorizontalAlignment.Center, Size.X, 10, new Color(0.5f, 0.5f, 0.55f));
        }
    }

    private static string Abbreviation(ItemInstance item)
    {
        var name = item.BaseName;
        var words = name.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return words.Length >= 2 ? $"{words[0][..Math.Min(4, words[0].Length)]} {words[1][..Math.Min(3, words[1].Length)]}" : name[..Math.Min(7, name.Length)];
    }

    public override Variant _GetDragData(Vector2 atPosition)
    {
        if (Item == null || Ref.IsZone) return default;
        var preview = new Label { Text = Item.DisplayName };
        preview.AddThemeColorOverride("font_color", new Color(RarityInfo.ColorHex(Item.Rarity)));
        SetDragPreview(preview);
        return Ref.ToDict();
    }

    public override bool _CanDropData(Vector2 atPosition, Variant data)
        => data.VariantType == Variant.Type.Dictionary && Screen.CanMove(SlotRef.FromDict(data.AsGodotDictionary()), Ref);

    public override void _DropData(Vector2 atPosition, Variant data)
        => Screen.Move(SlotRef.FromDict(data.AsGodotDictionary()), Ref);

    public override void _GuiInput(InputEvent @event)
    {
        if (@event is not InputEventMouseButton { Pressed: true } mb) return;
        if ((mb.ButtonIndex == MouseButton.Left && mb.DoubleClick) || mb.ButtonIndex == MouseButton.Right)
        {
            Screen.QuickAction(Ref);
            AcceptEvent();
        }
    }
}
