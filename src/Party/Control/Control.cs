using Godot;

namespace DungeonCrawler.Party;

/// <summary>Who drives an <see cref="Adventurer"/> body.</summary>
public enum ControlKind
{
    /// <summary>The player at this machine: movement from <see cref="LocalInput"/>, camera current.</summary>
    LocalHuman,
    /// <summary>Companion AI (single-player only).</summary>
    Companion,
    /// <summary>Another player's body: follows the pose the network hands it.</summary>
    Remote,
    /// <summary>Offline player: the body is out of the world until they come back.</summary>
    Parked,
}

/// <summary>One physics tick's worth of movement input, engine-free so a server can feed it from the wire.</summary>
public struct InputIntent
{
    public Vector2 Move;
    public bool Sprint;
    public bool Crouch;
    public bool JumpPressed;
    public bool Guard;
    /// <summary>False while a menu holds the keys: auto-run and jump ignore stale state.</summary>
    public bool Live;

    public static readonly InputIntent None = default;
}

/// <summary>The only place movement keys are read; everything else works from an <see cref="InputIntent"/>.</summary>
public static class LocalInput
{
    public static InputIntent Read(bool mouseCaptured, bool locked, Vector2? forced)
    {
        var live = mouseCaptured && !locked;
        if (!live && forced == null) return InputIntent.None;
        return new InputIntent
        {
            Move = forced ?? Input.GetVector("move_left", "move_right", "move_forward", "move_back"),
            Sprint = live && Input.IsActionPressed("sprint"),
            Crouch = live && Input.IsActionPressed("crouch"),
            JumpPressed = live && Input.IsActionJustPressed("jump"),
            Guard = live && Input.IsActionPressed("block"),
            Live = live || forced != null,
        };
    }
}

/// <summary>The last pose a remote player reported; the body eases toward it.</summary>
public sealed class RemotePose
{
    public Vector3 Position;
    public float Yaw;
    public float Pitch;
    public Vector3 Velocity;
    public bool Crouch;
    public bool Guard;
    public bool OnFloor = true;
}
