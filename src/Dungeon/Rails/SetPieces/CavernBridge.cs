using DungeonCrawler.Core;
using DungeonCrawler.Dungeon.Generation;
using DungeonCrawler.Dungeon.Props;
using DungeonCrawler.Net;
using Godot;

namespace DungeonCrawler.Dungeon.Rails;

/// <summary>
/// Ride 4's collapsing bridge (plan 13, Rulings 2-7): the cavern's party-lane deck cells, each with its own rails and sleepers. The party
/// track is cut bare over the whole span (<see cref="RailTrack.CutMore"/>, by the builder) and each cell's are laid here by
/// <see cref="RailTrack.LaySpan"/> of that same track, so they lie where the lane's own would and meet the track at both lips. The deck
/// itself is the cavern's (<see cref="ChasmDressing"/>): <see cref="GiveWay"/> hides a cell's rails, sends its deck and footing with
/// <see cref="ChasmDressing.SetGone"/>, tumbles a few timbers and plays <c>timber_collapse</c> every <see cref="BridgeRules.CueEveryCells"/>
/// cells; <see cref="Stand"/> puts every cell back. Headless no mesh is built, but the cells are kept (the footing goes on every role). The
/// <see cref="RailEncounterController"/> runs the collapse on the server and in single player; it tells "gone {n}" and "stands"
/// (<see cref="NetRef"/>), which a client's copy shows. The node stays at the origin.
/// </summary>
public partial class CavernBridge : Node3D, ISoundPoint
{
    /// <summary>The floor whose cavern decks give way (<see cref="FloorLayout.GoneBridges"/>).</summary>
    public FloorLayout Layout { get; init; } = null!;
    /// <summary>Its wire name ("bridge:{piece}"), set by the builder; empty: not told.</summary>
    public string NetRef { get; init; } = "";

    /// <summary>The cells (in line order) gone now: always the first <see cref="Gone"/> of them.</summary>
    public int Gone { get; private set; }
    /// <summary>Its deck cells.</summary>
    public int Count => _cells.Count;
    /// <summary>The party S of its start (the first cell's near edge) and end (the last cell's far edge); NaN before <see cref="Build"/>.</summary>
    public float From { get; private set; } = float.NaN;
    public float To { get; private set; } = float.NaN;
    /// <summary>The far edge of each cell, in line order (<see cref="BridgeRules.GoneCells"/> reads them).</summary>
    public IReadOnlyList<float> FarS => _far;
    /// <summary>Its cells: the grid cell and its party S span, in line order.</summary>
    public IReadOnlyList<(GridPos Cell, float FromS, float ToS)> Cells => _cells;
    /// <summary>The party S the gone cells reach to (the last gone cell's far edge), or <see cref="From"/> while none is gone.</summary>
    public float GoneToS => Gone > 0 ? _far[Gone - 1] : From;

    /// <summary>Where it groans and falls from, on every role: the line at its middle.</summary>
    public Vector3 SoundPoint { get; private set; }
    /// <summary>Where the cart's front comes onto it.</summary>
    public Vector3 Entry { get; private set; }

    private readonly List<(GridPos Cell, float FromS, float ToS)> _cells = new();
    private readonly List<float> _far = new();
    private readonly List<Transform3D> _middles = new();
    /// <summary>Each cell's rails and sleepers (null headless).</summary>
    private readonly List<Node3D?> _looks = new();
    /// <summary>Each cell's embers under it, shown once it is gone (null headless): the glow light and the haze rising out of the hole.</summary>
    private readonly List<(OmniLight3D Glow, MeshInstance3D Haze)?> _embers = new();
    /// <summary>The newest this many gone cells cast their embers' light (more keep their haze: <see cref="HazeCells"/>): the hole behind the train reads, few lights burn.</summary>
    public const int GlowCells = 3;
    /// <summary>The newest this many gone cells keep their embers' haze (the hole near the train; Task 6 review Minor 10: no 22 billboards once the bridge is gone).</summary>
    public const int HazeCells = 6;
    private readonly StandardMaterial3D _wood = new() { AlbedoColor = new Color(0.3f, 0.2f, 0.11f), Roughness = 0.95f };
    private readonly StandardMaterial3D _dark = new() { AlbedoColor = new Color(0.17f, 0.11f, 0.06f), Roughness = 1f };

