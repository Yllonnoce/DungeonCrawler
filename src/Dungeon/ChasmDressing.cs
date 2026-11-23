using DungeonCrawler.Dungeon.Generation;
using Godot;

namespace DungeonCrawler.Dungeon;

/// <summary>
/// A crevasse floor's drops and the bridges over them, in two halves. The footings (<see cref="BuildFootings"/>) are built on every
/// role, headless included: a bridge cell has no stone floor, so a deck's or a plank's box is the only thing a walker stands on there.
/// The look (<see cref="BuildLook"/>) is skipped headless: for each 4-connected region of chasm-or-bridge cells, shaft
/// walls (near-black at the rim, warming with depth) from the rim down <see cref="DrawDepth"/>, an ember floor and its light at the bottom, two drifting mist layers below
/// the lip; the deck slabs with a trestle bent under each, and the planks. The cavern's lane decks (<see cref="CrevasseRules.CavernDecks"/>)
/// can give way at runtime (<see cref="SetGone"/>), so each is drawn as its own look (<c>DeckLook_{x}_{y}</c>), the rest of the wood as one mesh.
/// A region holding a cell the ride's pit or the exit island cut (<see cref="RailLine.Pit"/>, <see cref="RailIsland.Cut"/>) is lava (plan 15,
/// Ruling 6): its shaft goes down <see cref="Rails.ExitBridgeRules.LavaDepth"/> to a molten surface lit as the rail gap's slag is
/// (<see cref="Rails.RailGap.Molten"/>, <see cref="Rails.RailGap.Glow"/>), with the upper mist layer only, as its haze, and no ember floor.
/// </summary>
public partial class ChasmDressing : Node3D
{
    /// <summary>How deep the drop is drawn below its lip; a body is judged fallen long before it gets there.</summary>
    public const float DrawDepth = 30f;

    private const float SlabThick = 0.2f;    // a deck or a plank, as thick as a stone floor slab
    private const float TrestleDepth = 12f;  // a deck cell's bent reaches this far below the deck
    private const float WallInset = 0.02f;   // a shaft face sits this far inside the drop, off its neighbour's own faces
    // two layers of wisps, deep enough that the shaft walls are seen falling away first; smoke lit from the embers below, the
    // lower layer the warmer. Each layer is about half clear (the noise's alpha ramp), so the glow shows through the gaps.
    private static readonly float[] MistDepths = { 8f, 18f };
    private static readonly float[] MistAlphas = { 0.25f, 0.32f };
    private static readonly Color[] MistColors = { new(0.25f, 0.14f, 0.1f), new(0.45f, 0.18f, 0.07f) };

    /// <summary>The 4-connected regions of chasm-or-bridge cells.</summary>
    public int Regions { get; private set; }
    public int Decks { get; private set; }
    public int Planks { get; private set; }
    /// <summary>The regions drawn as lava (each holds a cell the pit or the exit island cut).</summary>
    public int Lava { get; private set; }
    /// <summary>The cavern's lane decks (<see cref="CrevasseRules.CavernDecks"/>), each drawn and given way alone.</summary>
    public int Cavern => _cavern.Count;

    /// <summary>The floor's dressing while it is in the tree (one floor at a time), else null.</summary>
    public static ChasmDressing? Instance { get; private set; }

    private readonly HashSet<GridPos> _cavern = new();
    private readonly Dictionary<GridPos, CollisionShape3D> _deckShapes = new();
    private readonly Dictionary<GridPos, MeshInstance3D> _deckLooks = new();

    private readonly List<StandardMaterial3D> _mists = new();
    private float _time;

    /// <summary>Is the look drawn on this run? Not on a headless one (a dedicated server, a test flow), whose footings still stand.</summary>
    public static bool DrawsLook => !DisplayServer.GetName().Equals("headless", StringComparison.OrdinalIgnoreCase);

    public static ChasmDressing Build(FloorLayout layout, Func<GridPos, Vector3> surface, float cellSize, Material plank)
    {
        var node = new ChasmDressing { Name = "Chasms" };
        var regions = FindRegions(layout);
        node.Regions = regions.Count;
        var cut = LavaCells(layout);
        var lava = regions.Select(r => r.Any(cut.Contains)).ToList();
        node.Lava = lava.Count(l => l);
        node.Decks = layout.Bridges.Values.Count(k => k == BridgeKind.Deck);
        node.Planks = layout.Bridges.Values.Count(k => k == BridgeKind.Plank);
        node._cavern.UnionWith(CrevasseRules.CavernDecks(layout));
        node.AddChild(node.BuildFootings(layout, surface, cellSize));
        if (DrawsLook) node.BuildLook(layout, regions, lava, surface, cellSize, plank);
        GD.Print($"[chasm] regions={node.Regions} decks={node.Decks} planks={node.Planks} cavern={node.Cavern} lava={node.Lava}");
        return node;
    }

