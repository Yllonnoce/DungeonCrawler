using DungeonCrawler.Core;
using DungeonCrawler.Dungeon.Generation;
using Godot;

namespace DungeonCrawler.Dungeon.Props;

/// <summary>
/// The dungeon settling: now and then the ground shudders, the camera shakes, dust sifts from the ceiling and a few
/// stones drop and thud onto the floor near the party. Never a real cave-in, just the place reminding you it is old.
/// One lives under each floor root; hordes breaking loose borrow its shake.
/// </summary>
public partial class Tremor : Node
{
    public static Tremor? Instance { get; private set; }
    public int Tremors { get; private set; }
    public int RocksDropped { get; private set; }
    public bool Active => _active > 0f;

    private float _next;
    private float _active;
    private float _strength;
    private float _rockTimer;
    private int _rocksLeft;
    private readonly Random _rng = new();
    private Material? _stone;
    private static Texture2D? _puff;

    public override void _Ready()
    {
        Instance = this;
        _next = 25f + (float)_rng.NextDouble() * 40f;
    }

    public override void _ExitTree() { if (Instance == this) Instance = null; }

    /// <summary>Stone for the falling rocks: the theme's wall material, so they match what they fell from.</summary>
    public void SetStone(Material stone) => _stone = stone;

    public override void _Process(double delta)
    {
        var dt = (float)delta;
        var main = Main.Instance;
        if (main == null || !main.InGame) return;
        if (_active > 0f)
        {
            _active -= dt;
            _rockTimer -= dt;
            if (_rocksLeft > 0 && _rockTimer <= 0f) { DropRock(main); _rocksLeft--; _rockTimer = 0.15f + (float)_rng.NextDouble() * 0.35f; }
            return;
        }
        _next -= dt;
        if (_next <= 0f)
        {
            // heavier and more frequent in the caves
            var caves = main.Builder.CurrentTier?.Index == 1;
            Trigger(_rng.NextDouble() < (caves ? 0.4 : 0.25) ? 1f : 0.5f);
            _next = (caves ? 35f : 60f) + (float)_rng.NextDouble() * (caves ? 60f : 110f);
        }
    }

    /// <summary>Set one off: strength 0.5 is a shudder with a couple of stones, 1 a proper rumble with a handful.</summary>
    public void Trigger(float strength)
    {
        var main = Main.Instance;
        if (main == null || !main.InGame || !main.Party.HasBodies) return;
        _strength = Mathf.Clamp(strength, 0.2f, 1.5f);
        _active = 2.2f + _strength * 1.5f;
        _rocksLeft = 2 + (int)(_strength * 4) + _rng.Next(2);
        _rockTimer = 0.4f;
        Tremors++;
        var lead = main.Party.Lead;
        lead.Shake(0.05f + 0.09f * _strength, 1.6f + _strength * 1.4f);
        Audio.Synth.PlayAt(this, Audio.Synth.Rumble(), lead.GlobalPosition + Vector3.Up * 3f, -6f + 4f * _strength, 40f);
        EventBus.Instance.Log(_strength >= 1f ? "The ground heaves. Stone cracks somewhere above." : "The ground shudders. Dust sifts from the ceiling.");
        // dust sifting down around the party
        for (var i = 0; i < 3 + (int)(_strength * 3); i++)
            if (RandomCellNear(main, 9f, out var cell)) Dust(main.Builder.Surface(cell) + Vector3.Up * 3.7f, 1.2f + _strength * 1.5f);
    }

    /// <summary>Shake without stones: a horde tearing out of the wall, a boss landing.</summary>
    public static void ShakeOnly(float amplitude, float seconds)
    {
        var party = Main.Instance?.Party;
        if (party != null && party.HasBodies) party.Anchor.Shake(amplitude, seconds);
    }

    private bool RandomCellNear(Main main, float radius, out GridPos cell)
    {
        cell = default;
        var layout = main.Builder.Layout;
        if (layout == null) return false;
        var origin = main.Party.Lead.GlobalPosition;
        var mine = DungeonBuilder.WorldToCell(origin);
        for (var attempt = 0; attempt < 12; attempt++)
        {
            var a = (float)_rng.NextDouble() * Mathf.Tau;
            var r = 2f + (float)_rng.NextDouble() * radius;
            var p = origin + new Vector3(Mathf.Cos(a) * r, 0, Mathf.Sin(a) * r);
            var c = DungeonBuilder.WorldToCell(p);
            if (c == mine || !layout.IsWalkable(c) || layout.IsUnder(c) || layout[c] == Cell.Ramp || layout.IsWater(c)) continue;
            if (Math.Abs(main.Builder.Surface(c).Y - origin.Y) > 2.5f) continue; // same level, so the ceiling is the one above the party
            cell = c;
            return true;
        }
        return false;
    }

