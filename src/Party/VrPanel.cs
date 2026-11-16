using DungeonCrawler.Core;
using Godot;

namespace DungeonCrawler.Party;

/// <summary>
/// The game's whole flat interface (HUD, menus, dialogue, shop, cards) hung in the air in front of a headset player:
/// the picture of <see cref="Vr.Ui"/> on a sheet that drifts round to stay ahead of the head while you play and holds
/// still while a menu is open. While a menu holds the game the right controller is a pointer: aim it at the sheet, the
/// trigger is the left mouse button, the right stick scrolls, B closes. At any time the left hand's menu button is
/// Esc (pause, or close what is open), Y opens the bag and X the map.
/// </summary>
public partial class VrPanel : Node3D
{
    /// <summary>The sheet's size and where it hangs: this far ahead of the head and a little below the eyes, in metres.</summary>
    private const float Width = 1.6f, Height = 0.9f, Distance = 1.4f, Drop = 0.05f;
    /// <summary>How fast the sheet comes round to the front while you play (per second).</summary>
    private const float FollowRate = 3f;
    /// <summary>A held stick scrolls one notch this often.</summary>
    private const float ScrollEvery = 0.12f;

    private Adventurer _body = null!;
    private XRCamera3D _head = null!;
    private XRController3D _left = null!, _right = null!;
    private MeshInstance3D _dot = null!;
    private float _yaw, _scrollWait;
    private Vector3 _centre;
    private bool _placed, _wasMenu, _pressed;
    private bool _triggerHeld, _escHeld, _closeHeld, _bagHeld, _mapHeld;
    private Vector2 _at;

    /// <summary>Hang the interface before this head; null when there is no interface to hang (the game is not in a headset).</summary>
    public static VrPanel? Attach(Node3D rig, Adventurer body, XRCamera3D head, XRController3D left, XRController3D right)
    {
        if (Vr.Ui is not { } ui) return null;
        var panel = new VrPanel { Name = "VrPanel", TopLevel = true, ProcessMode = ProcessModeEnum.Always, _body = body, _head = head, _left = left, _right = right };
        panel.AddChild(new MeshInstance3D
        {
            Name = "Sheet",
            Mesh = new QuadMesh { Size = new Vector2(Width, Height) },
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            MaterialOverride = new StandardMaterial3D
            {
                ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, AlbedoTexture = ui.GetTexture(), Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
                NoDepthTest = true, CullMode = BaseMaterial3D.CullModeEnum.Disabled, RenderPriority = 10, // drawn over the walls it would otherwise sink into
            },
        });
        panel._dot = new MeshInstance3D
        {
            Name = "Pointer", Visible = false,
            Mesh = new SphereMesh { Radius = 0.008f, Height = 0.016f },
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            MaterialOverride = new StandardMaterial3D { ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, AlbedoColor = new Color(1f, 0.85f, 0.3f), NoDepthTest = true, RenderPriority = 11 },
        };
        panel.AddChild(panel._dot);
        rig.AddChild(panel);
        return panel;
    }

    private static float YawOf(Vector3 forward, float fallback)
        => new Vector2(forward.X, forward.Z).LengthSquared() > 1e-4f ? Mathf.Atan2(-forward.X, -forward.Z) : fallback;

    public override void _Process(double delta)
    {
        if (Vr.Ui is not { } ui || !IsInstanceValid(_body)) return;
        var dt = (float)delta;
        var menu = _body.Party.InputLocked; // a menu, a card, a loading screen: something flat owns the game
        Place(menu, dt);
        Buttons(menu);
        Point(ui, menu, dt);
        _wasMenu = menu;
    }

    /// <summary>Ahead of the head: drifting round while you play, put squarely in front and left there when a menu opens.</summary>
    private void Place(bool menu, float dt)
    {
        var headYaw = YawOf(-_head.GlobalTransform.Basis.Z, _yaw);
        var settle = !_placed || (menu && !_wasMenu);
        if (settle) _yaw = headYaw;
        else if (!menu) _yaw = Mathf.LerpAngle(_yaw, headYaw, 1f - Mathf.Exp(-FollowRate * dt));
        if (settle || !menu) _centre = _head.GlobalPosition + new Vector3(-Mathf.Sin(_yaw), 0f, -Mathf.Cos(_yaw)) * Distance + Vector3.Down * Drop;
        _placed = true;
        GlobalTransform = new Transform3D(new Basis(Vector3.Up, _yaw), _centre);
    }

