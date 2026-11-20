using DungeonCrawler.Dungeon.Generation;
using Godot;

namespace DungeonCrawler.Dungeon;

/// <summary>
/// A crevasse floor's drops and the bridges over them, in two halves. The footings (<see cref="BuildFootings"/>) are built on every
/// role, headless included: a bridge cell has no stone floor, so a deck's or a plank's box is the only thing a walker stands on there.
/// The look (<see cref="BuildLook"/>) is skipped headless: for each 4-connected region of chasm-or-bridge cells, near-black shaft
/// walls from the rim down <see cref="DrawDepth"/>, an ember floor and its light at the bottom, two drifting mist layers below
/// the lip; the deck slabs with a trestle bent under each, and the planks.
/// </summary>
public partial class ChasmDressing : Node3D
{
    /// <summary>How deep the drop is drawn below its lip; a body is judged fallen long before it gets there.</summary>
    public const float DrawDepth = 30f;

    private const float SlabThick = 0.2f;    // a deck or a plank, as thick as a stone floor slab
    private const float PlankWidth = 1.6f;   // a plank across a hall's crevasse
    private const float TrestleDepth = 12f;  // a deck cell's bent reaches this far below the deck
    private const float WallInset = 0.02f;   // a shaft face sits this far inside the drop, off its neighbour's own faces
    private static readonly float[] MistDepths = { 4f, 10f };
    private static readonly float[] MistAlphas = { 0.35f, 0.2f };

    /// <summary>The 4-connected regions of chasm-or-bridge cells.</summary>
    public int Regions { get; private set; }
    public int Decks { get; private set; }
    public int Planks { get; private set; }

    private readonly List<StandardMaterial3D> _mists = new();
    private float _time;

    /// <summary>Is the look drawn on this run? Not on a headless one (a dedicated server, a test flow), whose footings still stand.</summary>
    public static bool DrawsLook => !DisplayServer.GetName().Equals("headless", StringComparison.OrdinalIgnoreCase);

