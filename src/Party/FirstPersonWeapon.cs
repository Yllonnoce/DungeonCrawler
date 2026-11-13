using DungeonCrawler.Combat;
using DungeonCrawler.Items;
using Godot;

namespace DungeonCrawler.Party;

/// <summary>
/// The weapon you see in your hands: a model hung under the lead's camera, bobbing as you walk, swinging when you attack.
/// Swaps automatically when the equipped weapon changes.
/// </summary>
public partial class FirstPersonWeapon : Node3D
{
    /// <summary>Hand-tuned poses for the Quaternius models (see <see cref="HeldWeapon"/>), keyed by file.</summary>
    private static readonly Dictionary<string, (Vector3 pos, Vector3 rotDeg, float scale)> QuaterniusPoses = new()
    {
        ["Warrior_Sword"] = (new Vector3(0.40f, -0.34f, -0.60f), new Vector3(-65, 20, 12), 0.30f),
        ["Rogue_Dagger"] = (new Vector3(0.30f, -0.28f, -0.50f), new Vector3(-75, 20, 15), 0.35f),
        ["Wizard_Staff"] = (new Vector3(0.30f, -0.42f, -0.60f), new Vector3(-80, 10, 5), 0.40f),
        ["Ranger_Bow"] = (new Vector3(0.30f, -0.30f, -0.60f), new Vector3(10, -75, 0), 0.34f),
    };

    /// <summary>Where the hand holds a KayKit or built weapon, which way its blade points, and how long it looks.</summary>
    private static readonly Vector3 GripPos = new(0.44f, -0.42f, -0.62f);
    private static readonly Vector3 BladeDir = new Vector3(-0.26f, 0.80f, -0.54f).Normalized();
    private static float HeldLength(HeldModel m) => m.Name switch
    {
        "axe_1handed" => 0.42f, "axe_2handed" => 0.62f, "sword_2handed" => 0.7f, "mace" => 0.45f, "spear" => 1.1f, _ => 0.5f,
    };

    private Adventurer _owner = null!;
    private Node3D? _model;
    private WeaponKind? _shownKind;
    private Guid _shownItem;
    private Vector3 _restPos;
    private Vector3 _restRot;
    private float _bobPhase;
    private Tween? _tween;

    public void Bind(Adventurer owner) => _owner = owner;

    public override void _Process(double delta)
    {
        if (_owner == null) return;
        var weapon = _owner.State.Weapon;
        var kind = _owner.State.WeaponProfile.Kind;
        if (kind != _shownKind || (weapon?.Id ?? Guid.Empty) != _shownItem) Show(kind, weapon);
        Visible = _owner.IsLocal && !_owner.Downed && _model != null;
        if (_model == null || !Visible) return;

        // walking bob, only when no swing tween is running
        var speed = new Vector2(_owner.Velocity.X, _owner.Velocity.Z).Length();
        if (_tween == null || !_tween.IsRunning())
        {
            _bobPhase += (float)delta * (speed > 0.5f ? 7f : 2f);
            var amount = speed > 0.5f ? 0.02f : 0.006f;
            _model.Position = _restPos + new Vector3(Mathf.Sin(_bobPhase) * amount, Mathf.Abs(Mathf.Cos(_bobPhase)) * amount, 0);
        }
    }

    private void Show(WeaponKind kind, ItemInstance? item)
    {
        _shownKind = kind;
        _shownItem = item?.Id ?? Guid.Empty;
        _model?.QueueFree();
        _model = null;
        if (HeldWeapon.For(kind, item?.TwoHanded ?? false) is not { } held) return;
        if (held.Source == HeldSource.Quaternius) ShowQuaternius(held.Name);
        else ShowHeld(held);
    }

