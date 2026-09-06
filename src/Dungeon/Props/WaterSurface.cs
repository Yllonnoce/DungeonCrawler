using DungeonCrawler.Dungeon.Generation;
using Godot;

namespace DungeonCrawler.Dungeon.Props;

/// <summary>
/// One pond or stream, built from the layout: a sunken bed with sloped banks (collision included), a shader-lit
/// surface, a plank bridge and culvert grates for streams, drips for ponds, and a running-water ambience.
/// </summary>
public partial class WaterSurface : Node3D
{
    public const float Depth = 0.5f;          // bed below the floor at the middle
    public const float SurfaceDrop = 0.12f;   // water level below the floor
    public const float BankWidth = 1.2f;
    public const float ShoalDepth = 0.2f;     // under a bridge, so a body can still step up onto it
    private const int Sub = 4;                // quads per cell side

    private static Shader? _shader;
    private static NoiseTexture2D? _normalA, _normalB;

    public static WaterSurface Build(FloorLayout layout, WaterFeature feature, Func<GridPos, Vector3> surface, float cellSize, Material bedMaterial, Material plankMaterial)
    {
        var node = new WaterSurface { Name = $"Water_{feature.Kind}_{feature.Cells[0].X}_{feature.Cells[0].Y}" };
        var origin = surface(feature.Cells[0]);
        node.Position = origin;
        var floorY = origin.Y;
        var stream = feature.Kind == WaterKind.Stream;
        var cells = feature.Cells.ToHashSet();

        // ---- bed: a height field over the water cells that rises to floor level at every shore
        float BedY(float wx, float wz)
        {
            var cell = new GridPos((int)MathF.Floor(wx / cellSize), (int)MathF.Floor(wz / cellSize));
            float shore = float.MaxValue, wall = float.MaxValue, bridge = float.MaxValue;
            for (var oy = -2; oy <= 2; oy++)
                for (var ox = -2; ox <= 2; ox++)
                {
                    var c = new GridPos(cell.X + ox, cell.Y + oy);
                    var d = SquareDistance(wx, wz, c, cellSize);
                    if (cells.Contains(c)) { if (feature.Bridge == c) bridge = Math.Min(bridge, d); continue; }
                    if (layout.IsWalkable(c)) shore = Math.Min(shore, d); else wall = Math.Min(wall, d);
                }
            var t = Mathf.Clamp(shore / BankWidth, 0f, 1f);
            t = t * t * (3f - 2f * t);
            var depth = Mathf.Lerp(ShoalDepth, Depth, Mathf.Clamp(bridge / 1.0f, 0f, 1f));
            var y = floorY - depth * t;
            // a wall cube's underside is 0.2 below the floor top: the bed may not dive under it
            return Math.Max(y, floorY - 0.2f - wall * 0.6f);
        }
        var bed = new SurfaceTool();
        bed.Begin(Mesh.PrimitiveType.Triangles);
        var step = cellSize / Sub;
        foreach (var cell in feature.Cells)
        {
            var x0 = cell.X * cellSize;
            var z0 = cell.Y * cellSize;
            for (var i = 0; i < Sub; i++)
                for (var j = 0; j < Sub; j++)
                {
                    var ax = x0 + i * step; var az = z0 + j * step;
                    var bx = ax + step; var bz = az + step;
                    var a = new Vector3(ax, BedY(ax, az), az) - origin;
                    var b = new Vector3(bx, BedY(bx, az), az) - origin;
                    var c = new Vector3(bx, BedY(bx, bz), bz) - origin;
                    var d = new Vector3(ax, BedY(ax, bz), bz) - origin;
                    // Godot front faces wind clockwise seen from the front (here: from above)
                    bed.AddVertex(a); bed.AddVertex(b); bed.AddVertex(c);
                    bed.AddVertex(a); bed.AddVertex(c); bed.AddVertex(d);
                }
        }
        bed.GenerateNormals();
        var bedMesh = bed.Commit();
        bedMesh.SurfaceSetMaterial(0, bedMaterial);
        var bedNode = new MeshInstance3D { Mesh = bedMesh, Name = "Bed" };
        node.AddChild(bedNode);
        var body = new StaticBody3D { CollisionLayer = 1, CollisionMask = 0, Name = "BedBody" };
        body.AddChild(new CollisionShape3D { Shape = bedMesh.CreateTrimeshShape() });
        node.AddChild(body);

        // ---- surface: flat quads a little below the floor, UVs in world metres so the shader tiles evenly
        var st = new SurfaceTool();
        st.Begin(Mesh.PrimitiveType.Triangles);
        var h = cellSize / 2f;
        var level = floorY - SurfaceDrop;
        foreach (var cell in feature.Cells)
        {
            var c = surface(cell) with { Y = level };
            Vector3 P(float dx, float dz) => new Vector3(c.X + dx, level, c.Z + dz) - origin;
            void V(float dx, float dz) { var p = P(dx, dz); st.SetNormal(Vector3.Up); st.SetUV(new Vector2((p.X + origin.X) / cellSize, (p.Z + origin.Z) / cellSize)); st.AddVertex(p); }
            V(-h, -h); V(h, -h); V(h, h);
            V(-h, -h); V(h, h); V(-h, h);
        }
        var mat = new ShaderMaterial { Shader = WaterShader() };
        mat.SetShaderParameter("normal_a", NormalA());
        mat.SetShaderParameter("normal_b", NormalB());
        mat.SetShaderParameter("flow", stream ? new Vector2(feature.Flow.X, feature.Flow.Y) * 0.22f : new Vector2(0.015f, 0.02f));
        mat.SetShaderParameter("shallow_color", stream ? new Color(0.38f, 0.58f, 0.58f, 0.4f) : new Color(0.26f, 0.48f, 0.52f, 0.48f));
        node.AddChild(new MeshInstance3D { Mesh = st.Commit(), MaterialOverride = mat, Name = "Surface", CastShadow = GeometryInstance3D.ShadowCastingSetting.Off });

        // ---- stream furniture: culvert grates where it meets the walls, a plank bridge at the crossing
        var facing = new Vector3(feature.Flow.X, 0, feature.Flow.Y);
        if (stream)
        {
            var iron = new StandardMaterial3D { AlbedoColor = new Color(0.12f, 0.11f, 0.1f), Metallic = 0.7f, Roughness = 0.55f };
            var dark = new StandardMaterial3D { AlbedoColor = new Color(0.02f, 0.02f, 0.02f), ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded };
            foreach (var (end, dir) in new[] { (feature.Cells[0], -facing), (feature.Cells[^1], facing) })
            {
                if (layout.IsWalkable(end + new GridPos((int)dir.X, (int)dir.Z))) continue;
                var mouth = surface(end) + dir * (cellSize / 2f - 0.04f);
                var side = new Vector3(-dir.Z, 0, dir.X);
                var yaw = DungeonBuilder.Yaw(side);
                Box(node, origin, new Vector3(2.6f, 1.15f, 0.05f), mouth with { Y = floorY - 0.2f + 0.575f }, dark, yaw);
                for (var i = -2; i <= 2; i++)
                {
                    var bar = new MeshInstance3D { Mesh = new CylinderMesh { TopRadius = 0.04f, BottomRadius = 0.04f, Height = 1.1f }, MaterialOverride = iron };
                    node.AddChild(bar);
                    bar.Position = mouth + side * (i * 0.5f) - dir * 0.12f + Vector3.Up * 0.35f - origin;
                }
            }
            if (feature.Bridge is { } bridgeCell)
            {
                var centre = surface(bridgeCell);
                var across = new Vector3(-facing.Z, 0, facing.X);
                var yaw = DungeonBuilder.Yaw(across);
                Box(node, origin, new Vector3(cellSize + 0.4f, 0.12f, 1.7f), centre + Vector3.Up * 0.1f, plankMaterial, yaw, collide: true);
                foreach (var sd in new[] { 1f, -1f })
                {
                    Box(node, origin, new Vector3(cellSize + 0.4f, 0.06f, 0.06f), centre + facing * (sd * 0.82f) + Vector3.Up * 1.0f, plankMaterial, yaw);
                    for (var k = -1; k <= 1; k++)
                        Box(node, origin, new Vector3(0.09f, 0.95f, 0.09f), centre + facing * (sd * 0.82f) + across * (k * (cellSize / 2f - 0.15f)) + Vector3.Up * 0.6f, plankMaterial, yaw);
                }
            }
        }
        else
        {
            // drips from the ceiling into the pond
            var xs = feature.Cells.Select(c => c.X).ToList();
            var ys = feature.Cells.Select(c => c.Y).ToList();
            var centre = new Vector3((xs.Min() + xs.Max() + 1) / 2f * cellSize, floorY + 3.6f, (ys.Min() + ys.Max() + 1) / 2f * cellSize);
            var extents = new Vector3((xs.Max() - xs.Min() + 1) * cellSize / 2f - 0.4f, 0.05f, (ys.Max() - ys.Min() + 1) * cellSize / 2f - 0.4f);
            var drips = new GpuParticles3D
            {
                Name = "Drips", Amount = 8, Lifetime = 1.0, Randomness = 1f,
                ProcessMaterial = new ParticleProcessMaterial { Direction = Vector3.Down, Spread = 0f, InitialVelocityMin = 0.1f, InitialVelocityMax = 0.3f, Gravity = new Vector3(0, -9f, 0), EmissionShape = ParticleProcessMaterial.EmissionShapeEnum.Box, EmissionBoxExtents = extents, ScaleMin = 0.6f, ScaleMax = 1f },
                DrawPass1 = new SphereMesh { Radius = 0.03f, Height = 0.09f, RadialSegments = 6, Rings = 4 },
                MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color(0.6f, 0.8f, 0.9f, 0.8f), Transparency = BaseMaterial3D.TransparencyEnum.Alpha, ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded },
                Position = centre - origin,
            };
            node.AddChild(drips);
        }