    /// <summary>
    /// Lay each cell's rails and sleepers along <paramref name="track"/> (the party lane's: its <see cref="TrackSampler.Track"/> cut bare
    /// over the span), one mesh pair per cell under its own node <c>BridgeCell_{x}_{y}</c>; headless none, but the cells are kept.
    /// Add this node to the tree first (<see cref="RailTrack.LaySpan"/> places in its frame).
    /// </summary>
    public void Build(TrackSampler track, IReadOnlyList<(GridPos Cell, float FromS, float ToS)> cells)
    {
        _cells.Clear(); _far.Clear(); _middles.Clear(); _looks.Clear(); _embers.Clear();
        Gone = 0;
        if (cells.Count == 0) return;
        From = cells[0].FromS;
        To = cells[^1].ToS;
        SoundPoint = track.Point((From + To) / 2f);
        Entry = track.Point(From);
        foreach (var c in cells)
        {
            _cells.Add(c);
            _far.Add(c.ToS);
            _middles.Add(track.Frame((c.FromS + c.ToS) / 2f).Orthonormalized());
            Node3D? look = null;
            if (ChasmDressing.DrawsLook)
            {
                look = new Node3D { Name = $"BridgeCell_{c.Cell.X}_{c.Cell.Y}" };
                AddChild(look);
                track.Track.LaySpan(look, c.FromS, c.ToS, "Deck");
            }
            _looks.Add(look);
            _embers.Add(ChasmDressing.DrawsLook ? Embers(c.Cell, _middles[^1]) : null);
        }
    }

    /// <summary>
    /// Cells [<see cref="Gone"/>, <paramref name="upTo"/>) give way: their rails hidden, their decks and footings gone
    /// (<see cref="ChasmDressing.SetGone"/>). With a <paramref name="debrisRoot"/> (the live collapse, not a catch-up) a few timbers of
    /// each tumble under it and <c>timber_collapse</c> plays every <see cref="BridgeRules.CueEveryCells"/> cells, for the step's last
    /// <see cref="LiveCells"/> cells only (a client catching up on missed tells does not get every cell's at once). The server tells "gone {n}".
    /// With no <see cref="ChasmDressing"/> the footing cannot go: an error.
    /// </summary>
    public void GiveWay(int upTo, Node3D? debrisRoot)
    {
        upTo = Math.Min(upTo, _cells.Count);
        if (upTo <= Gone) return;
        var dressing = Dressing();
        for (var k = Gone; k < upTo; k++)
        {
            if (_looks[k] is { } look && IsInstanceValid(look)) look.Visible = false;
            dressing?.SetGone(Layout, _cells[k].Cell, true);
            if (debrisRoot == null || k < upTo - LiveCells) continue;
            if (ChasmDressing.DrawsLook) Tumble(debrisRoot, k);
            if (k % BridgeRules.CueEveryCells == 0) RideAudio.At("timber_collapse", _middles[k].Origin);
        }
        Gone = upTo;
        ShowEmbers();
        Tell($"gone {Gone}");
    }

    /// <summary>Every cell stands again: its rails shown, its deck and footing back. The server tells "stands" when cells were gone.</summary>
    public void Stand()
    {
        var was = Gone;
        var dressing = was > 0 || _cells.Any(c => Layout.GoneBridges.Contains(c.Cell)) ? Dressing() : null;
        for (var k = 0; k < _cells.Count; k++)
        {
            if (_looks[k] is { } look && IsInstanceValid(look)) look.Visible = true;
            if (k < was || Layout.GoneBridges.Contains(_cells[k].Cell)) dressing?.SetGone(Layout, _cells[k].Cell, false);
        }
        Gone = 0;
        ShowEmbers();
        if (was > 0) Tell("stands");
    }

