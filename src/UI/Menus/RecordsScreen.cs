using DungeonCrawler.Adventure.Run;
using DungeonCrawler.Core;
using DungeonCrawler.Save;
using DungeonCrawler.Save.Profile;
using Godot;

namespace DungeonCrawler.UI.Menus;

/// <summary>
/// The Records page off the title: best depth, the fastest story clear, the highest heat clear (and the modes that did it),
/// victories, elites, runs; the last seven dailies; the last twenty runs; the twenty-four deeds, earned ones bright. The
/// border is grey, gold after a heat-5 clear, ember after a heat-10 clear; the title sits under the heading.
/// </summary>
public partial class RecordsScreen : PanelContainer
{
    public event Action? Closed;
    private Label _heading = null!, _title = null!;
    private VBoxContainer _records = null!, _dailies = null!, _runs = null!, _deeds = null!;
    private StyleBoxFlat _style = null!;
    public static readonly Color GreyBorder = new(0.35f, 0.35f, 0.38f), GoldBorder = new("c9a44a"), EmberBorder = new("d9532b");

    /// <summary>How many deed rows the page lists (tests).</summary>
    public int DeedRows => _deeds.GetChildCount();
    public string TitleText => _title.Text;

    public override void _Ready()
    {
        CustomMinimumSize = new Vector2(760, 0);
        _style = new StyleBoxFlat { BgColor = new Color(0.06f, 0.06f, 0.07f, 0.96f), BorderWidthLeft = 2, BorderWidthRight = 2, BorderWidthTop = 2, BorderWidthBottom = 2, BorderColor = GreyBorder };
        AddThemeStyleboxOverride("panel", _style);
        var margin = new MarginContainer();
        foreach (var edge in new[] { "margin_left", "margin_right", "margin_top", "margin_bottom" }) margin.AddThemeConstantOverride(edge, 18);
        AddChild(margin);
        var v = new VBoxContainer();
        v.AddThemeConstantOverride("separation", 8);
        margin.AddChild(v);
        _heading = new Label { Text = "RECORDS", HorizontalAlignment = HorizontalAlignment.Center };
        _heading.AddThemeFontSizeOverride("font_size", 28);
        v.AddChild(_heading);
        _title = new Label { HorizontalAlignment = HorizontalAlignment.Center, Modulate = GoldBorder, Visible = false };
        v.AddChild(_title);
        var scroll = new ScrollContainer { CustomMinimumSize = new Vector2(720, 440), HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        v.AddChild(scroll);
        var body = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        body.AddThemeConstantOverride("separation", 10);
        scroll.AddChild(body);
        _records = Block(body, "THE RECORDS");
        _dailies = Block(body, "THE DAILIES");
        _runs = Block(body, "THE LAST RUNS");
        _deeds = Block(body, "THE DEEDS");
        var back = new Button { Text = "Back", FocusMode = FocusModeEnum.None, CustomMinimumSize = new Vector2(160, 36) };
        back.Pressed += () => { EventBus.Instance.PlaySfx("ui_close"); Closed?.Invoke(); };
        var row = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
        row.AddChild(back);
        v.AddChild(row);
    }

    private static VBoxContainer Block(VBoxContainer body, string heading)
    {
        var h = new Label { Text = heading, Modulate = new Color(0.7f, 0.65f, 0.55f) };
        h.AddThemeFontSizeOverride("font_size", 13);
        body.AddChild(h);
        var block = new VBoxContainer();
        block.AddThemeConstantOverride("separation", 2);
        body.AddChild(block);
        return block;
    }

    public void Refresh()
    {
        var p = ProfileStore.Current;
        var tier = ProfileRules.BorderTier(p);
        _style.BorderColor = tier switch { 2 => EmberBorder, 1 => GoldBorder, _ => GreyBorder };
        _title.Text = ProfileRules.Title(p) ?? "";
        _title.Visible = _title.Text != "";
        Fill(_records, Records(p));
        Fill(_dailies, p.Daily.Count == 0 ? new[] { "No daily descent yet." } : p.Daily.OrderByDescending(kv => kv.Key).Take(7).Select(kv => $"{kv.Key}   floor {kv.Value.Depth}   score {kv.Value.Score}{(kv.Value.Victory ? "   cleared" : kv.Value.Fallen ? "   fallen" : "")}"));
        Fill(_runs, p.Runs.Count == 0 ? new[] { "No run recorded yet." } : p.Runs.Select(r => $"{r.Run} · seed {r.Seed} · floor {r.DeepestFloor} · heat {r.Heat} · score {r.Score} · {(r.Victory ? "cleared" : r.Fallen ? "fallen" : "left")}"));
        foreach (var child in _deeds.GetChildren()) child.QueueFree();
        foreach (var d in DeedRules.All)
        {
            var earned = p.Deeds.TryGetValue(d.Id, out var utc);
            var when = earned && DateTime.TryParse(utc, null, System.Globalization.DateTimeStyles.RoundtripKind, out var at) ? at.ToString("yyyy-MM-dd") : "";
            var l = new Label
            {
                Text = earned ? $"{d.Name} — {d.Condition} · unlocked {d.UnlockText} · {when}" : $"{d.Name} — {d.Condition} · unlocks {d.UnlockText}",
                Modulate = earned ? Colors.White : new Color(1f, 1f, 1f, 0.55f), AutowrapMode = TextServer.AutowrapMode.WordSmart,
            };
            _deeds.AddChild(l);
        }
    }

    private static IEnumerable<string> Records(ProfileDto p)
    {
        var r = p.Records;
        yield return $"Best depth: {(r.BestDepth > 0 ? $"floor {r.BestDepth}" : "—")}";
        yield return $"Fastest story clear: {(r.FastestStorySeconds is { } s ? SlotInfo.FormatTime(s) : "—")}";
        var heat = ProfileRules.HighestHeatClear(p);
        var modes = heat < 0 ? "" : string.Join(", ", r.HeatClears.Where(kv => kv.Key.EndsWith($":{heat}")).Select(kv => kv.Key[..kv.Key.LastIndexOf(':')]).Distinct());
        yield return $"Highest heat clear: {(heat < 0 ? "—" : $"{heat} ({modes})")}";
        yield return $"Victories: {r.Victories}";
        yield return $"Elites slain: {r.ElitesSlain}";
        yield return $"Runs: {r.Runs}";
    }

    private static void Fill(VBoxContainer block, IEnumerable<string> lines)
    {
        foreach (var child in block.GetChildren()) child.QueueFree();
        foreach (var line in lines) block.AddChild(new Label { Text = line, AutowrapMode = TextServer.AutowrapMode.WordSmart });
    }
}