        // ---- sound
        var player = new AudioStreamPlayer3D
        {
            Stream = WaterAudio.Ambience(stream), Bus = "SFX", Autoplay = true, VolumeDb = stream ? -6f : -11f,
            MaxDistance = 16f, UnitSize = 4f, AttenuationModel = AudioStreamPlayer3D.AttenuationModelEnum.InverseDistance, Name = "Ambience",
        };
        var mid = feature.Cells[feature.Cells.Count / 2];
        player.Position = surface(mid) - origin + Vector3.Up * 0.3f;
        node.AddChild(player);
        return node;
    }

    private static float SquareDistance(float x, float z, GridPos cell, float size)
    {
        var dx = Math.Max(Math.Max(cell.X * size - x, 0f), x - (cell.X + 1) * size);
        var dz = Math.Max(Math.Max(cell.Y * size - z, 0f), z - (cell.Y + 1) * size);
        return MathF.Sqrt(dx * dx + dz * dz);
    }

    private static void Box(Node3D parent, Vector3 origin, Vector3 size, Vector3 worldPos, Material material, float yaw, bool collide = false)
    {
        var mi = new MeshInstance3D { Mesh = new BoxMesh { Size = size }, MaterialOverride = material, Position = worldPos - origin };
        mi.RotateY(yaw);
        parent.AddChild(mi);
        if (!collide) return;
        var body = new StaticBody3D { CollisionLayer = 1, CollisionMask = 0 };
        body.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = size } });
        mi.AddChild(body);
    }

    private static Shader WaterShader() => _shader ??= GD.Load<Shader>("res://assets/shaders/water.gdshader");
    private static NoiseTexture2D NormalA() => _normalA ??= new NoiseTexture2D { Noise = new FastNoiseLite { Frequency = 0.05f, NoiseType = FastNoiseLite.NoiseTypeEnum.SimplexSmooth, Seed = 3 }, Seamless = true, AsNormalMap = true, BumpStrength = 6f, Width = 256, Height = 256 };
    private static NoiseTexture2D NormalB() => _normalB ??= new NoiseTexture2D { Noise = new FastNoiseLite { Frequency = 0.09f, NoiseType = FastNoiseLite.NoiseTypeEnum.SimplexSmooth, Seed = 11 }, Seamless = true, AsNormalMap = true, BumpStrength = 5f, Width = 256, Height = 256 };
}

