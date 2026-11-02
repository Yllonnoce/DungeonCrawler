using DungeonCrawler.Core;
using DungeonCrawler.Items;
using DungeonCrawler.Party;
using DungeonCrawler.UI.Inventory;
using Godot;

namespace DungeonCrawler.UI.Shop;

/// <summary>
/// The merchant's shop: his stock for the floor on the left (price under each item, sold slots greyed), the party's bag on
/// the right (what each item would fetch), the purse in the middle. A click buys or sells, as does a drag from one side to
/// the other; buying or selling a legendary asks first. Every trade goes through <see cref="PartyController.ShopBuy"/> / <see cref="PartyController.ShopSell"/>.
/// </summary>
public partial class ShopScreen : PanelContainer
{
    private const int StockColumns = 4;
    private PartyController _party = null!;
    private int _floor;
    private readonly List<ItemSlotControl> _stock = new();
    private readonly List<ItemSlotControl> _bag = new();
    private GridContainer _stockGrid = null!;
    private Label _gold = null!;
    private ConfirmationDialog _confirm = null!;
    private int _pendingBuy = -1;
    private int _pendingSell = -1;
    private Guid _pendingSellItem;

    /// <summary>Raised as the shop opens, before it shows: the HUD closes its other screens.</summary>
    public event Action? Opening;
    /// <summary>Raised once the shop has closed.</summary>
    public event Action? Closed;

    public int Floor => _floor;
    /// <summary>The merchant the shop was opened from: he keeps his conversation pose while it is open. Null when opened otherwise.</summary>
    public Adventure.Npc? Keeper { get; private set; }
    /// <summary>True from <see cref="Opening"/> until the shop shows: the HUD keeps the mouse released while the dialogue it came from closes.</summary>
    public bool IsOpening { get; private set; }
    public string GoldText => _gold.Text;
    public ItemSlotControl StockCell(int index) => _stock[index];
    public ItemSlotControl BagCell(int index) => _bag[index];
    public int StockCount => _stock.Count;
    /// <summary>True while the legendary buy or sell question is up.</summary>
    public bool Confirming => _confirm.Visible;
    /// <summary>Answers the question as the OK button does (the headless flows).</summary>
    public void Confirm() { OnConfirmed(); _confirm.Hide(); }

    public void Bind(PartyController party) => _party = party;

    public override void _Ready()
    {
        ProcessMode = ProcessModeEnum.Always;
        var margin = new MarginContainer();
        foreach (var edge in new[] { "margin_left", "margin_right", "margin_top", "margin_bottom" }) margin.AddThemeConstantOverride(edge, 14);
        AddChild(margin);
        var root = new VBoxContainer();
        root.AddThemeConstantOverride("separation", 10);
        margin.AddChild(root);

        var header = new HBoxContainer();
        var title = new Label { Text = "The Merchant" };
        title.AddThemeFontSizeOverride("font_size", 20);
        header.AddChild(title);
        var hint = new Label { Text = "   Click an item to buy or sell it, or drag it across.  Esc: leave", Modulate = new Color(0.7f, 0.7f, 0.7f) };
        hint.AddThemeFontSizeOverride("font_size", 12);
        header.AddChild(hint);
        root.AddChild(header);

        var body = new HBoxContainer();
        body.AddThemeConstantOverride("separation", 18);
        root.AddChild(body);

        var left = new VBoxContainer();
        left.AddThemeConstantOverride("separation", 6);
        left.AddChild(new Label { Text = "His wares" });
        _stockGrid = new GridContainer { Columns = StockColumns };
        _stockGrid.AddThemeConstantOverride("h_separation", 4);
        _stockGrid.AddThemeConstantOverride("v_separation", 4);
        left.AddChild(_stockGrid);
        body.AddChild(left);

        var centre = new VBoxContainer { CustomMinimumSize = new Vector2(150, 0), Alignment = BoxContainer.AlignmentMode.Center };
        centre.AddThemeConstantOverride("separation", 14);
        _gold = new Label { HorizontalAlignment = HorizontalAlignment.Center, Modulate = new Color(1f, 0.85f, 0.4f) };
        _gold.AddThemeFontSizeOverride("font_size", 18);
        centre.AddChild(_gold);
        var arrows = new Label { Text = "buy  →\n←  sell", HorizontalAlignment = HorizontalAlignment.Center, Modulate = new Color(0.6f, 0.6f, 0.6f) };
        centre.AddChild(arrows);
        var leave = new Button { Text = "Leave", FocusMode = FocusModeEnum.None };
        leave.Pressed += Close;
        centre.AddChild(leave);
        body.AddChild(centre);

        var right = new VBoxContainer();
        right.AddThemeConstantOverride("separation", 6);
        right.AddChild(new Label { Text = "Your bag" });
        var bagGrid = new GridContainer { Columns = 8 };
        bagGrid.AddThemeConstantOverride("h_separation", 4);
        bagGrid.AddThemeConstantOverride("v_separation", 4);
        right.AddChild(bagGrid);
        body.AddChild(right);
        for (var i = 0; i < _party.Bag.Capacity; i++)
        {
            var cell = new ItemSlotControl { Shop = this, StockSide = false, Ref = SlotRef.Bag(i) };
            bagGrid.AddChild(cell);
            _bag.Add(cell);
        }

        _confirm = new ConfirmationDialog { Title = "A legendary", OkButtonText = "Buy", CancelButtonText = "Not now" };
        _confirm.Confirmed += OnConfirmed;
        _confirm.Canceled += () => { _pendingBuy = -1; _pendingSell = -1; };
        AddChild(_confirm);

        EventBus.Instance.GoldChanged += OnGoldChanged;
    }

