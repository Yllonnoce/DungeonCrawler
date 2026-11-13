using DungeonCrawler.Combat;
using DungeonCrawler.Items;
using Godot;

namespace DungeonCrawler.Party;

/// <summary>
/// The weapon you see in your hands: a model held by a "hand" under the lead's camera, bobbing as you walk and playing
/// the moves of <see cref="WeaponMoves"/> when you attack. Each weapon style swings its own way; skills have signature
/// moves; legendaries glow and leave a streak; heavy weapons jolt the view as they land. Swaps automatically when the
/// equipped weapon changes.
/// A move says where the blade points (raised, swung left or right, twisted) and where the hand is; each frame the view
/// samples the move and turns the hand so the blade points there, whatever model it holds.
/// </summary>
public partial class FirstPersonWeapon : Node3D
{
    /// <summary>Hand-tuned poses for the Quaternius models (see <see cref="HeldWeapon"/>), keyed by file.</summary>
    private static readonly Dictionary<string, (Vector3 pos, Vector3 rotDeg, float scale)> QuaterniusPoses = new()
    {
        ["Warrior_Sword"] = (new Vector3(0.40f, -0.34f, -0.60f), new Vector3(-65, 20, 12), 0.30f),
        ["Wizard_Staff"] = (new Vector3(0.30f, -0.42f, -0.60f), new Vector3(-80, 10, 5), 0.40f),
        ["Ranger_Bow"] = (new Vector3(0.30f, -0.30f, -0.60f), new Vector3(10, -75, 0), 0.34f),
    };

    /// <summary>Where the hand holds a KayKit or built weapon, which way its blade points, and how long it looks.</summary>
    private static readonly Vector3 GripPos = new(0.44f, -0.42f, -0.62f);
    private static readonly Vector3 BladeDir = new Vector3(-0.26f, 0.80f, -0.54f).Normalized();
    private static float HeldLength(HeldModel m) => m.Name switch
    {
        "dagger" => 0.34f, "axe_1handed" => 0.42f, "axe_2handed" => 0.62f, "sword_1handed" => 0.5f, "sword_2handed" => 0.7f, "mace" => 0.45f,
        "war_hammer" => 0.66f, "spear" => 1.1f, "glaive" => 1.15f, "wand" => 0.3f, "crossbow_2handed" => 0.5f, _ => 0.5f,
    };

    /// <summary>A crossbow is carried level and pointing ahead, not blade-up like the rest.</summary>
    private static readonly Vector3 CrossbowPos = new(0.26f, -0.3f, -0.5f);

    /// <summary>A direction blades never point (down and back, at the wielder's belt): the reference that fixes a blade's edge as it turns.</summary>
    private static readonly Vector3 EdgeReference = new Vector3(0, -0.5f, 0.85f).Normalized();

    /// <summary>The camera shake at an impact of jolt 1, and how long it lasts.</summary>
    private const float JoltAmplitude = 0.03f, JoltSeconds = 0.14f;
    /// <summary>How long a point of the streak behind the blade lasts.</summary>
    private const float TrailLife = 0.16f;

    private Adventurer _owner = null!;
    private Node3D _hand = null!;
    private Node3D? _model;
    private WeaponStyle? _shownStyle;
    private Guid _shownItem;
    private Vector3 _restPos;
    private float _bobPhase;

    // the blade at rest, in the hand's space: its elevation and bearing (degrees) and the frame that undoes it
    private float _restUp, _restLeft;
    private Basis _restFrameInverse = Basis.Identity;

    // the move playing
    private WeaponMove? _move;
    private float _moveTime;
    private readonly Queue<float> _impacts = new();
    private bool _holding, _guarding;
    private int _combo;
    private ulong _comboEndsMs;

    // the streak and the glow
    private MeshInstance3D _trail = null!;
    private ImmediateMesh _trailMesh = null!;
    private readonly List<(Vector3 tip, Vector3 root, float born)> _trailPoints = new();
    private Vector3 _tipLocal, _rootLocal;
    private bool _trailOn;
    private Color _trailColor = Colors.White;
    private StandardMaterial3D? _glow;
    private Color _legendColor;
    private float _clock;

    public void Bind(Adventurer owner) => _owner = owner;