/// <summary>A ring that spreads and fades on the water where something moved.</summary>
public partial class Ripple : MeshInstance3D
{
    private static Texture2D? _ring;
    private float _age;
    private const float Life = 0.9f;
    private StandardMaterial3D _mat = null!;

    public static void Spawn(Node parent, Vector3 at, float size = 1f)
    {
        var r = new Ripple
        {
            Mesh = new QuadMesh { Size = new Vector2(size, size), Orientation = PlaneMesh.OrientationEnum.Y },
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        };
        r._mat = new StandardMaterial3D { AlbedoTexture = Ring(), AlbedoColor = new Color(0.85f, 0.95f, 1f, 0.55f), Transparency = BaseMaterial3D.TransparencyEnum.Alpha, ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, NoDepthTest = false };
        r.MaterialOverride = r._mat;
        parent.AddChild(r);
        r.GlobalPosition = at;
        r.Scale = Vector3.One * 0.3f;
    }

    public override void _Process(double delta)
    {
        _age += (float)delta;
        var t = _age / Life;
        if (t >= 1f) { QueueFree(); return; }
        Scale = Vector3.One * (0.3f + t * 1.6f);
        _mat.AlbedoColor = _mat.AlbedoColor with { A = 0.55f * (1f - t) };
    }

    private static Texture2D Ring()
    {
        if (_ring != null) return _ring;
        const int n = 64;
        var img = Image.CreateEmpty(n, n, false, Image.Format.Rgba8);
        for (var y = 0; y < n; y++)
            for (var x = 0; x < n; x++)
            {
                var d = new Vector2((x + 0.5f) / n - 0.5f, (y + 0.5f) / n - 0.5f).Length() * 2f;
                var a = Mathf.Clamp(1f - Mathf.Abs(d - 0.8f) / 0.14f, 0f, 1f);
                img.SetPixel(x, y, new Color(1f, 1f, 1f, a));
            }
        _ring = ImageTexture.CreateFromImage(img);
        return _ring;
    }
}

