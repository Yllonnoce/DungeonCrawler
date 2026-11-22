using DungeonCrawler.Core;
using DungeonCrawler.Dungeon.Generation;
using DungeonCrawler.Dungeon.Props;
using DungeonCrawler.Net;
using Godot;

namespace DungeonCrawler.Dungeon.Rails;

/// <summary>
/// Ride 4's slag-line drop (plan 13, Rulings 16-18): the slag deck's <see cref="LeapRules.DropCells"/> cells two short of the cavern's end,
/// each with its own rails and sleepers. The enemy track is cut bare over the drop (<see cref="RailTrack.CutMore"/>, by the builder) and each
/// cell's are laid here by <see cref="RailTrack.LaySpan"/> of that same track, so they lie where the lane's own would and meet the track at both
/// lips. The deck itself is the cavern's (<see cref="ChasmDressing"/>): <see cref="GiveWay"/> hides the cells' rails, sends both decks and their
/// footing (<see cref="ChasmDressing.SetGone"/>), tumbles a few timbers, lights the red lamp on its stub post at the near edge (the party lane's
/// side, as the broken bridge's: <see cref="BrokenBridge.LampEnergy"/>, <see cref="BrokenBridge.LampRange"/>; dark until then) and plays
/// <c>timber_collapse</c>; <see cref="Stand"/> puts it all back. Headless no rail mesh is built, but the cells are kept (the footing goes on
/// every role). The <see cref="RailEncounterController"/> calls it on the server and in single player; it tells "drops" and "stands"
/// (<see cref="NetRef"/>), which a client's copy shows. The node stays at the origin.
/// </summary>
public partial class SlagDrop : Node3D, ISoundPoint
{
    /// <summary>The floor whose slag decks give way (<see cref="FloorLayout.GoneBridges"/>).</summary>
    public FloorLayout Layout { get; init; } = null!;
    /// <summary>Its wire name ("leap:{piece}"), set by the builder; empty: not told.</summary>
    public string NetRef { get; init; } = "";
    /// <summary>The drop's slag-lane cells, in line order (the twins of <see cref="LeapRules.DropSpan"/>'s party cells).</summary>
    public IReadOnlyList<GridPos> Twins { get; init; } = Array.Empty<GridPos>();

    /// <summary>The drop's cells have given way (until it stands again).</summary>
    public bool Dropped { get; private set; }
    /// <summary>The enemy S of its near edge and far edge; NaN before <see cref="Build"/>.</summary>
    public float From { get; private set; } = float.NaN;
    public float To { get; private set; } = float.NaN;
    /// <summary>Where it falls from, on every role: the slag line at its middle.</summary>
    public Vector3 SoundPoint { get; private set; }
    /// <summary>The red lamp's place in the world (the legibility gate may aim at it).</summary>
    public Vector3 LampPoint => _lamp is { } lamp && IsInstanceValid(lamp) ? lamp.GlobalPosition : SoundPoint;
    /// <summary>The red lamp's light is on (it is lit only while the drop is down).</summary>
    public bool LampLit => _lampLight is { } light && IsInstanceValid(light) && light.Visible;

    /// <summary>Each cell's enemy S span, in line order.</summary>
    private readonly List<(float FromS, float ToS)> _spans = new();
    private readonly List<Transform3D> _middles = new();
    /// <summary>Each cell's rails and sleepers (null headless).</summary>
    private readonly List<Node3D?> _looks = new();
    private MeshInstance3D? _lamp;
    private OmniLight3D? _lampLight;
    private StandardMaterial3D? _lit, _unlit;
    private readonly StandardMaterial3D _wood = new() { AlbedoColor = new Color(0.3f, 0.2f, 0.11f), Roughness = 0.95f };
    private readonly StandardMaterial3D _dark = new() { AlbedoColor = new Color(0.17f, 0.11f, 0.06f), Roughness = 1f };

    /// <summary>The lamp's post stands this far from the slag line's middle, toward the party lane (as the broken bridge's).</summary>
    const float PostOut = 1.6f;
    const float PostHeight = 1.3f;