    /// <summary>Client: what the server told ("gone {n}", "stands"), shown here with the collapse's timbers under <paramref name="root"/>.</summary>
    public bool ShowTold(string what, Node3D root)
    {
        if (what == "stands") { Stand(); return true; }
        if (what.StartsWith("gone ", StringComparison.Ordinal) && int.TryParse(what.AsSpan(5), out var n))
        {
            if (n < Gone) Stand();
            GiveWay(n, root);
            return true;
        }
        return false;
    }

    /// <summary>
    /// A cell's embers (the legibility fix round, as the broken bridge's gap glows: <see cref="BrokenBridge.EmberGlow"/>,
    /// <see cref="BrokenBridge.EmberHaze"/>): the glow <see cref="BrokenBridge.GlowDepth"/> under the deck's middle and the haze on its level,
    /// both hidden until the cell goes, so the hole it leaves reads from the seat as a warm glow up out of the dark.
    /// </summary>
    private (OmniLight3D, MeshInstance3D) Embers(GridPos cell, Transform3D middle)
    {
        var glow = BrokenBridge.EmberGlow($"GoneGlow_{cell.X}_{cell.Y}");
        glow.Visible = false;
        AddChild(glow);
        glow.GlobalPosition = middle.Origin + Vector3.Down * BrokenBridge.GlowDepth;
        var haze = BrokenBridge.EmberHaze($"GoneHaze_{cell.X}_{cell.Y}", BrokenBridge.HazeSize);
        haze.Visible = false;
        AddChild(haze);
        haze.GlobalPosition = middle.Origin;
        return (glow, haze);
    }

    /// <summary>The newest <see cref="HazeCells"/> gone cells' haze shown, the newest <see cref="GlowCells"/>' light too; a standing cell's hidden.</summary>
    private void ShowEmbers()
    {
        for (var k = 0; k < _embers.Count; k++)
        {
            if (_embers[k] is not { } e || !IsInstanceValid(e.Glow) || !IsInstanceValid(e.Haze)) continue;
            e.Haze.Visible = k < Gone && k >= Gone - HazeCells;
            e.Glow.Visible = k < Gone && k >= Gone - GlowCells;
        }
    }

    /// <summary>A live give-way tumbles timbers and cues for at most this many cells of one step.</summary>
    private const int LiveCells = 2;

    /// <summary>The floor's chasm dressing (it carries the decks' footing), or null with an error: the cells' footing cannot go or stand without it.</summary>
    private ChasmDressing? Dressing()
    {
        if (ChasmDressing.Instance is { } d && IsInstanceValid(d)) return d;
        GD.PushError($"[rails] ERROR: bridge {Name}: no ChasmDressing, so its cells' footing cannot go or stand");
        return null;
    }

    private void Tell(string what)
    {
        if (NetRef.Length > 0 && !NetSession.IsClient) EventBus.Instance?.RaisePropChanged(NetRef, what);
    }

    /// <summary>A plank across the deck and a sleeper of cell <paramref name="k"/> fall away under <paramref name="root"/> (cosmetic).</summary>
    private void Tumble(Node3D root, int k)
    {
        var parts = new List<(Vector3, Transform3D, Material)>
        {
            (new Vector3(2.1f, 0.06f, 0.92f), new Transform3D(Basis.Identity, new Vector3(0f, -0.05f, -1f)), _dark),
            (new Vector3(2.1f, 0.06f, 0.92f), new Transform3D(Basis.Identity, new Vector3(0f, -0.05f, 0f)), _dark),
            (new Vector3(1.4f, 0.12f, 0.22f), new Transform3D(Basis.Identity, new Vector3(0f, 0.06f, 1f)), _wood),
            (new Vector3(1.4f, 0.12f, 0.22f), new Transform3D(Basis.Identity, new Vector3(0f, 0.06f, 0.4f)), _wood),
        };
        TimberDebris.Tumble(root, _middles[k], parts, Vector3.Down * 4f, "BridgeTimber"); // down, not on ahead: the deck drops away
    }

    /// <summary>Cell <paramref name="k"/>'s rails-and-sleepers node, or null (headless; tests read it).</summary>
    internal Node3D? LookOf(int k) => k >= 0 && k < _looks.Count ? _looks[k] : null;
}