    /// <summary>The buttons that stand in for keys.</summary>
    private void Buttons(bool menu)
    {
        var esc = _left.IsButtonPressed("menu_button");
        var close = menu && _right.IsButtonPressed("by_button");
        var bag = _left.IsButtonPressed("by_button");
        var map = _left.IsButtonPressed("ax_button");
        if ((esc && !_escHeld) || (close && !_closeHeld)) Key(Godot.Key.Escape);
        if (bag && !_bagHeld) Action("inventory");
        if (map && !_mapHeld) Action("map");
        (_escHeld, _closeHeld, _bagHeld, _mapHeld) = (esc, close, bag, map);
    }

    private static void Key(Key key)
    {
        Input.ParseInputEvent(new InputEventKey { Keycode = key, PhysicalKeycode = key, Pressed = true });
        Input.ParseInputEvent(new InputEventKey { Keycode = key, PhysicalKeycode = key, Pressed = false });
    }

    private static void Action(string action)
    {
        Input.ParseInputEvent(new InputEventAction { Action = action, Pressed = true });
        Input.ParseInputEvent(new InputEventAction { Action = action, Pressed = false });
    }

    /// <summary>The right controller as a mouse on the sheet, while a menu is open.</summary>
    private void Point(SubViewport ui, bool menu, float dt)
    {
        var trigger = _right.GetFloat("trigger") > (_triggerHeld ? Vr.SqueezeOff : Vr.SqueezeOn);
        var on = false;
        if (menu)
        {
            var from = _right.GlobalPosition;
            var dir = -_right.GlobalTransform.Basis.Z;
            var normal = GlobalTransform.Basis.Z;
            var toward = dir.Dot(normal);
            if (Mathf.Abs(toward) > 1e-4f && (GlobalPosition - from).Dot(normal) / toward is > 0f and var reach)
            {
                var local = ToLocal(from + dir * reach);
                if (Mathf.Abs(local.X) <= Width / 2f && Mathf.Abs(local.Y) <= Height / 2f)
                {
                    on = true;
                    _dot.Position = new Vector3(local.X, local.Y, 0.005f);
                    var at = new Vector2((local.X / Width + 0.5f) * ui.Size.X, (0.5f - local.Y / Height) * ui.Size.Y);
                    if (at != _at)
                    {
                        ui.PushInput(new InputEventMouseMotion { Position = at, GlobalPosition = at, Relative = at - _at, ButtonMask = _pressed ? MouseButtonMask.Left : 0 }, true);
                        _at = at;
                    }
                }
            }
            if (on && trigger && !_triggerHeld) Button(ui, MouseButton.Left, true);

            _scrollWait -= dt;
            var scroll = _right.GetVector2("primary").Y;
            if (on && Mathf.Abs(scroll) > Vr.TurnPush && _scrollWait <= 0f)
            {
                _scrollWait = ScrollEvery;
                var wheel = scroll > 0f ? MouseButton.WheelUp : MouseButton.WheelDown;
                Button(ui, wheel, true);
                Button(ui, wheel, false);
            }
        }
        if (_pressed && (!trigger || !menu)) Button(ui, MouseButton.Left, false); // let go wherever the hand is by now, or a drag never ends
        _dot.Visible = on;
        _triggerHeld = trigger;
    }

    private void Button(SubViewport ui, MouseButton button, bool pressed)
    {
        if (button == MouseButton.Left) _pressed = pressed;
        ui.PushInput(new InputEventMouseButton { ButtonIndex = button, Pressed = pressed, Position = _at, GlobalPosition = _at, ButtonMask = _pressed ? MouseButtonMask.Left : 0 }, true);
    }
}