/// <summary>Water sounds synthesised in code: a looping trickle or lap, and a splash. No audio files needed.</summary>
public static class WaterAudio
{
    private const int Rate = 22050;
    private static AudioStreamWav? _stream, _pond, _splash;
    private static readonly Random Rng = new(7);

    public static AudioStreamWav Ambience(bool stream) => stream ? (_stream ??= Loop(4f, bright: 0.55f)) : (_pond ??= Loop(5f, bright: 0.18f));

    /// <summary>Filtered noise, looped with a crossfaded seam. Brighter mixes keep more hiss, like moving water.</summary>
    private static AudioStreamWav Loop(float seconds, float bright)
    {
        var n = (int)(Rate * seconds);
        var samples = new float[n];
        float lp1 = 0f, lp2 = 0f, lp3 = 0f;
        var wobble = 0f;
        for (var i = 0; i < n; i++)
        {
            var white = (float)(Rng.NextDouble() * 2 - 1);
            lp1 += (white - lp1) * 0.35f;
            lp2 += (lp1 - lp2) * 0.08f;
            lp3 += (lp2 - lp3) * 0.02f;
            wobble += (((float)Rng.NextDouble() * 2 - 1) - wobble) * 0.0008f;
            var gurgle = 0.75f + 0.25f * MathF.Sin(i * 0.0011f + wobble * 40f);
            samples[i] = (lp3 * 3.5f * (1f - bright) + lp2 * 1.6f * bright + lp1 * 0.35f * bright) * gurgle;
        }
        var fade = Rate / 5;
        for (var i = 0; i < fade; i++)
        {
            var t = (float)i / fade;
            samples[n - fade + i] = samples[n - fade + i] * (1f - t) + samples[i] * t;
        }
        return Wav(samples, loop: true, gain: 0.32f);
    }

    public static AudioStreamWav Splash()
    {
        if (_splash != null) return _splash;
        var n = (int)(Rate * 0.4f);
        var samples = new float[n];
        float lp = 0f;
        for (var i = 0; i < n; i++)
        {
            var t = (float)i / Rate;
            var white = (float)(Rng.NextDouble() * 2 - 1);
            lp += (white - lp) * 0.45f;
            var env = MathF.Exp(-t * 11f) * Math.Min(1f, t * 200f);
            var plop = MathF.Sin(t * 2f * MathF.PI * (160f - t * 220f)) * MathF.Exp(-t * 22f) * 0.7f;
            samples[i] = lp * env * 0.9f + plop;
        }
        return _splash = Wav(samples, loop: false, gain: 0.6f);
    }

    private static AudioStreamWav Wav(float[] samples, bool loop, float gain)
    {
        var data = new byte[samples.Length * 2];
        for (var i = 0; i < samples.Length; i++)
        {
            var v = (short)Mathf.Clamp(samples[i] * gain * 32767f, -32767f, 32767f);
            data[i * 2] = (byte)(v & 0xFF);
            data[i * 2 + 1] = (byte)((v >> 8) & 0xFF);
        }
        return new AudioStreamWav { Format = AudioStreamWav.FormatEnum.Format16Bits, MixRate = Rate, Stereo = false, Data = data, LoopMode = loop ? AudioStreamWav.LoopModeEnum.Forward : AudioStreamWav.LoopModeEnum.Disabled, LoopBegin = 0, LoopEnd = samples.Length };
    }

    /// <summary>A one-shot splash at a spot; the player frees itself.</summary>
    public static void SplashAt(Node parent, Vector3 at, float volumeDb = -4f)
    {
        var p = new AudioStreamPlayer3D { Stream = Splash(), Bus = "SFX", VolumeDb = volumeDb, MaxDistance = 18f, UnitSize = 4f, PitchScale = 0.85f + (float)Rng.NextDouble() * 0.3f };
        parent.AddChild(p);
        p.GlobalPosition = at;
        p.Finished += p.QueueFree;
        p.Play();
    }
}