    /// <summary>
    /// Lay each of <see cref="Twins"/>' rails and sleepers along <paramref name="enemy"/> (the enemy lane's: its <see cref="TrackSampler.Track"/>
    /// cut bare over [<paramref name="nearS"/>, <paramref name="farS"/>]), the span shared evenly between the cells, one mesh pair per cell under
    /// its own node <c>SlagDropCell_{x}_{y}</c> (headless none, but the cells are kept), and stand the stub post with its red lamp, dark, at the
    /// near edge on <paramref name="innerSide"/> (+1 the track frame's +X: the party lane's side). Add this node to the tree first
    /// (<see cref="RailTrack.LaySpan"/> places in its frame).
    /// </summary>
    public void Build(TrackSampler enemy, float nearS, float farS, int innerSide)
    {
        _spans.Clear(); _middles.Clear(); _looks.Clear();
        Dropped = false;
        From = nearS;
        To = farS;
        if (Twins.Count == 0 || !(farS > nearS)) return;
        SoundPoint = enemy.Point((nearS + farS) / 2f);
        var length = (farS - nearS) / Twins.Count;
        for (var k = 0; k < Twins.Count; k++)
        {
            float from = nearS + k * length, to = k == Twins.Count - 1 ? farS : nearS + (k + 1) * length; // the last ends at the exact far edge
            _spans.Add((from, to));
            _middles.Add(enemy.Frame((from + to) / 2f).Orthonormalized());
            Node3D? look = null;
            if (ChasmDressing.DrawsLook)
            {
                var cell = Twins[k];
                look = new Node3D { Name = $"SlagDropCell_{cell.X}_{cell.Y}" };
                AddChild(look);
                enemy.Track.LaySpan(look, from, to, "Deck");
            }
            _looks.Add(look);
        }
        BuildLamp(enemy.Frame(nearS).Orthonormalized(), innerSide);
    }

    /// <summary>
    /// The drop's cells give way: their rails hidden, their decks and footings gone (<see cref="ChasmDressing.SetGone"/>), the red lamp lit.
    /// With a <paramref name="root"/> (the live call, not a catch-up) a few timbers of each tumble under it and <c>timber_collapse</c> plays.
    /// The server tells "drops". With no <see cref="ChasmDressing"/> the footing cannot go: an error.
    /// </summary>
    public void GiveWay(Node3D? root)
    {
        if (Dropped || _spans.Count == 0) return;
        Dropped = true;
        var dressing = Dressing();
        for (var k = 0; k < Twins.Count; k++)
        {
            if (_looks[k] is { } look && IsInstanceValid(look)) look.Visible = false;
            dressing?.SetGone(Layout, Twins[k], true);
            if (root != null && ChasmDressing.DrawsLook) Tumble(root, k);
        }
        Light(true);
        if (root != null) RideAudio.At("timber_collapse", SoundPoint);
        Tell("drops");
    }

    /// <summary>The drop stands again: its rails shown, its decks and footing back, the lamp dark. The server tells "stands" when it was down.</summary>
    public void Stand()
    {
        var was = Dropped;
        var dressing = was || Twins.Any(Layout.GoneBridges.Contains) ? Dressing() : null;
        for (var k = 0; k < _looks.Count; k++)
            if (_looks[k] is { } look && IsInstanceValid(look)) look.Visible = true;
        foreach (var cell in Twins)
            if (was || Layout.GoneBridges.Contains(cell)) dressing?.SetGone(Layout, cell, false);
        Light(false);
        Dropped = false;
        if (was) Tell("stands");
    }

    /// <summary>Client: what the server told ("drops", "stands"), shown here with the timbers under <paramref name="root"/>. True when it was one of those.</summary>
    public bool ShowTold(string what, Node3D root)
    {
        switch (what)
        {
            case "drops": GiveWay(root); return true;
            case "stands": Stand(); return true;
            default: return false;
        }
    }

