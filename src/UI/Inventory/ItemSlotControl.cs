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
    /// <summary>Set on the shop's cells: clicks and drags go to the shop instead of <see cref="Screen"/>, and the price caption shows over the item.</summary>
    public UI.Shop.ShopScreen? Shop { get; set; }
    /// <summary>Which part of the shop this cell belongs to; <see cref="SlotRef.BagIndex"/> is the stock, ledger or bag index.</summary>
    public UI.Shop.ShopSection Section { get; set; }
    /// <summary>A shop cell on the merchant's side (wares or buyback), else the bag's side.</summary>
    public bool StockSide => Section != UI.Shop.ShopSection.Bag;
    /// <summary>Draws a green arrow: the item beats what the focused member wears there.</summary>
    public bool Upgrade { get; set; }
    /// <summary>Two-letter class glyphs, lit for the members who can use the item.</summary>
    public IReadOnlyList<(string Glyph, bool Usable)> Glyphs { get; set; } = Array.Empty<(string, bool)>();
    private bool _armed; // a left press on a shop cell that has not turned into a drag

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
            if (Upgrade) DrawString(font, new Vector2(0, 13), "▲", HorizontalAlignment.Right, Size.X - 3, 12, new Color(0.4f, 1f, 0.45f));
            var x = 3f;
            foreach (var (glyph, usable) in Glyphs)
            {
                DrawString(font, new Vector2(x, 10), glyph, HorizontalAlignment.Left, -1, 9, usable ? new Color(1f, 1f, 1f, 0.9f) : new Color(0.6f, 0.6f, 0.65f, 0.45f));
                x += font.GetStringSize(glyph, HorizontalAlignment.Left, -1, 9).X + 2f;
            }
        }
        if (Item != null && Shop != null && Caption != "")
        {
            DrawRect(new Rect2(new Vector2(1, Size.Y - 15), new Vector2(Size.X - 2, 14)), new Color(0f, 0f, 0f, 0.7f));
            DrawString(font, new Vector2(0, Size.Y - 4), Caption, HorizontalAlignment.Center, Size.X, 11, new Color(1f, 0.85f, 0.4f));
        }
        else if (Item == null && Caption != "")
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
        _armed = false;
        var preview = new Label { Text = Item.DisplayName };
        preview.AddThemeColorOverride("font_color", new Color(RarityInfo.ColorHex(Item.Rarity)));
        SetDragPreview(preview);
        if (Shop != null) return new Godot.Collections.Dictionary { ["shop"] = (int)Section, ["i"] = Ref.BagIndex, ["id"] = Item.Id.ToString("N") };
        // the item this cell shows rides along: in co-op the server refuses the move if that cell holds something else by the time it lands
        var d = Ref.ToDict();
        d["id"] = Item.Id.ToString("N");
        return d;
    }

    public override bool _CanDropData(Vector2 atPosition, Variant data)
    {
        if (data.VariantType != Variant.Type.Dictionary) return false;
        var d = data.AsGodotDictionary();
        if (Shop != null) return d.ContainsKey("shop") && (int)d["shop"] != (int)Section; // wares or buyback onto the bag buys, bag onto the merchant's side sells
        return !d.ContainsKey("shop") && Screen.CanMove(SlotRef.FromDict(d), Ref);
    }

    public override void _DropData(Vector2 atPosition, Variant data)
    {
        var d = data.AsGodotDictionary();
        if (Shop != null) Shop.Dropped((UI.Shop.ShopSection)(int)d["shop"], (int)d["i"], ShownId(d));
        else Screen.Move(SlotRef.FromDict(d), Ref, ShownId(d));
    }

    /// <summary>The id of the item the dragged cell showed, if the drag carried one.</summary>
    private static Guid? ShownId(Godot.Collections.Dictionary d)
        => d.ContainsKey("id") && Guid.TryParseExact((string)d["id"], "N", out var id) ? id : null;

    public override void _GuiInput(InputEvent @event)
    {
        if (Shop != null)
        {
            // a click (press and release without a drag) buys or sells; the action waits for the release so a drag never also clicks
            if (@event is not InputEventMouseButton { ButtonIndex: MouseButton.Left } click) return;
            if (click.Pressed) _armed = Item != null;
            else if (_armed) { _armed = false; Shop.Clicked(Section, Ref.BagIndex, Item?.Id); }
            AcceptEvent();
            return;
        }
        if (@event is not InputEventMouseButton { Pressed: true } mb) return;
        if ((mb.ButtonIndex == MouseButton.Left && mb.DoubleClick) || mb.ButtonIndex == MouseButton.Right)
        {
            Screen.QuickAction(Ref, Item?.Id);
            AcceptEvent();
        }
    }
}