    public override void _EnterTree() => Instance = this;

    public override void _ExitTree()
    {
        if (Instance == this) Instance = null;
    }

    /// <summary>
    /// A cavern deck <paramref name="cell"/> gives way (<paramref name="gone"/>) or stands again: its footing box is disabled or
    /// enabled, its look hidden or shown, and the cell added to or removed from <paramref name="layout"/>'s
    /// <see cref="FloorLayout.GoneBridges"/>, which the companions' footing reads. Every role, headless included (no look there).
    /// A cell that is no cavern deck, or one with no footing box (a deck on a ramp cell), is an error, and nothing changes.
    /// </summary>
    public void SetGone(FloorLayout layout, GridPos cell, bool gone)
    {
        if (!_cavern.Contains(cell))
        {
            GD.PushError($"[chasm] ERROR: SetGone on {cell}, not a cavern deck");
            return;
        }
        // a cavern deck on a ramp cell has no box (the ramp's floor carries a walker): going there would leave the footing
        // gone and the floor standing
        if (!_deckShapes.TryGetValue(cell, out var shape) || !IsInstanceValid(shape))
        {
            GD.PushError($"[chasm] ERROR: SetGone on {cell}, a cavern deck with no footing box (a ramp cell?)");
            return;
        }
        shape.Disabled = gone;
        if (_deckLooks.TryGetValue(cell, out var look) && IsInstanceValid(look)) look.Visible = !gone;
        if (gone) layout.GoneBridges.Add(cell);
        else layout.GoneBridges.Remove(cell);
    }

    /// <summary>The cells the ride's pit and the exit island cut (empty on a floor without them): a region holding one is lava.</summary>
    private static HashSet<GridPos> LavaCells(FloorLayout layout)
    {
        var cells = new HashSet<GridPos>();
        if (layout.Rail is not { } rail) return cells;
        if (rail.Pit is { } pit) cells.UnionWith(pit.Cut);
        if (rail.Island is { } island) cells.UnionWith(island.Cut(rail));
        return cells;
    }

    /// <summary>Each 4-connected region of chasm-or-bridge cells, scanned in (Y, X) order.</summary>
    private static List<List<GridPos>> FindRegions(FloorLayout layout)
    {
        bool InRegion(GridPos c) => layout.IsChasm(c) || layout.IsBridge(c);
        var seen = new HashSet<GridPos>();
        var regions = new List<List<GridPos>>();
        for (var y = 0; y < layout.Height; y++)
            for (var x = 0; x < layout.Width; x++)
            {
                var start = new GridPos(x, y);
                if (!InRegion(start) || !seen.Add(start)) continue;
                var region = new List<GridPos>();
                var queue = new Queue<GridPos>();
                queue.Enqueue(start);
                while (queue.Count > 0)
                {
                    var c = queue.Dequeue();
                    region.Add(c);
                    foreach (var n in c.Neighbours4())
                        if (InRegion(n) && seen.Add(n)) queue.Enqueue(n);
                }
                regions.Add(region);
            }
        return regions;
    }

    /// <summary>A bridge's world frame: its centre on the cell's floor, its along axis (the lane or the hall) and across it.</summary>
    private static (Vector3 Centre, Vector3 Along, Vector3 Across) Frame(FloorLayout layout, GridPos cell, BridgeKind kind, Func<GridPos, Vector3> surface)
    {
        var along = kind == BridgeKind.Plank ? PlankAlong(layout, cell) : DeckAlong(layout, cell);
        return (surface(cell), along, Vector3.Up.Cross(along)); // (across, up, along) is a right-handed basis
    }

    /// <summary>A deck cell runs along its lane (<see cref="CrevasseRules.DeckRunsAlongX"/>): the one rule the companions' footing reads too.</summary>
    private static Vector3 DeckAlong(FloorLayout layout, GridPos cell) => CrevasseRules.DeckRunsAlongX(layout, cell) ? Vector3.Right : Vector3.Back;

