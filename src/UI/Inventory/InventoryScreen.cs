using DungeonCrawler.Characters;
using DungeonCrawler.Core;
using DungeonCrawler.Items;
using DungeonCrawler.Party;
using Godot;

namespace DungeonCrawler.UI.Inventory;

/// <summary>
/// The Tab screen: four paper dolls across the top, the shared bag grid below, and a drop zone.
/// All item movement goes through <see cref="Move"/> so the rules live in one place.
/// </summary>
public partial class InventoryScreen : PanelContainer
{
    private static readonly EquipSlot[] DollOrder =
    {
        EquipSlot.Weapon, EquipSlot.Head, EquipSlot.Offhand,
        EquipSlot.Hands, EquipSlot.Body, EquipSlot.Feet,
        EquipSlot.Ring1, EquipSlot.Amulet, EquipSlot.Ring2,
    };

    private PartyController _party = null!;
    private readonly List<ItemSlotControl> _bag = new();
    private readonly Dictionary<(int, EquipSlot), ItemSlotControl> _equip = new();
    private readonly List<Label> _titles = new();
    private Label _info = null!;
    private ItemSlotControl _drop = null!, _destroy = null!;

    public void Bind(PartyController party) => _party = party;

    public ItemSlotControl BagCell(int index) => _bag[index];
    public ItemSlotControl EquipCell(int member, EquipSlot slot) => _equip[(member, slot)];

    public override void _Ready()
    {
        var margin = new MarginContainer();
        foreach (var edge in new[] { "margin_left", "margin_right", "margin_top", "margin_bottom" }) margin.AddThemeConstantOverride(edge, 14);
        AddChild(margin);
        var root = new VBoxContainer();
        root.AddThemeConstantOverride("separation", 10);
        margin.AddChild(root);

        var header = new HBoxContainer();
        header.AddChild(new Label { Text = "Inventory" });
        var hint = new Label { Text = "   Drag to move or equip.  Double-click / right-click: equip, unequip, or use.  Tab: close", Modulate = new Color(0.7f, 0.7f, 0.7f) };
        hint.AddThemeFontSizeOverride("font_size", 12);
        header.AddChild(hint);
        root.AddChild(header);

        var dolls = new HBoxContainer();
        dolls.AddThemeConstantOverride("separation", 12);
        root.AddChild(dolls);
        for (var m = 0; m < _party.Members.Length; m++)
        {
            var panel = new PanelContainer();
            var v = new VBoxContainer();
            v.AddThemeConstantOverride("separation", 4);
            panel.AddChild(v);
            var title = new Label();
            title.AddThemeFontSizeOverride("font_size", 13);
            v.AddChild(title);
            _titles.Add(title);
            var grid = new GridContainer { Columns = 3 };
            grid.AddThemeConstantOverride("h_separation", 4);
            grid.AddThemeConstantOverride("v_separation", 2);
            v.AddChild(grid);
            foreach (var slot in DollOrder)
            {
                var cell = MakeSlot(SlotRef.Equip(m, slot), SlotRules.Label(slot));
                grid.AddChild(cell);
                _equip[(m, slot)] = cell;
            }
            dolls.AddChild(panel);
        }

        var bottom = new HBoxContainer();
        bottom.AddThemeConstantOverride("separation", 16);
        root.AddChild(bottom);

        var bagGrid = new GridContainer { Columns = 8 };
        bagGrid.AddThemeConstantOverride("h_separation", 4);
        bagGrid.AddThemeConstantOverride("v_separation", 4);
        bottom.AddChild(bagGrid);
        for (var i = 0; i < _party.Bag.Capacity; i++)
        {
            var cell = MakeSlot(SlotRef.Bag(i), "");
            bagGrid.AddChild(cell);
            _bag.Add(cell);
        }

        var side = new VBoxContainer();
        side.AddThemeConstantOverride("separation", 8);
        side.CustomMinimumSize = new Vector2(220, 0);
        bottom.AddChild(side);
        _info = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart };
        _info.AddThemeFontSizeOverride("font_size", 12);
        side.AddChild(_info);
        _drop = MakeSlot(SlotRef.Drop, "DROP");
        _drop.TooltipText = "Drag an item here to leave it on the floor. Companions will not pick it up.";
        side.AddChild(_drop);
        _destroy = MakeSlot(SlotRef.Destroy, "DESTROY");
        _destroy.TooltipText = "Drag an item here to be rid of it for good.";
        side.AddChild(_destroy);
    }

    private ItemSlotControl MakeSlot(SlotRef r, string caption)
        => new() { Screen = this, Ref = r, Caption = caption };

    public void Refresh()
    {
        for (var m = 0; m < _party.Members.Length; m++)
        {
            var c = _party.Members[m];
            _titles[m].Text = $"{c.Name}  {c.Class.Name} L{c.Level}   Armor {c.Stats[Stat.Armor]:0}";
            _titles[m].AddThemeColorOverride("font_color", m == _party.FocusedIndex ? new Color(1f, 0.9f, 0.5f) : Colors.White);
            foreach (var slot in DollOrder)
            {
                var cell = _equip[(m, slot)];
                cell.Item = c.Equipment[slot];
                cell.Highlight = m == _party.FocusedIndex;
                cell.TooltipText = cell.Item == null ? $"{SlotRules.Label(slot)} slot" : ItemText.Describe(cell.Item);
                cell.QueueRedraw();
            }
        }
        var focused = _party.Members[_party.FocusedIndex];
        for (var i = 0; i < _bag.Count; i++)
        {
            var cell = _bag[i];
            cell.Item = _party.Bag[i];
            ItemInstance? compare = null;
            if (cell.Item is { IsEquippable: true } it && focused.Equipment.PreferredSlot(focused, it) is { } ps) compare = focused.Equipment[ps];
            cell.TooltipText = cell.Item == null ? "" : ItemText.Describe(cell.Item, compare);
            cell.QueueRedraw();
        }
        _info.Text = $"Focused: {focused.Name} (F to change)\nDouble-click a bag item to equip it on {focused.Name} or drink it.\nArrows: {_party.ArrowCount}   Free cells: {_party.Bag.FreeSlots}";
        _drop.QueueRedraw();
        _destroy.QueueRedraw();
    }

    private ItemInstance? Get(SlotRef r) => BagActions.Get(_party.Bag, _party.Members, r.ToSlot());

    /// <summary>Drops are accepted generously so the player always gets a message; the rules live in <see cref="BagActions"/>.</summary>
    public bool CanMove(SlotRef from, SlotRef to) => BagActions.CanMove(_party.Bag, _party.Members, from.ToSlot(), to.ToSlot());

    public void Move(SlotRef from, SlotRef to)
    {
        _party.BagMove(from.ToSlot(), to.ToSlot(), _party.Local);
        Refresh();
    }

    /// <summary>Double-click / right-click: equip on the focused member, use a consumable, or unequip to the bag.</summary>
    public void QuickAction(SlotRef r)
    {
        _party.BagQuick(r.ToSlot(), _party.FocusedIndex, _party.Local);
        Refresh();
    }
}