    /// <summary>The floor's chasm dressing (it carries the decks' footing), or null with an error: the cells' footing cannot go or stand without it.</summary>
    private ChasmDressing? Dressing()
    {
        if (ChasmDressing.Instance is { } d && IsInstanceValid(d)) return d;
        GD.PushError($"[rails] ERROR: slag drop {Name}: no ChasmDressing, so its cells' footing cannot go or stand");
        return null;
    }

    private void Tell(string what)
    {
        if (NetRef.Length > 0 && !NetSession.IsClient) EventBus.Instance?.RaisePropChanged(NetRef, what);
    }

    /// <summary>The stub post and its red lamp at the near edge (in the enemy track's <paramref name="frame"/> there, −Z along the line), on the deck short of the drop: dark.</summary>
    private void BuildLamp(Transform3D frame, int innerSide)
    {
        var red = new Color(1f, 0.08f, 0.04f);
        _lit = new StandardMaterial3D { AlbedoColor = red, EmissionEnabled = true, Emission = red, EmissionEnergyMultiplier = 3f };
        _unlit = new StandardMaterial3D { AlbedoColor = new Color(0.25f, 0.05f, 0.04f), Roughness = 0.6f };
        var post = new Vector3(innerSide * PostOut, 0f, 0.3f);
        AddChild(new MeshInstance3D { Name = "DropLampPost", Mesh = new BoxMesh { Size = new Vector3(0.12f, PostHeight, 0.12f) }, MaterialOverride = _wood, Transform = frame * new Transform3D(Basis.Identity, post + Vector3.Up * (PostHeight / 2f)) });
        _lamp = new MeshInstance3D { Name = "DropLamp", Mesh = new BoxMesh { Size = new Vector3(0.2f, 0.24f, 0.2f) }, MaterialOverride = _unlit, Transform = frame * new Transform3D(Basis.Identity, post + Vector3.Up * (PostHeight + 0.12f)) };
        AddChild(_lamp);
        _lampLight = new OmniLight3D
        {
            Name = "DropLampLight", LightColor = red, LightEnergy = BrokenBridge.LampEnergy, OmniRange = BrokenBridge.LampRange, ShadowEnabled = false, Visible = false,
            Transform = frame * new Transform3D(Basis.Identity, post + Vector3.Up * (PostHeight + 0.2f)),
        };
        AddChild(_lampLight);
    }

    /// <summary>The red lamp lit (the drop is down) or dark.</summary>
    private void Light(bool on)
    {
        if (_lampLight is { } light && IsInstanceValid(light)) light.Visible = on;
        if (_lamp is { } lamp && IsInstanceValid(lamp)) lamp.MaterialOverride = on ? _lit : _unlit;
    }

    /// <summary>Planks across the deck and a sleeper of cell <paramref name="k"/> fall away under <paramref name="root"/> (cosmetic), as the bridge's do.</summary>
    private void Tumble(Node3D root, int k)
    {
        var parts = new List<(Vector3, Transform3D, Material)>
        {
            (new Vector3(2.1f, 0.06f, 0.92f), new Transform3D(Basis.Identity, new Vector3(0f, -0.05f, -1f)), _dark),
            (new Vector3(2.1f, 0.06f, 0.92f), new Transform3D(Basis.Identity, new Vector3(0f, -0.05f, 0f)), _dark),
            (new Vector3(1.4f, 0.12f, 0.22f), new Transform3D(Basis.Identity, new Vector3(0f, 0.06f, 1f)), _wood),
            (new Vector3(1.4f, 0.12f, 0.22f), new Transform3D(Basis.Identity, new Vector3(0f, 0.06f, 0.4f)), _wood),
        };
        TimberDebris.Tumble(root, _middles[k], parts, Vector3.Down * 4f, "SlagDropTimber"); // down: the deck drops away
    }

    /// <summary>Cell <paramref name="k"/>'s rails-and-sleepers node, or null (headless; tests read it).</summary>
    internal Node3D? LookOf(int k) => k >= 0 && k < _looks.Count ? _looks[k] : null;
}