    private void OnConfirmed()
    {
        var (buy, sell) = (_pendingBuy, _pendingSell);
        _pendingBuy = -1; _pendingSell = -1;
        if (buy >= 0) Buy(buy);
        else if (sell >= 0 && _party.Bag[sell]?.Id == _pendingSellItem) Sell(sell); // the cell still holds what was asked about (by id: a co-op bag sync rebuilds the items)
    }

    public override void _ExitTree() => EventBus.Instance.GoldChanged -= OnGoldChanged;

    private void OnGoldChanged(int total, int delta) { if (Visible) Refresh(); }

    /// <summary>Shows the merchant's stock for <paramref name="floor"/> beside the bag.</summary>
    public void Open(int floor, Adventure.Npc? keeper = null)
    {
        _floor = floor;
        Keeper = keeper;
        IsOpening = true;
        try
        {
            Opening?.Invoke();
            BuildStock();
            Visible = true;
        }
        finally { IsOpening = false; }
        Refresh();
        EventBus.Instance.PlaySfx("ui_open");
        GD.Print($"[Shop] open for floor {floor}: {_stock.Count} wares, {_party.ShopSoldOnFloor.Count} sold, {_party.Gold} gold");
        Callable.From(() => { if (IsInsideTree()) Position = ((GetViewportRect().Size - Size) / 2f).Floor(); }).CallDeferred();
    }

    public void Close()
    {
        if (!Visible) return;
        Visible = false;
        if (Keeper is { } keeper && IsInstanceValid(keeper)) keeper.Converse(null); // back to Idle once the trading ends
        Keeper = null;
        _pendingBuy = -1;
        _pendingSell = -1;
        _confirm.Hide();
        EventBus.Instance.PlaySfx("ui_close");
        Closed?.Invoke();
    }

    /// <summary>One cell per stock item of the floor (the stock is rolled once per floor; a new floor rebuilds the cells).</summary>
    private void BuildStock()
    {
        var stock = _party.ShopStockFor(_floor);
        if (_stock.Count == stock.Items.Count) return;
        foreach (var c in _stock) c.QueueFree();
        _stock.Clear();
        for (var i = 0; i < stock.Items.Count; i++)
        {
            var cell = new ItemSlotControl { Shop = this, StockSide = true, Ref = SlotRef.Bag(i) };
            _stockGrid.AddChild(cell);
            _stock.Add(cell);
        }
    }

