using DungeonCrawler.Save;
using Godot;

namespace DungeonCrawler.UI.Menus;

/// <summary>Four save slots with their headers. Mode decides whether clicking saves into or loads from a slot.</summary>
public partial class SlotList : VBoxContainer
{
    public enum Mode { Save, Load }

    public event Action<string>? SlotChosen;
    public event Action? Cancelled;

    private Mode _mode;
    private readonly List<Button> _buttons = new();
    private Label _title = null!;

    public override void _Ready()
    {
        AddThemeConstantOverride("separation", 6);
        _title = new Label();
        AddChild(_title);
        foreach (var slot in SaveManager.AllSlots)
        {
            var b = new Button { Alignment = HorizontalAlignment.Left, CustomMinimumSize = new Vector2(520, 44), FocusMode = FocusModeEnum.None };
            var s = slot;
            b.Pressed += () => SlotChosen?.Invoke(s);
            AddChild(b);
            _buttons.Add(b);
        }
        var cancel = new Button { Text = "Back", CustomMinimumSize = new Vector2(120, 36), FocusMode = FocusModeEnum.None };
        cancel.Pressed += () => Cancelled?.Invoke();
        AddChild(cancel);
    }

    public void Show(Mode mode)
    {
        _mode = mode;
        _title.Text = mode == Mode.Save ? "Save to which slot?" : "Load which slot?";
        var i = 0;
        foreach (var slot in SaveManager.AllSlots)
        {
            var info = SaveManager.Info(slot);
            var b = _buttons[i++];
            b.Text = $"{SaveManager.DisplayName(slot)}    {info.Summary}";
            b.Disabled = mode == Mode.Load && (!info.Exists || info.Corrupt);
        }
        Visible = true;
    }
}