    private void DropRock(Main main)
    {
        if (!RandomCellNear(main, 10f, out var cell)) return;
        var builder = main.Builder;
        var at = builder.Surface(cell) + new Vector3(((float)_rng.NextDouble() - 0.5f) * 2.4f, builder.CeilingAboveAt(cell) - 0.5f, ((float)_rng.NextDouble() - 0.5f) * 2.4f); // out of the vault above
        var size = 0.18f + (float)_rng.NextDouble() * 0.22f * (0.6f + _strength * 0.6f);
        var rock = new FallingRock { Size = size, Stone = _stone, Puff = Puff() };
        builder.FloorRoot!.AddChild(rock);
        rock.GlobalPosition = at;
        rock.Rotation = new Vector3((float)_rng.NextDouble() * 3f, (float)_rng.NextDouble() * 3f, (float)_rng.NextDouble() * 3f);
        RocksDropped++;
    }

    private void Dust(Vector3 at, float seconds)
    {
        var dust = new GpuParticles3D
        {
            Amount = 24, Lifetime = 1.4, OneShot = true, Explosiveness = 0.15f,
            ProcessMaterial = new ParticleProcessMaterial { Direction = Vector3.Down, Spread = 8f, InitialVelocityMin = 0.4f, InitialVelocityMax = 1.2f, Gravity = new Vector3(0, -2.5f, 0), ScaleMin = 0.3f, ScaleMax = 0.8f, EmissionShape = ParticleProcessMaterial.EmissionShapeEnum.Box, EmissionBoxExtents = new Vector3(1.2f, 0.05f, 1.2f) },
            DrawPass1 = new QuadMesh { Size = new Vector2(0.7f, 0.7f) },
            MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color(0.5f, 0.45f, 0.38f, 0.14f), AlbedoTexture = Puff(), Transparency = BaseMaterial3D.TransparencyEnum.Alpha, BillboardMode = BaseMaterial3D.BillboardModeEnum.Particles, ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded },
        };
        AddChild(dust);
        dust.GlobalPosition = at;
        dust.Emitting = true;
        dust.Finished += dust.QueueFree;
    }

    private static Texture2D Puff() => _puff ??= Fire.Puff();
}

/// <summary>A stone that falls, thuds, puffs dust, lies for a while and is gone.</summary>
public partial class FallingRock : RigidBody3D
{
    public float Size { get; set; } = 0.3f;
    public Material? Stone { get; set; }
    public Texture2D? Puff { get; set; }
    private bool _landed;
    private float _age;
    private float _rest;

    public override void _Ready()
    {
        CollisionLayer = 0;
        CollisionMask = 1; // the world only: it never pins a body
        Mass = 4f;
        AddChild(new MeshInstance3D { Mesh = new BoxMesh { Size = new Vector3(Size, Size * 0.75f, Size * 0.9f) }, MaterialOverride = Stone, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off });
        AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = new Vector3(Size, Size * 0.75f, Size * 0.9f) } });
        AngularVelocity = new Vector3(2f, 1f, 3f);
    }

    public override void _PhysicsProcess(double delta)
    {
        _age += (float)delta;
        if (!_landed && _age > 0.3f && LinearVelocity.Length() < 0.4f)
        {
            _landed = true;
            Audio.Synth.PlayAt(GetParent(), Audio.Synth.Thud(), GlobalPosition, -8f + Size * 10f, 18f);
            var dust = new GpuParticles3D
            {
                Amount = 10, Lifetime = 0.8, OneShot = true, Explosiveness = 0.9f,
                ProcessMaterial = new ParticleProcessMaterial { Direction = Vector3.Up, Spread = 60f, InitialVelocityMin = 0.4f, InitialVelocityMax = 1.2f, Gravity = new Vector3(0, -1f, 0), ScaleMin = 0.4f, ScaleMax = 0.9f },
                DrawPass1 = new QuadMesh { Size = new Vector2(0.5f, 0.5f) },
                MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color(0.5f, 0.45f, 0.38f, 0.2f), AlbedoTexture = Puff, Transparency = BaseMaterial3D.TransparencyEnum.Alpha, BillboardMode = BaseMaterial3D.BillboardModeEnum.Particles, ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded },
            };
            GetParent().AddChild(dust);
            dust.GlobalPosition = GlobalPosition;
            dust.Emitting = true;
            dust.Finished += dust.QueueFree;
        }
        if (_landed) _rest += (float)delta;
        if (_rest > 12f || _age > 30f || GlobalPosition.Y < -20f) QueueFree();
    }
}