    /// <summary>A plank runs along its hall, the axis without the drop on either side (<see cref="CrevasseRules.PlankRunsAlongX"/>).</summary>
    private static Vector3 PlankAlong(FloorLayout layout, GridPos cell) => CrevasseRules.PlankRunsAlongX(layout, cell) ? Vector3.Right : Vector3.Back;

    /// <summary>A deck fills its cell; a plank is <see cref="CrevasseRules.PlankWidth"/> across, the band a companion's footing keeps to.</summary>
    private static Vector3 SlabSize(BridgeKind kind, float cellSize) => new(kind == BridgeKind.Plank ? CrevasseRules.PlankWidth : cellSize, SlabThick, cellSize);

    /// <summary>
    /// One static body on layer 1 with a box per bridge cell, its top the cell's floor. Built on every role. A deck on a ramp
    /// cell gets none: the ramp's own slope (and the slab under it) is its floor.
    /// </summary>
    private StaticBody3D BuildFootings(FloorLayout layout, Func<GridPos, Vector3> surface, float cellSize)
    {
        var body = new StaticBody3D { Name = "Footings", CollisionLayer = 1, CollisionMask = 0 };
        foreach (var (cell, kind) in layout.Bridges.OrderBy(b => b.Key.Y).ThenBy(b => b.Key.X))
        {
            if (layout[cell] == Cell.Ramp) continue;
            var (centre, along, across) = Frame(layout, cell, kind, surface);
            var shape = new CollisionShape3D
            {
                Name = $"{kind}_{cell.X}_{cell.Y}",
                Shape = new BoxShape3D { Size = SlabSize(kind, cellSize) },
                Transform = new Transform3D(new Basis(across, Vector3.Up, along), centre + Vector3.Down * (SlabThick / 2f)),
            };
            body.AddChild(shape);
            if (_cavern.Contains(cell)) _deckShapes[cell] = shape;
        }
        return body;
    }