    /// <summary>One of the hand models the view was first tuned for, at its own pose.</summary>
    private void ShowQuaternius(string file)
    {
        var path = $"res://assets/models/quaternius_rpg/weapons/{file}.obj";
        if (!QuaterniusPoses.TryGetValue(file, out var pose) || !ResourceLoader.Exists(path)) return;
        var mesh = new MeshInstance3D { Mesh = GD.Load<Mesh>(path), Name = "WeaponMesh", CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
        AddChild(mesh);
        _model = mesh;
        _restPos = pose.pos;
        _restRot = pose.rotDeg;
        mesh.Position = _restPos;
        mesh.RotationDegrees = _restRot;
        mesh.Scale = Vector3.One * pose.scale;
    }

    /// <summary>
    /// A KayKit or built weapon (the model the item shows on the floor): hand at the origin and blade along +Y, so a
    /// pivot at the grip turned to point the blade up and forward holds it like the sword; scaled to its held length.
    /// </summary>
    private void ShowHeld(HeldModel held)
    {
        if (ItemVisuals.Held(held) is not { } weapon) return;
        var pivot = new Node3D { Name = "WeaponPivot" };
        AddChild(pivot);
        pivot.AddChild(weapon);
        var box = ItemVisuals.Bounds(weapon);
        var longest = Math.Max(0.05f, Math.Max(box.Size.X, Math.Max(box.Size.Y, box.Size.Z)));
        weapon.Scale = Vector3.One * (HeldLength(held) / longest);
        foreach (var mi in Core.AnimatedModel.Meshes(weapon)) mi.CastShadow = GeometryInstance3D.ShadowCastingSetting.Off;
        var y = BladeDir;
        var x = new Vector3(0, 0, -1).Cross(y).Normalized();
        var z = x.Cross(y).Normalized();
        _model = pivot;
        _restPos = GripPos;
        _restRot = new Basis(x, y, z).GetEuler() * (180f / Mathf.Pi);
        pivot.Position = _restPos;
        pivot.RotationDegrees = _restRot;
    }

    /// <summary>Swing (melee), thrust (heavy), draw and release (bow), or raise (cast).</summary>
    public void PlayAttack(AbilityKind kind, bool heavy, bool ranged)
    {
        if (_model == null) return;
        _tween?.Kill();
        _tween = CreateTween();
        var m = _model;
        if (ranged)
        {
            _tween.TweenProperty(m, "position", _restPos + new Vector3(0.06f, 0.02f, 0.18f), 0.12).SetEase(Tween.EaseType.Out);
            _tween.TweenProperty(m, "position", _restPos, 0.25).SetEase(Tween.EaseType.In);
        }
        else if (kind is AbilityKind.Projectile or AbilityKind.Nova or AbilityKind.PartyBuff or AbilityKind.SelfBuff)
        {
            _tween.TweenProperty(m, "rotation_degrees", _restRot + new Vector3(35, 0, 0), 0.15).SetEase(Tween.EaseType.Out);
            _tween.TweenProperty(m, "rotation_degrees", _restRot, 0.3).SetEase(Tween.EaseType.In);
        }
        else if (heavy)
        {
            _tween.TweenProperty(m, "rotation_degrees", _restRot + new Vector3(30, -25, 0), 0.10);
            _tween.TweenProperty(m, "rotation_degrees", _restRot + new Vector3(-45, 35, -20), 0.12).SetEase(Tween.EaseType.Out);
            _tween.TweenProperty(m, "rotation_degrees", _restRot, 0.35).SetEase(Tween.EaseType.In);
        }
        else
        {
            _tween.TweenProperty(m, "rotation_degrees", _restRot + new Vector3(-35, 25, -15), 0.09).SetEase(Tween.EaseType.Out);
            _tween.TweenProperty(m, "position", _restPos + new Vector3(-0.08f, 0.02f, -0.12f), 0.09);
            _tween.Parallel().TweenProperty(m, "rotation_degrees", _restRot, 0.28).SetEase(Tween.EaseType.In);
            _tween.TweenProperty(m, "position", _restPos, 0.28).SetEase(Tween.EaseType.In);
        }
    }

    /// <summary>A parry: a quick outward flick, mirrored from the light swing, 0.25 s in all.</summary>
    public void PlayParry()
    {
        if (_model == null) return;
        _tween?.Kill();
        _tween = CreateTween();
        var m = _model;
        _tween.TweenProperty(m, "rotation_degrees", _restRot + new Vector3(-20, -35, 25), 0.08).SetEase(Tween.EaseType.Out);
        _tween.TweenProperty(m, "position", _restPos + new Vector3(0.10f, 0.03f, -0.08f), 0.08);
        _tween.Parallel().TweenProperty(m, "rotation_degrees", _restRot, 0.17).SetEase(Tween.EaseType.In);
        _tween.TweenProperty(m, "position", _restPos, 0.17).SetEase(Tween.EaseType.In);
    }

    /// <summary>Raise the weapon while guarding.</summary>
    public void SetGuard(bool guarding)
    {
        if (_model == null || (_tween != null && _tween.IsRunning())) return;
        _model.RotationDegrees = guarding ? _restRot + new Vector3(20, -40, 30) : _restRot;
    }
}