    public void Refresh()
    {
        if (_stock.Count == 0) BuildStock();
        var stock = _party.ShopStockFor(_floor);
        for (var i = 0; i < _stock.Count; i++)
        {
            var cell = _stock[i];
            var item = i < stock.Items.Count ? stock.Items[i] : null;
            var sold = _party.ShopSoldOnFloor.Contains(i);
            cell.Item = item;
            cell.Caption = item == null ? "" : $"{Prices.BuyPrice(item)}g";
            cell.Modulate = sold ? new Color(1f, 1f, 1f, 0.4f) : Colors.White;
            cell.TooltipText = item == null ? "" : sold ? $"{ItemText.Describe(item)}\n\nSold." : $"{ItemText.Describe(item)}\n\nPrice: {Prices.BuyPrice(item)} gold";
            cell.QueueRedraw();
        }
        for (var i = 0; i < _bag.Count; i++)
        {
            var cell = _bag[i];
            var item = _party.Bag[i];
            cell.Item = item;
            cell.Caption = item != null && Prices.Sellable(item) ? $"{Prices.SellPrice(item)}g" : "";
            cell.TooltipText = item == null ? "" : Prices.Sellable(item) ? $"{ItemText.Describe(item)}\n\nSells for {Prices.SellPrice(item)} gold" : $"{ItemText.Describe(item)}\n\nHe will not buy this.";
            cell.QueueRedraw();
        }
        _gold.Text = $"{_party.Gold} gold";
    }

    /// <summary>A click on a cell: a stock cell buys it, a bag cell sells it.</summary>
    public void Clicked(bool stockSide, int index)
    {
        if (stockSide) TryBuy(index);
        else TrySell(index);
    }

    /// <summary>A drag landed on the other side: from the stock it buys, from the bag it sells.</summary>
    public void Dropped(bool fromStock, int index) => Clicked(fromStock, index);

    /// <summary>Buys stock slot <paramref name="index"/>, asking first when it is a legendary.</summary>
    public void TryBuy(int index)
    {
        var stock = _party.ShopStockFor(_floor);
        if (index >= 0 && index < stock.Items.Count && stock.Items[index].Rarity == Rarity.Legendary && !_party.ShopSoldOnFloor.Contains(index))
        {
            var item = stock.Items[index];
            _pendingBuy = index;
            _pendingSell = -1;
            _confirm.OkButtonText = "Buy";
            _confirm.DialogText = $"Buy {item.DisplayName} for {Prices.BuyPrice(item)} gold?";
            _confirm.PopupCentered();
            return;
        }
        Buy(index);
    }

    /// <summary>Sells bag cell <paramref name="index"/>, asking first when it is a legendary (he never resells one).</summary>
    public void TrySell(int index)
    {
        if (index >= 0 && index < _party.Bag.Capacity && _party.Bag[index] is { Rarity: Rarity.Legendary } item && Prices.Sellable(item))
        {
            _pendingSell = index;
            _pendingSellItem = item.Id;
            _pendingBuy = -1;
            _confirm.OkButtonText = "Sell";
            _confirm.DialogText = $"Sell {item.DisplayName} for {Prices.SellPrice(item)} gold? The merchant never resells.";
            _confirm.PopupCentered();
            return;
        }
        Sell(index);
    }

    private void Buy(int index)
    {
        _party.ShopBuy(index);
        Refresh();
    }

    private void Sell(int index)
    {
        _party.ShopSell(BagSlot.Bag(index));
        Refresh();
    }

    /// <summary>The talk pose rules of a conversation: Talk while his line is still playing, the waiting pose after.</summary>
    public override void _Process(double delta)
    {
        if (Visible && Keeper is { } keeper && IsInstanceValid(keeper)) keeper.Converse(Audio.AudioManager.Instance?.VoiceBusy == true);
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (!Visible) return;
        if (@event is InputEventKey { Pressed: true, Echo: false, Keycode: Key.Escape } || @event.IsActionPressed("ui_cancel"))
        {
            Close();
            GetViewport().SetInputAsHandled();
        }
    }
}