    public static ChasmDressing Build(FloorLayout layout, Func<GridPos, Vector3> surface, float cellSize, Material plank)
    {
        var node = new ChasmDressing { Name = "Chasms" };
        var regions = FindRegions(layout);
        node.Regions = regions.Count;
        node.Decks = layout.Bridges.Values.Count(k => k == BridgeKind.Deck);
        node.Planks = layout.Bridges.Values.Count(k => k == BridgeKind.Plank);
        node.AddChild(BuildFootings(layout, surface, cellSize));
        if (DrawsLook) node.BuildLook(layout, regions, surface, cellSize, plank);
        else node.SetProcess(false);
        GD.Print($"[chasm] regions={node.Regions} decks={node.Decks} planks={node.Planks}");
        return node;
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

    /// <summary>A deck cell runs along its lane: the step between its neighbours in the party or the enemy line (X when neither holds it).</summary>
    private static Vector3 DeckAlong(FloorLayout layout, GridPos cell)
    {
        if (layout.Rail is { } rail)
            foreach (var lane in new[] { rail.Party, rail.Enemy })
            {
                var i = IndexOf(lane, cell);
                if (i < 0) continue;
                var step = lane[Math.Min(lane.Count - 1, i + 1)] - lane[Math.Max(0, i - 1)];
                return Math.Abs(step.X) >= Math.Abs(step.Y) ? Vector3.Right : Vector3.Back;
            }
        return Vector3.Right;
    }

    private static int IndexOf(IReadOnlyList<GridPos> lane, GridPos cell)
    {
        for (var i = 0; i < lane.Count; i++) if (lane[i] == cell) return i;
        return -1;
    }

    /// <summary>A plank runs along its hall, the axis without the drop on either side.</summary>
    private static Vector3 PlankAlong(FloorLayout layout, GridPos cell)
        => layout.IsChasm(cell + new GridPos(1, 0)) || layout.IsChasm(cell + new GridPos(-1, 0)) ? Vector3.Back : Vector3.Right;

    private static Vector3 SlabSize(BridgeKind kind, float cellSize) => new(kind == BridgeKind.Plank ? PlankWidth : cellSize, SlabThick, cellSize);

    /// <summary>
    /// One static body on layer 1 with a box per bridge cell, its top the cell's floor. Built on every role. A deck on a ramp
    /// cell gets none: the ramp's own slope (and the slab under it) is its floor.
    /// </summary>
    private static StaticBody3D BuildFootings(FloorLayout layout, Func<GridPos, Vector3> surface, float cellSize)
    {
        var body = new StaticBody3D { Name = "Footings", CollisionLayer = 1, CollisionMask = 0 };
        foreach (var (cell, kind) in layout.Bridges.OrderBy(b => b.Key.Y).ThenBy(b => b.Key.X))
        {
            if (layout[cell] == Cell.Ramp) continue;
            var (centre, along, across) = Frame(layout, cell, kind, surface);
            body.AddChild(new CollisionShape3D
            {
                Name = $"{kind}_{cell.X}_{cell.Y}",
                Shape = new BoxShape3D { Size = SlabSize(kind, cellSize) },
                Transform = new Transform3D(new Basis(across, Vector3.Up, along), centre + Vector3.Down * (SlabThick / 2f)),
            });
        }
        return body;
    }

    private void BuildLook(FloorLayout layout, List<List<GridPos>> regions, Func<GridPos, Vector3> surface, float cellSize, Material plank)
    {
        var rock = new StandardMaterial3D { AlbedoColor = new Color(0.045f, 0.04f, 0.038f), Roughness = 1f, CullMode = BaseMaterial3D.CullModeEnum.Disabled };
        var ember = new StandardMaterial3D
        {
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            AlbedoTexture = Noise(0.03f, 7, new[] { 0f, 0.55f, 1f }, new[] { new Color(0.08f, 0.01f, 0f), new Color(0.62f, 0.13f, 0.02f), new Color(1f, 0.56f, 0.16f) }),
        };
        var mistNoise = Noise(0.012f, 19, new[] { 0.3f, 1f }, new[] { new Color(1f, 1f, 1f, 0f), new Color(1f, 1f, 1f, 1f) });
        for (var m = 0; m < MistDepths.Length; m++)
            _mists.Add(new StandardMaterial3D
            {
                ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
                CullMode = BaseMaterial3D.CullModeEnum.Disabled, AlbedoTexture = mistNoise,
                AlbedoColor = new Color(0.36f, 0.3f, 0.28f, MistAlphas[m]), Uv1Scale = Vector3.One * (m == 0 ? 1f : 0.7f),
            });

        for (var r = 0; r < regions.Count; r++)
        {
            var region = regions[r];
            var cells = region.ToHashSet();
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
                    var h = cellSize / 2f;
                    Quad(shaft, -d,
                        (mid + side * h) with { Y = top }, (mid - side * h) with { Y = top },
                        (mid - side * h) with { Y = bottom }, (mid + side * h) with { Y = bottom });
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

        // the bridges' wood in one mesh: a deck slab with a trestle bent under it, a plank across a hall
        var wood = new SurfaceTool();
        wood.Begin(Mesh.PrimitiveType.Triangles);
        foreach (var (cell, kind) in layout.Bridges.OrderBy(b => b.Key.Y).ThenBy(b => b.Key.X))
        {
            var (centre, along, across) = Frame(layout, cell, kind, surface);
            var basis = new Basis(across, Vector3.Up, along);
            var ramp = layout[cell] == Cell.Ramp; // the ramp's slope is the deck: only the bent goes under its slab
            if (!ramp) Box(wood, SlabSize(kind, cellSize), centre + Vector3.Down * (SlabThick / 2f), basis);
            if (kind != BridgeKind.Deck) continue;
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
        AddMesh(wood, plank, "Bridges", shadows: true);
    }

    public override void _Process(double delta)
    {
        _time += (float)delta;
        for (var m = 0; m < _mists.Count; m++)
            _mists[m].Uv1Offset = m == 0 ? new Vector3(_time * 0.012f, _time * 0.007f, 0) : new Vector3(-_time * 0.008f, _time * 0.011f, 0);
    }

    /// <summary>A seamless noise texture coloured through a ramp.</summary>
    private static NoiseTexture2D Noise(float frequency, int seed, float[] offsets, Color[] colors) => new()
    {
        Width = 256, Height = 256, Seamless = true,
        Noise = new FastNoiseLite { Frequency = frequency, Seed = seed, NoiseType = FastNoiseLite.NoiseTypeEnum.SimplexSmooth, FractalOctaves = 3 },
        ColorRamp = new Gradient { Offsets = offsets, Colors = colors },
    };

    private void AddMesh(SurfaceTool st, Material material, string name, bool shadows)
        => AddChild(new MeshInstance3D
        {
            Name = name, Mesh = st.Commit(), MaterialOverride = material,
            CastShadow = shadows ? GeometryInstance3D.ShadowCastingSetting.On : GeometryInstance3D.ShadowCastingSetting.Off,
        });

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
    private static void Quad(SurfaceTool st, Vector3 normal, Vector3 a, Vector3 b, Vector3 c, Vector3 d, float uvPerMetre = 0.25f)
    {
        var ring = (b - a).Cross(c - a).Dot(normal) > 0f ? new[] { a, d, c, b } : new[] { a, b, c, d };
        Vector2 Uv(Vector3 p) => Mathf.Abs(normal.Y) > 0.5f ? new Vector2(p.X, p.Z) * uvPerMetre : new Vector2(p.X + p.Z, p.Y) * uvPerMetre;
        foreach (var i in new[] { 0, 1, 2, 0, 2, 3 })
        {
            st.SetNormal(normal);
            st.SetUV(Uv(ring[i]));
            st.AddVertex(ring[i]);
        }
    }

    /// <summary>A box of <paramref name="size"/> (in its basis' X, Y, Z) at <paramref name="centre"/>, appended to the mesh.</summary>
    private static void Box(SurfaceTool st, Vector3 size, Vector3 centre, Basis basis)
        => st.AppendFrom(new BoxMesh { Size = size }, 0, new Transform3D(basis, centre));
}