    public override void _Ready()
    {
        _hand = new Node3D { Name = "Hand" };
        AddChild(_hand);
        _trailMesh = new ImmediateMesh();
        _trail = new MeshInstance3D
        {
            Name = "SwingTrail", Mesh = _trailMesh, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            MaterialOverride = new StandardMaterial3D
            {
                ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, VertexColorUseAsAlbedo = true, Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
                BlendMode = BaseMaterial3D.BlendModeEnum.Add, CullMode = BaseMaterial3D.CullModeEnum.Disabled,
            },
        };
        AddChild(_trail);
    }

    public override void _Process(double delta)
    {
        if (_owner == null) return;
        var dt = (float)delta;
        _clock += dt;
        var weapon = _owner.State.Weapon;
        var style = WeaponStyles.Of(weapon);
        if (style != _shownStyle || (weapon?.Id ?? Guid.Empty) != _shownItem) Show(style, weapon);
        Visible = _owner.IsLocal && !_owner.Downed && _model != null;
        if (_model == null || !Visible) return;

        if (_move != null)
        {
            _moveTime += dt;
            while (_impacts.Count > 0 && _impacts.Peek() <= _moveTime)
            {
                _impacts.Dequeue();
                if (_move.Jolt > 0f) _owner.Shake(JoltAmplitude * _move.Jolt, JoltSeconds);
            }
            Apply(_move.Sample(_moveTime), Vector3.Zero);
            if (_moveTime >= _move.Seconds) _move = null;
        }
        else if (!_holding)
        {
            // walking bob, between moves
            var speed = new Vector2(_owner.Velocity.X, _owner.Velocity.Z).Length();
            _bobPhase += dt * (speed > 0.5f ? 7f : 2f);
            var amount = speed > 0.5f ? 0.02f : 0.006f;
            Apply(_guarding ? WeaponMoves.Guard : MovePose.Rest, new Vector3(Mathf.Sin(_bobPhase) * amount, Mathf.Abs(Mathf.Cos(_bobPhase)) * amount, 0));
            _trailOn = false;
        }
        if (_glow != null) _glow.AlbedoColor = _legendColor with { A = (_move != null ? 0.32f : 0.14f) + 0.06f * Mathf.Sin(_clock * 3f) };
        UpdateTrail();
    }

    /// <summary>Turn the hand so the blade points where the pose says, and place it.</summary>
    private void Apply(MovePose p, Vector3 bob)
    {
        var up = Mathf.DegToRad(_restUp + p.Up);
        var left = Mathf.DegToRad(_restLeft + p.Left);
        var dir = new Vector3(-Mathf.Cos(up) * Mathf.Sin(left), Mathf.Sin(up), -Mathf.Cos(up) * Mathf.Cos(left));
        _hand.Basis = Frame(dir) * new Basis(Vector3.Up, Mathf.DegToRad(p.Twist)) * _restFrameInverse;
        _hand.Position = _restPos + new Vector3(p.X, p.Y, p.Z) + bob;
    }

    /// <summary>A frame whose Y is the blade and whose X keeps the edge level, the same way for every direction a blade points.</summary>
    private static Basis Frame(Vector3 blade)
    {
        var x = EdgeReference.Cross(blade).Normalized();
        return new Basis(x, blade, x.Cross(blade));
    }

    // ---- the model ---------------------------------------------------------------------------

    private void Show(WeaponStyle style, ItemInstance? item)
    {
        _shownStyle = style;
        _shownItem = item?.Id ?? Guid.Empty;
        _move = null;
        _holding = false;
        _model?.QueueFree();
        _model = null;
        _glow = null;
        _hand.Basis = Basis.Identity;
        _trailPoints.Clear();
        if (HeldWeapon.For(style) is not { } held) return;
        if (held.Source == HeldSource.Quaternius) ShowQuaternius(held.Name);
        else ShowHeld(held, style);
        if (_model == null) return;
        FindBlade(style);
        if (item?.Rarity == Rarity.Legendary) Glow(item);
        Apply(MovePose.Rest, Vector3.Zero);
    }

