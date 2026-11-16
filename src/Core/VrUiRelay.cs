using Godot;

namespace DungeonCrawler.Core;

/// <summary>
/// In a headset the interface lives in its own viewport (<see cref="Vr.Ui"/>), where the keys never arrive on their
/// own: this hands every key, action and gamepad press on to it, so Esc, the bag key and the rest still reach the HUD.
/// The mouse is not passed on; in a headset the pointer is the right controller (<see cref="Party.VrPanel"/>).
/// </summary>
public partial class VrUiRelay : Node
{
    public override void _Ready() => ProcessMode = ProcessModeEnum.Always; // the pause menu must still hear Esc

    public override void _Input(InputEvent @event)
    {
        if (Vr.Ui is { } ui && @event is InputEventKey or InputEventAction or InputEventJoypadButton or InputEventJoypadMotion) ui.PushInput(@event, true);
    }
}