    private void BuildLook(FloorLayout layout, List<List<GridPos>> regions, List<bool> lava, Func<GridPos, Vector3> surface, float cellSize, Material plank)
    {
        // near-black rock that warms with depth, as if lit by the embers far below (unshaded: the glow is painted down the walls,
        // since the ember light reaches only the lowest part of the shaft and a player at the lip sees only its upper walls)
        var rock = new StandardMaterial3D { ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, VertexColorUseAsAlbedo = true, VertexColorIsSrgb = true, CullMode = BaseMaterial3D.CullModeEnum.Disabled };
        var ember = new StandardMaterial3D
        {
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, DisableFog = true, // a glow far down, not lost in the floor's haze
            AlbedoTexture = Noise(0.03f, 7, new[] { 0f, 0.55f, 1f }, new[] { new Color(0.08f, 0.01f, 0f), new Color(0.62f, 0.13f, 0.02f), new Color(1f, 0.56f, 0.16f) }),
        };
        var mistNoise = Noise(0.012f, 19, new[] { 0.55f, 0.9f }, new[] { new Color(1f, 1f, 1f, 0f), new Color(1f, 1f, 1f, 1f) });
        for (var m = 0; m < MistDepths.Length; m++)
            _mists.Add(new StandardMaterial3D
            {
                ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
                CullMode = BaseMaterial3D.CullModeEnum.Disabled, AlbedoTexture = mistNoise,
                AlbedoColor = MistColors[m] with { A = MistAlphas[m] }, Uv1Scale = Vector3.One * (m == 0 ? 1f : 0.7f),
                ProximityFadeEnabled = true, ProximityFadeDistance = 2.5f, // no hard line where a layer meets the shaft or a post
            });

        Material? molten = null;
        for (var r = 0; r < regions.Count; r++)
        {
            var region = regions[r];
            var cells = region.ToHashSet();
            if (lava[r]) { LavaLook(region, cells, r, surface, cellSize, rock, molten ??= Rails.RailGap.Molten()); continue; }
            var bottom = region.Min(c => surface(c).Y) - DrawDepth;

            // the shaft: every edge to a cell outside the region, from that neighbour's floor down to the bottom
            var shaft = new SurfaceTool();
            shaft.Begin(Mesh.PrimitiveType.Triangles);
            foreach (var c in region)
                foreach (var n in c.Neighbours4())
                {
                    if (cells.Contains(n)) continue;
                    var d = new Vector3(n.X - c.X, 0, n.Y - c.Y);
                    var side = new Vector3(-d.Z, 0, d.X);
                    var mid = new Vector3((c.X + 0.5f) * cellSize, 0, (c.Y + 0.5f) * cellSize) + d * (cellSize / 2f - WallInset);
                    var top = surface(n).Y;
                    var lip = surface(c).Y;
                    var h = cellSize / 2f;
                    Color Glow(Vector3 p) => WallGlow(lip - p.Y);
                    // in bands, so the warming shows in the upper walls a player can see
                    var bands = new[] { top, lip - 3f, lip - 8f, bottom }.Where(y => y <= top && y >= bottom).Distinct().OrderByDescending(y => y).ToArray();
                    for (var k = 0; k + 1 < bands.Length; k++)
                        Quad(shaft, -d,
                            (mid + side * h) with { Y = bands[k] }, (mid - side * h) with { Y = bands[k] },
                            (mid - side * h) with { Y = bands[k + 1] }, (mid + side * h) with { Y = bands[k + 1] }, colour: Glow);
                }
            AddMesh(shaft, rock, $"Shaft{r}", shadows: false);

            // the ember floor and its light, and the mist layers below the lip of each cell
            var floor = new SurfaceTool();
            floor.Begin(Mesh.PrimitiveType.Triangles);
            foreach (var c in region) CellQuad(floor, c, bottom, cellSize, 1f / 8f);
            AddMesh(floor, ember, $"Embers{r}", shadows: false);
            var cx = region.Average(c => (float)c.X);
            var cy = region.Average(c => (float)c.Y);
            var heart = region.OrderBy(c => (c.X - cx) * (c.X - cx) + (c.Y - cy) * (c.Y - cy)).ThenBy(c => c.Y).ThenBy(c => c.X).First();
            AddChild(new OmniLight3D
            {
                Name = $"EmberGlow{r}", LightColor = new Color(1f, 0.45f, 0.15f), LightEnergy = 1.5f, OmniRange = 18f, ShadowEnabled = false,
                Position = new Vector3((heart.X + 0.5f) * cellSize, bottom + 3f, (heart.Y + 0.5f) * cellSize),
            });
            for (var m = 0; m < MistDepths.Length; m++)
            {
                var mist = new SurfaceTool();
                mist.Begin(Mesh.PrimitiveType.Triangles);
                foreach (var c in region) CellQuad(mist, c, surface(c).Y - MistDepths[m], cellSize, 1f / 16f);
                AddMesh(mist, _mists[m], $"Mist{r}_{m}", shadows: false);
            }
        }

        // the bridges' wood in one mesh: a deck slab with a trestle bent under it, a plank across a hall; a cavern deck, which can
        // give way, is its own look (in line order, the party lane's first)
        var wood = new SurfaceTool();
        wood.Begin(Mesh.PrimitiveType.Triangles);
        foreach (var (cell, kind) in layout.Bridges.OrderBy(b => b.Key.Y).ThenBy(b => b.Key.X))
            if (!_cavern.Contains(cell)) BridgeWood(wood, layout, cell, kind, surface, cellSize);
        AddMesh(wood, plank, "Bridges", shadows: true);
        foreach (var cell in CrevasseRules.CavernDecks(layout))
        {
            var look = new SurfaceTool();
            look.Begin(Mesh.PrimitiveType.Triangles);
            BridgeWood(look, layout, cell, BridgeKind.Deck, surface, cellSize);
            _deckLooks[cell] = AddMesh(look, plank, $"DeckLook_{cell.X}_{cell.Y}", shadows: true);
        }
    }

