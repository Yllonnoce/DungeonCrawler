using DungeonCrawler.Adventure;
using DungeonCrawler.Adventure.Fortune;
using DungeonCrawler.Core;
using DungeonCrawler.Party;
using Godot;

namespace DungeonCrawler.Dungeon.Props;

/// <summary>
/// The fortune shrine: an altar with its price written over it. Touching it (once) pays the price and opens the boon card;
/// the <see cref="FortuneDirector"/> resolves the pick (the server's, in co-op). The meshes come from <c>DungeonBuilder.AltarMeshes</c>.
/// </summary>
public partial class Shrine : StaticBody3D, IInteractable
{
    public ShrineRules.Price Price { get; set; }
    public int Floor { get; set; }
    public bool Used { get; private set; }
    private Label3D? _label;

    public string Prompt => Used ? "Cold altar" : Price switch
    {
        ShrineRules.Price.Blood => "Shrine: it asks blood (a quarter of every member's breath)",
        ShrineRules.Price.Gold => $"Shrine: it asks {ShrineRules.GoldPrice(Floor)} gold",
        ShrineRules.Price.Curse => "Shrine: it asks a curse",
        _ => "Shrine: touch the altar",
    };

    public override void _Ready()
    {
        Name = "Shrine";
        CollisionLayer = 9; // solid, and something to use
        CollisionMask = 0;
        _label = new Label3D
        {
            Text = Used ? "Cold." : ShrineRules.PriceLine(Price, Floor), FontSize = 30, PixelSize = 0.0045f, Modulate = new Color(0.95f, 0.8f, 0.55f), OutlineSize = 8,
            Billboard = BaseMaterial3D.BillboardModeEnum.Enabled, NoDepthTest = false, Position = new Vector3(0, 2.0f, 0),
            AutowrapMode = TextServer.AutowrapMode.Word, Width = 420f, HorizontalAlignment = HorizontalAlignment.Center, VisibilityRangeEnd = 10f, VisibilityRangeEndMargin = 2f,
        };
        AddChild(_label);
    }

    /// <summary>One use: the candles go out and the line changes. <paramref name="silent"/> for a client or a reload (no event).</summary>
    public void MarkUsed(bool silent = false)
    {
        Used = true;
        if (_label != null) _label.Text = "Cold.";
        foreach (var light in GetChildren().OfType<OmniLight3D>()) light.LightEnergy = 0.3f;
        foreach (var mi in GetChildren().OfType<MeshInstance3D>())
            if (mi.MaterialOverride is StandardMaterial3D { EmissionEnabled: true } candle) { var cold = (StandardMaterial3D)candle.Duplicate(); cold.EmissionEnabled = false; mi.MaterialOverride = cold; }
        if (!silent) EventBus.Instance.RaisePropChanged(Net.NetRefs.Shrine(0), "used");
    }

    public void Interact(PartyController party, Adventurer actor)
    {
        if (Used || Net.NetSession.IsClient) return; // a client's reach sends InteractIntent like every prop
        FortuneDirector.Instance?.OpenShrine(this, party, actor);
    }
}
