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
    private static readonly Dictionary<WeaponKind, (string file, Vector3 pos, Vector3 rotDeg, float scale)> Models = new()
    {
        [WeaponKind.Sword] = ("Warrior_Sword", new Vector3(0.40f, -0.34f, -0.60f), new Vector3(-65, 20, 12), 0.30f),
        [WeaponKind.Axe] = ("Warrior_Sword", new Vector3(0.40f, -0.34f, -0.60f), new Vector3(-65, 20, 12), 0.30f),
        [WeaponKind.Mace] = ("Warrior_Sword", new Vector3(0.40f, -0.34f, -0.60f), new Vector3(-65, 20, 12), 0.30f),
        [WeaponKind.Dagger] = ("Rogue_Dagger", new Vector3(0.30f, -0.28f, -0.50f), new Vector3(-75, 20, 15), 0.35f),
        [WeaponKind.Polearm] = ("Cleric_Staff", new Vector3(0.28f, -0.40f, -0.60f), new Vector3(-80, 10, 5), 0.40f),
        [WeaponKind.Staff] = ("Wizard_Staff", new Vector3(0.30f, -0.42f, -0.60f), new Vector3(-80, 10, 5), 0.40f),
        [WeaponKind.Bow] = ("Ranger_Bow", new Vector3(0.30f, -0.30f, -0.60f), new Vector3(10, -75, 0), 0.34f),
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
        if (kind != _shownKind || (weapon?.Id ?? Guid.Empty) != _shownItem) Show(kind, weapon?.Id ?? Guid.Empty);
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

    private void Show(WeaponKind kind, Guid itemId)
    {
        _shownKind = kind;
        _shownItem = itemId;
        _model?.QueueFree();
        _model = null;
        if (!Models.TryGetValue(kind, out var spec)) return;
        var path = $"res://assets/models/quaternius_rpg/weapons/{spec.file}.obj";
        if (!ResourceLoader.Exists(path)) return;
        var mesh = new MeshInstance3D { Mesh = GD.Load<Mesh>(path), Name = "WeaponMesh", CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
        AddChild(mesh);
        _model = mesh;
        _restPos = spec.pos;
        _restRot = spec.rotDeg;
        mesh.Position = _restPos;
        mesh.RotationDegrees = _restRot;
        mesh.Scale = Vector3.One * spec.scale;
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

    /// <summary>Raise the weapon while guarding.</summary>
    public void SetGuard(bool guarding)
    {
        if (_model == null || (_tween != null && _tween.IsRunning())) return;
        _model.RotationDegrees = guarding ? _restRot + new Vector3(20, -40, 30) : _restRot;
    }
}