    /// <summary>
    /// A lava region's look: its shaft from each outside neighbour's floor down <see cref="Rails.ExitBridgeRules.LavaDepth"/> below the
    /// region's lowest lip (dark at the rim, glowing toward the lava), the molten surface there with the slag's glow over its middle, and
    /// the upper mist layer halfway down as its haze. No ember floor.
    /// </summary>
    private void LavaLook(List<GridPos> region, HashSet<GridPos> cells, int r, Func<GridPos, Vector3> surface, float cellSize, Material rock, Material molten)
    {
        var lip = region.Min(c => surface(c).Y);
        var bottom = lip - Rails.ExitBridgeRules.LavaDepth;
        var shaft = new SurfaceTool();
        shaft.Begin(Mesh.PrimitiveType.Triangles);
        foreach (var c in region)
            foreach (var n in c.Neighbours4())
            {
                if (cells.Contains(n)) continue;
                var d = new Vector3(n.X - c.X, 0, n.Y - c.Y);
                var side = new Vector3(-d.Z, 0, d.X);
                var mid = new Vector3((c.X + 0.5f) * cellSize, 0, (c.Y + 0.5f) * cellSize) + d * (cellSize / 2f - WallInset);
                var top = surface(n).Y;
                if (top <= bottom) continue;
                var h = cellSize / 2f;
                Quad(shaft, -d, (mid + side * h) with { Y = top }, (mid - side * h) with { Y = top }, (mid - side * h) with { Y = bottom }, (mid + side * h) with { Y = bottom },
                    colour: p => LavaGlow((lip - p.Y) / Rails.ExitBridgeRules.LavaDepth));
            }
        AddMesh(shaft, rock, $"Shaft{r}", shadows: false);
        var pool = new SurfaceTool();
        pool.Begin(Mesh.PrimitiveType.Triangles);
        foreach (var c in region) CellQuad(pool, c, bottom, cellSize, 1f / 4f);
        AddMesh(pool, molten, $"Lava{r}", shadows: false);
        var cx = region.Average(c => (float)c.X);
        var cy = region.Average(c => (float)c.Y);
        var heart = region.OrderBy(c => (c.X - cx) * (c.X - cx) + (c.Y - cy) * (c.Y - cy)).ThenBy(c => c.Y).ThenBy(c => c.X).First();
        var reach = region.Max(c => MathF.Sqrt((c.X - heart.X) * (c.X - heart.X) + (c.Y - heart.Y) * (c.Y - heart.Y))) * cellSize;
        var glow = Rails.RailGap.Glow($"LavaGlow{r}", Mathf.Clamp(reach + cellSize, Rails.RailGap.GlowRange, 18f));
        glow.Position = new Vector3((heart.X + 0.5f) * cellSize, bottom + 0.8f, (heart.Y + 0.5f) * cellSize);
        AddChild(glow);
        var haze = new SurfaceTool();
        haze.Begin(Mesh.PrimitiveType.Triangles);
        foreach (var c in region) CellQuad(haze, c, surface(c).Y - Rails.ExitBridgeRules.LavaDepth / 2f, cellSize, 1f / 16f);
        AddMesh(haze, _mists[0], $"Mist{r}_0", shadows: false);
    }

    /// <summary>A lava shaft's wall colour at <paramref name="t"/> of the way down (0 the rim, 1 the lava): near-black, then the molten glow.</summary>
    private static Color LavaGlow(float t)
        => new Color(0.04f, 0.03f, 0.025f).Lerp(new Color(0.75f, 0.24f, 0.05f), Mathf.Clamp(t, 0f, 1f) * Mathf.Clamp(t, 0f, 1f));

    /// <summary>One bridge cell's wood: its slab (none on a ramp, whose slope is the deck) and, under a deck, a trestle bent.</summary>
    private static void BridgeWood(SurfaceTool wood, FloorLayout layout, GridPos cell, BridgeKind kind, Func<GridPos, Vector3> surface, float cellSize)
    {
        var (centre, along, across) = Frame(layout, cell, kind, surface);
        var basis = new Basis(across, Vector3.Up, along);
        var ramp = layout[cell] == Cell.Ramp; // the ramp's slope is the deck: only the bent goes under its slab
        if (!ramp) Box(wood, SlabSize(kind, cellSize), centre + Vector3.Down * (SlabThick / 2f), basis);
        if (kind != BridgeKind.Deck) return;
        var under = ramp ? layout.LevelOf(cell) * DungeonBuilder.LevelHeight : centre.Y - SlabThick;
        var postAt = cellSize / 2f - 0.35f;
        foreach (var sgn in new[] { -1f, 1f })
            Box(wood, new Vector3(0.3f, TrestleDepth, 0.3f), (centre + across * (sgn * postAt)) with { Y = under - TrestleDepth / 2f }, basis);
        Box(wood, new Vector3(cellSize - 0.2f, 0.3f, 0.3f), centre with { Y = under - 0.15f }, basis);            // the cap under the deck
        Box(wood, new Vector3(cellSize - 0.4f, 0.25f, 0.25f), centre with { Y = under - TrestleDepth / 2f }, basis); // a tie halfway down
        // one diagonal brace from the cap at one post to the tie at the other
        var from = (centre + across * postAt) with { Y = under - 0.3f };
        var to = (centre - across * postAt) with { Y = under - TrestleDepth / 2f };
        var x = (to - from).Normalized();
        Box(wood, new Vector3(from.DistanceTo(to), 0.2f, 0.2f), (from + to) / 2f, new Basis(x, along.Cross(x), along));
    }