    /// <summary>One of the hand models the view was first tuned for, at its own pose under the hand.</summary>
    private void ShowQuaternius(string file)
    {
        var path = $"res://assets/models/quaternius_rpg/weapons/{file}.obj";
        if (!QuaterniusPoses.TryGetValue(file, out var pose) || !ResourceLoader.Exists(path)) return;
        var mesh = new MeshInstance3D { Mesh = GD.Load<Mesh>(path), Name = "WeaponMesh", CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
        _hand.AddChild(mesh);
        _model = mesh;
        _restPos = pose.pos;
        mesh.RotationDegrees = pose.rotDeg;
        mesh.Scale = Vector3.One * pose.scale;
    }

    /// <summary>
    /// A KayKit or built weapon (the model the item shows on the floor): hand at the origin and blade along +Y, so a
    /// pivot at the grip turned to point the blade up and forward holds it like the sword; scaled to its held length.
    /// A crossbow is held level instead, centred on the hand and pointing ahead.
    /// </summary>
    private void ShowHeld(HeldModel held, WeaponStyle style)
    {
        if (ItemVisuals.Held(held) is not { } weapon) return;
        var pivot = new Node3D { Name = "WeaponPivot" };
        _hand.AddChild(pivot);
        pivot.AddChild(weapon);
        var box = ItemVisuals.Bounds(weapon);
        var longest = Math.Max(0.05f, Math.Max(box.Size.X, Math.Max(box.Size.Y, box.Size.Z)));
        weapon.Scale = Vector3.One * (HeldLength(held) / longest);
        foreach (var mi in Core.AnimatedModel.Meshes(weapon)) mi.CastShadow = GeometryInstance3D.ShadowCastingSetting.Off;
        _model = pivot;
        if (style == WeaponStyle.Crossbow)
        {
            _restPos = CrossbowPos;
            weapon.Position = -box.GetCenter() * weapon.Scale;
            pivot.RotationDegrees = new Vector3(0, 180, 0); // the pack lays it along +Z, bow end first: turn the bow end away
            return;
        }
        var y = BladeDir;
        var x = new Vector3(0, 0, -1).Cross(y).Normalized();
        var z = x.Cross(y).Normalized();
        _restPos = GripPos;
        pivot.Basis = new Basis(x, y, z);
    }

    /// <summary>
    /// The blade's tip and a point down toward the hand, in the model's own space, and from them the way the blade
    /// points at rest. The tip is the end of the model's longest side that stands higher in the view (for a crossbow,
    /// the end further ahead). A Quaternius model, whose origin is not at its handle, is moved so the hand holds it at
    /// the grip (a bow at its middle) without changing how it looks at rest: every move then turns it about the grip.
    /// </summary>
    private void FindBlade(WeaponStyle style)
    {
        var box = ItemVisuals.Bounds(_model!);
        var size = box.Size;
        var axis = size.X >= size.Y && size.X >= size.Z ? Vector3.Right : size.Y >= size.Z ? Vector3.Up : Vector3.Back;
        var half = axis * (axis.Dot(size) / 2f);
        var a = box.GetCenter() + half;
        var b = box.GetCenter() - half;
        var (ha, hb) = (_model!.Transform * a, _model.Transform * b);
        var aIsTip = style == WeaponStyle.Crossbow ? ha.Z < hb.Z : ha.Y >= hb.Y;
        var (tip, butt) = aIsTip ? (a, b) : (b, a);
        if (_model is MeshInstance3D)
        {
            var grip = butt + (tip - butt) * (WeaponStyles.IsRanged(style) ? 0.5f : 0.12f);
            var gripInHand = _model.Transform * grip;
            _model.Position -= gripInHand;
            _restPos += gripInHand;
        }
        _tipLocal = tip;
        _rootLocal = butt + (tip - butt) * 0.55f;
        var rest = (_model.Transform * _tipLocal).Normalized();
        if (rest.LengthSquared() < 0.5f) rest = BladeDir;
        _restUp = Mathf.RadToDeg(Mathf.Asin(Mathf.Clamp(rest.Y, -1f, 1f)));
        _restLeft = Mathf.RadToDeg(Mathf.Atan2(-rest.X, -rest.Z));
        var up = Mathf.DegToRad(_restUp);
        var left = Mathf.DegToRad(_restLeft);
        _restFrameInverse = Frame(new Vector3(-Mathf.Cos(up) * Mathf.Sin(left), Mathf.Sin(up), -Mathf.Cos(up) * Mathf.Cos(left))).Inverse();
    }

    /// <summary>A legendary weapon glows the colour of what it inflicts (gold if nothing) and always leaves a streak.</summary>
    private void Glow(ItemInstance item)
    {
        var status = item.Weapon?.OnHit.Length > 0 ? item.Weapon.OnHit[0].Kind : (StatusKind?)null;
        var c = TrailColor.ForStatus(status);
        _legendColor = new Color(c.R, c.G, c.B);
        _glow = new StandardMaterial3D
        {
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            BlendMode = BaseMaterial3D.BlendModeEnum.Add, AlbedoColor = _legendColor with { A = 0.14f },
        };
        foreach (var mi in Core.AnimatedModel.Meshes(_model!)) mi.MaterialOverlay = _glow;
    }

    // ---- moves -------------------------------------------------------------------------------

    /// <summary>
    /// Play the move for an attack or a skill. <paramref name="budget"/> is the time until the member can act again (see
    /// <see cref="AbilityRules.SwingSeconds"/>); a move that would outlast it is played faster.
    /// </summary>
    public void PlayAttack(AbilityProfile ability, bool heavy, bool perfect, float budget)
    {
        if (_model == null || _shownStyle is not { } style) return;
        WeaponMove? move;
        if (ability.Kind == AbilityKind.WeaponAttack)
        {
            if (!heavy && !WeaponStyles.IsRanged(style))
            {
                var since = ((long)Time.GetTicksMsec() - (long)_comboEndsMs) / 1000f;
                _combo = WeaponMoves.NextCombo(_combo, since);
            }
            else _combo = 0;
            move = WeaponMoves.Attack(style, heavy, _combo);
        }
        else move = WeaponMoves.Skill(ability.Id, ability.Kind, style);
        if (move == null) return;

        Color? trail = null;
        if (ability.Kind != AbilityKind.WeaponAttack && move.Trail) trail = ToColor(TrailColor.ForSkill(ability.Id));
        else if (_glow != null) trail = _legendColor;
        else if (perfect) trail = ToColor(TrailColor.Gold);
        Play(move.FitTo(budget), trail);
    }

    /// <summary>A parry: the blade flicked out across the body, the same for every weapon.</summary>
    public void PlayParry()
    {
        if (_model == null) return;
        Play(WeaponMoves.Parry, null);
    }

    /// <summary>Lay the weapon across the body while guarding (between moves).</summary>
    public void SetGuard(bool guarding) => _guarding = guarding;

    /// <summary>Hold the hand still at one pose of a move until the next move or weapon (debug pose sheets).</summary>
    public void HoldPose(MoveKey k)
    {
        _move = null;
        _holding = true;
        Apply(MovePose.Of(k), Vector3.Zero);
    }

    private void Play(WeaponMove move, Color? trail)
    {
        _holding = false;
        _move = move;
        _moveTime = 0f;
        _impacts.Clear();
        foreach (var t in move.ImpactTimes()) _impacts.Enqueue(t);
        _trailOn = trail != null;
        if (trail is { } c) _trailColor = c;
        _comboEndsMs = Time.GetTicksMsec() + (ulong)(move.Seconds * 1000f);
    }

    private static Color ToColor(TrailColor c) => new(c.R, c.G, c.B);

    // ---- the streak behind the blade ---------------------------------------------------------

    /// <summary>Record where the blade is while a streaked move plays, and draw the last moments of it as a fading ribbon.</summary>
    private void UpdateTrail()
    {
        _trailPoints.RemoveAll(p => _clock - p.born > TrailLife);
        if (_trailOn && _move != null && _model != null)
            _trailPoints.Add((ToLocal(_model.ToGlobal(_tipLocal)), ToLocal(_model.ToGlobal(_rootLocal)), _clock));
        _trailMesh.ClearSurfaces();
        if (_trailPoints.Count < 2) return;
        _trailMesh.SurfaceBegin(Mesh.PrimitiveType.TriangleStrip);
        foreach (var (tip, root, born) in _trailPoints)
        {
            var fade = 1f - (_clock - born) / TrailLife;
            _trailMesh.SurfaceSetColor(_trailColor with { A = 0.7f * fade });
            _trailMesh.SurfaceAddVertex(tip);
            _trailMesh.SurfaceSetColor(_trailColor with { A = 0f });
            _trailMesh.SurfaceAddVertex(root);
        }
        _trailMesh.SurfaceEnd();
    }
}