    /// <summary>Only the drawn look drifts its mist: a headless dressing (no mist) does not process at all.</summary>
    public override void _Ready() => SetProcess(_mists.Count > 0);

    public override void _Process(double delta)
    {
        if (_mists.Count == 0) return;
        _time += (float)delta;
        for (var m = 0; m < _mists.Count; m++)
            _mists[m].Uv1Offset = m == 0 ? new Vector3(_time * 0.012f, _time * 0.007f, 0) : new Vector3(-_time * 0.008f, _time * 0.011f, 0);
    }

    /// <summary>The shaft wall's colour this far below its lip: near-black at the rim, a dull ember red by 8 m, brighter at the bottom.</summary>
    private static Color WallGlow(float depth)
    {
        ReadOnlySpan<float> at = stackalloc float[] { 0f, 3f, 8f, DrawDepth };
        var colours = new[] { new Color(0.03f, 0.025f, 0.022f), new Color(0.07f, 0.03f, 0.018f), new Color(0.2f, 0.065f, 0.025f), new Color(0.5f, 0.16f, 0.045f) };
        for (var k = 0; k + 1 < at.Length; k++)
            if (depth <= at[k + 1]) return colours[k].Lerp(colours[k + 1], Mathf.Clamp((depth - at[k]) / (at[k + 1] - at[k]), 0f, 1f));
        return colours[^1];
    }

    /// <summary>A seamless noise texture coloured through a ramp.</summary>
    private static NoiseTexture2D Noise(float frequency, int seed, float[] offsets, Color[] colors) => new()
    {
        Width = 256, Height = 256, Seamless = true,
        Noise = new FastNoiseLite { Frequency = frequency, Seed = seed, NoiseType = FastNoiseLite.NoiseTypeEnum.SimplexSmooth, FractalOctaves = 3 },
        ColorRamp = new Gradient { Offsets = offsets, Colors = colors },
    };

    private MeshInstance3D AddMesh(SurfaceTool st, Material material, string name, bool shadows)
    {
        var mesh = new MeshInstance3D
        {
            Name = name, Mesh = st.Commit(), MaterialOverride = material,
            CastShadow = shadows ? GeometryInstance3D.ShadowCastingSetting.On : GeometryInstance3D.ShadowCastingSetting.Off,
        };
        AddChild(mesh);
        return mesh;
    }

    /// <summary>A flat quad over a cell at <paramref name="y"/>, facing up, UVs in world metres times <paramref name="uvPerMetre"/>.</summary>
    private static void CellQuad(SurfaceTool st, GridPos c, float y, float cellSize, float uvPerMetre)
    {
        var x0 = c.X * cellSize; var z0 = c.Y * cellSize;
        Quad(st, Vector3.Up, new Vector3(x0, y, z0), new Vector3(x0 + cellSize, y, z0), new Vector3(x0 + cellSize, y, z0 + cellSize), new Vector3(x0, y, z0 + cellSize), uvPerMetre);
    }

    /// <summary>
    /// A quad a-b-c-d (in order round its edge) facing <paramref name="normal"/>: Godot's front faces wind clockwise seen from the
    /// front, so the winding is flipped when (b - a) x (c - a) points along the normal. UVs are world X/Z, or X/Y on a wall.
    /// </summary>
    private static void Quad(SurfaceTool st, Vector3 normal, Vector3 a, Vector3 b, Vector3 c, Vector3 d, float uvPerMetre = 0.25f, Func<Vector3, Color>? colour = null)
    {
        var ring = (b - a).Cross(c - a).Dot(normal) > 0f ? new[] { a, d, c, b } : new[] { a, b, c, d };
        Vector2 Uv(Vector3 p) => Mathf.Abs(normal.Y) > 0.5f ? new Vector2(p.X, p.Z) * uvPerMetre : new Vector2(p.X + p.Z, p.Y) * uvPerMetre;
        foreach (var i in new[] { 0, 1, 2, 0, 2, 3 })
        {
            st.SetNormal(normal);
            if (colour != null) st.SetColor(colour(ring[i]));
            st.SetUV(Uv(ring[i]));
            st.AddVertex(ring[i]);
        }
    }

    /// <summary>A box of <paramref name="size"/> (in its basis' X, Y, Z) at <paramref name="centre"/>, appended to the mesh.</summary>
    private static void Box(SurfaceTool st, Vector3 size, Vector3 centre, Basis basis)
        => st.AppendFrom(new BoxMesh { Size = size }, 0, new Transform3D(basis, centre));
}
